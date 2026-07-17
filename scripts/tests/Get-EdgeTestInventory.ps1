[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$InventoryPath,
    [string]$OutputPath,
    [switch]$Update
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$requiredMetadata = @(
    'TestKind', 'TestRuntime', 'TestRuntimeDependencies', 'TestRunnerMode', 'TestCadence',
    'TestCapability', 'TestRisk', 'TestConcern', 'TestProfile', 'TestOwner', 'TestRuleId', 'TestRequired')
$allowedTestKinds = @('Aggregate', 'Application', 'Architecture', 'Conformance', 'Contract', 'Deployment', 'Integration', 'Persistence', 'UI', 'Unit', 'Workflow')
$allowedRuntimes = @('Pure', 'Filesystem', 'Network', 'Avalonia', 'SQLite', 'Windows')
$allowedDependencies = @(
    'AssemblyLoad', 'ControlledConcurrency', 'FakeHttp', 'FakeTime', 'Filesystem', 'Headless',
    'IsolatedDatabase', 'Loopback', 'MSBuild', 'PluginLoad', 'PowerShell', 'ProcessEnvironment',
    'Reflection', 'Release', 'Roslyn', 'SharedOutputDirectory')
$allowedConcerns = @('Security', 'Reliability', 'Compatibility', 'Accessibility', 'Performance')
$allowedProfiles = @('Default', 'Simulation', 'GoldenDataset', 'LiveExternal')

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string]$PathValue)
    if ([IO.Path]::IsPathRooted($PathValue)) { return [IO.Path]::GetFullPath($PathValue) }
    return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $PathValue))
}

function Get-ProjectProperty {
    param(
        [Parameter(Mandatory)][xml]$Project,
        [Parameter(Mandatory)][string]$Name,
        [switch]$Required,
        [switch]$AllowEmpty
    )

    $nodes = @($Project.SelectNodes("/Project/PropertyGroup/$Name"))
    if ($nodes.Count -gt 1) {
        throw "EDGE-TEST-INVENTORY-001 direct project metadata '$Name' must not be duplicated; actual=$($nodes.Count)."
    }
    if ($nodes.Count -eq 0) {
        if ($Required) { throw "EDGE-TEST-INVENTORY-001 direct project metadata '$Name' is required exactly once." }
        return ''
    }
    $value = ([string]$nodes[0].InnerText).Trim()
    if ($Required -and -not $AllowEmpty -and [string]::IsNullOrWhiteSpace($value)) {
        throw "EDGE-TEST-INVENTORY-001 direct project metadata '$Name' cannot be empty."
    }
    return $value
}

function ConvertTo-Dependencies {
    param([AllowEmptyString()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return [string[]]@() }
    return [string[]]@($Value.Split(';', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() })
}

function Get-NormalizedOverrides {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return [object[]]@() }
    return [object[]]@($Value)
}

function Test-TrueValue {
    param([AllowEmptyString()][string]$Value)
    return $Value.Equals('true', [StringComparison]::OrdinalIgnoreCase)
}

function New-ProjectInventoryEntry {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][xml]$Project,
        [AllowNull()][object[]]$Overrides
    )

    $values = @{}
    foreach ($name in $requiredMetadata) {
        $values[$name] = Get-ProjectProperty -Project $Project -Name $name -Required -AllowEmpty:($name -eq 'TestRuntimeDependencies')
    }
    $dependencies = @(ConvertTo-Dependencies $values.TestRuntimeDependencies)
    return [pscustomobject][ordered]@{
        projectPath = $ProjectPath
        projectName = [IO.Path]::GetFileNameWithoutExtension($ProjectPath)
        testKind = $values.TestKind
        runtime = $values.TestRuntime
        runtimeDependencies = [string[]]$dependencies
        runnerMode = $values.TestRunnerMode
        cadence = $values.TestCadence
        capability = $values.TestCapability
        risk = $values.TestRisk
        concern = $values.TestConcern
        profile = $values.TestProfile
        owner = $values.TestOwner
        ruleId = $values.TestRuleId
        required = Test-TrueValue $values.TestRequired
        overrides = if (@($Overrides).Count -eq 0) { $null } else { [object[]]$Overrides }
    }
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}
if ([string]::IsNullOrWhiteSpace($InventoryPath)) {
    $InventoryPath = Join-Path $PSScriptRoot 'edge-test-inventory.json'
} else {
    $InventoryPath = Resolve-RepositoryPath $InventoryPath
}

