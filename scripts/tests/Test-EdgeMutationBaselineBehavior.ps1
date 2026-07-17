[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$gate = Join-Path $PSScriptRoot 'Test-EdgeMutationBaseline.ps1'
$workingRoot = Join-Path ([IO.Path]::GetTempPath()) "edge-mutation-baseline-$([Guid]::NewGuid().ToString('N'))"
$outputDirectory = Join-Path $workingRoot 'artifacts/mutation/edge-domain'
$reportPath = Join-Path $outputDirectory 'reports/mutation-report.json'
$logPath = Join-Path $outputDirectory 'stryker-console.log'
$baselinePath = Join-Path $workingRoot 'scripts/tests/baselines/edge-mutation-baseline.json'
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

function Write-Json {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object]$Value
    )

    [void](New-Item (Split-Path $Path -Parent) -ItemType Directory -Force)
    $Value | ConvertTo-Json -Depth 32 | Set-Content $Path -Encoding utf8
}

function New-Mutant {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$Status
    )

    $coveredBy = if ($Status -in @('Killed', 'Survived')) { @('test-1') } else { @() }
    $killedBy = if ($Status -eq 'Killed') { @('test-1') } else { @() }
    return [ordered]@{
        id = $Id
        status = $Status
        coveredBy = $coveredBy
        killedBy = $killedBy
    }
}

function Write-Report {
    param([Parameter(Mandatory)][string[]]$Statuses)

    $mutants = for ($index = 0; $index -lt $Statuses.Count; $index++) {
        New-Mutant -Id ([string]($index + 1)) -Status $Statuses[$index]
    }
    Write-Json $reportPath ([ordered]@{
        files = [ordered]@{
            'Aggregate.cs' = [ordered]@{ mutants = @($mutants) }
        }
    })
}

function Invoke-Gate {
    $output = (& pwsh -NoLogo -NoProfile -File $gate `
        -RepositoryRoot $workingRoot `
        -OutputDirectory $outputDirectory `
        -BaselinePath $baselinePath 2>&1 | Out-String).TrimEnd()
    $exitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    return [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory)][object]$Result,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][string]$Pattern
    )

    if ($Result.ExitCode -eq 0 -or $Result.Output -notmatch $Pattern) {
        throw "$Label did not fail closed. exit=$($Result.ExitCode), output=$($Result.Output)"
    }
}

try {
    Write-Json (Join-Path $workingRoot '.config/dotnet-tools.json') ([ordered]@{
        version = 1
        isRoot = $true
        tools = [ordered]@{
            'dotnet-stryker' = [ordered]@{
                version = '4.16.0'
                commands = @('dotnet-stryker')
            }
        }
    })
    Write-Json (Join-Path $workingRoot 'src/Tests/IIoT.Edge.Domain.Tests/stryker-config.json') ([ordered]@{
        'stryker-config' = [ordered]@{
            project = 'IIoT.Edge.Domain.csproj'
            'test-projects' = @('IIoT.Edge.Domain.Tests.csproj')
            mutate = @('Config/Aggregates/*.cs', 'Hardware/Aggregates/*.cs')
            reporters = @('json')
            'test-runner' = 'mtp'
            concurrency = 1
        }
    })
    $semanticSource = $requiredSemanticTests |
        ForEach-Object { "    public void $($_)() {}" }
    [void](New-Item (Join-Path $workingRoot 'src/Tests/IIoT.Edge.Domain.Tests') -ItemType Directory -Force)
    @('public sealed class MutationSemanticFixture', '{') + $semanticSource + @('}') |
        Set-Content (Join-Path $workingRoot 'src/Tests/IIoT.Edge.Domain.Tests/MutationSemanticFixture.cs') -Encoding utf8

    $statuses = @(
        'Killed', 'Killed', 'Killed', 'Killed', 'Killed', 'Killed',
        'Survived', 'Survived', 'NoCoverage', 'Ignored', 'Timeout', 'CompileError'
    )
    Write-Report -Statuses $statuses
    [void](New-Item $outputDirectory -ItemType Directory -Force)
    @(
        'Number of tests found: 8',
        '14 mutants created',
        '9 total mutants will be tested'
    ) | Set-Content $logPath -Encoding utf8

    $baseline = [ordered]@{
        schemaVersion = 1
        ruleId = 'TEST-GOV-007'
        mode = 'report-only'
        testRunner = 'mtp'
        tool = 'dotnet-stryker/4.16.0'
        targetProject = 'src/Core/IIoT.Edge.Domain/IIoT.Edge.Domain.csproj'
        testProject = 'src/Tests/IIoT.Edge.Domain.Tests/IIoT.Edge.Domain.Tests.csproj'
        mutate = @('Config/Aggregates/*.cs', 'Hardware/Aggregates/*.cs')
        requiredSemanticTests = $requiredSemanticTests
        initialTestCount = 8
        createdMutants = 14
        totalMutants = 12
        evaluatedMutants = 9
        detected = 6
        survived = 2
        noCoverage = 1
        ignored = 1
        timeout = 1
        compileErrors = 1
        mutationScore = 0.7
        artifact = 'artifacts/mutation/edge-domain/reports/mutation-report.json'
        traceLog = 'artifacts/mutation/edge-domain/stryker-console.log'
    }
    Write-Json $baselinePath $baseline

    $valid = Invoke-Gate
    if ($valid.ExitCode -ne 0) {
        throw "Exact mutation report and baseline should pass. output=$($valid.Output)"
    }

    $falsifiedScore = Get-Content $baselinePath -Raw | ConvertFrom-Json -Depth 32
    $falsifiedScore.mutationScore = 0.9
    Write-Json $baselinePath $falsifiedScore
    Assert-Rejected (Invoke-Gate) 'Falsified baseline score' 'baseline result ledger is inconsistent'
    Write-Json $baselinePath $baseline

    $reportMutations = @(
        [pscustomobject]@{ Label = 'killed-to-survived'; Index = 0; Status = 'Survived' },
        [pscustomobject]@{ Label = 'no-coverage-to-ignored'; Index = 8; Status = 'Ignored' },
        [pscustomobject]@{ Label = 'ignored-to-compile-error'; Index = 9; Status = 'CompileError' },
        [pscustomobject]@{ Label = 'killed-to-timeout'; Index = 0; Status = 'Timeout' }
    )
    foreach ($mutation in $reportMutations) {
        $changed = @($statuses)
        $changed[$mutation.Index] = $mutation.Status
        Write-Report -Statuses $changed
        Assert-Rejected (Invoke-Gate) "Report status drift $($mutation.Label)" 'mutation result ledger changed'
    }

    Write-Report -Statuses $statuses
    $final = Invoke-Gate
    if ($final.ExitCode -ne 0) {
        throw "Restored exact mutation report should pass. output=$($final.Output)"
    }

    Write-Host 'EDGE_MUTATION_BASELINE_BEHAVIOR_OK valid=2 falsifiedScore=1 statusDrift=4'
} finally {
    Remove-Item $workingRoot -Recurse -Force -ErrorAction SilentlyContinue
    $global:LASTEXITCODE = 0
}
