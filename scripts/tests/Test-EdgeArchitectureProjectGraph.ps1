[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$SolutionPath,
    [string]$AnalyzerPackageRoot,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FullPath {
    param(
        [Parameter(Mandatory)][string]$BasePath,
        [Parameter(Mandatory)][string]$PathValue
    )

    $normalized = $PathValue.Replace('\', [IO.Path]::DirectorySeparatorChar).Replace('/', [IO.Path]::DirectorySeparatorChar)
    if ([IO.Path]::IsPathRooted($normalized)) {
        return [IO.Path]::GetFullPath($normalized)
    }
    return [IO.Path]::GetFullPath((Join-Path $BasePath $normalized))
}

function Resolve-PhysicalPath {
    param([Parameter(Mandatory)][string]$PathValue)

    $fullPath = [IO.Path]::GetFullPath($PathValue)
    $root = [IO.Path]::GetPathRoot($fullPath)
    if ([string]::IsNullOrWhiteSpace($root)) { return $fullPath }

    $current = $root
    $relative = $fullPath.Substring($root.Length)
    foreach ($segment in $relative.Split(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
        [StringSplitOptions]::RemoveEmptyEntries)) {
        $candidate = Join-Path $current $segment
        if (-not (Test-Path $candidate)) {
            $current = $candidate
            continue
        }

        $entry = Get-Item $candidate -Force
        if ($null -ne $entry.LinkType) {
            $target = $entry.ResolveLinkTarget($true)
            if ($null -ne $target) {
                $current = $target.FullName
                continue
            }
        }
        $current = $entry.FullName
    }

    return [IO.Path]::GetFullPath($current)
}

function Test-IsPathInside {
    param(
        [Parameter(Mandatory)][string]$PathValue,
        [Parameter(Mandatory)][string]$RootPath
    )

    $physicalPath = Resolve-PhysicalPath $PathValue
    $physicalRoot = (Resolve-PhysicalPath $RootPath).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    return $physicalPath.Equals($physicalRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $physicalPath.StartsWith(
            $physicalRoot + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)
}

function Get-RepositoryPath {
    param([Parameter(Mandatory)][string]$FullPath)
    return [IO.Path]::GetRelativePath($RepositoryRoot, $FullPath).Replace('\', '/')
}

function Get-ProjectProperty {
    param(
        [Parameter(Mandatory)][xml]$Project,
        [Parameter(Mandatory)][string]$Name
    )

    $nodes = @($Project.SelectNodes("/Project/PropertyGroup/$Name"))
    if ($nodes.Count -eq 0) {
        return ''
    }
    return ([string]$nodes[-1].InnerText).Trim()
}

function Test-TrueValue {
    param([AllowEmptyString()][string]$Value)
    return $Value.Equals('true', [StringComparison]::OrdinalIgnoreCase)
}

function Remove-CSharpNonCodeText {
    param([Parameter(Mandatory)][string]$SourceText)

    $nonCodePattern = '(?s)(?:\$*)"{3,}.*?"{3,}|(?:\$@|@\$|@)"(?:""|[^"])*"|\$?"(?:\\.|[^"\\])*"|/\*.*?\*/|//[^\r\n]*'
    return [regex]::Replace($SourceText, $nonCodePattern, ' ')
}

function Get-ProjectRole {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][xml]$Project
    )

    if ($ProjectPath -match '(^|/)src/Tests/') {
        if (Test-TrueValue (Get-ProjectProperty $Project 'IsTestProject')) { return 'Test' }
        return 'Unknown'
    }
    if ($ProjectPath -match '(^|/)src/Testing/IIoT\.Edge\.Testing\.') { return 'TestSupport' }
    if ($ProjectPath -match '(^|/)src/Testing/' -and
        (Test-TrueValue (Get-ProjectProperty $Project 'IsEdgePluginOwnedCompanion'))) { return 'TestSupport' }
    if ($ProjectPath -match '(^|/)src/Testing/IIoT\.Edge\.TestPlugin/' -and
        (Test-TrueValue (Get-ProjectProperty $Project 'IsEdgePluginTestFixture'))) { return 'TestFixture' }
    if ($ProjectPath -match '(^|/)src/Analyzers/') { return 'Analyzer' }
    if ($ProjectName -eq 'IIoT.Edge.Domain') { return 'Domain' }
    if ($ProjectName -eq 'IIoT.Edge.Application' -or $ProjectName.StartsWith('IIoT.Edge.Application.', [StringComparison]::Ordinal)) { return 'Application' }
    if ($ProjectName -eq 'IIoT.Edge.SharedKernel') { return 'SharedKernel' }
    if ($ProjectName -eq 'IIoT.Edge.UI.Shared') { return 'UiShared' }
    if ($ProjectName -eq 'IIoT.Edge.Module.Sdk') { return 'ModuleSdk' }
    if ($ProjectName.StartsWith('IIoT.Edge.Module.', [StringComparison]::Ordinal)) { return 'ConcretePlugin' }
    if ($ProjectName -eq 'IIoT.Edge.Presentation.VisualTestData') { return 'VisualTestData' }
    if ($ProjectName.StartsWith('IIoT.Edge.Presentation.', [StringComparison]::Ordinal)) { return 'Presentation' }
    if ($ProjectName.StartsWith('IIoT.Edge.Infrastructure.', [StringComparison]::Ordinal)) { return 'Infrastructure' }
    if ($ProjectName -eq 'IIoT.Edge.RuntimeLayoutSync') { return 'Tool' }
    if ($ProjectName.StartsWith('IIoT.Edge.Host.', [StringComparison]::Ordinal) -or
        $ProjectName -in @('IIoT.Edge.Shell', 'IIoT.Edge.Launcher', 'IIoT.Edge.Installer')) { return 'Host' }
    return 'Unknown'
}

function Test-IsExactDebugCondition {
    param([AllowEmptyString()][string]$Condition)
    if ([string]::IsNullOrWhiteSpace($Condition)) { return $false }
    $compact = [regex]::Replace($Condition, '\s+', '')
    return $compact -in @(
        "'`$(Configuration)'=='Debug'",
        '"$(Configuration)"=="Debug"',
        "'`$(Configuration)'==`"Debug`"",
        '"$(Configuration)"==''Debug''')
}

function Test-IsActiveEdge {
    param([AllowEmptyString()][string]$Condition)
    if ([string]::IsNullOrWhiteSpace($Condition)) { return $true }
    $compact = [regex]::Replace($Condition, '\s+', '')
    if ($compact -match '^(?:''\$\(Configuration\)''|"\$\(Configuration\)")(?<operator>==|!=)(?:''(?<value>Debug|Release)''|"(?<value>Debug|Release)")$') {
        $equals = $Configuration.Equals($Matches['value'], [StringComparison]::OrdinalIgnoreCase)
        if ($Matches['operator'] -eq '==') { return $equals }
        return -not $equals
    }

    # Compound and unknown conditions remain visible; only exact simple configuration predicates may hide an edge.
    return $true
}

function Test-IsForbiddenAnalyzerSourceEdge {
    param([Parameter(Mandatory)][string]$TargetPath)

    return [IO.Path]::GetFileNameWithoutExtension($TargetPath) -in @(
        'IIoT.Edge.Module.Analyzers',
        'IIoT.Edge.Architecture.Analyzers')
}

function Test-IsArchitectureDiagnosticText {
    param([AllowEmptyString()][string]$Value)
    return $Value -match [string]$architectureCatalog.CompilerIdPattern
}

function Test-ContainsArchitectureSeverityConfiguration {
    param([AllowEmptyString()][string]$Value)

    return $Value -match "(?im)^\s*dotnet_diagnostic\.(?:$($architectureCatalog.CompilerIdAlternation))\.severity\s*=" -or
        $Value -match '(?im)^\s*dotnet_analyzer_diagnostic\.category-IIoT\.Architecture\.severity\s*='
}

function Test-ContainsArchitecturePragmaSuppression {
    param([Parameter(Mandatory)][string]$SourceCode)

    foreach ($pragma in [regex]::Matches(
        $SourceCode,
        '(?im)^\s*#pragma\s+warning\s+disable(?<ids>[^\r\n]*)')) {
        $ids = $pragma.Groups['ids'].Value.Trim()
        if ([string]::IsNullOrWhiteSpace($ids)) { return $true }
        foreach ($id in $ids.Split(
            [char[]]@(',', ' ', "`t"),
            [StringSplitOptions]::RemoveEmptyEntries)) {
            if ($architectureCompilerIds.Contains($id.Trim())) { return $true }
        }
    }
    return $false
}

