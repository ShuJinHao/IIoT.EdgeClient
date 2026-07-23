[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$InventoryPath,
    [string]$SourceRoot = 'src',
    [switch]$UpdateCandidateBaselines
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string]$PathValue)
    if ([IO.Path]::IsPathRooted($PathValue)) { return [IO.Path]::GetFullPath($PathValue) }
    return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $PathValue))
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string[]]$Lines)
    $payload = [string]::Join("`n", $Lines)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($payload)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-CSharpCompatibilityDeclarations {
    param([Parameter(Mandatory)][string]$Source)

    $root = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($Source).GetRoot()
    $result = [System.Collections.Generic.List[object]]::new()
    foreach ($node in $root.DescendantNodes()) {
        $kind = $null
        $identifiers = @()
        switch ($node.GetType().Name) {
            { $_ -in @('ClassDeclarationSyntax', 'InterfaceDeclarationSyntax', 'StructDeclarationSyntax', 'RecordDeclarationSyntax', 'EnumDeclarationSyntax') } {
                $kind = 'type'; $identifiers = @($node.Identifier)
            }
            'DelegateDeclarationSyntax' { $kind = 'delegate'; $identifiers = @($node.Identifier) }
            { $_ -in @('MethodDeclarationSyntax', 'LocalFunctionStatementSyntax') } {
                $kind = 'method'; $identifiers = @($node.Identifier)
            }
            'PropertyDeclarationSyntax' { $kind = 'property'; $identifiers = @($node.Identifier) }
            'EventDeclarationSyntax' { $kind = 'event'; $identifiers = @($node.Identifier) }
            'FieldDeclarationSyntax' {
                $isConst = @($node.Modifiers | Where-Object {
                    $_.RawKind -eq [int][Microsoft.CodeAnalysis.CSharp.SyntaxKind]::ConstKeyword
                }).Count -gt 0
                $kind = if ($isConst) { 'const' } else { 'field' }
                $identifiers = @($node.Declaration.Variables | ForEach-Object { $_.Identifier })
            }
            'EventFieldDeclarationSyntax' {
                $kind = 'event'
                $identifiers = @($node.Declaration.Variables | ForEach-Object { $_.Identifier })
            }
            'EnumMemberDeclarationSyntax' { $kind = 'enum-member'; $identifiers = @($node.Identifier) }
            'UsingDirectiveSyntax' {
                if ($null -ne $node.Alias) { $kind = 'alias'; $identifiers = @($node.Alias.Name.Identifier) }
            }
        }

        foreach ($identifier in $identifiers) {
            $result.Add([pscustomobject]@{
                Kind = $kind
                Symbol = [string]$identifier.ValueText
                DeclarationText = [regex]::Replace([string]$node.ToString(), '\s+', ' ').Trim()
                IdentifierStart = [int]$identifier.Span.Start
            })
        }

        if ($node.GetType().Name -in @('ClassDeclarationSyntax', 'StructDeclarationSyntax', 'RecordDeclarationSyntax') -and
            $null -ne $node.ParameterList) {
            $parameterKind = if ($node.GetType().Name -eq 'RecordDeclarationSyntax') {
                'record-property'
            } else {
                'primary-constructor-parameter'
            }
            foreach ($parameter in $node.ParameterList.Parameters) {
                $result.Add([pscustomobject]@{
                    Kind = $parameterKind
                    Symbol = [string]$parameter.Identifier.ValueText
                    DeclarationText = [regex]::Replace([string]$parameter.ToString(), '\s+', ' ').Trim()
                    IdentifierStart = [int]$parameter.Identifier.Span.Start
                })
            }
        }
    }
    return $result.ToArray()
}

