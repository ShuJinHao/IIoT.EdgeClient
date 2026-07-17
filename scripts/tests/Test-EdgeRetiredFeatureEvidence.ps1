[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$LedgerPath,
    [string]$DiscoveredInventoryPath,
    [string]$InventoryPath,
    [string]$ActiveInputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ruleMarker = 'EDGE-RETIRED-FEATURE-EVIDENCE-001'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

function Resolve-RepositoryPath([string]$PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) { return [IO.Path]::GetFullPath($PathValue) }
    return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $PathValue))
}

$LedgerPath = if ([string]::IsNullOrWhiteSpace($LedgerPath)) {
    Join-Path $RepositoryRoot 'scripts/tests/baselines/edge-regression-ledger.json'
} else { Resolve-RepositoryPath $LedgerPath }
$DiscoveredInventoryPath = if ([string]::IsNullOrWhiteSpace($DiscoveredInventoryPath)) {
    Join-Path $RepositoryRoot 'scripts/tests/discovered-test-inventory.json'
} else { Resolve-RepositoryPath $DiscoveredInventoryPath }
$InventoryPath = if ([string]::IsNullOrWhiteSpace($InventoryPath)) {
    Join-Path $RepositoryRoot 'scripts/tests/edge-test-inventory.json'
} else { Resolve-RepositoryPath $InventoryPath }
$ActiveInputRoot = if ([string]::IsNullOrWhiteSpace($ActiveInputRoot)) {
    $RepositoryRoot
} elseif ([IO.Path]::IsPathRooted($ActiveInputRoot)) {
    [IO.Path]::GetFullPath($ActiveInputRoot)
} else {
    [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $ActiveInputRoot))
}
$ledgerValidatorPath = Join-Path $RepositoryRoot 'scripts/tests/Test-EdgeRegressionLedger.ps1'

foreach ($requiredPath in @($LedgerPath, $DiscoveredInventoryPath, $InventoryPath, $ledgerValidatorPath)) {
    if (-not (Test-Path $requiredPath -PathType Leaf)) {
        throw "$ruleMarker required file does not exist: $requiredPath"
    }
}
if (-not (Test-Path $ActiveInputRoot -PathType Container)) {
    throw "$ruleMarker active input root does not exist: $ActiveInputRoot"
}

function Assert-ExactStringSet {
    param(
        [Parameter(Mandatory)][object[]]$Actual,
        [Parameter(Mandatory)][string[]]$Expected,
        [Parameter(Mandatory)][string]$Label
    )

    $actualStrings = @($Actual | ForEach-Object { [string]$_ })
    $actualUnique = @($actualStrings | Sort-Object -Unique)
    $expectedUnique = @($Expected | Sort-Object -Unique)
    if ($actualStrings.Count -ne $actualUnique.Count -or
        $actualUnique.Count -ne $expectedUnique.Count -or
        ($actualUnique -join "`n") -cne ($expectedUnique -join "`n")) {
        throw "$ruleMarker $Label drifted: expected=[$($expectedUnique -join ', ')] actual=[$($actualStrings -join ', ')]."
    }
}