function Test-ContainsRealSuppressMessageAttribute {
    param(
        [Parameter(Mandatory)][object]$SyntaxRoot,
        [Parameter(Mandatory)][object]$SemanticModel
    )

    foreach ($attribute in @($SyntaxRoot.DescendantNodes() | Where-Object {
        $_.GetType().Name -eq 'AttributeSyntax'
    })) {
        $symbol = $SemanticModel.GetSymbolInfo($attribute).Symbol
        $attributeType = if ($null -ne $symbol) {
            $symbol.ContainingType
        } else {
            $SemanticModel.GetTypeInfo($attribute).Type
        }
        if ($null -eq $attributeType -or
            ([string]$attributeType) -cne 'System.Diagnostics.CodeAnalysis.SuppressMessageAttribute') {
            continue
        }
        foreach ($argument in @($attribute.ArgumentList.Arguments)) {
            $constant = $SemanticModel.GetConstantValue($argument.Expression)
            # A referenced-project const can leave constructor overload resolution incomplete in
            # this source-only scan. Once the exact system attribute type is known, an unresolved
            # argument must fail closed because it can carry a mandatory architecture ID.
            if (-not $constant.HasValue -or
                ($constant.Value -is [string] -and
                 ([string]$constant.Value) -match [string]$architectureCatalog.CompilerIdPattern)) {
                return $true
            }
        }
    }
    return $false
}

function Add-Finding {
    param(
        [Parameter(Mandatory)][string]$RuleId,
        [Parameter(Mandatory)][string]$Message
    )
    $key = "$RuleId|$Message"
    if ($findingKeys.Add($key)) {
        $findings.Add("$RuleId $Message")
    }
}

