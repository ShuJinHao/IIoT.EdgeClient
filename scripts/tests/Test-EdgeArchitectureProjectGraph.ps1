[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$SolutionPath,
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

function Get-ProjectRole {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][xml]$Project
    )

    if (Test-TrueValue (Get-ProjectProperty $Project 'IsEdgePluginTestFixture')) { return 'TestFixture' }
    if (Test-TrueValue (Get-ProjectProperty $Project 'IsTestProject')) { return 'Test' }
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
    if (Test-IsExactDebugCondition $Condition) { return $Configuration -eq 'Debug' }

    $compact = [regex]::Replace($Condition, '\s+', '')
    if ($compact -match [regex]::Escape('$(Configuration)')) {
        if ($compact.IndexOf('Release', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            if ($compact.Contains('!=')) { return $Configuration -ne 'Release' }
            if ($compact.Contains('==') -or $compact.Contains('Equals(')) { return $Configuration -eq 'Release' }
        }
        if ($compact.IndexOf('Debug', [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            if ($compact.Contains('!=')) { return $Configuration -ne 'Debug' }
            if ($compact.Contains('==') -or $compact.Contains('Equals(')) { return $Configuration -eq 'Debug' }
        }
    }

    # Unknown conditions remain visible to the graph; fail-closed is safer than hiding an edge.
    return $true
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
    return [string[]]@(Get-ChildItem $RepositoryRoot -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' } |
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
        'TestFixture' { return $Target.Role -in @('Application', 'ModuleSdk', 'SharedKernel', 'UiShared') }
        'Test' { return $Target.Role -ne 'Unknown' }
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
            if ($target.Role -in @('Test', 'TestFixture')) { return $target }
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
    foreach ($projectFile in @(Get-ChildItem $RepositoryRoot -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' })) {
        $projectPaths.Add($projectFile.FullName)
    }
}

$findingKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$findings = [System.Collections.Generic.List[string]]::new()
$projectsByPath = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
$projects = [System.Collections.Generic.List[object]]::new()

foreach ($projectPath in @($projectPaths | Sort-Object -Unique)) {
    if (-not (Test-Path $projectPath -PathType Leaf)) {
        Add-Finding 'WSARCH004' "registered project does not exist: $(Get-RepositoryPath $projectPath)"
        continue
    }
    [xml]$projectXml = Get-Content $projectPath -Raw
    $name = [IO.Path]::GetFileNameWithoutExtension($projectPath)
    $relative = Get-RepositoryPath $projectPath
    $info = [pscustomobject]@{
        FullPath = $projectPath
        RelativePath = $relative
        Directory = Split-Path $projectPath -Parent
        Name = $name
        Role = Get-ProjectRole $relative $name $projectXml
        Xml = $projectXml
        ActiveEdges = [System.Collections.Generic.List[object]]::new()
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

    foreach ($package in @($project.Xml.SelectNodes('/Project/ItemGroup/PackageReference'))) {
        $packageName = ([System.Xml.XmlElement]$package).GetAttribute('Include').Trim()
        if ($project.Role -notin @('Test', 'Analyzer') -and
            ($packageName -match '(?i)(^xunit|Test\.Sdk|TestPlatform|Moq|NSubstitute|FluentAssertions)')) {
            Add-Finding 'WSARCH003' "$($project.RelativePath) references test package '$packageName'."
        }
    }

    $edgeNodes = @($project.Xml.SelectNodes('/Project/ItemGroup/ProjectReference'))
    foreach ($node in $edgeNodes) {
        $include = ([System.Xml.XmlElement]$node).GetAttribute('Include').Trim()
        if ([string]::IsNullOrWhiteSpace($include)) { continue }
        $condition = ([System.Xml.XmlElement]$node).GetAttribute('Condition').Trim()
        $targetPath = Resolve-FullPath $project.Directory $include
        $edge = [pscustomobject]@{
            Kind = 'ProjectReference'
            TargetPath = $targetPath
            Condition = $condition
            ReferenceOutputAssembly = ([System.Xml.XmlElement]$node).GetAttribute('ReferenceOutputAssembly').Trim()
        }

        $target = $null
        if ($projectsByPath.TryGetValue($targetPath, [ref]$target) -and $target.Role -eq 'VisualTestData') {
            $isExactDebugEdge = $project.Name -eq 'IIoT.Edge.Host.Bootstrap' -and (Test-IsExactDebugCondition $condition)
            if (-not $isExactDebugEdge -and $project.Role -ne 'Test') {
                Add-Finding 'WSARCH003' "$($project.RelativePath) -> $($target.RelativePath) must be the exact Debug-only Host.Bootstrap edge; condition='$condition'."
            }
        }

        if (Test-IsActiveEdge $condition) { $project.ActiveEdges.Add($edge) }
    }

    foreach ($msbuildNode in @($project.Xml.SelectNodes('//*[local-name()="MSBuild"][@Projects]'))) {
        $condition = ([System.Xml.XmlElement]$msbuildNode).GetAttribute('Condition').Trim()
        foreach ($targetPath in @(Resolve-ProjectExpression $project ([System.Xml.XmlElement]$msbuildNode).GetAttribute('Projects'))) {
            if (Test-IsActiveEdge $condition) {
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

        if ($project.Role -notin @('Test', 'TestFixture', 'Analyzer') -and $target.Role -in @('Test', 'TestFixture')) {
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

    if ($project.Role -notin @('Test', 'TestFixture', 'Analyzer')) {
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
