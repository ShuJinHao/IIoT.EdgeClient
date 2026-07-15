[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$InventoryPath,
    [string]$ResultsDirectory = 'artifacts/test-results',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$CollectCoverage,
    [ValidateRange(1, 8)]
    [int]$PureThrottle = 4
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
$ResultsDirectory = Resolve-RepositoryPath $ResultsDirectory

& (Join-Path $PSScriptRoot 'Get-EdgeTestInventory.ps1') `
    -RepositoryRoot $RepositoryRoot `
    -InventoryPath $InventoryPath

$inventory = Get-Content $InventoryPath -Raw | ConvertFrom-Json -Depth 20
if (Test-Path $ResultsDirectory -PathType Container) {
    Remove-Item $ResultsDirectory -Recurse -Force
}
[void](New-Item $ResultsDirectory -ItemType Directory -Force)

function New-TestJob {
    param([Parameter(Mandatory)][object]$Project)

    $project = $Project
    $projectPath = Resolve-RepositoryPath ([string]$project.projectPath)
    $projectName = [string]$project.projectName
    $trxName = "$projectName.trx"
    $projectResultsDirectory = Join-Path $ResultsDirectory $projectName
    [void](New-Item $projectResultsDirectory -ItemType Directory -Force)

    $testArguments = @(
        'test',
        $projectPath,
        '-c',
        $Configuration,
        '--no-build',
        '--no-restore',
        '--logger',
        "trx;LogFileName=$trxName",
        '--results-directory',
        $projectResultsDirectory,
        '-p:BuildInParallel=false',
        '--disable-build-servers',
        '--nologo',
        '-noAutoResponse'
    )
    if ($CollectCoverage) {
        $testArguments += @(
            '--collect',
            'XPlat Code Coverage;Format=cobertura'
        )
    }

    return [pscustomobject]@{
        ProjectName = $projectName
        TestKind = [string]$project.testKind
        Runtime = [string]$project.runtime
        RunnerMode = [string]$project.runnerMode
        Cadence = [string]$project.cadence
        Arguments = [string[]]$testArguments
    }
}

$requiredProjects = @($inventory.projects | Where-Object { [bool]$_.required })
$pureProjects = @($requiredProjects | Where-Object {
    [string]$_.runtime -ceq 'Pure' -and [string]$_.runnerMode -ceq 'Parallel'
})
$resourceProjects = @($requiredProjects | Where-Object {
    [string]$_.runtime -cne 'Pure' -or [string]$_.runnerMode -cne 'Parallel'
})
$invalidResourceModes = @($resourceProjects | Where-Object { [string]$_.runnerMode -cne 'Serial' })
if ($pureProjects.Count -eq 0 -or $invalidResourceModes.Count -gt 0) {
    throw "EDGE-TEST-RUN-001 execution taxonomy is invalid: pureParallel=$($pureProjects.Count), nonPureNonSerial=$($invalidResourceModes.Count)."
}

$pureJobs = @($pureProjects | ForEach-Object { New-TestJob -Project $_ })
$resourceJobs = @($resourceProjects | ForEach-Object { New-TestJob -Project $_ })
Write-Host "TEST_SCHEDULE pureParallel=$($pureJobs.Count) resourceSerial=$($resourceJobs.Count) throttle=$PureThrottle coverage=$($CollectCoverage.IsPresent)"
foreach ($job in $pureJobs) {
    Write-Host "TEST_RUN schedule=parallel project=$($job.ProjectName) kind=$($job.TestKind) runtime=$($job.Runtime) mode=$($job.RunnerMode) cadence=$($job.Cadence)"
}

$parallelResults = @($pureJobs | ForEach-Object -Parallel {
    $job = $_
    $nativeOutput = @(& dotnet @($job.Arguments) 2>&1 | ForEach-Object { $_.ToString() })
    [pscustomobject]@{
        ProjectName = [string]$job.ProjectName
        ExitCode = [int]$LASTEXITCODE
        Output = [string[]]$nativeOutput
    }
} -ThrottleLimit $PureThrottle)

if ($parallelResults.Count -ne $pureJobs.Count -or
    @($parallelResults.ProjectName | Sort-Object -Unique).Count -ne $pureJobs.Count) {
    throw "EDGE-TEST-RUN-001 parallel runner result set is incomplete: expected=$($pureJobs.Count), actual=$($parallelResults.Count)."
}
foreach ($result in @($parallelResults | Sort-Object ProjectName)) {
    foreach ($line in @($result.Output)) { Write-Host $line }
    Write-Host "TEST_RUN_RESULT schedule=parallel project=$($result.ProjectName) exitCode=$($result.ExitCode)"
}
$parallelFailures = @($parallelResults | Where-Object { [int]$_.ExitCode -ne 0 })
if ($parallelFailures.Count -gt 0) {
    throw "EDGE-TEST-RUN-001 required Pure test project failed: $(@($parallelFailures.ProjectName) -join ', ')."
}

foreach ($job in $resourceJobs) {
    Write-Host "TEST_RUN schedule=serial project=$($job.ProjectName) kind=$($job.TestKind) runtime=$($job.Runtime) mode=$($job.RunnerMode) cadence=$($job.Cadence)"
    & dotnet @($job.Arguments)
    $exitCode = $LASTEXITCODE
    Write-Host "TEST_RUN_RESULT schedule=serial project=$($job.ProjectName) exitCode=$exitCode"
    if ($exitCode -ne 0) {
        throw "EDGE-TEST-RUN-001 required resource test project failed: $($job.ProjectName)."
    }
}

Write-Host "Edge required test execution completed: projects=$($requiredProjects.Count), pureParallel=$($pureJobs.Count), resourceSerial=$($resourceJobs.Count), throttle=$PureThrottle."