function Expand-ProjectPattern {
    param(
        [Parameter(Mandatory)][object]$ProjectInfo,
        [Parameter(Mandatory)][string]$Pattern
    )

    if ($Pattern.IndexOfAny([char[]]'*?') -lt 0) {
        return [string[]]@((Resolve-FullPath -BasePath ($ProjectInfo.Directory) -PathValue $Pattern))
    }

    $absolutePattern = (Join-Path $ProjectInfo.Directory $Pattern).Replace('\', '/')
    $patternRegex = [regex]::Escape($absolutePattern)
    $patternRegex = $patternRegex.Replace('\*\*', '.*')
    $patternRegex = $patternRegex.Replace('\*', '[^/]*')
    $patternRegex = $patternRegex.Replace('\?', '[^/]')
    $patternRegex = '^' + $patternRegex + '$'
    return [string[]]@(Get-ChildItem $RepositoryRoot -Recurse -Filter '*.csproj' -File -Force |
        Where-Object { (Get-RepositoryPath $_.FullName) -notmatch '(^|/)(?:bin|obj)(?:/|$)' } |
        Where-Object { $_.FullName.Replace('\', '/') -match $patternRegex } |
        ForEach-Object { $_.FullName } |
        Sort-Object -Unique)
}

function Resolve-ProjectExpression {
    param(
        [Parameter(Mandatory)][object]$ProjectInfo,
        [Parameter(Mandatory)][string]$Expression
    )

    $results = [System.Collections.Generic.List[string]]::new()
    foreach ($part in $Expression.Split(';', [StringSplitOptions]::RemoveEmptyEntries)) {
        $candidate = $part.Trim()
        if ($candidate -match '^@\(([^)]+)\)$') {
            $itemName = $Matches[1]
            $itemNodes = @($ProjectInfo.Xml.SelectNodes("/Project/ItemGroup/$itemName"))
            foreach ($itemNode in $itemNodes) {
                $include = ([System.Xml.XmlElement]$itemNode).GetAttribute('Include').Trim()
                if (-not [string]::IsNullOrWhiteSpace($include)) {
                    foreach ($expandedPath in @(Expand-ProjectPattern -ProjectInfo $ProjectInfo -Pattern $include)) {
                        $results.Add($expandedPath)
                    }
                }
            }
            continue
        }

        if ($candidate -match '^\$\(([^)]+)\)$') {
            $propertyName = $Matches[1]
            $candidate = Get-ProjectProperty -Project ($ProjectInfo.Xml) -Name $propertyName
            if ([string]::IsNullOrWhiteSpace($candidate)) {
                Add-Finding 'WSARCH004' "$($ProjectInfo.RelativePath) references missing MSBuild project property '$propertyName'."
                continue
            }
        }

        $candidate = $candidate.Replace('$(MSBuildThisFileDirectory)', $ProjectInfo.Directory + [IO.Path]::DirectorySeparatorChar)
        if ($candidate -match "^\$\(\[System\.IO\.Path\]::GetFullPath\('(.+)'\)\)$") {
            $candidate = $Matches[1]
        }

        if ($candidate -match '\$\(' -or $candidate -match '%\(') {
            Add-Finding 'WSARCH004' "$($ProjectInfo.RelativePath) contains an unresolved hidden MSBuild project edge '$candidate'. Register a concrete item/path in the graph ledger."
            continue
        }
        foreach ($expandedPath in @(Expand-ProjectPattern -ProjectInfo $ProjectInfo -Pattern $candidate)) {
            $results.Add($expandedPath)
        }
    }
    return [string[]]$results
}

function Test-IsAllowedDirectEdge {
    param(
        [Parameter(Mandatory)][object]$Source,
        [Parameter(Mandatory)][object]$Target,
        [Parameter(Mandatory)][object]$Edge
    )

    switch ($Source.Role) {
        'Analyzer' { return $false }
        'Domain' { return $Target.Role -eq 'SharedKernel' }
        'Application' { return $Target.Role -in @('Domain', 'SharedKernel') }
        'SharedKernel' { return $false }
        'UiShared' { return $Target.Role -eq 'SharedKernel' }
        'ModuleSdk' { return $Target.Role -in @('Application', 'SharedKernel') }
        'Infrastructure' {
            if ($Target.Role -eq 'Application') { return $true }
            if ($Source.Name -in @('IIoT.Edge.Infrastructure.Integration', 'IIoT.Edge.Infrastructure.Update') -and
                $Target.Name -eq 'IIoT.Edge.Infrastructure.CloudClient') { return $true }
            if ($Source.Name -eq 'IIoT.Edge.Infrastructure.Update' -and $Target.Role -eq 'SharedKernel') { return $true }
            return $false
        }
        'Presentation' {
            if ($Target.Role -in @('Application', 'UiShared', 'SharedKernel')) { return $true }
            return $Source.Name -eq 'IIoT.Edge.Presentation.Navigation' -and
                   $Target.Name -eq 'IIoT.Edge.Presentation.Panels'
        }
        'VisualTestData' { return $Target.Role -eq 'Application' }
        'ConcretePlugin' {
            if ($Target.Role -in @('Application', 'ModuleSdk', 'SharedKernel', 'UiShared')) { return $true }
            return $Target.Name -eq 'IIoT.Edge.Presentation.Navigation'
        }
        'Host' {
            if ($Target.Role -in @('Test', 'TestFixture', 'ConcretePlugin', 'Analyzer')) { return $false }
            if ($Source.Name -eq 'IIoT.Edge.Launcher' -and $Target.Name -eq 'IIoT.Edge.Shell') {
                return $Edge.ReferenceOutputAssembly.Equals('false', [StringComparison]::OrdinalIgnoreCase)
            }
            return $true
        }
        'Tool' { return $false }
        'TestSupport' { return $Target.Role -notin @('Unknown', 'Test', 'TestFixture', 'Analyzer') }
        'TestFixture' { return $Target.Role -in @('Application', 'ModuleSdk', 'SharedKernel', 'UiShared', 'TestSupport') }
        'Test' { return Test-IsAllowedTestEdge -Source $Source -Target $Target }
        default { return $false }
    }
}

function Test-IsAllowedTestEdge {
    param(
        [Parameter(Mandatory)][object]$Source,
        [Parameter(Mandatory)][object]$Target
    )

    if ($Target.Role -in @('Unknown', 'Test')) { return $false }

    switch ($Source.TestKind) {
        'Aggregate' { return $Target.Role -in @('Domain', 'SharedKernel', 'TestSupport') }
        'Application' { return $Target.Role -in @('Application', 'Domain', 'SharedKernel', 'TestSupport') }
        'Architecture' { return $Target.Role -in @('Analyzer', 'TestSupport') }
        'Contract' { return $Target.Role -in @('Application', 'Infrastructure', 'SharedKernel', 'TestSupport') }
        'Unit' { return $Target.Role -in @('Application', 'Domain', 'Infrastructure', 'Host', 'SharedKernel', 'TestSupport') }
        'Deployment' { return $Target.Role -in @('Host', 'Tool', 'SharedKernel', 'TestSupport') }
        'UI' { return $Target.Role -in @('Application', 'Host', 'Infrastructure', 'Presentation', 'VisualTestData', 'UiShared', 'SharedKernel', 'TestSupport') }
        'Conformance' {
            if ($Target.Role -eq 'ConcretePlugin') {
                return $Source.Name -in @(
                    'IIoT.Edge.Module.Homogenization.ConformanceTests',
                    'IIoT.Edge.Module.Homogenization.ConformanceFilesystemTests')
            }
            return $Target.Role -in @('Application', 'Host', 'Infrastructure', 'ModuleSdk', 'TestFixture', 'TestSupport', 'SharedKernel', 'UiShared')
        }
        'Workflow' {
            if ($Target.Role -eq 'ConcretePlugin') {
                return $Source.Name -in @(
                    'IIoT.Edge.Module.Homogenization.WorkflowTests',
                    'IIoT.Edge.Module.Homogenization.WorkflowFilesystemTests')
            }
            return $Target.Role -in @('Application', 'Host', 'Infrastructure', 'ModuleSdk', 'SharedKernel', 'TestSupport')
        }
        'Persistence' { return $Target.Role -in @('Application', 'Host', 'Infrastructure', 'SharedKernel', 'TestSupport') }
        'Integration' { return $Target.Role -in @('Application', 'Host', 'Infrastructure', 'ModuleSdk', 'TestFixture', 'SharedKernel', 'TestSupport') }
        default { return $false }
    }
}

function Get-ReachableTestAsset {
    param([Parameter(Mandatory)][object]$Start)

    $visited = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $queue = [System.Collections.Generic.Queue[object]]::new()
    $queue.Enqueue($Start)
    [void]$visited.Add($Start.FullPath)
    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        foreach ($edge in @($current.ActiveEdges)) {
            $target = $null
            if (-not $projectsByPath.TryGetValue($edge.TargetPath, [ref]$target)) { continue }
            if ($target.Role -in @('Test', 'TestFixture', 'TestSupport')) { return $target }
            if ($visited.Add($target.FullPath)) { $queue.Enqueue($target) }
        }
    }
    return $null
}

function Visit-Project {
    param(
        [Parameter(Mandatory)][object]$ProjectInfo,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Stack
    )

    if ($visitState[$ProjectInfo.FullPath] -eq 2) { return }
    if ($visitState[$ProjectInfo.FullPath] -eq 1) {
        $cycleStart = $Stack.IndexOf($ProjectInfo.FullPath)
        $cycle = @($Stack.GetRange($cycleStart, $Stack.Count - $cycleStart)) + $ProjectInfo.FullPath
        Add-Finding 'WSARCH001' "project/MSBuild cycle: $((@($cycle | ForEach-Object { Get-RepositoryPath $_ })) -join ' -> ')"
        return
    }

    $visitState[$ProjectInfo.FullPath] = 1
    $Stack.Add($ProjectInfo.FullPath)
    foreach ($edge in @($ProjectInfo.ActiveEdges)) {
        $target = $null
        if ($projectsByPath.TryGetValue($edge.TargetPath, [ref]$target)) {
            Visit-Project $target $Stack
        }
    }
    $Stack.RemoveAt($Stack.Count - 1)
    $visitState[$ProjectInfo.FullPath] = 2
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

$catalogRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$architectureCatalog = & (Join-Path $PSScriptRoot 'Get-EdgeArchitectureDiagnosticCatalog.ps1') `
    -RepositoryRoot $catalogRepositoryRoot `
    -AnalyzerPackageRoot $AnalyzerPackageRoot
if ((Split-Path ([string]$architectureCatalog.AnalyzerPackageRoot) -Leaf) -cne '2.0.0') {
    throw "WSARCH006 resolved Edge Analyzer package must remain pinned to 2.0.0: $($architectureCatalog.AnalyzerPackageRoot)"
}
$architectureCompilerIds = [System.Collections.Generic.HashSet[string]]::new(
    [string[]]$architectureCatalog.CompilerIds,
    [StringComparer]::OrdinalIgnoreCase)

$dotnetCommandPath = (Get-Command dotnet).Source
$dotnetExecutable = (Get-Item $dotnetCommandPath).Target
if ([string]::IsNullOrWhiteSpace($dotnetExecutable)) {
    $dotnetExecutable = $dotnetCommandPath
}
$sdkDirectory = Join-Path (Split-Path $dotnetExecutable -Parent) "sdk/$(dotnet --version)"
$microsoftBuildAssembly = Join-Path $sdkDirectory 'Microsoft.Build.dll'
if (-not (Test-Path $microsoftBuildAssembly -PathType Leaf)) {
    throw "WSARCH004 Microsoft.Build evaluation assembly does not exist: $microsoftBuildAssembly"
}
[void][Reflection.Assembly]::LoadFrom($microsoftBuildAssembly)
# PowerShell already hosts a matching Roslyn pair for its own parser/compiler. Loading
# the SDK Roslyn binaries into the default context can bind a different minor version
# than PowerShell and fail before the graph is inspected.
if ($null -eq ('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type])) {
    throw 'WSARCH004 PowerShell Roslyn CSharpSyntaxTree type is unavailable.'
}
$globalProperties = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$globalProperties['Configuration'] = $Configuration
$projectCollection = [Microsoft.Build.Evaluation.ProjectCollection]::new($globalProperties)

$projectPaths = [System.Collections.Generic.List[string]]::new()
if (-not [string]::IsNullOrWhiteSpace($SolutionPath)) {
    $resolvedSolution = Resolve-FullPath $RepositoryRoot $SolutionPath
    if (-not (Test-Path $resolvedSolution -PathType Leaf)) {
        throw "WSARCH004 solution registry does not exist: $resolvedSolution"
    }
    [xml]$solution = Get-Content $resolvedSolution -Raw
    foreach ($projectNode in @($solution.SelectNodes('//Project'))) {
        $projectPaths.Add((Resolve-FullPath (Split-Path $resolvedSolution -Parent) ([System.Xml.XmlElement]$projectNode).GetAttribute('Path')))
    }
} else {
    foreach ($projectFile in @(Get-ChildItem $RepositoryRoot -Recurse -Filter '*.csproj' -File -Force |
        Where-Object { (Get-RepositoryPath $_.FullName) -notmatch '(^|/)(?:bin|obj)(?:/|$)' })) {
        $projectPaths.Add($projectFile.FullName)
    }
}

$findingKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$findings = [System.Collections.Generic.List[string]]::new()
$projectsByPath = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
$projects = [System.Collections.Generic.List[object]]::new()
$inspectedAnalyzerConfigPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$inspectedMsBuildDeclarationPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

function Test-ArchitectureAnalyzerConfigFile {
    param([Parameter(Mandatory)][string]$ConfigPath)

    $fullPath = [IO.Path]::GetFullPath($ConfigPath)
    if (-not $inspectedAnalyzerConfigPaths.Add($fullPath) -or
        -not (Test-Path $fullPath -PathType Leaf)) {
        return
    }

    $configText = Get-Content $fullPath -Raw
    if (Test-ContainsArchitectureSeverityConfiguration $configText) {
        Add-Finding 'WSARCH006' "$(Get-RepositoryPath $fullPath) configures mandatory Edge architecture diagnostic severity; all IIoT.Architecture descriptors are build-blocking and NotConfigurable."
    }
}

function Test-ArchitectureMsBuildDeclarationFile {
    param([Parameter(Mandatory)][string]$DeclarationPath)

    $fullPath = [IO.Path]::GetFullPath($DeclarationPath)
    if (-not $inspectedMsBuildDeclarationPaths.Add($fullPath) -or
        -not (Test-Path $fullPath -PathType Leaf)) {
        return
    }

    [xml]$declaration = Get-Content $fullPath -Raw
    foreach ($property in @($declaration.SelectNodes(
        '//*[local-name()="RunAnalyzers" or local-name()="RunAnalyzersDuringBuild" or local-name()="NoWarn" or local-name()="WarningsNotAsErrors"]'))) {
        $name = [string]$property.LocalName
        $value = ([string]$property.InnerText).Trim()
        if ($name -in @('RunAnalyzers', 'RunAnalyzersDuringBuild')) {
            if ($value.Equals('false', [StringComparison]::OrdinalIgnoreCase)) {
                Add-Finding 'WSARCH006' "$(Get-RepositoryPath $fullPath) contains raw/conditional/target-time $name=false; mandatory analyzers cannot be disabled in any build path."
            }
            continue
        }
        if (Test-IsArchitectureDiagnosticText $value) {
            Add-Finding 'WSARCH006' "$(Get-RepositoryPath $fullPath) contains raw/conditional/target-time $name for a mandatory Edge architecture diagnostic."
        }
    }
}

foreach ($analyzerConfig in @(Get-ChildItem $RepositoryRoot -Recurse -File -Force |
    Where-Object {
        $_.Name -in @('.editorconfig', '.globalconfig') -and
        (Get-RepositoryPath $_.FullName) -notmatch '(^|/)(?:bin|obj)(?:/|$)'
    })) {
    Test-ArchitectureAnalyzerConfigFile $analyzerConfig.FullName
}

foreach ($declarationFile in @(Get-ChildItem $RepositoryRoot -Recurse -File -Force |
    Where-Object {
        $_.Extension -in @('.csproj', '.props', '.targets', '.proj') -and
        (Get-RepositoryPath $_.FullName) -notmatch '(^|/)(?:bin|obj)(?:/|$)'
    })) {
    Test-ArchitectureMsBuildDeclarationFile $declarationFile.FullName
}

foreach ($projectPath in @($projectPaths | Sort-Object -Unique)) {
    if (-not (Test-Path $projectPath -PathType Leaf)) {
        Add-Finding 'WSARCH004' "registered project does not exist: $(Get-RepositoryPath $projectPath)"
        continue
    }
    [xml]$projectXml = Get-Content $projectPath -Raw
    $evaluatedProject = [Microsoft.Build.Evaluation.Project]::new(
        $projectPath,
        $globalProperties,
        'Current',
        $projectCollection)
    $name = [IO.Path]::GetFileNameWithoutExtension($projectPath)
    $relative = Get-RepositoryPath $projectPath
    $info = [pscustomobject]@{
        FullPath = $projectPath
        RelativePath = $relative
        Directory = Split-Path $projectPath -Parent
        Name = $name
        Role = Get-ProjectRole $relative $name $projectXml
        TestKind = Get-ProjectProperty -Project $projectXml -Name 'TestKind'
        TestRuntime = Get-ProjectProperty -Project $projectXml -Name 'TestRuntime'
        EffectiveAssemblyName = $evaluatedProject.GetPropertyValue('AssemblyName')
        EffectiveIsTestProject = $evaluatedProject.GetPropertyValue('IsTestProject')
        Xml = $projectXml
        EvaluatedProject = $evaluatedProject
        ActiveEdges = [System.Collections.Generic.List[object]]::new()
        CompileSources = [System.Collections.Generic.List[object]]::new()
    }
    if ($projectsByPath.ContainsKey($projectPath)) {
        Add-Finding 'WSARCH004' "duplicate project registry path: $relative"
        continue
    }
    $projectsByPath.Add($projectPath, $info)
    $projects.Add($info)
}

foreach ($project in $projects) {
    if ($project.Role -eq 'Unknown') {
        Add-Finding 'WSARCH004' "$($project.RelativePath) has no registered project role."
    }

    $canonicalTestPath = "src/Tests/$($project.Name)/$($project.Name).csproj"
    $canonicalSupportPath = "src/Testing/$($project.Name)/$($project.Name).csproj"
    $isEffectiveTest = Test-TrueValue $project.EffectiveIsTestProject
    $isTestLikeAssemblyName = $project.EffectiveAssemblyName -match '(?i)(?:Tests?|Testing|TestKit)$|(?:^|\.)(?:Tests?|Testing|TestKit)(?:\.|$)'
    if ($project.Role -eq 'Test') {
        if ($project.RelativePath -ne $canonicalTestPath -or -not $isEffectiveTest -or
            $project.EffectiveAssemblyName -ne $project.Name -or -not $isTestLikeAssemblyName) {
            Add-Finding 'WSARCH005' "$($project.RelativePath) test identity/path mismatch: AssemblyName='$($project.EffectiveAssemblyName)', IsTestProject='$($project.EffectiveIsTestProject)', expectedPath='$canonicalTestPath'."
        }
    } elseif ($project.Role -eq 'TestSupport') {
        $isPluginOwnedCompanion = Test-TrueValue (Get-ProjectProperty -Project ($project.Xml) -Name 'IsEdgePluginOwnedCompanion')
        $hasValidSupportIdentity = $project.Name.StartsWith('IIoT.Edge.Testing.', [StringComparison]::Ordinal) -or
            ($isPluginOwnedCompanion -and
             $project.Name -match '^IIoT\.Edge\.Module\.[A-Za-z0-9]+\.Companion$')
        if ($project.RelativePath -ne $canonicalSupportPath -or $isEffectiveTest -or
            $project.EffectiveAssemblyName -ne $project.Name -or
            -not $hasValidSupportIdentity) {
            Add-Finding 'WSARCH005' "$($project.RelativePath) TestSupport identity/path mismatch: AssemblyName='$($project.EffectiveAssemblyName)', IsTestProject='$($project.EffectiveIsTestProject)'."
        }
    } elseif ($project.Role -eq 'TestFixture') {
        if ($project.RelativePath -ne 'src/Testing/IIoT.Edge.TestPlugin/IIoT.Edge.TestPlugin.csproj' -or
            $project.Name -ne 'IIoT.Edge.TestPlugin' -or $isEffectiveTest -or
            $project.EffectiveAssemblyName -ne $project.Name) {
            Add-Finding 'WSARCH005' "$($project.RelativePath) plugin fixture identity/path mismatch."
        }
    } elseif ($isEffectiveTest -or $isTestLikeAssemblyName) {
        Add-Finding 'WSARCH005' "$($project.RelativePath) production role '$($project.Role)' cannot use test identity: AssemblyName='$($project.EffectiveAssemblyName)', IsTestProject='$($project.EffectiveIsTestProject)'."
    }

    $approvedAnalyzerReferenceCount = 0
    foreach ($package in @($project.EvaluatedProject.GetItems('PackageReference'))) {
        $packageName = $package.EvaluatedInclude.Trim()
        if ($packageName.Equals('IIoT.Edge.Module.Analyzers', [StringComparison]::Ordinal)) {
            $expectedTargetsFile = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Directory.Build.targets'))
            $definingPath = [IO.Path]::GetFullPath($package.GetMetadataValue('DefiningProjectFullPath'))
            $includeAssets = $package.GetMetadataValue('IncludeAssets')
            $includeAssetNames = @(
                $includeAssets.Split(';', [StringSplitOptions]::RemoveEmptyEntries) |
                    ForEach-Object { $_.Trim() }
            )
            $isApprovedAnalyzerPackage =
                $package.GetMetadataValue('PrivateAssets').Equals('all', [StringComparison]::OrdinalIgnoreCase) -and
                $package.GetMetadataValue('GeneratePathProperty').Equals('true', [StringComparison]::OrdinalIgnoreCase) -and
                $includeAssetNames -contains 'analyzers' -and
                $definingPath.Equals($expectedTargetsFile, [StringComparison]::OrdinalIgnoreCase)
            if ($isApprovedAnalyzerPackage) {
                $approvedAnalyzerReferenceCount++
            } else {
                Add-Finding 'WSARCH006' "$($project.RelativePath) has an unapproved Edge Analyzer package declaration."
            }
        }
        if ($project.Role -notin @('Test', 'Analyzer') -and
            ($packageName -match '(?i)(^xunit|Test\.Sdk|TestPlatform|Moq|NSubstitute|FluentAssertions)')) {
            $definingPath = $package.GetMetadataValue('DefiningProjectFullPath')
            Add-Finding 'WSARCH003' "$($project.RelativePath) references test package '$packageName' from $(Get-RepositoryPath $definingPath)."
        }
    }

    $requiresArchitectureAnalyzer = $project.Role -notin @('Test', 'Analyzer', 'TestFixture')
    if ($requiresArchitectureAnalyzer) {
        foreach ($propertyName in @('RunAnalyzers', 'RunAnalyzersDuringBuild')) {
            $propertyValue = $project.EvaluatedProject.GetPropertyValue($propertyName).Trim()
            if ($propertyValue.Equals('false', [StringComparison]::OrdinalIgnoreCase)) {
                Add-Finding 'WSARCH006' "$($project.RelativePath) disables mandatory analyzers through $propertyName=false."
            }
        }
        foreach ($propertyName in @('NoWarn', 'WarningsNotAsErrors')) {
            $propertyValue = $project.EvaluatedProject.GetPropertyValue($propertyName)
            if (Test-IsArchitectureDiagnosticText $propertyValue) {
                Add-Finding 'WSARCH006' "$($project.RelativePath) suppresses/downgrades mandatory architecture diagnostics through $propertyName."
            }
        }
        foreach ($itemName in @('AnalyzerConfigFiles', 'EditorConfigFiles', 'GlobalAnalyzerConfigFiles')) {
            foreach ($configItem in @($project.EvaluatedProject.GetItems($itemName))) {
                $configPath = $configItem.GetMetadataValue('FullPath')
                if ([string]::IsNullOrWhiteSpace($configPath)) {
                    $configPath = Resolve-FullPath $project.Directory $configItem.EvaluatedInclude
                }
                Test-ArchitectureAnalyzerConfigFile $configPath
            }
        }
        foreach ($propertyName in @('AnalyzerConfigFiles', 'EditorConfigFiles', 'GlobalAnalyzerConfigFiles')) {
            foreach ($configValue in @($project.EvaluatedProject.GetPropertyValue($propertyName).Split(
                ';',
                [StringSplitOptions]::RemoveEmptyEntries))) {
                $expandedConfigValue = $project.EvaluatedProject.ExpandString($configValue.Trim())
                if ($expandedConfigValue -notmatch '[@$]\(') {
                    Test-ArchitectureAnalyzerConfigFile (Resolve-FullPath $project.Directory $expandedConfigValue)
                }
            }
        }
    }

    $registeredProjectReferencePaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in @($project.EvaluatedProject.GetItems('ProjectReference'))) {
        $targetPath = $item.GetMetadataValue('FullPath')
        if ([string]::IsNullOrWhiteSpace($targetPath)) {
            $targetPath = Resolve-FullPath $project.Directory $item.EvaluatedInclude
        } else {
            $targetPath = [IO.Path]::GetFullPath($targetPath)
        }

        if (Test-IsForbiddenAnalyzerSourceEdge -TargetPath $targetPath) {
            Add-Finding 'WSARCH006' "$($project.RelativePath) must consume the Edge Analyzer package, not its source project."
            continue
        }

        $edge = [pscustomobject]@{
            Kind = 'ProjectReference'
            TargetPath = $targetPath
            Condition = ''
            ReferenceOutputAssembly = $item.GetMetadataValue('ReferenceOutputAssembly')
        }

        $target = $null
        if ($projectsByPath.TryGetValue($targetPath, [ref]$target) -and $target.Role -eq 'VisualTestData') {
            $definingFile = $item.GetMetadataValue('DefiningProjectFullPath')
            [xml]$definingXml = Get-Content $definingFile -Raw
            $matchingNode = @($definingXml.SelectNodes('//ProjectReference')) |
                Where-Object {
                    $include = ([System.Xml.XmlElement]$_).GetAttribute('Include')
                    -not [string]::IsNullOrWhiteSpace($include) -and
                    (Resolve-FullPath (Split-Path $definingFile -Parent) $include) -eq $targetPath
                } |
                Select-Object -First 1
            $condition = if ($null -eq $matchingNode) { '' } else { ([System.Xml.XmlElement]$matchingNode).GetAttribute('Condition').Trim() }
            $isExactDebugEdge = $project.Name -eq 'IIoT.Edge.Host.Bootstrap' -and (Test-IsExactDebugCondition $condition)
            if (-not $isExactDebugEdge -and $project.Role -ne 'Test') {
                Add-Finding 'WSARCH003' "$($project.RelativePath) -> $($target.RelativePath) must be the exact Debug-only Host.Bootstrap edge; condition='$condition'."
            }
        }

        $project.ActiveEdges.Add($edge)
        [void]$registeredProjectReferencePaths.Add($targetPath)
    }


    if ($requiresArchitectureAnalyzer -and $approvedAnalyzerReferenceCount -ne 1) {
        Add-Finding 'WSARCH006' "$($project.RelativePath) must receive exactly one pinned Edge Analyzer reference; actual=$approvedAnalyzerReferenceCount."
    }
    if (-not $requiresArchitectureAnalyzer -and $approvedAnalyzerReferenceCount -ne 0) {
        Add-Finding 'WSARCH006' "$($project.RelativePath) Analyzer exclusion does not match its validated role '$($project.Role)'."
    }

    $ownedCompileItems = $project.CompileSources
    foreach ($compileItem in @($project.EvaluatedProject.GetItems('Compile'))) {
        $compilePath = $compileItem.GetMetadataValue('FullPath')
        if ([string]::IsNullOrWhiteSpace($compilePath)) {
            $compilePath = Resolve-FullPath $project.Directory $compileItem.EvaluatedInclude
        } else {
            $compilePath = [IO.Path]::GetFullPath($compilePath)
        }

        $isInsideProject = Test-IsPathInside $compilePath $project.Directory
        if (-not $isInsideProject) {
            $ruleId = if ($project.Role -in @('Test', 'TestSupport', 'TestFixture')) { 'WSTEST003' } else { 'WSARCH007' }
            $definingPath = $compileItem.GetMetadataValue('DefiningProjectFullPath')
            Add-Finding $ruleId "$($project.RelativePath) compiles source outside its physical project root: $(Get-RepositoryPath $compilePath), declared by $(Get-RepositoryPath $definingPath)."
            continue
        }

        $relativeCompilePath = [IO.Path]::GetRelativePath($project.Directory, $compilePath)
        if ($relativeCompilePath -match '^(?:bin|obj)[/\\]') {
            $isExplicitGenerated =
                $compileItem.GetMetadataValue('AutoGen').Equals('true', [StringComparison]::OrdinalIgnoreCase) -or
                $compileItem.GetMetadataValue('Generated').Equals('true', [StringComparison]::OrdinalIgnoreCase) -or
                $compileItem.GetMetadataValue('DesignTime').Equals('true', [StringComparison]::OrdinalIgnoreCase)
            if (-not $isExplicitGenerated) {
                $ruleId = if ($project.Role -in @('Test', 'TestSupport', 'TestFixture')) { 'WSTEST003' } else { 'WSARCH007' }
                Add-Finding $ruleId "$($project.RelativePath) compiles a non-generated bin/obj source '$relativeCompilePath'."
            }
            continue
        }

        if (Test-Path $compilePath -PathType Leaf) {
            $ownedCompileItems.Add([pscustomobject]@{
                FullName = $compilePath
            })
        }
    }

    if ($requiresArchitectureAnalyzer) {
        $sourceDocuments = [System.Collections.Generic.List[object]]::new()
        foreach ($source in $ownedCompileItems) {
            $sourceText = Get-Content $source.FullName -Raw
            $sourceDocuments.Add([pscustomobject]@{
                FullName = $source.FullName
                SourceText = $sourceText
                SourceCode = Remove-CSharpNonCodeText $sourceText
            })
        }

        $realSuppressMessagePaths = [System.Collections.Generic.HashSet[string]]::new(
            [StringComparer]::OrdinalIgnoreCase)
        $hasSuppressMessageCandidate = @($sourceDocuments | Where-Object {
            $_.SourceCode -match '(?i)\bSuppressMessage(?:Attribute)?\b'
        }).Count -gt 0
        if ($hasSuppressMessageCandidate) {
            # SuppressMessage attempts in inactive preprocessor branches are still forbidden.
            # Replace every #if/#elif condition with an independent scan-only symbol, then
            # exhaustively activate the structural branches. This also reaches #if false and
            # contradictory conditions while aliases/fake attributes retain semantic identity.
            $suppressionScanDocuments = [System.Collections.Generic.List[object]]::new()
            $conditionalBranchSymbols = [System.Collections.Generic.List[string]]::new()
            $scanSymbolPrefix = "__IIOT_EDGE_SUPPRESSION_SCAN_$([Guid]::NewGuid().ToString('N'))"
            foreach ($source in $sourceDocuments) {
                $probeTree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
                    [string]$source.SourceText,
                    [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default,
                    [string]$source.FullName,
                    [Text.Encoding]::UTF8,
                    [Threading.CancellationToken]::None)
                $conditionReplacements = [System.Collections.Generic.List[object]]::new()
                foreach ($trivia in $probeTree.GetRoot().DescendantTrivia($null, $true)) {
                    if (-not $trivia.HasStructure) { continue }
                    $directive = $trivia.GetStructure()
                    if ($directive.GetType().Name -notin @(
                        'IfDirectiveTriviaSyntax',
                        'ElifDirectiveTriviaSyntax')) {
                        continue
                    }

                    $scanSymbol = "${scanSymbolPrefix}_$($conditionalBranchSymbols.Count)"
                    $conditionalBranchSymbols.Add($scanSymbol)
                    $conditionReplacements.Add([pscustomobject]@{
                        Start = [int]$directive.Condition.Span.Start
                        Length = [int]$directive.Condition.Span.Length
                        Symbol = $scanSymbol
                    })
                }

                $scanText = [Text.StringBuilder]::new([string]$source.SourceText)
                foreach ($replacement in @($conditionReplacements | Sort-Object Start -Descending)) {
                    [void]$scanText.Remove($replacement.Start, $replacement.Length)
                    [void]$scanText.Insert($replacement.Start, [string]$replacement.Symbol)
                }
                $suppressionScanDocuments.Add([pscustomobject]@{
                    FullName = $source.FullName
                    SourceText = $scanText.ToString()
                })
            }

            $maximumConditionalBranches = 10
            if ($conditionalBranchSymbols.Count -gt $maximumConditionalBranches) {
                Add-Finding 'WSARCH006' "$($project.RelativePath) contains SuppressMessage candidates across $($conditionalBranchSymbols.Count) conditional branches; exhaustive semantic suppression scanning is capped at $maximumConditionalBranches and therefore fails closed."
            } else {
                $valuationCount = 1 -shl $conditionalBranchSymbols.Count
                for ($valuation = 0; $valuation -lt $valuationCount; $valuation++) {
                    $activeSymbols = [System.Collections.Generic.List[string]]::new()
                    for ($symbolIndex = 0; $symbolIndex -lt $conditionalBranchSymbols.Count; $symbolIndex++) {
                        if (($valuation -band (1 -shl $symbolIndex)) -ne 0) {
                            $activeSymbols.Add($conditionalBranchSymbols[$symbolIndex])
                        }
                    }

                    $parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithPreprocessorSymbols(
                        [string[]]$activeSymbols)
                    $parsedDocuments = [System.Collections.Generic.List[object]]::new()
                    foreach ($source in $suppressionScanDocuments) {
                        $syntaxTree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
                            [string]$source.SourceText,
                            $parseOptions,
                            [string]$source.FullName,
                            [Text.Encoding]::UTF8,
                            [Threading.CancellationToken]::None)
                        $parsedDocuments.Add([pscustomobject]@{
                            FullName = $source.FullName
                            SyntaxTree = $syntaxTree
                            SyntaxRoot = $syntaxTree.GetRoot()
                        })
                    }

                    $suppressionCompilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
                        "EdgeArchitectureSuppressionScan_$($project.Name)_$valuation",
                        [Microsoft.CodeAnalysis.SyntaxTree[]]@($parsedDocuments | ForEach-Object { $_.SyntaxTree }),
                        [Microsoft.CodeAnalysis.MetadataReference[]]@(
                            [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile([object].Assembly.Location)),
                        [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new(
                            [Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary))
                    foreach ($parsedSource in $parsedDocuments) {
                        if (Test-ContainsRealSuppressMessageAttribute `
                            -SyntaxRoot $parsedSource.SyntaxRoot `
                            -SemanticModel ($suppressionCompilation.GetSemanticModel($parsedSource.SyntaxTree))) {
                            [void]$realSuppressMessagePaths.Add([string]$parsedSource.FullName)
                        }
                    }
                }
            }
        }
        foreach ($source in $sourceDocuments) {
            if ((Test-ContainsArchitecturePragmaSuppression $source.SourceCode) -or
                $realSuppressMessagePaths.Contains([string]$source.FullName)) {
                Add-Finding 'WSARCH006' "$(Get-RepositoryPath $source.FullName) suppresses mandatory Edge architecture diagnostics in source."
            }
        }
    }

    # Evaluated items alone omit false conditional declarations. Read the root and every
    # repository-owned import as well: only an exact Configuration equality/inequality may
    # hide a declaration; compound or unknown predicates remain visible (fail closed).
    $projectDeclarationFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$projectDeclarationFiles.Add($project.FullPath)
    foreach ($import in @($project.EvaluatedProject.Imports)) {
        $importPath = [IO.Path]::GetFullPath($import.ImportedProject.FullPath)
        if ($importPath.StartsWith(
            $RepositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
            [void]$projectDeclarationFiles.Add($importPath)
        }
    }

    foreach ($declarationFile in $projectDeclarationFiles) {
        [xml]$declarationXml = Get-Content $declarationFile -Raw
        foreach ($rawItem in @($declarationXml.SelectNodes('//ProjectReference'))) {
            $itemElement = [System.Xml.XmlElement]$rawItem
            $itemCondition = $itemElement.GetAttribute('Condition').Trim()
            $groupCondition = if ($itemElement.ParentNode -is [System.Xml.XmlElement]) {
                ([System.Xml.XmlElement]$itemElement.ParentNode).GetAttribute('Condition').Trim()
            } else { '' }
            $condition = @($groupCondition, $itemCondition) |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Join-String -Separator ' and '
            if (-not (Test-IsActiveEdge $condition)) { continue }

            $include = $itemElement.GetAttribute('Include').Trim()
            if ([string]::IsNullOrWhiteSpace($include)) { continue }
            $definingDirectory = Split-Path $declarationFile -Parent
            $include = $include.Replace(
                '$(MSBuildThisFileDirectory)',
                $definingDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar)
            $expandedInclude = $project.EvaluatedProject.ExpandString($include)
            if ($expandedInclude -match '%\(' -or $expandedInclude -match '@\(' -or $expandedInclude -match '\$\(') {
                Add-Finding 'WSARCH004' "$($project.RelativePath) contains unresolved ProjectReference '$expandedInclude' from $(Get-RepositoryPath $declarationFile)."
                continue
            }

            foreach ($candidate in $expandedInclude.Split(';', [StringSplitOptions]::RemoveEmptyEntries)) {
                $targetPath = Resolve-FullPath $project.Directory $candidate.Trim()
                if (Test-IsForbiddenAnalyzerSourceEdge -TargetPath $targetPath) {
                    Add-Finding 'WSARCH006' "$($project.RelativePath) declares a forbidden Edge Analyzer source ProjectReference in $(Get-RepositoryPath $declarationFile)."
                    continue
                }
                if ($registeredProjectReferencePaths.Add($targetPath)) {
                    $project.ActiveEdges.Add([pscustomobject]@{
                        Kind = 'ProjectReference'
                        TargetPath = $targetPath
                        Condition = $condition
                        ReferenceOutputAssembly = $itemElement.GetAttribute('ReferenceOutputAssembly').Trim()
                    })
                }
            }
        }
    }

    $projectInstance = $project.EvaluatedProject.CreateProjectInstance()
    foreach ($targetInstance in @($projectInstance.Targets.Values)) {
        foreach ($task in @($targetInstance.Children | Where-Object {
            $_.GetType().Name -eq 'ProjectTaskInstance' -and $_.Name -eq 'MSBuild'
        })) {
            $taskFile = [IO.Path]::GetFullPath($task.Location.File)
            if (-not $taskFile.StartsWith(
                $RepositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar,
                [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $condition = "$($targetInstance.Condition) $($task.Condition)"
            if (-not (Test-IsActiveEdge $condition)) { continue }
            $projectsExpression = $task.Parameters['Projects']
            if ([string]::IsNullOrWhiteSpace($projectsExpression)) {
                Add-Finding 'WSARCH004' "$($project.RelativePath) contains a repository MSBuild task with an empty Projects expression in $(Get-RepositoryPath $taskFile)."
                continue
            }

            if ($projectsExpression -match '%\(' -or
                ($projectsExpression -match '@\(' -and $projectsExpression -notmatch '^\s*@\([A-Za-z_][A-Za-z0-9_.-]*\)\s*$')) {
                Add-Finding 'WSARCH004' "$($project.RelativePath) contains a dynamic MSBuild Projects expression '$projectsExpression' in $(Get-RepositoryPath $taskFile)."
                continue
            }

            if ($projectsExpression -match '^\s*@\((?<itemName>[A-Za-z_][A-Za-z0-9_.-]*)\)\s*$') {
                $projectItems = @($project.EvaluatedProject.GetItems($Matches['itemName']))
                if ($projectItems.Count -eq 0) {
                    Add-Finding 'WSARCH004' "$($project.RelativePath) MSBuild Projects item '$projectsExpression' is unresolved or target-local in $(Get-RepositoryPath $taskFile)."
                    continue
                }
                $expandedProjects = ($projectItems | ForEach-Object {
                    $fullPath = $_.GetMetadataValue('FullPath')
                    if ([string]::IsNullOrWhiteSpace($fullPath)) { $_.EvaluatedInclude } else { $fullPath }
                }) -join ';'
            } else {
                $expandedProjects = $project.EvaluatedProject.ExpandString($projectsExpression)
            }
            if ([string]::IsNullOrWhiteSpace($expandedProjects)) {
                Add-Finding 'WSARCH004' "$($project.RelativePath) MSBuild Projects expression '$projectsExpression' evaluated empty in $(Get-RepositoryPath $taskFile)."
                continue
            }
            if ($expandedProjects -match '%\(' -or $expandedProjects -match '@\(' -or $expandedProjects -match '\$\(') {
                Add-Finding 'WSARCH004' "$($project.RelativePath) contains unresolved evaluated MSBuild edge '$expandedProjects' from $(Get-RepositoryPath $taskFile)."
                continue
            }

            foreach ($candidate in $expandedProjects.Split(';', [StringSplitOptions]::RemoveEmptyEntries)) {
                $targetPath = Resolve-FullPath $project.Directory $candidate.Trim()
                $project.ActiveEdges.Add([pscustomobject]@{
                    Kind = 'MSBuild'
                    TargetPath = $targetPath
                    Condition = $condition
                    ReferenceOutputAssembly = 'false'
                })
            }
        }
    }
}

foreach ($project in $projects) {
    foreach ($edge in @($project.ActiveEdges)) {
        $target = $null
        if (-not $projectsByPath.TryGetValue($edge.TargetPath, [ref]$target)) {
            Add-Finding 'WSARCH004' "$($project.RelativePath) has hidden/unregistered $($edge.Kind) edge to $(Get-RepositoryPath $edge.TargetPath)."
            continue
        }

        if ($project.Role -notin @('Test', 'TestFixture', 'TestSupport', 'Analyzer') -and
            $target.Role -in @('Test', 'TestFixture', 'TestSupport')) {
            Add-Finding 'WSARCH003' "$($project.RelativePath) -> $($target.RelativePath) is a production-to-test edge."
            continue
        }

        if (-not (Test-IsAllowedDirectEdge $project $target $edge)) {
            $ruleId = if ($project.Role -eq 'ConcretePlugin' -and $target.Role -eq 'ConcretePlugin') {
                'PLUG002'
            } elseif ($project.Role -eq 'ConcretePlugin') {
                'PLUG001'
            } else {
                'WSARCH004'
            }
            Add-Finding $ruleId "$($project.RelativePath) [$($project.Role)] -> $($target.RelativePath) [$($target.Role)] via $($edge.Kind) is not in the approved role matrix."
        }
    }

    if ($project.Role -notin @('Test', 'TestFixture', 'TestSupport', 'Analyzer')) {
        $reachable = Get-ReachableTestAsset $project
        if ($null -ne $reachable) {
            Add-Finding 'WSARCH003' "$($project.RelativePath) reaches test asset $($reachable.RelativePath) transitively."
        }
    }

    if ($project.Role -eq 'ModuleSdk') {
        $role = Get-ProjectProperty -Project ($project.Xml) -Name 'EdgeModuleRole'
        $isPlugin = Get-ProjectProperty -Project ($project.Xml) -Name 'IsEdgePluginModule'
        $isPackable = Get-ProjectProperty -Project ($project.Xml) -Name 'IsPackable'
        if ($role -ne 'Sdk' -or -not $isPlugin.Equals('false', [StringComparison]::OrdinalIgnoreCase) -or
            -not $isPackable.Equals('false', [StringComparison]::OrdinalIgnoreCase)) {
            Add-Finding 'PLUG004' "$($project.RelativePath) SDK metadata must be EdgeModuleRole=Sdk, IsEdgePluginModule=false, IsPackable=false."
        }
    }

    if ($project.Role -eq 'ConcretePlugin') {
        $role = Get-ProjectProperty -Project ($project.Xml) -Name 'EdgeModuleRole'
        $isPlugin = Get-ProjectProperty -Project ($project.Xml) -Name 'IsEdgePluginModule'
        $moduleId = Get-ProjectProperty -Project ($project.Xml) -Name 'PluginModuleId'
        $isPackable = Get-ProjectProperty -Project ($project.Xml) -Name 'IsPackable'
        if ($project.Name.EndsWith('.Shared', [StringComparison]::OrdinalIgnoreCase)) {
            Add-Finding 'PLUG002' "$($project.RelativePath) is a forbidden plugin-family Shared business project."
        }
        if ($role -ne 'Entry' -or -not $isPlugin.Equals('true', [StringComparison]::OrdinalIgnoreCase) -or
            [string]::IsNullOrWhiteSpace($moduleId) -or -not $isPackable.Equals('true', [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path (Join-Path $project.Directory 'plugin.json') -PathType Leaf)) {
            Add-Finding 'PLUG004' "$($project.RelativePath) entry metadata/plugin.json must declare EdgeModuleRole=Entry, IsEdgePluginModule=true, PluginModuleId, IsPackable=true."
        }
    }

    if ($project.Role -eq 'TestFixture') {
        $role = Get-ProjectProperty -Project ($project.Xml) -Name 'EdgeModuleRole'
        $isPlugin = Get-ProjectProperty -Project ($project.Xml) -Name 'IsEdgePluginModule'
        $moduleId = Get-ProjectProperty -Project ($project.Xml) -Name 'PluginModuleId'
        $isPackable = Get-ProjectProperty -Project ($project.Xml) -Name 'IsPackable'
        if ($role -ne 'Fixture' -or -not $isPlugin.Equals('true', [StringComparison]::OrdinalIgnoreCase) -or
            [string]::IsNullOrWhiteSpace($moduleId) -or -not $isPackable.Equals('false', [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path (Join-Path $project.Directory 'plugin.json') -PathType Leaf)) {
            Add-Finding 'PLUG004' "$($project.RelativePath) fixture metadata/plugin.json must declare EdgeModuleRole=Fixture, IsEdgePluginModule=true, PluginModuleId, IsPackable=false."
        }
    }

    if ($project.Role -eq 'Test') {
        $metadataNames = @(
            'TestKind', 'TestRuntime', 'TestRuntimeDependencies', 'TestRunnerMode', 'TestCadence',
            'TestCapability', 'TestRisk', 'TestConcern', 'TestProfile', 'TestOwner', 'TestRuleId', 'TestRequired')
        foreach ($metadataName in $metadataNames) {
            $metadataNodes = @($project.Xml.SelectNodes("/Project/PropertyGroup/$metadataName"))
            if ($metadataNodes.Count -ne 1) {
                Add-Finding 'WSTEST001' "$($project.RelativePath) must declare direct $metadataName exactly once; actual=$($metadataNodes.Count)."
            } elseif ($metadataName -ne 'TestRuntimeDependencies' -and
                      [string]::IsNullOrWhiteSpace(([string]$metadataNodes[0].InnerText).Trim())) {
                Add-Finding 'WSTEST001' "$($project.RelativePath) direct $metadataName cannot be empty."
            }
        }

        $testRuntime = Get-ProjectProperty -Project ($project.Xml) -Name 'TestRuntime'
        $testKind = Get-ProjectProperty -Project ($project.Xml) -Name 'TestKind'
        $runtimeDependenciesText = Get-ProjectProperty -Project ($project.Xml) -Name 'TestRuntimeDependencies'
        $runtimeDependencies = @($runtimeDependenciesText.Split(';', [StringSplitOptions]::RemoveEmptyEntries) |
            ForEach-Object { $_.Trim() })
        $runnerMode = Get-ProjectProperty -Project ($project.Xml) -Name 'TestRunnerMode'
        $testCadence = Get-ProjectProperty -Project ($project.Xml) -Name 'TestCadence'
        $testRisk = Get-ProjectProperty -Project ($project.Xml) -Name 'TestRisk'
        $testConcern = Get-ProjectProperty -Project ($project.Xml) -Name 'TestConcern'
        $testProfile = Get-ProjectProperty -Project ($project.Xml) -Name 'TestProfile'
        $testRequired = Get-ProjectProperty -Project ($project.Xml) -Name 'TestRequired'
        $allowedTestKinds = @('Aggregate', 'Application', 'Architecture', 'Conformance', 'Contract', 'Deployment', 'Integration', 'Persistence', 'UI', 'Unit', 'Workflow')
        $allowedRuntimes = @('Pure', 'Filesystem', 'Network', 'Avalonia', 'SQLite', 'Windows')
        $allowedRuntimeDependencies = @(
            'AssemblyLoad', 'ControlledConcurrency', 'FakeHttp', 'FakeTime', 'Filesystem', 'Headless',
            'IsolatedDatabase', 'Loopback', 'MSBuild', 'PluginLoad', 'PowerShell', 'ProcessEnvironment',
            'Reflection', 'Release', 'Roslyn', 'SharedOutputDirectory')
        if ($testKind -notin $allowedTestKinds -or $testRuntime -notin $allowedRuntimes -or
            $runnerMode -notin @('Parallel', 'Serial') -or $testCadence -notin @('PR', 'Nightly', 'Release', 'Manual') -or
            $testRisk -notin @('P0', 'P1', 'P2') -or
            $testConcern -notin @('Security', 'Reliability', 'Compatibility', 'Accessibility', 'Performance') -or
            $testProfile -notin @('Default', 'Simulation', 'GoldenDataset', 'LiveExternal') -or
            $testRequired -notin @('true', 'false')) {
            Add-Finding 'WSTEST001' "$($project.RelativePath) has unsupported test taxonomy metadata."
        }
        if ($runtimeDependencies.Count -ne (@($runtimeDependencies | Sort-Object -Unique)).Count -or
            @($runtimeDependencies | Where-Object { $_ -eq 'None' -or $_ -notin $allowedRuntimeDependencies }).Count -gt 0) {
            Add-Finding 'WSTEST001' "$($project.RelativePath) has duplicate/unsupported TestRuntimeDependencies='$runtimeDependenciesText'."
        }

        $allowedRuntimesByKind = @{
            Aggregate = @('Pure')
            Application = @('Pure')
            Architecture = @('Pure', 'Filesystem')
            Conformance = @('Pure', 'Filesystem')
            Contract = @('Pure', 'Filesystem', 'Network')
            Deployment = @('Filesystem', 'Windows')
            Integration = @('Pure', 'Filesystem', 'Network', 'SQLite')
            Persistence = @('Filesystem', 'SQLite')
            UI = @('Avalonia')
            Unit = @('Pure')
            Workflow = @('Pure', 'Filesystem', 'SQLite')
        }
        if ($allowedRuntimesByKind.ContainsKey($testKind) -and $testRuntime -notin $allowedRuntimesByKind[$testKind]) {
            Add-Finding 'WSTEST002' "$($project.RelativePath) TestKind=$testKind is incompatible with TestRuntime=$testRuntime."
        }

        if ($testRuntime -eq 'Pure' -and $runnerMode -ne 'Parallel') {
            Add-Finding 'WSTEST002' "$($project.RelativePath) is Pure and must use TestRunnerMode=Parallel."
        }
        if ($testRuntime -ne 'Pure' -and $runnerMode -ne 'Serial') {
            Add-Finding 'WSTEST002' "$($project.RelativePath) is resource-backed and must use TestRunnerMode=Serial."
        }
        if ($testRuntime -eq 'Pure' -and $runtimeDependencies -contains 'Loopback') {
            Add-Finding 'WSTEST002' "$($project.RelativePath) is Pure and cannot declare Loopback."
        }
        if ($testRuntime -eq 'Avalonia' -and
            ($testKind -ne 'UI' -or $runnerMode -ne 'Serial' -or $runtimeDependencies -notcontains 'Headless')) {
            Add-Finding 'WSTEST002' "$($project.RelativePath) Avalonia runners must be UI/Serial and declare Headless."
        }
        if ($testRuntime -eq 'SQLite' -and $runtimeDependencies -notcontains 'IsolatedDatabase') {
            Add-Finding 'WSTEST002' "$($project.RelativePath) SQLite runners must declare IsolatedDatabase."
        }
        if ($testRuntime -eq 'Network' -and $runtimeDependencies -notcontains 'Loopback' -and $testProfile -ne 'LiveExternal') {
            Add-Finding 'WSTEST002' "$($project.RelativePath) Network runners must declare Loopback or LiveExternal."
        }
        $testSources = @($project.CompileSources)
        $testCases = @($testSources | Where-Object {
            (Get-Content $_.FullName -Raw) -match '\[(?:Xunit\.)?(?:AvaloniaFact|Fact|Theory)\b'
        })
        if ($testCases.Count -eq 0) {
            Add-Finding 'WSTEST004' "$($project.RelativePath) contains no executable test cases. Remove empty runners."
        }

        foreach ($source in $testSources) {
            $sourceText = Get-Content $source.FullName -Raw
            $sourceCode = Remove-CSharpNonCodeText $sourceText
            if ($testRuntime -eq 'Pure') {
                $declaresFileType = $sourceCode -match '\b(?:class|struct|record|interface)\s+File\b'
                $declaresDirectoryType = $sourceCode -match '\b(?:class|struct|record|interface)\s+Directory\b'
                $declaresProcessType = $sourceCode -match '\b(?:class|struct|record|interface)\s+Process\b'
                $usesFileSystem =
                    $sourceCode -match '\bSystem\.IO\.(?:File|Directory)\s*\.' -or
                    (-not $declaresFileType -and $sourceCode -match '\bFile\.(?:Append|Copy|Create|Delete|Move|Open|Read|Replace|Set|Write)[A-Za-z0-9_]*\s*\(') -or
                    (-not $declaresDirectoryType -and $sourceCode -match '\bDirectory\.(?:Create|Delete|Enumerate|Get|Move|Set)[A-Za-z0-9_]*\s*\(')
                $usesProcess =
                    $sourceCode -match '\bSystem\.Diagnostics\.Process\s*\.' -or
                    (-not $declaresProcessType -and $sourceCode -match '\bProcess\.(?:Start|GetProcess|Kill|WaitForExit)[A-Za-z0-9_]*\s*\(')
                $usesNetwork = $sourceCode -match '\b(?:System\.Net\.Sockets|Socket|TcpListener|TcpClient|UdpClient)\b'
                $usesDefaultHttpClient = $sourceCode -match '\bnew\s+(?:System\.Net\.Http\.)?HttpClient\s*\(\s*\)'
                if ($usesFileSystem -or $usesProcess -or $usesNetwork -or $usesDefaultHttpClient) {
                    Add-Finding 'WSTEST009' "$(Get-RepositoryPath $source.FullName) uses a real Filesystem/Process/Network resource inside a Pure runner. Move it to a resource-backed Serial runner or inject a deterministic fake."
                }
            }
            if ($sourceText -match '\bSkip\s*=' -or $sourceText -match '\[(?:Xunit\.)?Explicit\b') {
                Add-Finding 'WSTEST005' "$(Get-RepositoryPath $source.FullName) contains skipped/explicit test behavior."
            }

            # A cancellable infinite wait models an externally blocked dependency without sleeping for wall time.
            # Every finite delay must instead be driven by an observable completion, barrier, or fake clock.
            $withoutCancellableInfiniteWaits = [regex]::Replace(
                $sourceText,
                '\bTask\.Delay\s*\(\s*Timeout\.InfiniteTimeSpan\s*,\s*[A-Za-z_][A-Za-z0-9_.]*\s*\)',
                '')
            if ($withoutCancellableInfiniteWaits -match '\bTask\.Delay\s*\(' -or
                $sourceText -match '\bThread\.Sleep\s*\(' -or
                $sourceText -match '\bSpinWait\.SpinUntil\s*\(' -or
                ($sourceCode -match '\bStopwatch\b' -and
                 $sourceCode -match '(?:Assert\.[A-Za-z]+\s*\([^;]*\bElapsed|\bElapsed(?:Milliseconds|Ticks|\.TotalMilliseconds)\s*(?:<|>|<=|>=))') -or
                ($sourceCode -match '\bDateTime(?:Offset)?\.UtcNow\b' -and
                 $sourceCode -match '\b(?:deadline|timeoutAt|expiresAt)\b' -and
                 $sourceCode -match '\bTask\.Yield\s*\(') -or
                ($sourceCode -match '\bHttpClient\b[\s\S]{0,240}\bTimeout\s*=\s*TimeSpan\.(?:From|Parse)' -and
                 $sourceCode -match 'Timeout\.InfiniteTimeSpan|WaitForCancellation')) {
                Add-Finding 'WSTEST008' "$(Get-RepositoryPath $source.FullName) contains a fixed wall-clock wait. Use observable completion/barriers/fake time; only cancellable Timeout.InfiniteTimeSpan dependency doubles are allowed."
            }
        }
    }

    if ($project.Role -eq 'TestSupport') {
        $supportCases = @($project.CompileSources |
            Where-Object { (Get-Content $_.FullName -Raw) -match '\[(?:Xunit\.)?(?:AvaloniaFact|Fact|Theory)\b' })
        if ($supportCases.Count -gt 0) {
            Add-Finding 'WSTEST006' "$($project.RelativePath) test support contains executable [Fact]/[Theory] cases."
        }
    }
}

$pureTests = @($projects | Where-Object {
    $_.Role -eq 'Test' -and $_.TestRuntime -eq 'Pure'
})
foreach ($testProject in @($projects | Where-Object { $_.Role -eq 'Test' })) {
    $visited = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $queue = [System.Collections.Generic.Queue[object]]::new()
    $queue.Enqueue($testProject)
    [void]$visited.Add($testProject.FullPath)
    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        foreach ($edge in @($current.ActiveEdges)) {
            $target = $null
            if (-not $projectsByPath.TryGetValue($edge.TargetPath, [ref]$target)) { continue }
            if (-not $visited.Add($target.FullPath)) { continue }
            $queue.Enqueue($target)
            if ($current.Role -eq 'TestSupport' -and $target.Role -ne 'TestSupport' -and
                -not (Test-IsAllowedTestEdge -Source $testProject -Target $target)) {
                Add-Finding 'WSTEST010' "$($testProject.Name) transitive closure reaches $($target.RelativePath) [$($target.Role)] outside TestKind=$($testProject.TestKind) ownership."
            }
            if ($current.Role -eq 'TestSupport' -and $testProject.TestRuntime -eq 'Pure' -and
                $target.Name -in @(
                    'IIoT.Edge.Infrastructure.Persistence.EfCore',
                    'IIoT.Edge.Infrastructure.Persistence.Dapper')) {
                Add-Finding 'WSTEST007' "$($testProject.Name) Pure runner reaches forbidden persistence through TestSupport $($current.RelativePath) -> $($target.RelativePath)."
            }
        }
    }
}

$persistenceFreePureRunnerNames = @(
    'IIoT.Edge.Application.Tests',
    'IIoT.Edge.Architecture.AnalyzerTests',
    'IIoT.Edge.Caching.UnitTests',
    'IIoT.Edge.Cloud.ContractTests',
    'IIoT.Edge.Domain.Tests',
    'IIoT.Edge.Installer.UnitTests',
    'IIoT.Edge.Mes.ContractTests',
    'IIoT.Edge.Module.Homogenization.ConformanceTests',
    'IIoT.Edge.Module.Homogenization.WorkflowTests',
    'IIoT.Edge.Plc.ContractTests',
    'IIoT.Edge.Runtime.WorkflowTests')
foreach ($pureTest in @($pureTests | Where-Object { $_.Name -in $persistenceFreePureRunnerNames })) {
    $visited = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $queue = [System.Collections.Generic.Queue[object]]::new()
    $queue.Enqueue($pureTest)
    [void]$visited.Add($pureTest.FullPath)
    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        foreach ($edge in @($current.ActiveEdges)) {
            $target = $null
            if (-not $projectsByPath.TryGetValue($edge.TargetPath, [ref]$target)) { continue }
            if ($visited.Add($target.FullPath)) { $queue.Enqueue($target) }
        }
    }
    $forbiddenPureClosure = @($projects | Where-Object {
        $visited.Contains($_.FullPath) -and
        $_.Name -in @(
            'IIoT.Edge.Infrastructure.Persistence.EfCore',
            'IIoT.Edge.Infrastructure.Persistence.Dapper')
    })
    foreach ($forbidden in $forbiddenPureClosure) {
        Add-Finding 'WSTEST007' "$($pureTest.Name) pure closure reaches forbidden project $($forbidden.RelativePath)."
    }
}

$visitState = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in $projects) { $visitState[$project.FullPath] = 0 }
foreach ($project in $projects) {
    Visit-Project $project ([System.Collections.Generic.List[string]]::new())
}

if ($findings.Count -gt 0) {
    throw "Edge architecture project graph failed ($($findings.Count)):`n$($findings -join "`n")"
}

Write-Host "Edge architecture project graph passed: projects=$($projects.Count), configuration=$Configuration, cycles=0, production-test-paths=0."
