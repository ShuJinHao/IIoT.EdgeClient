[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$InventoryPath,
    [string]$DiscoveredInventoryPath,
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
if ([string]::IsNullOrWhiteSpace($DiscoveredInventoryPath)) {
    $DiscoveredInventoryPath = Join-Path $PSScriptRoot 'discovered-test-inventory.json'
}
if ([string]::IsNullOrWhiteSpace($CountsPath)) {
    $CountsPath = Join-Path $PSScriptRoot 'required-test-counts.json'
}
$ResultsDirectory = Resolve-RepositoryPath $ResultsDirectory

& (Join-Path $PSScriptRoot 'Get-EdgeDiscoveredTestInventory.ps1') `
    -RepositoryRoot $RepositoryRoot `
    -InventoryPath $InventoryPath `
    -DiscoveredInventoryPath $DiscoveredInventoryPath `
    -Configuration $Configuration

$inventory = Get-Content $InventoryPath -Raw | ConvertFrom-Json -Depth 20
$discoveredInventory = Get-Content $DiscoveredInventoryPath -Raw | ConvertFrom-Json -Depth 30
$counts = Get-Content $CountsPath -Raw | ConvertFrom-Json -Depth 20
if ([int]$inventory.schemaVersion -ne 3 -or [int]$discoveredInventory.schemaVersion -ne 3 -or
    [int]$counts.schemaVersion -ne 3 -or
    [string]$counts.configuration -ne $Configuration) {
    throw 'EDGE-TEST-RESULT-001 required count schema or configuration is invalid.'
}
$invalidRegressionIds = @($discoveredInventory.cases | Where-Object {
    [string]::IsNullOrWhiteSpace([string]$_.regressionId) -or
    [string]$_.regressionId -notmatch '^[A-Z0-9][A-Z0-9._-]+$'
})
if ([string]$discoveredInventory.defaultRegressionIdSource -cne 'project.ruleId' -or $invalidRegressionIds.Count -gt 0) {
    throw "EDGE-TEST-RESULT-001 discovered inventory regressionId contract is invalid; invalid=$($invalidRegressionIds.Count)."
}

$expectedByProject = @{}
foreach ($entry in @($counts.projects)) {
    $path = [string]$entry.projectPath
    if ($expectedByProject.ContainsKey($path) -or [int]$entry.discovered -le 0) {
        throw "EDGE-TEST-RESULT-001 invalid count entry for $path."
    }
    $expectedByProject[$path] = [int]$entry.discovered
}
if ([int]$counts.caseCount -ne [int]$discoveredInventory.caseCount) {
    throw 'EDGE-TEST-RESULT-001 required total count differs from discovered case inventory.'
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

    $discoveredEntry = @($discoveredInventory.projects | Where-Object { [string]$_.projectPath -eq $projectPath })
    if ($discoveredEntry.Count -ne 1) {
        throw "EDGE-TEST-RESULT-001 discovered project entry is not unique for $projectPath."
    }
    $discovered = [int]$discoveredEntry[0].discovered

    $trxCandidates = @(Get-ChildItem $ResultsDirectory -Recurse -Filter "$projectName.trx" -File -ErrorAction SilentlyContinue)
    if ($trxCandidates.Count -ne 1) {
        throw "EDGE-TEST-RESULT-001 TRX count mismatch for ${projectName}: expected=1 actual=$($trxCandidates.Count)."
    }
    $trxPath = $trxCandidates[0].FullName
    [xml]$trx = Get-Content $trxPath -Raw
    $namespace = [string]$trx.DocumentElement.NamespaceURI
    $manager = [Xml.XmlNamespaceManager]::new($trx.NameTable)
    $manager.AddNamespace('t', $namespace)
    $counterNodes = @($trx.SelectNodes('//t:ResultSummary/t:Counters', $manager))
    if ($counterNodes.Count -ne 1) {
        throw "EDGE-TEST-RESULT-001 TRX does not contain one counter summary: $trxPath"
    }
    $counters = [System.Xml.XmlElement]$counterNodes[0]
    $trxTotal = Get-CounterValue -Counters $counters -Name 'total'
    $executed = Get-CounterValue -Counters $counters -Name 'executed'
    $passed = Get-CounterValue -Counters $counters -Name 'passed'
    $failed = Get-CounterValue -Counters $counters -Name 'failed'
    $skipped = (Get-CounterValue -Counters $counters -Name 'notExecuted') +
        (Get-CounterValue -Counters $counters -Name 'inconclusive') +
        (Get-CounterValue -Counters $counters -Name 'notRunnable')

    if ($discovered -ne $expected -or
        $trxTotal -ne $discovered -or
        $executed -ne $discovered -or
        $passed -ne $executed -or
        $failed -ne 0 -or
        $skipped -ne 0) {
        throw "EDGE-TEST-RESULT-001 $projectName mismatch: expected=$expected discovered=$discovered trxTotal=$trxTotal executed=$executed passed=$passed failed=$failed skipped=$skipped."
    }

    $row = [pscustomobject][ordered]@{
        project = $projectName
        discovered = $discovered
        trxTotal = $trxTotal
        executed = $executed
        passed = $passed
        failed = $failed
        skipped = $skipped
    }
    $rows.Add($row)
    Write-Host "TEST_RESULT project=$projectName discovered=$discovered trxTotal=$trxTotal executed=$executed passed=$passed failed=$failed skipped=$skipped"
}

$summary = [pscustomobject][ordered]@{
    schemaVersion = 3
    configuration = $Configuration
    projects = [object[]]$rows
    totals = [pscustomobject][ordered]@{
        discovered = [int](($rows | Measure-Object discovered -Sum).Sum)
        trxTotal = [int](($rows | Measure-Object trxTotal -Sum).Sum)
        executed = [int](($rows | Measure-Object executed -Sum).Sum)
        passed = [int](($rows | Measure-Object passed -Sum).Sum)
        failed = [int](($rows | Measure-Object failed -Sum).Sum)
        skipped = [int](($rows | Measure-Object skipped -Sum).Sum)
    }
}
$totals = $summary.totals
if ($totals.discovered -ne $totals.trxTotal -or
    $totals.discovered -ne $totals.executed -or
    $totals.executed -ne $totals.passed -or
    $totals.failed -ne 0 -or
    $totals.skipped -ne 0) {
    throw "EDGE-TEST-RESULT-001 total mismatch: discovered=$($totals.discovered) trxTotal=$($totals.trxTotal) executed=$($totals.executed) passed=$($totals.passed) failed=$($totals.failed) skipped=$($totals.skipped)."
}
if (-not [string]::IsNullOrWhiteSpace($SummaryPath)) {
    $resolvedSummary = Resolve-RepositoryPath $SummaryPath
    [void](New-Item (Split-Path $resolvedSummary -Parent) -ItemType Directory -Force)
    [IO.File]::WriteAllText(
        $resolvedSummary,
        (($summary | ConvertTo-Json -Depth 20) + "`n"),
        [Text.UTF8Encoding]::new($false))
}

Write-Host "TEST_TOTAL discovered=$($summary.totals.discovered) trxTotal=$($summary.totals.trxTotal) executed=$($summary.totals.executed) passed=$($summary.totals.passed) failed=$($summary.totals.failed) skipped=$($summary.totals.skipped)"