function Get-CSharpCompatibilityReferenceText {
    param([Parameter(Mandatory)][string]$Source)

    $root = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($Source).GetRoot()
    $characters = $Source.ToCharArray()
    $declarationStarts = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($declaration in @(Get-CSharpCompatibilityDeclarations $Source)) {
        [void]$declarationStarts.Add([int]$declaration.IdentifierStart)
    }
    foreach ($trivia in $root.DescendantTrivia()) {
        if ($trivia.RawKind -in @(
            [int][Microsoft.CodeAnalysis.CSharp.SyntaxKind]::WhitespaceTrivia,
            [int][Microsoft.CodeAnalysis.CSharp.SyntaxKind]::EndOfLineTrivia)) { continue }
        for ($index = $trivia.FullSpan.Start; $index -lt $trivia.FullSpan.End; $index++) {
            if ($characters[$index] -notin @("`r", "`n")) { $characters[$index] = ' ' }
        }
    }

    $literalKinds = @(
        [int][Microsoft.CodeAnalysis.CSharp.SyntaxKind]::StringLiteralToken,
        [int][Microsoft.CodeAnalysis.CSharp.SyntaxKind]::Utf8StringLiteralToken,
        [int][Microsoft.CodeAnalysis.CSharp.SyntaxKind]::CharacterLiteralToken,
        [int][Microsoft.CodeAnalysis.CSharp.SyntaxKind]::InterpolatedStringTextToken,
        [int][Microsoft.CodeAnalysis.CSharp.SyntaxKind]::SingleLineRawStringLiteralToken,
        [int][Microsoft.CodeAnalysis.CSharp.SyntaxKind]::MultiLineRawStringLiteralToken)
    foreach ($token in $root.DescendantTokens()) {
        if ($token.RawKind -notin $literalKinds -and -not $declarationStarts.Contains([int]$token.Span.Start)) { continue }
        for ($index = $token.Span.Start; $index -lt $token.Span.End; $index++) {
            if ($characters[$index] -notin @("`r", "`n")) { $characters[$index] = ' ' }
        }
    }
    return -join $characters
}

$evidenceTextCache = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
function Get-CompatibilityEvidenceText {
    param([Parameter(Mandatory)][string]$Path)

    $cached = $null
    if ($evidenceTextCache.TryGetValue($Path, [ref]$cached)) { return $cached }
    $source = Get-Content $Path -Raw
    $text = if ([IO.Path]::GetExtension($Path).Equals('.cs', [StringComparison]::OrdinalIgnoreCase)) {
        Get-CSharpCompatibilityReferenceText $source
    } else {
        $source
    }
    $evidenceTextCache[$Path] = $text
    return $text
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}
if ([string]::IsNullOrWhiteSpace($InventoryPath)) {
    $InventoryPath = Join-Path $PSScriptRoot 'edge-compatibility-inventory.json'
} else {
    $InventoryPath = Resolve-RepositoryPath $InventoryPath
}
$resolvedSourceRoot = Resolve-RepositoryPath $SourceRoot

