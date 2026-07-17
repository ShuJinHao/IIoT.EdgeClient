[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$InventoryPath,
    [string]$DiscoveredInventoryPath,
    [string]$OutputPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}
if ([string]::IsNullOrWhiteSpace($InventoryPath)) { $InventoryPath = Join-Path $PSScriptRoot 'edge-test-inventory.json' }
if ([string]::IsNullOrWhiteSpace($DiscoveredInventoryPath)) { $DiscoveredInventoryPath = Join-Path $PSScriptRoot 'discovered-test-inventory.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $PSScriptRoot 'required-test-counts.json' }

& (Join-Path $PSScriptRoot 'Get-EdgeDiscoveredTestInventory.ps1') `
    -RepositoryRoot $RepositoryRoot `
    -InventoryPath $InventoryPath `
    -DiscoveredInventoryPath $DiscoveredInventoryPath `
    -Configuration $Configuration `
    -Update

$discovered = Get-Content $DiscoveredInventoryPath -Raw | ConvertFrom-Json -Depth 30
$document = [pscustomobject][ordered]@{
    schemaVersion = 3
    inventoryPath = 'scripts/tests/edge-test-inventory.json'
    discoveredInventoryPath = 'scripts/tests/discovered-test-inventory.json'
    configuration = $Configuration
    caseCount = [int]$discovered.caseCount
    projects = @($discovered.projects | ForEach-Object {
        [pscustomobject][ordered]@{
            projectPath = [string]$_.projectPath
            discovered = [int]$_.discovered
        }
    })
}
$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $OutputPath))
}
[IO.File]::WriteAllText(
    $resolvedOutput,
    (($document | ConvertTo-Json -Depth 30) + "`n"),
    [Text.UTF8Encoding]::new($false))
Write-Host "Updated required test counts: projects=$(@($document.projects).Count), cases=$($document.caseCount), path=$resolvedOutput"
