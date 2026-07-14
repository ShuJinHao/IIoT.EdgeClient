[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$InventoryPath,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string]$PathValue)

    if ([IO.Path]::IsPathRooted($PathValue)) {
        return [IO.Path]::GetFullPath($PathValue)
    }

    return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $PathValue))
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

$inventory = Get-Content $InventoryPath -Raw | ConvertFrom-Json -Depth 20
if ([int]$inventory.schemaVersion -ne 1) {
    throw "EDGE-TEST-INVENTORY-001 unsupported schemaVersion '$($inventory.schemaVersion)'."
}

$solutionPath = Resolve-RepositoryPath ([string]$inventory.solutionPath)
[xml]$solution = Get-Content $solutionPath -Raw
$solutionProjects = @($solution.SelectNodes('//Project') | ForEach-Object {
    ([string]$_.Path).Replace('\', '/')
} | Sort-Object -Unique)

$repositoryProjects = @(Get-ChildItem $RepositoryRoot -Recurse -Filter '*.csproj' -File |
    Where-Object { $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]' } |
    ForEach-Object { [IO.Path]::GetRelativePath($RepositoryRoot, $_.FullName).Replace('\', '/') } |
    Sort-Object -Unique)

if ($solutionProjects.Count -ne [int]$inventory.solutionProjectCount -or
    ($solutionProjects -join '|') -ne ($repositoryProjects -join '|')) {
    throw "EDGE-TEST-INVENTORY-001 solution/repository project mismatch: solution=$($solutionProjects.Count), repository=$($repositoryProjects.Count), expected=$($inventory.solutionProjectCount)."
}

$actualTestProjects = [System.Collections.Generic.List[string]]::new()
$actualFixtureProjects = [System.Collections.Generic.List[string]]::new()
foreach ($projectPath in $repositoryProjects) {
    [xml]$project = Get-Content (Resolve-RepositoryPath $projectPath) -Raw
    if ((Get-ProjectProperty -Project $project -Name 'IsTestProject').Equals('true', [StringComparison]::OrdinalIgnoreCase)) {
        $actualTestProjects.Add($projectPath)
    }
    if ((Get-ProjectProperty -Project $project -Name 'IsEdgePluginTestFixture').Equals('true', [StringComparison]::OrdinalIgnoreCase)) {
        $actualFixtureProjects.Add($projectPath)
    }
}
$actualTestProjectPaths = @($actualTestProjects | Sort-Object -Unique)
$inventoryTestProjectPaths = @($inventory.projects | ForEach-Object {
    if (-not [bool]$_.required) {
        throw "EDGE-TEST-INVENTORY-001 every listed test project must be required: $($_.projectPath)."
    }
    if ([string]::IsNullOrWhiteSpace([string]$_.projectName) -or
        [string]::IsNullOrWhiteSpace([string]$_.classification) -or
        [string]::IsNullOrWhiteSpace([string]$_.runtime)) {
        throw "EDGE-TEST-INVENTORY-001 test project metadata is incomplete: $($_.projectPath)."
    }
    if ([IO.Path]::GetFileNameWithoutExtension([string]$_.projectPath) -ne [string]$_.projectName) {
        throw "EDGE-TEST-INVENTORY-001 test project name does not match its project file: $($_.projectPath)."
    }
    ([string]$_.projectPath).Replace('\', '/')
} | Sort-Object -Unique)

if ($inventoryTestProjectPaths.Count -ne [int]$inventory.testProjectCount -or
    ($actualTestProjectPaths -join '|') -ne ($inventoryTestProjectPaths -join '|')) {
    throw "EDGE-TEST-INVENTORY-001 test project mismatch: actual=[$($actualTestProjectPaths -join ', ')], inventory=[$($inventoryTestProjectPaths -join ', ')]."
}

$inventoryFixturePaths = @($inventory.testFixtures | ForEach-Object {
    ([string]$_.projectPath).Replace('\', '/')
} | Sort-Object -Unique)
$actualFixturePaths = @($actualFixtureProjects | Sort-Object -Unique)
if (($actualFixturePaths -join '|') -ne ($inventoryFixturePaths -join '|')) {
    throw "EDGE-TEST-INVENTORY-001 fixture project mismatch: actual=[$($actualFixturePaths -join ', ')], inventory=[$($inventoryFixturePaths -join ', ')]."
}

foreach ($fixture in @($inventory.testFixtures)) {
    $fixturePath = ([string]$fixture.projectPath).Replace('\', '/')
    if ([string]$fixture.fixtureKind -ne 'Plugin' -or [bool]$fixture.productionEligible) {
        throw "EDGE-TEST-INVENTORY-001 test fixture cannot be production eligible: $fixturePath."
    }
    [xml]$fixtureProject = Get-Content (Resolve-RepositoryPath $fixturePath) -Raw
    if (-not (Get-ProjectProperty -Project $fixtureProject -Name 'IsEdgePluginTestFixture').Equals('true', [StringComparison]::OrdinalIgnoreCase) -or
        -not (Get-ProjectProperty -Project $fixtureProject -Name 'IsEdgePluginModule').Equals('true', [StringComparison]::OrdinalIgnoreCase) -or
        -not (Get-ProjectProperty -Project $fixtureProject -Name 'IsPackable').Equals('false', [StringComparison]::OrdinalIgnoreCase) -or
        (Get-ProjectProperty -Project $fixtureProject -Name 'IsTestProject').Equals('true', [StringComparison]::OrdinalIgnoreCase) -or
        -not $fixturePath.StartsWith('src/Testing/', [StringComparison]::Ordinal)) {
        throw "EDGE-TEST-INVENTORY-001 plugin fixture metadata or location is invalid: $fixturePath."
    }
}

$result = [pscustomobject][ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    solutionPath = [string]$inventory.solutionPath
    solutionProjectCount = $solutionProjects.Count
    testProjectCount = $actualTestProjectPaths.Count
    testProjects = $actualTestProjectPaths
    testFixtures = @($inventory.testFixtures)
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = Resolve-RepositoryPath $OutputPath
    [void](New-Item (Split-Path $resolvedOutput -Parent) -ItemType Directory -Force)
    [IO.File]::WriteAllText(
        $resolvedOutput,
        (($result | ConvertTo-Json -Depth 20) + "`n"),
        [Text.UTF8Encoding]::new($false))
}

Write-Host "Edge test inventory passed: solution=$($solutionProjects.Count), tests=$($actualTestProjectPaths.Count), fixtures=$(@($inventory.testFixtures).Count)."
