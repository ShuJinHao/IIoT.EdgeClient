[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$OutputDirectory = 'artifacts/mutation/edge-domain',
    [string]$BaselinePath,
    [switch]$Collect,
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

$OutputDirectory = Resolve-RepositoryPath $OutputDirectory
$BaselinePath = if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    Join-Path $PSScriptRoot 'baselines/edge-mutation-baseline.json'
} else {
    Resolve-RepositoryPath $BaselinePath
}
$configPath = Join-Path $RepositoryRoot 'src/Tests/IIoT.Edge.Domain.Tests/stryker-config.json'
$toolManifestPath = Join-Path $RepositoryRoot '.config/dotnet-tools.json'
$expectedTarget = 'src/Core/IIoT.Edge.Domain/IIoT.Edge.Domain.csproj'
$expectedTestProject = 'src/Tests/IIoT.Edge.Domain.Tests/IIoT.Edge.Domain.Tests.csproj'
$expectedMutate = @('Config/Aggregates/*.cs', 'Hardware/Aggregates/*.cs')
$requiredSemanticTests = @(
    'SystemConfigEntity_WhenKeyInvalid_ShouldReject',
    'SystemConfigEntity_WhenSortOrderInvalid_ShouldReject',
    'DeviceParamEntity_WhenRequiredFieldsInvalid_ShouldReject',
    'DeviceParamEntity_WhenSortOrderInvalid_ShouldReject',
    'NetworkDeviceEntity_WhenRequiredFieldsInvalid_ShouldReject',
    'NetworkDeviceEntity_WhenNavigationCollectionsExposed_ShouldBeReadOnly',
    'SerialDeviceEntity_WhenPortFieldsInvalid_ShouldReject',
    'IoMappingEntity_WhenRequiredFieldsInvalid_ShouldReject',
    'IoMappingEntity_WhenPlcAddressEmpty_ShouldKeepUnconfiguredState'
)

$manifest = Get-Content $toolManifestPath -Raw | ConvertFrom-Json -Depth 16
$tool = $manifest.tools.'dotnet-stryker'
if ($null -eq $tool -or $tool.version -ne '4.16.0' -or @($tool.commands) -notcontains 'dotnet-stryker') {
    throw 'TEST-GOV-007 mutation tool must remain pinned to dotnet-stryker/4.16.0.'
}

$config = (Get-Content $configPath -Raw | ConvertFrom-Json -Depth 16).'stryker-config'
if ($config.project -ne 'IIoT.Edge.Domain.csproj' -or
    @($config.'test-projects').Count -ne 1 -or
    @($config.'test-projects')[0] -ne 'IIoT.Edge.Domain.Tests.csproj' -or
    (@($config.mutate) -join '|') -ne ($expectedMutate -join '|') -or
    @($config.reporters).Count -ne 1 -or @($config.reporters)[0] -ne 'json' -or
    [string]$config.'test-runner' -ne 'mtp' -or
    [int]$config.concurrency -ne 1) {
    throw 'TEST-GOV-007 mutation target/config drifted from the reviewed Domain Aggregate MTP report-only scope.'
}

