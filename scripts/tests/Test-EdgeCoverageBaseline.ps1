[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$ResultsDirectory = 'artifacts/test-results',
    [string]$InventoryPath,
    [string]$BaselinePath,
    [switch]$Update
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

function Resolve-RepositoryPath([string]$PathValue) {
    if ([IO.Path]::IsPathRooted($PathValue)) {
        return [IO.Path]::GetFullPath($PathValue)
    }
    return [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $PathValue))
}

$ResultsDirectory = Resolve-RepositoryPath $ResultsDirectory
$InventoryPath = if ([string]::IsNullOrWhiteSpace($InventoryPath)) {
    Join-Path $PSScriptRoot 'edge-test-inventory.json'
} else {
    Resolve-RepositoryPath $InventoryPath
}
$BaselinePath = if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    Join-Path $PSScriptRoot 'baselines/edge-coverage-baseline.json'
} else {
    Resolve-RepositoryPath $BaselinePath
}

$highRiskThresholds = @(
    [ordered]@{
        path = 'src/Application/IIoT.Edge.Application/Common/Caching/Memory/EdgeMemoryCacheService.cs'
        minimumLineRate = 0.90
        minimumBranchRate = 0.85
    },
    [ordered]@{
        path = 'src/Application/IIoT.Edge.Application/Abstractions/DataPipeline/DataPipelineNonRetryableException.cs'
        minimumLineRate = 0.90
        minimumBranchRate = 0.85
    }
)

if (-not (Test-Path $InventoryPath -PathType Leaf)) {
    throw "TEST-GOV-007 coverage inventory does not exist: $InventoryPath"
}
$inventory = Get-Content $InventoryPath -Raw | ConvertFrom-Json -Depth 32
$requiredProjects = @($inventory.projects | Where-Object { $_.required -eq $true })
if ($requiredProjects.Count -eq 0) {
    throw 'TEST-GOV-007 coverage cannot run with zero required test projects.'
}