function New-RegexSet {
    param(
        [Parameter(Mandatory)][object[]]$Patterns,
        [Parameter(Mandatory)][string]$Label
    )

    $patternStrings = @($Patterns | ForEach-Object { [string]$_ })
    if ($patternStrings.Count -eq 0 -or @($patternStrings | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
        throw "$ruleMarker $Label must contain non-empty patterns."
    }
    $regexes = [Collections.Generic.List[Text.RegularExpressions.Regex]]::new()
    foreach ($pattern in $patternStrings) {
        try {
            $regexes.Add([Text.RegularExpressions.Regex]::new(
                $pattern,
                [Text.RegularExpressions.RegexOptions]::CultureInvariant))
        } catch {
            throw "$ruleMarker invalid $Label pattern '$pattern': $($_.Exception.Message)"
        }
    }
    return [Text.RegularExpressions.Regex[]]$regexes.ToArray()
}

function Test-MatchesAnyRegex {
    param(
        [AllowEmptyString()][string]$Value,
        [Parameter(Mandatory)][Text.RegularExpressions.Regex[]]$Regexes
    )

    foreach ($regex in $Regexes) {
        if ($regex.IsMatch($Value)) { return $true }
    }
    return $false
}

function Get-CurrentDeclarationKey([string]$Identity) {
    $argumentIndex = $Identity.IndexOf('(', [StringComparison]::Ordinal)
    if ($argumentIndex -ge 0) { return $Identity.Substring(0, $argumentIndex) }
    return $Identity
}

function Invoke-Git([string[]]$Arguments) {
    $output = @(& git -C $RepositoryRoot @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    if ($LASTEXITCODE -ne 0) {
        throw "$ruleMarker git command failed: git $($Arguments -join ' ')`n$($output -join "`n")"
    }
    return [string[]]$output
}

function Test-IsExcludedActivePath([string]$RelativePath) {
    $segments = @($RelativePath.Split('/', [StringSplitOptions]::RemoveEmptyEntries))
    foreach ($segment in $segments) {
        if ($segment -in @('.git', '.vs', '.idea', 'artifacts', 'bin', 'obj', 'TestResults')) {
            return $true
        }
    }
    if ($segments.Count -gt 0 -and $segments[0] -ceq 'docs') { return $true }
    $extension = [IO.Path]::GetExtension($RelativePath).ToLowerInvariant()
    return $extension -in @('.md', '.mdx', '.rst', '.adoc')
}

function Read-TextIfSupported([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -eq 0) { return '' }
    foreach ($value in $bytes) {
        if ($value -eq 0) { return $null }
    }
    try {
        return [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    } catch [Text.DecoderFallbackException] {
        return $null
    }
}

$ledger = Get-Content $LedgerPath -Raw | ConvertFrom-Json -Depth 64
$discovered = Get-Content $DiscoveredInventoryPath -Raw | ConvertFrom-Json -Depth 64
$entries = @($ledger.entries)
$evidenceProperty = $ledger.PSObject.Properties['retirementEvidence']
if ($null -eq $evidenceProperty) {
    throw "$ruleMarker ledger has no retirement evidence."
}
$retirementEvidence = @($evidenceProperty.Value)
if ($retirementEvidence.Count -eq 0) {
    throw "$ruleMarker ledger has an empty retirement evidence collection."
}

$currentSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($identity in @($discovered.cases | ForEach-Object { [string]$_.identity })) {
    [void]$currentSet.Add((Get-CurrentDeclarationKey $identity))
}

$requiredAllowedPaths = @(
    'scripts/tests/Test-EdgeRegressionLedger.ps1',
    'scripts/tests/baselines/edge-regression-ledger.json'
)
$allTokenRegexes = [Collections.Generic.List[Text.RegularExpressions.Regex]]::new()
$allPathRegexes = [Collections.Generic.List[Text.RegularExpressions.Regex]]::new()
$totalExpectedDeclarations = 0

foreach ($evidence in $retirementEvidence) {
    $ruleId = [string]$evidence.ruleId
    $disposition = [string]$evidence.disposition
    $replacement = [string]$evidence.replacement
    $expectedCount = [int]$evidence.expectedDeclarationCount
    $sourceCommit = [string]$evidence.sourceCommit
    $sourceTree = [string]$evidence.sourceTree
    if ([string]::IsNullOrWhiteSpace($ruleId) -or
        [string]::IsNullOrWhiteSpace($disposition) -or
        $replacement -cne "decision:$ruleId" -or
        $expectedCount -le 0) {
        throw "$ruleMarker retirement evidence identity, disposition, decision, or expected count is invalid."
    }
    if ($sourceCommit -cne [string]$ledger.baselineCommit -or
        $sourceTree -cne [string]$ledger.baselineTree) {
        throw "$ruleMarker retirement evidence is not bound to the ledger frozen source commit/tree: $ruleId."
    }

    Assert-ExactStringSet `
        -Actual @($evidence.allowedNonDocumentationPaths) `
        -Expected $requiredAllowedPaths `
        -Label "non-documentation allowlist for $ruleId"
    $oldSourcePaths = @($evidence.oldSourcePaths | ForEach-Object { [string]$_ })
    if ($oldSourcePaths.Count -eq 0 -or
        @($oldSourcePaths | Sort-Object -Unique).Count -ne $oldSourcePaths.Count) {
        throw "$ruleMarker old source paths must be non-empty and unique: $ruleId."
    }

    $tokenRegexes = @(New-RegexSet -Patterns @($evidence.tokenPatterns) -Label "token for $ruleId")
    $pathRegexes = @(New-RegexSet -Patterns @($evidence.pathPatterns) -Label "path for $ruleId")
    foreach ($regex in $tokenRegexes) { $allTokenRegexes.Add($regex) }
    foreach ($regex in $pathRegexes) { $allPathRegexes.Add($regex) }

    $candidates = @($entries | Where-Object {
        $identity = @(
            [string]$_.oldKey,
            [string]$_.oldSourcePath,
            [string]$_.oldClass,
            [string]$_.oldMethod
        ) -join "`n"
        (Test-MatchesAnyRegex -Value $identity -Regexes $tokenRegexes) -or
        (Test-MatchesAnyRegex -Value ([string]$_.oldSourcePath) -Regexes $pathRegexes)
    })
    if ($candidates.Count -ne $expectedCount) {
        throw "$ruleMarker declaration count drifted for ${ruleId}: expected=$expectedCount actual=$($candidates.Count)."
    }
    $candidateKeys = @($candidates | ForEach-Object { [string]$_.oldKey })
    if (@($candidateKeys | Sort-Object -Unique).Count -ne $candidateKeys.Count) {
        throw "$ruleMarker duplicate oldKey exists in retirement evidence: $ruleId."
    }
    Assert-ExactStringSet `
        -Actual @($candidates | ForEach-Object { [string]$_.oldSourcePath } | Sort-Object -Unique) `
        -Expected $oldSourcePaths `
        -Label "old source paths used by $ruleId"

    $decisionEntries = @($entries | Where-Object {
        [string]$_.disposition -ceq $disposition -or [string]$_.replacement -ceq $replacement
    })
    Assert-ExactStringSet `
        -Actual @($decisionEntries | ForEach-Object { [string]$_.oldKey }) `
        -Expected $candidateKeys `
        -Label "decision declarations for $ruleId"
    foreach ($candidate in $candidates) {
        if ([string]$candidate.disposition -cne $disposition -or
            [string]$candidate.replacement -cne $replacement) {
            throw "$ruleMarker declaration has wrong disposition or decision: $($candidate.oldKey)."
        }
        if ([string]::IsNullOrWhiteSpace([string]$candidate.reason)) {
            throw "$ruleMarker declaration has no reviewed reason: $($candidate.oldKey)."
        }
        if ($currentSet.Contains([string]$candidate.oldKey)) {
            throw "$ruleMarker old declaration returned to current discovery: $($candidate.oldKey)."
        }
    }

    [void](Invoke-Git @('cat-file', '-e', "$sourceCommit`^{commit}"))
    $actualTree = @(Invoke-Git @('rev-parse', "$sourceCommit`^{tree}"))
    if ($actualTree.Count -ne 1 -or $actualTree[0] -cne $sourceTree) {
        throw "$ruleMarker frozen source tree drifted for ${ruleId}: expected=$sourceTree actual=$($actualTree -join ',')."
    }
    foreach ($oldSourcePath in $oldSourcePaths) {
        [void](Invoke-Git @('cat-file', '-e', "$sourceCommit`:$oldSourcePath"))
    }
    $totalExpectedDeclarations += $expectedCount
}

$allowedPathSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($path in $requiredAllowedPaths) { [void]$allowedPathSet.Add($path) }
$matchedPathSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$unexpectedMatches = [Collections.Generic.List[string]]::new()
$activeFileCount = 0
foreach ($file in Get-ChildItem $ActiveInputRoot -Recurse -Force -File) {
    $relativePath = [IO.Path]::GetRelativePath($ActiveInputRoot, $file.FullName).Replace('\', '/')
    if (Test-IsExcludedActivePath $relativePath) { continue }
    $activeFileCount++

    $pathMatch = Test-MatchesAnyRegex -Value $relativePath -Regexes $allPathRegexes.ToArray()
    $textMatch = $false
    $text = Read-TextIfSupported $file.FullName
    if ($null -ne $text) {
        $textMatch = Test-MatchesAnyRegex -Value $text -Regexes $allTokenRegexes.ToArray()
    }
    if (-not $pathMatch -and -not $textMatch) { continue }

    [void]$matchedPathSet.Add($relativePath)
    if (-not $allowedPathSet.Contains($relativePath)) {
        $matchKinds = @()
        if ($pathMatch) { $matchKinds += 'path' }
        if ($textMatch) { $matchKinds += 'token' }
        $unexpectedMatches.Add("$relativePath ($($matchKinds -join '+'))")
    }
}

if ($unexpectedMatches.Count -gt 0) {
    throw "$ruleMarker retired feature returned to active non-documentation inputs: $($unexpectedMatches -join ', ')."
}
Assert-ExactStringSet `
    -Actual @($matchedPathSet) `
    -Expected $requiredAllowedPaths `
    -Label 'active non-documentation governance matches'

$ledgerValidationOutput = @(& pwsh -NoLogo -NoProfile -File $ledgerValidatorPath `
    -RepositoryRoot $RepositoryRoot `
    -LedgerPath $LedgerPath `
    -DiscoveredInventoryPath $DiscoveredInventoryPath `
    -InventoryPath $InventoryPath 2>&1 | ForEach-Object { $_.ToString() })
$ledgerValidationExitCode = $LASTEXITCODE
$global:LASTEXITCODE = 0
if ($ledgerValidationExitCode -ne 0) {
    throw "$ruleMarker regression ledger validation failed:`n$($ledgerValidationOutput -join "`n")"
}

Write-Host "$ruleMarker passed: evidence=$($retirementEvidence.Count), declarations=$totalExpectedDeclarations, activeFiles=$activeFileCount, allowedMatches=$($matchedPathSet.Count), unexpected=0."