$testText = @(Get-ChildItem (Split-Path $expectedTestProject -Parent | ForEach-Object { Join-Path $RepositoryRoot $_ }) -Filter '*.cs' -File |
    ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
foreach ($semanticTest in $requiredSemanticTests) {
    if (-not $testText.Contains($semanticTest, [StringComparison]::Ordinal)) {
        throw "TEST-GOV-007 required mutation regression semantic test is missing: $semanticTest"
    }
}

if ($Collect) {
    if (Test-Path $OutputDirectory) {
        Remove-Item $OutputDirectory -Recurse -Force
    }
    [void](New-Item $OutputDirectory -ItemType Directory -Force)
    $consoleLogPath = Join-Path $OutputDirectory 'stryker-console.log'
    Push-Location (Join-Path $RepositoryRoot 'src/Tests/IIoT.Edge.Domain.Tests')
    try {
        $strykerOutput = & dotnet stryker `
            --config-file 'stryker-config.json' `
            --output $OutputDirectory `
            --configuration Release `
            --skip-version-check `
            --log-to-file `
            --verbosity trace 2>&1
        [IO.File]::WriteAllLines(
            $consoleLogPath,
            [string[]]@($strykerOutput | ForEach-Object { $_.ToString() }),
            [Text.UTF8Encoding]::new($false))
        @($strykerOutput | Where-Object {
            $_.ToString() -match 'Number of tests found:|mutants created\s*$|total mutants will be tested\s*$|report has been generated'
        }) | ForEach-Object { Write-Host $_.ToString() }
        if ($LASTEXITCODE -ne 0) {
            throw "TEST-GOV-007 dotnet-stryker failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }
}

$reports = @(Get-ChildItem $OutputDirectory -Recurse -Filter 'mutation-report.json' -File -ErrorAction SilentlyContinue)
if ($reports.Count -ne 1) {
    throw "TEST-GOV-007 mutation artifact count mismatch: expected=1 actual=$($reports.Count)."
}
$report = Get-Content $reports[0].FullName -Raw | ConvertFrom-Json -Depth 64
$mutants = @($report.files.PSObject.Properties | ForEach-Object { @($_.Value.mutants) })
if ($mutants.Count -eq 0) {
    throw 'TEST-GOV-007 mutation report is empty; target drift or an empty run cannot satisfy the gate.'
}

$statusCounts = @{}
foreach ($status in @('Killed', 'Survived', 'NoCoverage', 'Ignored', 'Timeout', 'CompileError')) {
    $statusCounts[$status] = @($mutants | Where-Object { $_.status -eq $status }).Count
}
$knownCount = [int](($statusCounts.Values | Measure-Object -Sum).Sum)
if ($knownCount -ne $mutants.Count) {
    $unknown = @($mutants | Where-Object { $_.status -notin $statusCounts.Keys } | Select-Object -ExpandProperty status -Unique)
    throw "TEST-GOV-007 mutation report contains unknown statuses: $($unknown -join ', ')."
}
$scoreDenominator = $statusCounts.Killed + $statusCounts.Survived + $statusCounts.NoCoverage + $statusCounts.Timeout
$evaluatedMutants = $statusCounts.Killed + $statusCounts.Survived + $statusCounts.Timeout
$invalidKilledEvidence = @($mutants | Where-Object {
    $_.status -eq 'Killed' -and (@($_.coveredBy).Count -eq 0 -or @($_.killedBy).Count -eq 0)
})
$invalidSurvivorEvidence = @($mutants | Where-Object {
    $_.status -eq 'Survived' -and @($_.coveredBy).Count -eq 0
})
if ($statusCounts.Killed -le 0 -or $evaluatedMutants -le 0 -or
    $invalidKilledEvidence.Count -gt 0 -or $invalidSurvivorEvidence.Count -gt 0) {
    throw "TEST-GOV-007 mutation execution is not credible: evaluated=$evaluatedMutants killed=$($statusCounts.Killed) killedWithoutEvidence=$($invalidKilledEvidence.Count) survivedWithoutCoverage=$($invalidSurvivorEvidence.Count)."
}

$consoleLogs = @(Get-ChildItem $OutputDirectory -Filter 'stryker-console.log' -File -ErrorAction SilentlyContinue)
if ($consoleLogs.Count -ne 1) {
    throw "TEST-GOV-007 mutation console log count mismatch: expected=1 actual=$($consoleLogs.Count)."
}
$consoleText = Get-Content $consoleLogs[0].FullName -Raw
if ($consoleText -notmatch 'Number of tests found:\s*(\d+)' -or
    $consoleText -notmatch '(?m)^.*?([0-9]+) mutants created\s*$') {
    throw 'TEST-GOV-007 mutation trace lacks initial test or created-mutant evidence.'
}
$initialTestCount = [int]([regex]::Match($consoleText, 'Number of tests found:\s*(\d+)').Groups[1].Value)
$createdMutants = [int]([regex]::Match($consoleText, '(?m)^.*?([0-9]+) mutants created\s*$').Groups[1].Value)
$testedMatch = [regex]::Match($consoleText, '(?m)^.*?([0-9]+)\s+total mutants will be tested\s*$')
if (-not $testedMatch.Success -or [int]$testedMatch.Groups[1].Value -ne $evaluatedMutants -or $initialTestCount -le 0) {
    throw "TEST-GOV-007 mutation trace/result mismatch: tests=$initialTestCount created=$createdMutants traceEvaluated=$($testedMatch.Groups[1].Value) reportEvaluated=$evaluatedMutants."
}
$mutationScore = if ($scoreDenominator -eq 0) {
    0.0
} else {
    [Math]::Round(($statusCounts.Killed + $statusCounts.Timeout) / $scoreDenominator, 6)
}

$actual = [ordered]@{
    schemaVersion = 1
    ruleId = 'TEST-GOV-007'
    mode = 'report-only'
    testRunner = 'mtp'
    tool = 'dotnet-stryker/4.16.0'
    targetProject = $expectedTarget
    testProject = $expectedTestProject
    mutate = $expectedMutate
    requiredSemanticTests = $requiredSemanticTests
    initialTestCount = $initialTestCount
    createdMutants = $createdMutants
    totalMutants = $mutants.Count
    evaluatedMutants = $evaluatedMutants
    detected = $statusCounts.Killed
    survived = $statusCounts.Survived
    noCoverage = $statusCounts.NoCoverage
    ignored = $statusCounts.Ignored
    timeout = $statusCounts.Timeout
    compileErrors = $statusCounts.CompileError
    mutationScore = $mutationScore
    artifact = [IO.Path]::GetRelativePath($RepositoryRoot, $reports[0].FullName).Replace('\', '/')
    traceLog = [IO.Path]::GetRelativePath($RepositoryRoot, $consoleLogs[0].FullName).Replace('\', '/')
}

if ($Update) {
    [void](New-Item (Split-Path $BaselinePath -Parent) -ItemType Directory -Force)
    [ordered]@{
        schemaVersion = 2
        ruleId = 'TEST-GOV-007'
        mode = 'report-only'
        testRunner = 'mtp'
        tool = 'dotnet-stryker/4.16.0'
        targetProject = $expectedTarget
        testProject = $expectedTestProject
        mutate = $expectedMutate
        requiredSemanticTests = $requiredSemanticTests
        minimumMutationScore = $mutationScore
    } | ConvertTo-Json -Depth 12 | Set-Content $BaselinePath -Encoding utf8
    Write-Host "Edge mutation policy updated: minimumScore=$mutationScore scope=$expectedTarget."
    return
}

if (-not (Test-Path $BaselinePath -PathType Leaf)) {
    throw "TEST-GOV-007 mutation baseline does not exist: $BaselinePath"
}
$baseline = Get-Content $BaselinePath -Raw | ConvertFrom-Json -Depth 32
if ($baseline.schemaVersion -ne 2 -or $baseline.ruleId -ne 'TEST-GOV-007' -or
    $baseline.mode -ne 'report-only' -or $baseline.tool -ne 'dotnet-stryker/4.16.0' -or
    $baseline.testRunner -ne 'mtp' -or
    $baseline.targetProject -ne $expectedTarget -or $baseline.testProject -ne $expectedTestProject -or
    (@($baseline.mutate) -join '|') -ne ($expectedMutate -join '|') -or
    (@($baseline.requiredSemanticTests) -join '|') -ne ($requiredSemanticTests -join '|')) {
    throw 'TEST-GOV-007 mutation baseline target/tool/semantic ledger drifted.'
}
$minimumMutationScore = [double]$baseline.minimumMutationScore
if ($minimumMutationScore -lt 0.0 -or $minimumMutationScore -gt 1.0) {
    throw "TEST-GOV-007 minimumMutationScore is outside [0,1]: $minimumMutationScore."
}
if ([double]$actual.mutationScore -lt $minimumMutationScore) {
    throw "TEST-GOV-007 mutation score is below the production quality policy: minimum=$minimumMutationScore actual=$($actual.mutationScore)."
}

Write-Host "Edge mutation quality gate passed: currentTests=$($actual.initialTestCount), currentMutants=$($actual.totalMutants), evaluated=$($actual.evaluatedMutants), score=$($actual.mutationScore), minimum=$minimumMutationScore, emptyRun=0."