$coverageFiles = @(Get-ChildItem $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml' -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '[/\\]In[/\\]' })
$trxFiles = @(Get-ChildItem $ResultsDirectory -Recurse -Filter '*.trx' -File -ErrorAction SilentlyContinue)
if ($coverageFiles.Count -ne $requiredProjects.Count) {
    throw "TEST-GOV-007 coverage report count mismatch: required=$($requiredProjects.Count), reports=$($coverageFiles.Count). Every required runner must emit exactly one report."
}
if ($trxFiles.Count -ne $requiredProjects.Count) {
    throw "TEST-GOV-007 TRX count mismatch while validating coverage: required=$($requiredProjects.Count), trx=$($trxFiles.Count)."
}

$lineMap = @{}
foreach ($coverageFile in $coverageFiles) {
    [xml]$document = Get-Content $coverageFile.FullName -Raw
    foreach ($class in @($document.SelectNodes('/coverage/packages/package/classes/class'))) {
        $path = ([string]$class.filename).Replace('\', '/')
        $srcIndex = $path.IndexOf('src/', [StringComparison]::OrdinalIgnoreCase)
        if ($srcIndex -ge 0) {
            $path = $path.Substring($srcIndex)
        } elseif (Test-Path (Join-Path $RepositoryRoot "src/$path") -PathType Leaf) {
            $path = "src/$path"
        } elseif (-not (Test-Path (Join-Path $RepositoryRoot $path) -PathType Leaf)) {
            continue
        }
        if ($path -match '^src/(?:Tests|Testing)/' -or
            $path -match '/(?:bin|obj)/' -or
            $path -match '\.(?:g|AssemblyInfo)\.cs$') {
            continue
        }

        foreach ($line in @($class.SelectNodes('lines/line'))) {
            if ($null -eq $line) {
                continue
            }
            $lineNumber = [int]$line.number
            $hits = [int]$line.hits
            $key = "$path`:$lineNumber"
            if (-not $lineMap.ContainsKey($key)) {
                $lineMap[$key] = [pscustomobject]@{
                    Path = $path
                    Line = $lineNumber
                    Covered = $false
                    BranchValid = 0
                    BranchCovered = 0
                }
            }
            $record = $lineMap[$key]
            if ($hits -gt 0) {
                $record.Covered = $true
            }
            $conditionCoverage = ([Xml.XmlElement]$line).GetAttribute('condition-coverage')
            if ($conditionCoverage -match '\((\d+)\s*/\s*(\d+)\)') {
                $covered = [int]$Matches[1]
                $valid = [int]$Matches[2]
                if ($valid -gt $record.BranchValid -or
                    ($valid -eq $record.BranchValid -and $covered -gt $record.BranchCovered)) {
                    $record.BranchValid = $valid
                    $record.BranchCovered = $covered
                }
            }
        }
    }
}

if ($lineMap.Count -eq 0) {
    throw 'TEST-GOV-007 coverage reports contain zero production source lines.'
}

function Get-Metric([object[]]$Lines) {
    $lineValid = $Lines.Count
    $lineCovered = @($Lines | Where-Object Covered).Count
    $branchValid = [int](($Lines | Measure-Object -Property BranchValid -Sum).Sum)
    $branchCovered = [int](($Lines | Measure-Object -Property BranchCovered -Sum).Sum)
    return [ordered]@{
        lineValid = $lineValid
        lineCovered = $lineCovered
        lineRate = if ($lineValid -eq 0) { 1.0 } else { [Math]::Round($lineCovered / $lineValid, 6) }
        branchValid = $branchValid
        branchCovered = $branchCovered
        branchRate = if ($branchValid -eq 0) { 1.0 } else { [Math]::Round($branchCovered / $branchValid, 6) }
    }
}

$allLines = @($lineMap.Values)
$componentMetrics = [Collections.Generic.List[object]]::new()
foreach ($componentGroup in @($allLines | Group-Object { ($_.Path -split '/')[1] } | Sort-Object Name)) {
    $componentMetrics.Add([ordered]@{
        component = $componentGroup.Name
        metrics = Get-Metric @($componentGroup.Group)
    })
}

$fileMetrics = @{}
foreach ($fileGroup in @($allLines | Group-Object Path)) {
    $fileMetrics[$fileGroup.Name] = Get-Metric @($fileGroup.Group)
}
$thresholdResults = [Collections.Generic.List[object]]::new()
foreach ($threshold in $highRiskThresholds) {
    $path = [string]$threshold.path
    if (-not $fileMetrics.ContainsKey($path)) {
        throw "TEST-GOV-007 high-risk source is absent from coverage: $path"
    }
    $metrics = $fileMetrics[$path]
    if ($metrics.lineRate -lt [double]$threshold.minimumLineRate -or
        ($metrics.branchValid -gt 0 -and $metrics.branchRate -lt [double]$threshold.minimumBranchRate)) {
        throw "TEST-GOV-007 high-risk threshold failed for ${path}: line=$($metrics.lineRate) (min=$($threshold.minimumLineRate)), branch=$($metrics.branchRate) (min=$($threshold.minimumBranchRate))."
    }
    $thresholdResults.Add([ordered]@{
        path = $path
        minimumLineRate = $threshold.minimumLineRate
        minimumBranchRate = $threshold.minimumBranchRate
        metrics = $metrics
    })
}

$actual = [ordered]@{
    schemaVersion = 1
    ruleId = 'TEST-GOV-007'
    collector = 'coverlet.collector/10.0.1'
    requiredReportCount = $requiredProjects.Count
    reportCount = $coverageFiles.Count
    productionFileCount = @($fileMetrics.Keys).Count
    overall = Get-Metric $allLines
    components = @($componentMetrics)
    highRiskThresholds = @($thresholdResults)
}

if ($Update) {
    [void](New-Item (Split-Path $BaselinePath -Parent) -ItemType Directory -Force)
    $actual | ConvertTo-Json -Depth 12 | Set-Content $BaselinePath -Encoding utf8
    Write-Host "Edge coverage baseline updated: reports=$($actual.reportCount), files=$($actual.productionFileCount), line=$($actual.overall.lineRate), branch=$($actual.overall.branchRate)."
    return
}

if (-not (Test-Path $BaselinePath -PathType Leaf)) {
    throw "TEST-GOV-007 coverage baseline does not exist: $BaselinePath"
}
$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json -Depth 32
if ($baseline.schemaVersion -ne 1 -or $baseline.ruleId -ne 'TEST-GOV-007' -or
    $baseline.collector -ne 'coverlet.collector/10.0.1') {
    throw 'TEST-GOV-007 coverage baseline schema/tool pin is invalid.'
}
if ($baseline.requiredReportCount -ne $requiredProjects.Count -or
    $baseline.reportCount -ne $requiredProjects.Count) {
    throw 'TEST-GOV-007 coverage baseline target drifted from the required runner inventory.'
}

$findings = [Collections.Generic.List[string]]::new()
if ($actual.overall.lineRate -lt [double]$baseline.overall.lineRate -or
    $actual.overall.branchRate -lt [double]$baseline.overall.branchRate -or
    $actual.overall.lineCovered -lt [int]$baseline.overall.lineCovered -or
    $actual.overall.branchCovered -lt [int]$baseline.overall.branchCovered) {
    $findings.Add("overall regressed: line $($baseline.overall.lineCovered)/$($baseline.overall.lineRate) -> $($actual.overall.lineCovered)/$($actual.overall.lineRate), branch $($baseline.overall.branchCovered)/$($baseline.overall.branchRate) -> $($actual.overall.branchCovered)/$($actual.overall.branchRate)")
}

$actualComponents = @{}
foreach ($component in $actual.components) { $actualComponents[[string]$component.component] = $component.metrics }
foreach ($component in @($baseline.components)) {
    $name = [string]$component.component
    if (-not $actualComponents.ContainsKey($name)) {
        $findings.Add("covered production component disappeared: $name")
        continue
    }
    $current = $actualComponents[$name]
    if ($current.lineRate -lt [double]$component.metrics.lineRate -or
        $current.branchRate -lt [double]$component.metrics.branchRate) {
        $findings.Add("component $name coverage rate regressed: line $($component.metrics.lineRate)->$($current.lineRate), branch $($component.metrics.branchRate)->$($current.branchRate)")
    }
}

if ($findings.Count -gt 0) {
    throw "TEST-GOV-007 coverage ratchet failed:`n - $($findings -join "`n - ")"
}

Write-Host "Edge coverage ratchet passed: reports=$($actual.reportCount), files=$($actual.productionFileCount), line=$($actual.overall.lineRate), branch=$($actual.overall.branchRate), highRisk=$($thresholdResults.Count)."