if (-not (Test-Path $InventoryPath -PathType Leaf)) {
    throw "TEST-COMPAT-001 inventory does not exist: $InventoryPath"
}
if (-not (Test-Path $resolvedSourceRoot -PathType Container)) {
    throw "TEST-COMPAT-001 source root does not exist: $resolvedSourceRoot"
}
if (-not ('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type])) {
    throw 'TEST-COMPAT-001 Roslyn syntax runtime is unavailable.'
}

$inventory = Get-Content $InventoryPath -Raw | ConvertFrom-Json -Depth 40
if ([int]$inventory.schemaVersion -ne 3 -or [string]$inventory.ruleId -cne 'TEST-COMPAT-001') {
    throw 'TEST-COMPAT-001 inventory schemaVersion/ruleId is invalid.'
}

$requiredTokens = @('alias', 'adapter', 'wrapper', 'compat', 'legacy', 'shadow', 'obsolete', 'fallback', '双写', '影子')
$scanTokens = @($inventory.scanTokens | ForEach-Object { ([string]$_).ToLowerInvariant() })
if (($scanTokens -join '|') -cne ($requiredTokens -join '|') -or
    @($scanTokens | Sort-Object -Unique).Count -ne $scanTokens.Count) {
    throw 'TEST-COMPAT-001 scanTokens must contain the locked compatibility candidate vocabulary exactly once and in order.'
}

$extensions = @('.cs', '.csproj', '.json', '.axaml')
$candidates = [System.Collections.Generic.List[object]]::new()
$declaredSymbolCandidates = [System.Collections.Generic.List[object]]::new()
$declarationPatterns = @(
    [pscustomobject]@{
        kind = 'type'
        pattern = '(?m)^\s*(?:(?:public|internal|private|protected|sealed|abstract|static|partial|readonly|ref|file)\s+)*(?:class|interface|struct|enum|record(?:\s+(?:class|struct))?)\s+(?<symbol>[A-Za-z_][A-Za-z0-9_]*)'
    },
    [pscustomobject]@{
        kind = 'method'
        pattern = '(?m)^\s*(?:(?:public|internal|private|protected)\s+)(?:(?:static|virtual|override|abstract|async|extern|sealed|new|partial|unsafe)\s+)*(?!(?:class|interface|struct|enum|record)\b)(?:[A-Za-z_][A-Za-z0-9_\.]*)(?:\s*<[^\r\n;{}()]+>)?(?:\s*\[\s*\])?\??\s+(?<symbol>[A-Za-z_][A-Za-z0-9_]*)(?:\s*<[^\r\n;{}()]+>)?\s*\('
    },
    [pscustomobject]@{
        kind = 'method'
        pattern = '(?m)^\s*(?!(?:public|internal|private|protected|class|interface|struct|enum|record|return|await|throw|if|for|foreach|while|switch|catch|using|lock|CREATE|ALTER|DROP|SELECT|INSERT|UPDATE|DELETE|EXISTS|INDEX|TABLE|ON)\b)(?:(?:static|virtual|override|abstract|async|extern|sealed|new|partial|unsafe)\s+)*(?:[A-Za-z_][A-Za-z0-9_\.]*)(?:\s*<[^\r\n;{}()]+>)?(?:\s*\[\s*\])?\??\s+(?<symbol>[A-Za-z_][A-Za-z0-9_]*)(?:\s*<[^\r\n;{}()]+>)?\s*\('
    },
    [pscustomobject]@{
        kind = 'property'
        pattern = '(?m)^\s*(?:(?:public|internal|private|protected)\s+)(?:(?:static|virtual|override|abstract|sealed|required|new|unsafe)\s+)*(?!(?:class|interface|struct|enum|record|event|const)\b)(?:[A-Za-z_][A-Za-z0-9_\.]*)(?:\s*<[^\r\n;{}()]+>)?(?:\s*\[\s*\])?\??\s+(?<symbol>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|=>)'
    },
    [pscustomobject]@{
        kind = 'property'
        pattern = '(?m)^\s*(?!(?:public|internal|private|protected|class|interface|struct|enum|record|event|const|return|await|throw|if|for|foreach|while|switch|catch|using|lock)\b)(?:(?:static|virtual|override|abstract|sealed|required|new|unsafe)\s+)*(?:[A-Za-z_][A-Za-z0-9_\.]*)(?:\s*<[^\r\n;{}()]+>)?(?:\s*\[\s*\])?\??\s+(?<symbol>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|=>)'
    },
    [pscustomobject]@{
        kind = 'const'
        pattern = '(?m)^\s*(?:(?:public|internal|private|protected)\s+)?(?:(?:new|unsafe)\s+)*const\s+(?:[A-Za-z_][A-Za-z0-9_\.]*)(?:\s*<[^\r\n;{}()]+>)?(?:\s*\[\s*\])?\??\s+(?<symbol>[A-Za-z_][A-Za-z0-9_]*)\s*='
    },
    [pscustomobject]@{
        kind = 'event'
        pattern = '(?m)^\s*(?:(?:public|internal|private|protected)\s+)?(?:(?:static|virtual|override|abstract|sealed|new)\s+)*event\s+(?:[A-Za-z_][A-Za-z0-9_\.]*)(?:\s*<[^\r\n;{}()]+>)?\??\s+(?<symbol>[A-Za-z_][A-Za-z0-9_]*)\s*(?:[;={])'
    },
    [pscustomobject]@{
        kind = 'field'
        pattern = '(?m)^\s*(?:(?:public|internal|private|protected)\s+)(?:(?:static|readonly|volatile|new|unsafe|required)\s+)*(?!(?:class|interface|struct|enum|record|event|const)\b)(?:[A-Za-z_][A-Za-z0-9_\.]*)(?:\s*<[^\r\n;{}()]+>)?(?:\s*\[\s*\])?\??\s+(?<symbol>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=(?!>)|;)'
    }
)
$recordDeclarationPattern = '(?ms)\brecord(?:\s+(?:class|struct))?\s+[A-Za-z_][A-Za-z0-9_]*(?:\s*<[^\r\n;{}()]+>)?\s*\((?<parameters>.*?)\)\s*(?:[:{;])'
foreach ($file in @(Get-ChildItem $resolvedSourceRoot -Recurse -File | Sort-Object FullName)) {
    if ([IO.Path]::GetExtension($file.Name).ToLowerInvariant() -notin $extensions) { continue }
    $relativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $file.FullName).Replace('\', '/')
    if ($relativePath -match '(^|/)(?:Tests|Testing|bin|obj)(?:/|$)') { continue }
    $text = Get-Content $file.FullName -Raw
    if ([IO.Path]::GetExtension($file.Name).Equals('.cs', [StringComparison]::OrdinalIgnoreCase)) {
        foreach ($declaration in @(Get-CSharpCompatibilityDeclarations $text)) {
            $symbol = [string]$declaration.Symbol
            foreach ($token in $scanTokens) {
                if ($symbol.IndexOf($token, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    $declaredSymbolCandidates.Add([pscustomobject][ordered]@{
                        token = $token
                        kind = [string]$declaration.Kind
                        symbol = $symbol
                        declarationPath = $relativePath
                        declarationText = [string]$declaration.DeclarationText
                    })
                }
            }
        }
    }
    foreach ($token in $scanTokens) {
        $occurrences = [regex]::Matches(
            $text,
            [regex]::Escape($token),
            [Text.RegularExpressions.RegexOptions]::IgnoreCase).Count
        if ($occurrences -gt 0) {
            $candidates.Add([pscustomobject][ordered]@{
                token = $token
                path = $relativePath
                occurrences = $occurrences
            })
        }
    }
}

$dispositions = @($inventory.candidateDispositions)
$dispositionIds = @($dispositions | ForEach-Object { [string]$_.id })
if ($dispositions.Count -eq 0 -or @($dispositionIds | Sort-Object -Unique).Count -ne $dispositionIds.Count) {
    throw 'TEST-COMPAT-001 candidate dispositions must be non-empty and have unique IDs.'
}
$dispositionKeys = @($dispositions | ForEach-Object {
    "$(([string]$_.token).ToLowerInvariant())|$([string]$_.pathPattern)"
})
if (@($dispositionKeys | Sort-Object -Unique).Count -ne $dispositionKeys.Count) {
    throw 'TEST-COMPAT-001 candidate token/pathPattern dispositions must be unique.'
}

$unclassified = [System.Collections.Generic.List[string]]::new()
foreach ($candidate in $candidates) {
    $matches = @($dispositions | Where-Object {
        ([string]$_.token).Equals([string]$candidate.token, [StringComparison]::OrdinalIgnoreCase) -and
        [string]$candidate.path -match [string]$_.pathPattern
    })
    if ($matches.Count -ne 1) {
        $unclassified.Add("$($candidate.token)|$($candidate.path)")
    }
}
if ($unclassified.Count -gt 0) {
    throw "TEST-COMPAT-001 active compatibility candidates must have exactly one disposition; unclassified=$($unclassified.Count): $($unclassified -join ', ')."
}

$evidenceCount = 0
foreach ($disposition in $dispositions) {
    foreach ($field in @('id', 'token', 'pathPattern', 'status', 'rationale')) {
        if ([string]::IsNullOrWhiteSpace([string]$disposition.$field)) {
            throw "TEST-COMPAT-001 candidate disposition '$($disposition.id)' is missing '$field'."
        }
    }
    if ([string]$disposition.status -notin @('OrdinaryAbstraction', 'MigrationWindow')) {
        throw "TEST-COMPAT-001 candidate disposition '$($disposition.id)' has unsupported status '$($disposition.status)'."
    }
    $tokenCandidates = @($candidates | Where-Object {
        ([string]$_.token).Equals([string]$disposition.token, [StringComparison]::OrdinalIgnoreCase) -and
        [string]$_.path -match [string]$disposition.pathPattern
    })
    if ($tokenCandidates.Count -eq 0) {
        throw "TEST-COMPAT-001 dead candidate disposition '$($disposition.id)' has no active source candidate."
    }
    $manifestLines = @($tokenCandidates | Sort-Object path | ForEach-Object {
        "$($_.token)|$($_.path)|$($_.occurrences)"
    })
    $actualHash = Get-Sha256 $manifestLines
    $actualOccurrences = [int](($tokenCandidates | Measure-Object occurrences -Sum).Sum)
    if ($UpdateCandidateBaselines) {
        $disposition.candidateCount = $tokenCandidates.Count
        $disposition.occurrenceCount = $actualOccurrences
        $disposition.manifestSha256 = $actualHash
    } elseif ([int]$disposition.candidateCount -ne $tokenCandidates.Count -or
        [int]$disposition.occurrenceCount -ne $actualOccurrences -or
        [string]$disposition.manifestSha256 -cne $actualHash) {
        throw "TEST-COMPAT-001 candidate manifest drift for '$($disposition.id)': expectedFiles=$($disposition.candidateCount) actualFiles=$($tokenCandidates.Count) expectedOccurrences=$($disposition.occurrenceCount) actualOccurrences=$actualOccurrences. Register a justified disposition or delete the dead compatibility path."
    }

    $callEvidence = @($disposition.callEvidence)
    if ($callEvidence.Count -eq 0) {
        throw "TEST-COMPAT-001 $($disposition.id) has no real consumer evidence; delete the abstraction instead."
    }
    foreach ($evidence in $callEvidence) {
        $path = Resolve-RepositoryPath ([string]$evidence.path)
        if (-not (Test-Path $path -PathType Leaf)) {
            throw "TEST-COMPAT-001 $($disposition.id) evidence path does not exist: $($evidence.path)"
        }
        $actual = [regex]::Matches((Get-CompatibilityEvidenceText $path), [string]$evidence.pattern).Count
        $expected = [int]$evidence.expectedOccurrences
        if ($expected -le 0 -or $actual -ne $expected) {
            throw "TEST-COMPAT-001 $($disposition.id) consumer ratchet changed for $($evidence.path): expected=$expected actual=$actual. Zero consumers require physical deletion; new consumers are forbidden."
        }
        $evidenceCount += $actual
    }

    if ([string]$disposition.status -eq 'MigrationWindow') {
        foreach ($field in @('producer', 'replacementPath', 'deletionCondition', 'latestRemovalBatch')) {
            if ([string]::IsNullOrWhiteSpace([string]$disposition.$field)) {
                throw "TEST-COMPAT-001 $($disposition.id) migration window is missing '$field'."
            }
        }
        if (@($disposition.currentConsumers).Count -eq 0) {
            throw "TEST-COMPAT-001 $($disposition.id) migration window lacks current consumers."
        }
    }
}

$migrationWindows = @($inventory.migrationWindows)
foreach ($entry in $migrationWindows) {
    foreach ($field in @('id', 'producer', 'replacementPath', 'deletionCondition', 'latestRemovalBatch')) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.$field)) {
            throw "TEST-COMPAT-001 migration window '$($entry.id)' is missing '$field'."
        }
    }
    if ([string]$entry.status -cne 'MigrationWindow' -or
        @($entry.currentConsumers).Count -eq 0 -or @($entry.callEvidence).Count -eq 0) {
        throw "TEST-COMPAT-001 $($entry.id) is not a bounded migration window with real consumers."
    }
    foreach ($evidence in @($entry.callEvidence)) {
        $path = Resolve-RepositoryPath ([string]$evidence.path)
        if (-not (Test-Path $path -PathType Leaf)) {
            throw "TEST-COMPAT-001 $($entry.id) evidence path does not exist: $($evidence.path)"
        }
        $actual = [regex]::Matches((Get-CompatibilityEvidenceText $path), [string]$evidence.pattern).Count
        $expected = [int]$evidence.expectedOccurrences
        if ($expected -le 0 -or $actual -ne $expected) {
            throw "TEST-COMPAT-001 $($entry.id) call-site ratchet changed for $($evidence.path): expected=$expected actual=$actual."
        }
        $evidenceCount += $actual
    }
}

