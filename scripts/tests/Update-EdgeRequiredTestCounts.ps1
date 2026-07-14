[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$InventoryPath,
    [string]$OutputPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ListedTests {
    param([Parameter(Mandatory)][string]$Output)

    $collect = $false
    $tests = [System.Collections.Generic.List[string]]::new()
    foreach ($line in [regex]::Split($Output, '\r?\n')) {
        if ($line -match 'Tests are available\s*:|测试可用\s*:|Tests disponibles\s*:|Tests disponibles sont\s*:') {
            $collect = $true
            continue
        }
        if ($collect -and $line -match '^\s{2,}\S') {
            $trimmed = $line.Trim()
            if ($trimmed -notmatch '^(Test Run|Total tests|Passed!|Failed!|警告|Warning)') {
                $tests.Add($trimmed)
            }
        }
    }
    return [string[]]@($tests | Sort-Object)
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}
if ([string]::IsNullOrWhiteSpace($InventoryPath)) {
    $InventoryPath = Join-Path $PSScriptRoot 'edge-test-inventory.json'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot 'required-test-counts.json'
}

& (Join-Path $PSScriptRoot 'Get-EdgeTestInventory.ps1') -RepositoryRoot $RepositoryRoot -InventoryPath $InventoryPath

$inventory = Get-Content $InventoryPath -Raw | ConvertFrom-Json -Depth 20
$counts = [System.Collections.Generic.List[object]]::new()
foreach ($project in @($inventory.projects)) {
    $projectPath = [string]$project.projectPath
    $output = & dotnet test $projectPath -c $Configuration --no-build --no-restore --list-tests --nologo -noAutoResponse 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "EDGE-TEST-COUNT-001 discovery failed for ${projectPath}:`n$(($output | Out-String).Trim())"
    }
    $tests = @(Get-ListedTests -Output ($output | Out-String))
    if ($tests.Count -eq 0) {
        throw "EDGE-TEST-COUNT-001 discovery returned zero tests for $projectPath."
    }
    $counts.Add([pscustomobject][ordered]@{
        projectPath = $projectPath
        discovered = $tests.Count
    })
    Write-Host "Discovered $($tests.Count): $projectPath"
}

$document = [pscustomobject][ordered]@{
    schemaVersion = 1
    inventoryPath = 'scripts/tests/edge-test-inventory.json'
    configuration = $Configuration
    projects = [object[]]$counts
}
$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $OutputPath))
}
[void](New-Item (Split-Path $resolvedOutput -Parent) -ItemType Directory -Force)
[IO.File]::WriteAllText(
    $resolvedOutput,
    (($document | ConvertTo-Json -Depth 20) + "`n"),
    [Text.UTF8Encoding]::new($false))
Write-Host "Updated required test counts: $resolvedOutput"
