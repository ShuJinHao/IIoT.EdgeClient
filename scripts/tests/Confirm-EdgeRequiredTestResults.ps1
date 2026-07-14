[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$InventoryPath,
    [string]$CountsPath,
    [string]$ResultsDirectory = 'artifacts/test-results',
    [string]$SummaryPath,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
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

function Get-CounterValue {
    param(
        [Parameter(Mandatory)][System.Xml.XmlElement]$Counters,
        [Parameter(Mandatory)][string]$Name
    )

    $value = $Counters.GetAttribute($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return 0
    }
    return [int]$value
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}
if ([string]::IsNullOrWhiteSpace($InventoryPath)) {
    $InventoryPath = Join-Path $PSScriptRoot 'edge-test-inventory.json'
}
if ([string]::IsNullOrWhiteSpace($CountsPath)) {
    $CountsPath = Join-Path $PSScriptRoot 'required-test-counts.json'
}
$ResultsDirectory = Resolve-RepositoryPath $ResultsDirectory

& (Join-Path $PSScriptRoot 'Get-EdgeTestInventory.ps1') -RepositoryRoot $RepositoryRoot -InventoryPath $InventoryPath

$inventory = Get-Content $InventoryPath -Raw | ConvertFrom-Json -Depth 20
$counts = Get-Content $CountsPath -Raw | ConvertFrom-Json -Depth 20
if ([int]$counts.schemaVersion -ne 1 -or [string]$counts.configuration -ne $Configuration) {
    throw 'EDGE-TEST-RESULT-001 required count schema or configuration is invalid.'
}

$expectedByProject = @{}
foreach ($entry in @($counts.projects)) {
    $path = [string]$entry.projectPath
    if ($expectedByProject.ContainsKey($path) -or [int]$entry.discovered -le 0) {
        throw "EDGE-TEST-RESULT-001 invalid count entry for $path."
    }
    $expectedByProject[$path] = [int]$entry.discovered
}

$inventoryPaths = @($inventory.projects | ForEach-Object { [string]$_.projectPath } | Sort-Object)
$countPaths = @($expectedByProject.Keys | Sort-Object)
if (($inventoryPaths -join '|') -ne ($countPaths -join '|')) {
    throw 'EDGE-TEST-RESULT-001 required count projects differ from the machine inventory.'
}

$rows = [System.Collections.Generic.List[object]]::new()
foreach ($project in @($inventory.projects)) {
    $projectPath = [string]$project.projectPath
    $projectName = [string]$project.projectName
    $expected = [int]$expectedByProject[$projectPath]

    $listOutput = & dotnet test $projectPath -c $Configuration --no-build --no-restore --list-tests --nologo -noAutoResponse 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "EDGE-TEST-RESULT-001 discovery failed for ${projectName}:`n$(($listOutput | Out-String).Trim())"
    }
    $discovered = @(Get-ListedTests -Output ($listOutput | Out-String)).Count

    $trxPath = Join-Path $ResultsDirectory "$projectName.trx"
    if (-not (Test-Path $trxPath -PathType Leaf)) {
        throw "EDGE-TEST-RESULT-001 missing TRX for ${projectName}: $trxPath"
    }
    [xml]$trx = Get-Content $trxPath -Raw
    $namespace = [string]$trx.DocumentElement.NamespaceURI
    $manager = [Xml.XmlNamespaceManager]::new($trx.NameTable)
    $manager.AddNamespace('t', $namespace)
    $counterNodes = @($trx.SelectNodes('//t:ResultSummary/t:Counters', $manager))
    if ($counterNodes.Count -ne 1) {
        throw "EDGE-TEST-RESULT-001 TRX does not contain one counter summary: $trxPath"
    }
    $counters = [System.Xml.XmlElement]$counterNodes[0]
    $executed = Get-CounterValue -Counters $counters -Name 'executed'
    $passed = Get-CounterValue -Counters $counters -Name 'passed'
    $failed = Get-CounterValue -Counters $counters -Name 'failed'
    $skipped = (Get-CounterValue -Counters $counters -Name 'notExecuted') +
        (Get-CounterValue -Counters $counters -Name 'inconclusive') +
        (Get-CounterValue -Counters $counters -Name 'notRunnable')

    if ($discovered -ne $expected -or
        $executed -ne $discovered -or
        $passed -ne $executed -or
        $failed -ne 0 -or
        $skipped -ne 0) {
        throw "EDGE-TEST-RESULT-001 $projectName mismatch: expected=$expected discovered=$discovered executed=$executed passed=$passed failed=$failed skipped=$skipped."
    }

    $row = [pscustomobject][ordered]@{
        project = $projectName
        discovered = $discovered
        executed = $executed
        passed = $passed
        failed = $failed
        skipped = $skipped
    }
    $rows.Add($row)
    Write-Host "TEST_RESULT project=$projectName discovered=$discovered executed=$executed passed=$passed failed=$failed skipped=$skipped"
}

$summary = [pscustomobject][ordered]@{
    schemaVersion = 1
    configuration = $Configuration
    projects = [object[]]$rows
    totals = [pscustomobject][ordered]@{
        discovered = [int](($rows | Measure-Object discovered -Sum).Sum)
        executed = [int](($rows | Measure-Object executed -Sum).Sum)
        passed = [int](($rows | Measure-Object passed -Sum).Sum)
        failed = [int](($rows | Measure-Object failed -Sum).Sum)
        skipped = [int](($rows | Measure-Object skipped -Sum).Sum)
    }
}
if (-not [string]::IsNullOrWhiteSpace($SummaryPath)) {
    $resolvedSummary = Resolve-RepositoryPath $SummaryPath
    [void](New-Item (Split-Path $resolvedSummary -Parent) -ItemType Directory -Force)
    [IO.File]::WriteAllText(
        $resolvedSummary,
        (($summary | ConvertTo-Json -Depth 20) + "`n"),
        [Text.UTF8Encoding]::new($false))
}

Write-Host "TEST_TOTAL discovered=$($summary.totals.discovered) executed=$($summary.totals.executed) passed=$($summary.totals.passed) failed=$($summary.totals.failed) skipped=$($summary.totals.skipped)"