$boundedMigrationWindows = [System.Collections.Generic.List[object]]::new()
foreach ($disposition in @($dispositions | Where-Object { [string]$_.status -eq 'MigrationWindow' })) {
    $boundedMigrationWindows.Add([pscustomobject]@{
        Id = [string]$disposition.id
        Source = 'candidateDisposition'
        Entry = $disposition
    })
}
foreach ($entry in $migrationWindows) {
    $boundedMigrationWindows.Add([pscustomobject]@{
        Id = [string]$entry.id
        Source = 'migrationWindow'
        Entry = $entry
    })
}
$boundedWindowIds = @($boundedMigrationWindows | ForEach-Object { [string]$_.Id })
if (@($boundedWindowIds | Sort-Object -Unique).Count -ne $boundedWindowIds.Count) {
    throw 'TEST-COMPAT-001 bounded migration window IDs must be unique across candidateDispositions and migrationWindows.'
}

$registeredSymbols = @($inventory.symbolCandidates)
$registeredSymbolKeys = @($registeredSymbols | ForEach-Object {
    $kind = if ($null -eq $_.PSObject.Properties['kind'] -or
        [string]::IsNullOrWhiteSpace([string]$_.kind)) { 'type' } else { ([string]$_.kind).ToLowerInvariant() }
    $declarationPattern = if ($null -eq $_.PSObject.Properties['declarationPattern']) { '' } else { [string]$_.declarationPattern }
    "$(([string]$_.token).ToLowerInvariant())|$kind|$([string]$_.symbol)|$([string]$_.declarationPath)|$declarationPattern"
})
if ($registeredSymbols.Count -eq 0 -or
    @($registeredSymbolKeys | Sort-Object -Unique).Count -ne $registeredSymbolKeys.Count) {
    throw 'TEST-COMPAT-001 symbolCandidates must be non-empty and have unique token/kind/symbol/declarationPath keys.'
}