$existingInventory = if (Test-Path $InventoryPath -PathType Leaf) {
    Get-Content $InventoryPath -Raw | ConvertFrom-Json -Depth 30
} else {
    $null
}
$solutionRelativePath = if ($null -ne $existingInventory -and -not [string]::IsNullOrWhiteSpace([string]$existingInventory.solutionPath)) {
    [string]$existingInventory.solutionPath
} else {
    'IIoT.EdgeClient.slnx'
}
$solutionPath = Resolve-RepositoryPath $solutionRelativePath
[xml]$solution = Get-Content $solutionPath -Raw
$solutionProjectEntries = @($solution.SelectNodes('//Project') | ForEach-Object { ([string]$_.Path).Replace('\', '/') })
$duplicateSolutionProjects = @($solutionProjectEntries | Group-Object | Where-Object Count -gt 1)
if ($duplicateSolutionProjects.Count -gt 0) {
    throw "EDGE-TEST-INVENTORY-001 duplicate solution project entries: $($duplicateSolutionProjects.Name -join ', ')."
}
$solutionProjects = @($solutionProjectEntries | Sort-Object)
$repositoryProjects = @(Get-ChildItem $RepositoryRoot -Recurse -Filter '*.csproj' -File |
    Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' } |
    ForEach-Object { [IO.Path]::GetRelativePath($RepositoryRoot, $_.FullName).Replace('\', '/') } |
    Sort-Object)
if (($solutionProjects -join '|') -ne ($repositoryProjects -join '|')) {
    throw "EDGE-TEST-INVENTORY-001 solution/repository project mismatch: solution=$($solutionProjects.Count), repository=$($repositoryProjects.Count)."
}

$projectXmlByPath = @{}
$testProjectPaths = [System.Collections.Generic.List[string]]::new()
$fixtureProjectPaths = [System.Collections.Generic.List[string]]::new()
foreach ($projectPath in $repositoryProjects) {
    [xml]$project = Get-Content (Resolve-RepositoryPath $projectPath) -Raw
    $projectXmlByPath[$projectPath] = $project
    if (Test-TrueValue (Get-ProjectProperty -Project $project -Name 'IsTestProject')) { $testProjectPaths.Add($projectPath) }
    if (Test-TrueValue (Get-ProjectProperty -Project $project -Name 'IsEdgePluginTestFixture')) { $fixtureProjectPaths.Add($projectPath) }
}

if ($Update) {
    $existingOverrides = [System.Collections.Generic.List[object]]::new()
    if ($null -ne $existingInventory) {
        foreach ($entry in @($existingInventory.projects)) {
            foreach ($override in @(Get-NormalizedOverrides $entry.overrides)) { $existingOverrides.Add($override) }
        }
    }
    $overridesByProject = @{}
    foreach ($override in $existingOverrides) {
        if ([string]::IsNullOrWhiteSpace([string]$override.source)) { continue }
        $matchingProjects = @($testProjectPaths | Where-Object {
            Test-Path (Join-Path (Split-Path (Resolve-RepositoryPath $_) -Parent) ([string]$override.source)) -PathType Leaf
        })
        if ($matchingProjects.Count -ne 1) {
            throw "EDGE-TEST-INVENTORY-001 cannot migrate override source '$($override.source)'; matchingProjects=$($matchingProjects.Count)."
        }
        if (-not $overridesByProject.ContainsKey($matchingProjects[0])) {
            $overridesByProject[$matchingProjects[0]] = [System.Collections.Generic.List[object]]::new()
        }
        $overridesByProject[$matchingProjects[0]].Add($override)
    }

    $fixtureEntries = if ($null -ne $existingInventory) { @($existingInventory.testFixtures) } else { @() }
    $updatedProjects = @($testProjectPaths | Sort-Object | ForEach-Object {
        $projectOverrides = if ($overridesByProject.ContainsKey($_)) { @($overridesByProject[$_]) } else { @() }
        New-ProjectInventoryEntry -ProjectPath $_ -Project $projectXmlByPath[$_] -Overrides $projectOverrides
    })
    $updated = [pscustomobject][ordered]@{
        schemaVersion = 3
        defaultRegressionIdSource = 'project.ruleId'
        solutionPath = $solutionRelativePath
        solutionProjectCount = $solutionProjects.Count
        testProjectCount = $updatedProjects.Count
        projects = $updatedProjects
        testFixtures = $fixtureEntries
    }
    [IO.File]::WriteAllText(
        $InventoryPath,
        (($updated | ConvertTo-Json -Depth 30) + "`n"),
        [Text.UTF8Encoding]::new($false))
    $existingInventory = $updated
}

if ($null -eq $existingInventory -or [int]$existingInventory.schemaVersion -ne 3) {
    throw "EDGE-TEST-INVENTORY-001 unsupported schemaVersion '$($existingInventory.schemaVersion)'."
}
$inventory = $existingInventory
if ($inventory.PSObject.Properties.Name -notcontains 'defaultRegressionIdSource' -or
    [string]$inventory.defaultRegressionIdSource -cne 'project.ruleId') {
    throw 'EDGE-TEST-INVENTORY-001 defaultRegressionIdSource must be project.ruleId.'
}
if ([int]$inventory.solutionProjectCount -ne $solutionProjects.Count -or [int]$inventory.testProjectCount -ne $testProjectPaths.Count) {
    throw "EDGE-TEST-INVENTORY-001 project counts are stale: solution=$($solutionProjects.Count), tests=$($testProjectPaths.Count)."
}

$inventoryEntries = @($inventory.projects)
$duplicateInventoryPaths = @($inventoryEntries | Group-Object { ([string]$_.projectPath).Replace('\', '/') } | Where-Object Count -gt 1)
if ($duplicateInventoryPaths.Count -gt 0) {
    throw "EDGE-TEST-INVENTORY-001 duplicate inventory project entries: $($duplicateInventoryPaths.Name -join ', ')."
}
$inventoryPaths = @($inventoryEntries | ForEach-Object { ([string]$_.projectPath).Replace('\', '/') } | Sort-Object)
if (($inventoryPaths -join '|') -ne ((@($testProjectPaths | Sort-Object)) -join '|')) {
    throw 'EDGE-TEST-INVENTORY-001 inventory does not contain exactly every test runner.'
}

foreach ($entry in $inventoryEntries) {
    $projectPath = ([string]$entry.projectPath).Replace('\', '/')
    [xml]$project = $projectXmlByPath[$projectPath]
    $actual = New-ProjectInventoryEntry -ProjectPath $projectPath -Project $project -Overrides @(Get-NormalizedOverrides $entry.overrides)
    foreach ($field in @('projectName', 'testKind', 'runtime', 'runnerMode', 'cadence', 'capability', 'risk', 'concern', 'profile', 'owner', 'ruleId', 'required')) {
        if ([string]$entry.$field -cne [string]$actual.$field) {
            throw "EDGE-TEST-INVENTORY-001 $projectPath metadata mismatch: $field csproj='$($actual.$field)' inventory='$($entry.$field)'."
        }
    }
    $dependencies = @($entry.runtimeDependencies | ForEach-Object { [string]$_ })
    if (($dependencies -join ';') -cne (@($actual.runtimeDependencies) -join ';') -or
        $dependencies.Count -ne (@($dependencies | Sort-Object -Unique)).Count -or
        @($dependencies | Where-Object { $_ -eq 'None' -or $_ -notin $allowedDependencies }).Count -gt 0) {
        throw "EDGE-TEST-INVENTORY-001 $projectPath runtimeDependencies are stale, duplicated, or unsupported."
    }
    if ([string]$entry.testKind -notin $allowedTestKinds -or [string]$entry.runtime -notin $allowedRuntimes -or
        [string]$entry.runnerMode -notin @('Parallel', 'Serial') -or [string]$entry.cadence -notin @('PR', 'Nightly', 'Release', 'Manual') -or
        [string]$entry.risk -notin @('P0', 'P1', 'P2') -or [string]$entry.concern -notin $allowedConcerns -or
        [string]$entry.profile -notin $allowedProfiles) {
        throw "EDGE-TEST-INVENTORY-001 $projectPath has unsupported taxonomy metadata."
    }
    if ([string]$entry.runtime -eq 'Pure' -and [string]$entry.runnerMode -ne 'Parallel') {
        throw "EDGE-TEST-INVENTORY-001 Pure runner must be Parallel: $projectPath."
    }
    if ([string]$entry.runtime -ne 'Pure' -and [string]$entry.runnerMode -ne 'Serial') {
        throw "EDGE-TEST-INVENTORY-001 resource-backed runner must be Serial: $projectPath."
    }
    if ([string]$entry.testKind -eq 'Unit' -and [string]$entry.runtime -ne 'Pure') {
        throw "EDGE-TEST-INVENTORY-001 Unit runner must be an in-memory Pure runner; move filesystem/SQLite/UI cases to a physically named resource runner: $projectPath."
    }

    $overrideKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($override in @(Get-NormalizedOverrides $entry.overrides)) {
        $hasSource = -not [string]::IsNullOrWhiteSpace([string]$override.source)
        $hasPattern = $override.PSObject.Properties.Name -contains 'casePattern' -and -not [string]::IsNullOrWhiteSpace([string]$override.casePattern)
        if ($hasSource -eq $hasPattern -or [string]::IsNullOrWhiteSpace([string]$override.regressionId)) {
            throw "EDGE-TEST-INVENTORY-001 $projectPath override must declare exactly one source/casePattern and a regressionId."
        }
        $key = if ($hasSource) { "source:$([string]$override.source)" } else { "pattern:$([string]$override.casePattern)" }
        if (-not $overrideKeys.Add($key)) { throw "EDGE-TEST-INVENTORY-001 $projectPath duplicate override '$key'." }
        if ($hasSource) {
            $sourcePath = [IO.Path]::GetFullPath((Join-Path (Split-Path (Resolve-RepositoryPath $projectPath) -Parent) ([string]$override.source)))
            if (-not $sourcePath.StartsWith((Split-Path (Resolve-RepositoryPath $projectPath) -Parent), [StringComparison]::Ordinal) -or
                -not (Test-Path $sourcePath -PathType Leaf)) {
                throw "EDGE-TEST-INVENTORY-001 $projectPath override source does not exist inside the runner: $($override.source)."
            }
        }
        if ([string]$override.regressionId -notmatch '^[A-Z0-9][A-Z0-9._-]+$') {
            throw "EDGE-TEST-INVENTORY-001 invalid regressionId '$($override.regressionId)'."
        }
        if ($override.PSObject.Properties.Name -contains 'risk' -and [string]$override.risk -notin @('P0', 'P1', 'P2')) { throw "EDGE-TEST-INVENTORY-001 invalid override risk." }
        if ($override.PSObject.Properties.Name -contains 'concern' -and [string]$override.concern -notin $allowedConcerns) { throw "EDGE-TEST-INVENTORY-001 invalid override concern." }
    }
}

$fixtureEntries = @($inventory.testFixtures)
$inventoryFixturePaths = @($fixtureEntries | ForEach-Object { ([string]$_.projectPath).Replace('\', '/') } | Sort-Object)
if (($inventoryFixturePaths -join '|') -ne ((@($fixtureProjectPaths | Sort-Object)) -join '|')) {
    throw 'EDGE-TEST-INVENTORY-001 fixture project inventory is stale.'
}
foreach ($fixture in $fixtureEntries) {
    $fixturePath = ([string]$fixture.projectPath).Replace('\', '/')
    [xml]$fixtureProject = $projectXmlByPath[$fixturePath]
    if ([string]$fixture.fixtureKind -ne 'Plugin' -or [bool]$fixture.productionEligible -or
        -not (Test-TrueValue (Get-ProjectProperty -Project $fixtureProject -Name 'IsEdgePluginTestFixture')) -or
        -not (Test-TrueValue (Get-ProjectProperty -Project $fixtureProject -Name 'IsEdgePluginModule')) -or
        (Test-TrueValue (Get-ProjectProperty -Project $fixtureProject -Name 'IsPackable')) -or
        (Test-TrueValue (Get-ProjectProperty -Project $fixtureProject -Name 'IsTestProject')) -or
        -not $fixturePath.StartsWith('src/Testing/', [StringComparison]::Ordinal)) {
        throw "EDGE-TEST-INVENTORY-001 plugin fixture metadata/location is invalid: $fixturePath."
    }
}

$result = [pscustomobject][ordered]@{
    schemaVersion = 3
    defaultRegressionIdSource = 'project.ruleId'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    solutionPath = [string]$inventory.solutionPath
    solutionProjectCount = $solutionProjects.Count
    testProjectCount = $testProjectPaths.Count
    testProjects = @($inventory.projects)
    testFixtures = $fixtureEntries
}
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = Resolve-RepositoryPath $OutputPath
    [void](New-Item (Split-Path $resolvedOutput -Parent) -ItemType Directory -Force)
    [IO.File]::WriteAllText($resolvedOutput, (($result | ConvertTo-Json -Depth 30) + "`n"), [Text.UTF8Encoding]::new($false))
}

Write-Host "Edge test inventory passed: solution=$($solutionProjects.Count), tests=$($testProjectPaths.Count), fixtures=$($fixtureEntries.Count)."