$unregisteredSymbols = [System.Collections.Generic.List[string]]::new()
foreach ($candidate in $declaredSymbolCandidates) {
    $matches = @($registeredSymbols | Where-Object {
        $registrationKind = if ($null -eq $_.PSObject.Properties['kind'] -or
            [string]::IsNullOrWhiteSpace([string]$_.kind)) { 'type' } else { ([string]$_.kind).ToLowerInvariant() }
        $declarationPattern = if ($null -eq $_.PSObject.Properties['declarationPattern']) { '' } else { [string]$_.declarationPattern }
        ([string]$_.token).Equals([string]$candidate.token, [StringComparison]::OrdinalIgnoreCase) -and
        $registrationKind.Equals([string]$candidate.kind, [StringComparison]::Ordinal) -and
        ([string]$_.symbol).Equals([string]$candidate.symbol, [StringComparison]::Ordinal) -and
        ([string]$_.declarationPath).Equals([string]$candidate.declarationPath, [StringComparison]::Ordinal) -and
        ([string]::IsNullOrWhiteSpace($declarationPattern) -or [string]$candidate.declarationText -match $declarationPattern)
    })
    if ($matches.Count -ne 1) {
        $unregisteredSymbols.Add("$($candidate.token)|$($candidate.kind)|$($candidate.symbol)|$($candidate.declarationPath)")
    }
}
if ($unregisteredSymbols.Count -gt 0) {
    throw "TEST-COMPAT-001 every compatibility-like declaration requires exact per-candidate evidence; unregisteredSymbols=$($unregisteredSymbols.Count): $($unregisteredSymbols -join ', ')."
}

foreach ($registration in $registeredSymbols) {
    $registrationKind = if ($null -eq $registration.PSObject.Properties['kind'] -or
        [string]::IsNullOrWhiteSpace([string]$registration.kind)) { 'type' } else { ([string]$registration.kind).ToLowerInvariant() }
    if ($registrationKind -notin @(
        'type', 'delegate', 'method', 'property', 'field', 'const', 'event', 'alias',
        'enum-member', 'record-property', 'primary-constructor-parameter')) {
        throw "TEST-COMPAT-001 symbol candidate '$($registration.symbol)' has unsupported kind '$registrationKind'."
    }
    foreach ($field in @('token', 'symbol', 'declarationPath', 'status', 'rationale')) {
        if ([string]::IsNullOrWhiteSpace([string]$registration.$field)) {
            throw "TEST-COMPAT-001 symbol candidate '$($registration.symbol)' is missing '$field'."
        }
    }
    if ([string]$registration.status -notin @('OrdinaryAbstraction', 'MigrationWindow')) {
        throw "TEST-COMPAT-001 symbol candidate '$($registration.symbol)' has unsupported status '$($registration.status)'."
    }
    if ([string]$registration.status -eq 'MigrationWindow') {
        $windowId = if ($null -eq $registration.PSObject.Properties['windowId']) {
            ''
        } else {
            [string]$registration.windowId
        }
        if ([string]::IsNullOrWhiteSpace($windowId)) {
            throw "TEST-COMPAT-001 migration symbol '$($registration.symbol)' is missing 'windowId'."
        }
        $windowMatches = @($boundedMigrationWindows | Where-Object {
            ([string]$_.Id).Equals($windowId, [StringComparison]::Ordinal)
        })
        if ($windowMatches.Count -ne 1) {
            throw "TEST-COMPAT-001 migration symbol '$($registration.symbol)' references unknown migration window '$windowId'."
        }

        $boundWindow = $windowMatches[0]
        if ([string]$boundWindow.Source -eq 'candidateDisposition' -and
            (-not ([string]$boundWindow.Entry.token).Equals([string]$registration.token, [StringComparison]::OrdinalIgnoreCase) -or
             -not ([string]$registration.declarationPath -match [string]$boundWindow.Entry.pathPattern))) {
            throw "TEST-COMPAT-001 migration symbol '$($registration.symbol)' does not belong to candidate window '$windowId'."
        }

    }
    $activeMatches = @($declaredSymbolCandidates | Where-Object {
        ([string]$_.token).Equals([string]$registration.token, [StringComparison]::OrdinalIgnoreCase) -and
        ([string]$_.kind).Equals($registrationKind, [StringComparison]::Ordinal) -and
        ([string]$_.symbol).Equals([string]$registration.symbol, [StringComparison]::Ordinal) -and
        ([string]$_.declarationPath).Equals([string]$registration.declarationPath, [StringComparison]::Ordinal) -and
        ($null -eq $registration.PSObject.Properties['declarationPattern'] -or
            [string]::IsNullOrWhiteSpace([string]$registration.declarationPattern) -or
            [string]$_.declarationText -match [string]$registration.declarationPattern)
    })
    if ($activeMatches.Count -ne 1) {
        throw "TEST-COMPAT-001 dead or ambiguous symbol registration '$($registration.symbol)' has activeMatches=$($activeMatches.Count)."
    }

    $callEvidence = @($registration.callEvidence)
    if ($callEvidence.Count -eq 0) {
        throw "TEST-COMPAT-001 symbol candidate '$($registration.symbol)' has no exact consumer evidence; delete it."
    }
    foreach ($evidence in $callEvidence) {
        $path = Resolve-RepositoryPath ([string]$evidence.path)
        if (-not (Test-Path $path -PathType Leaf)) {
            throw "TEST-COMPAT-001 symbol candidate '$($registration.symbol)' evidence path does not exist: $($evidence.path)"
        }
        $actual = [regex]::Matches((Get-CompatibilityEvidenceText $path), [string]$evidence.pattern).Count
        $expected = [int]$evidence.expectedOccurrences
        if ($expected -le 0 -or $actual -ne $expected) {
            throw "TEST-COMPAT-001 symbol candidate '$($registration.symbol)' consumer ratchet changed for $($evidence.path): expected=$expected actual=$actual."
        }
        $evidenceCount += $actual
    }
}

if ($UpdateCandidateBaselines) {
    [IO.File]::WriteAllText(
        $InventoryPath,
        (($inventory | ConvertTo-Json -Depth 40) + "`n"),
        [Text.UTF8Encoding]::new($false))
}

Write-Host "Edge compatibility inventory passed: candidates=$($candidates.Count), candidateOccurrences=$([int](($candidates | Measure-Object occurrences -Sum).Sum)), dispositions=$($dispositions.Count), symbolCandidates=$($declaredSymbolCandidates.Count), migrationWindows=$($migrationWindows.Count), pinnedCallEvidence=$evidenceCount, unclassified=0, unregisteredSymbols=0, deadConsumers=0, newConsumers=0."
