[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$gate = Join-Path $PSScriptRoot 'Test-EdgeGovernanceBaselineMonotonicity.ps1'
$workingRoot = Join-Path ([IO.Path]::GetTempPath()) "edge-baseline-monotonicity-$([Guid]::NewGuid().ToString('N'))"

function Invoke-Git {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string[]]$Arguments
    )
    $output = (& git -C $RepositoryRoot @Arguments 2>&1 | Out-String).TrimEnd()
    $exitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    if ($exitCode -ne 0) {
        throw "Fixture git $($Arguments -join ' ') failed with exit code ${exitCode}:`n$output"
    }
    return $output
}

function Invoke-Gate {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$BaseRef
    )
    $output = (& pwsh -NoLogo -NoProfile -File $gate -RepositoryRoot $RepositoryRoot -BaseRef $BaseRef 2>&1 | Out-String).TrimEnd()
    $exitCode = $LASTEXITCODE
    $global:LASTEXITCODE = 0
    return [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

function Write-Json {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object]$Value
    )
    [void](New-Item (Split-Path $Path -Parent) -ItemType Directory -Force)
    $Value | ConvertTo-Json -Depth 32 | Set-Content $Path -Encoding utf8
}

function Assert-FailedWith {
    param(
        [Parameter(Mandatory)][object]$Result,
        [Parameter(Mandatory)][string]$Pattern,
        [Parameter(Mandatory)][string]$Label
    )
    if ($Result.ExitCode -eq 0 -or $Result.Output -notmatch $Pattern) {
        throw "$Label did not fail closed as expected. exit=$($Result.ExitCode), output=$($Result.Output)"
    }
}

try {
    [void](New-Item $workingRoot -ItemType Directory -Force)
    Invoke-Git $workingRoot @('init') | Out-Null
    Invoke-Git $workingRoot @('config', 'user.email', 'edge-baseline-fixture@example.invalid') | Out-Null
    Invoke-Git $workingRoot @('config', 'user.name', 'Edge Baseline Fixture') | Out-Null
    Set-Content (Join-Path $workingRoot 'README.md') 'fixture' -Encoding utf8
    Invoke-Git $workingRoot @('add', 'README.md') | Out-Null
    Invoke-Git $workingRoot @('commit', '-m', 'seed') | Out-Null
    $baseRef = (Invoke-Git $workingRoot @('rev-parse', 'HEAD')).Trim()
    Set-Content (Join-Path $workingRoot 'CANDIDATE.md') 'candidate' -Encoding utf8
    Invoke-Git $workingRoot @('add', 'CANDIDATE.md') | Out-Null
    Invoke-Git $workingRoot @('commit', '-m', 'candidate change') | Out-Null

    $coverage = [ordered]@{
        schemaVersion = 1
        ruleId = 'TEST-GOV-007'
        collector = 'coverlet.collector/10.0.1'
        requiredReportCount = 2
        reportCount = 2
        productionFileCount = 1
        overall = [ordered]@{ lineValid = 10; lineCovered = 9; lineRate = 0.9; branchValid = 10; branchCovered = 8; branchRate = 0.8 }
        components = @()
        highRiskThresholds = @()
    }
    $metrics = [ordered]@{}
    foreach ($name in @('production.exact', 'production.near', 'testSupport.exact', 'testSupport.near', 'tests.exact', 'tests.near')) {
        $metrics[$name] = [ordered]@{ groupCount = 0; instanceCount = 0 }
    }
    $duplication = [ordered]@{
        schemaVersion = 1
        ruleId = 'TEST-GOV-006'
        algorithm = [ordered]@{ exactMeaningfulLineWindow = 16; nearMeaningfulLineWindow = 24; minimumDistinctFiles = 2 }
        sourceFileCount = 1
        metrics = $metrics
        groups = @()
    }
    $mutation = [ordered]@{
        schemaVersion = 1
        ruleId = 'TEST-GOV-007'
        mode = 'report-only'
        testRunner = 'mtp'
        tool = 'dotnet-stryker/4.16.0'
        targetProject = 'src/Core/IIoT.Edge.Domain/IIoT.Edge.Domain.csproj'
        testProject = 'src/Tests/IIoT.Edge.Domain.Tests/IIoT.Edge.Domain.Tests.csproj'
        mutate = @('Config/Aggregates/*.cs')
        requiredSemanticTests = @('Aggregate_ShouldRejectInvalidState')
        initialTestCount = 2
        createdMutants = 4
        totalMutants = 4
        evaluatedMutants = 4
        detected = 3
        survived = 1
        noCoverage = 0
        ignored = 0
        timeout = 0
        compileErrors = 0
        mutationScore = 0.75
        artifact = 'artifacts/mutation-report.json'
        traceLog = 'artifacts/stryker-console.log'
    }
    $evidence = @([ordered]@{ path = 'src/Legacy.cs'; pattern = 'LegacyCall'; expectedOccurrences = 1 })
    $compatibility = [ordered]@{
        schemaVersion = 3
        ruleId = 'TEST-COMPAT-001'
        scanTokens = @('legacy')
        candidateDispositions = @([ordered]@{
            id = 'EDGE-CANDIDATE-LEGACY'
            token = 'legacy'
            pathPattern = '^src/'
            status = 'OrdinaryAbstraction'
            rationale = 'fixture'
            candidateCount = 1
            occurrenceCount = 1
            manifestSha256 = 'fixture'
            callEvidence = $evidence
        })
        migrationWindows = @()
        symbolCandidates = @([ordered]@{
            token = 'legacy'
            kind = 'type'
            symbol = 'LegacyThing'
            declarationPath = 'src/Legacy.cs'
            status = 'OrdinaryAbstraction'
            rationale = 'fixture'
            callEvidence = $evidence
        })
    }

    $coveragePath = Join-Path $workingRoot 'scripts/tests/baselines/edge-coverage-baseline.json'
    $duplicationPath = Join-Path $workingRoot 'scripts/tests/baselines/edge-duplication-baseline.json'
    $mutationPath = Join-Path $workingRoot 'scripts/tests/baselines/edge-mutation-baseline.json'
    $compatibilityPath = Join-Path $workingRoot 'scripts/tests/edge-compatibility-inventory.json'
    Write-Json $coveragePath $coverage
    Write-Json $duplicationPath $duplication
    Write-Json $mutationPath $mutation
    Write-Json $compatibilityPath $compatibility
    [void](New-Item (Join-Path $workingRoot 'src') -ItemType Directory -Force)
    Set-Content (Join-Path $workingRoot 'src/Legacy.cs') 'sealed class LegacyThing { void LegacyCall() {} }' -Encoding utf8

    Assert-FailedWith (Invoke-Gate $workingRoot $baseRef) 'same-change bootstrap is forbidden' 'Uncommitted bootstrap'

    Invoke-Git $workingRoot @('add', 'scripts', 'src') | Out-Null
    Invoke-Git $workingRoot @('commit', '-m', 'bootstrap baselines') | Out-Null
    $bootstrapRef = (Invoke-Git $workingRoot @('rev-parse', 'HEAD')).Trim()

    $unchanged = Invoke-Gate $workingRoot $baseRef
    if ($unchanged.ExitCode -ne 0) {
        throw "Unchanged anchored baselines should pass. output=$($unchanged.Output)"
    }

    Assert-FailedWith (Invoke-Gate $workingRoot $bootstrapRef) 'BaseRef must identify the pre-change commit' 'Candidate HEAD base reference'

    $weakenedCoverage = Get-Content $coveragePath -Raw | ConvertFrom-Json -Depth 32
    $weakenedCoverage.overall.lineRate = 0.8
    Write-Json $coveragePath $weakenedCoverage
    Assert-FailedWith (Invoke-Gate $workingRoot $baseRef) 'coverage overall line rate weakened' 'Coverage self-authorization'
    Invoke-Git $workingRoot @('checkout', '--', 'scripts/tests/baselines/edge-coverage-baseline.json') | Out-Null

    $expandedDuplication = Get-Content $duplicationPath -Raw | ConvertFrom-Json -Depth 32
    $expandedDuplication.metrics.'production.exact'.groupCount = 1
    Write-Json $duplicationPath $expandedDuplication
    Assert-FailedWith (Invoke-Gate $workingRoot $baseRef) 'duplication production.exact groups expanded' 'Duplication self-authorization'
    Invoke-Git $workingRoot @('checkout', '--', 'scripts/tests/baselines/edge-duplication-baseline.json') | Out-Null

    $weakenedMutation = Get-Content $mutationPath -Raw | ConvertFrom-Json -Depth 32
    $weakenedMutation.mutationScore = 0.5
    Write-Json $mutationPath $weakenedMutation
    Assert-FailedWith (Invoke-Gate $workingRoot $baseRef) 'mutation score weakened' 'Mutation self-authorization'
    Invoke-Git $workingRoot @('checkout', '--', 'scripts/tests/baselines/edge-mutation-baseline.json') | Out-Null

    $expandedCompatibility = Get-Content $compatibilityPath -Raw | ConvertFrom-Json -Depth 32
    $expandedCompatibility.candidateDispositions[0].status = 'MigrationWindow'
    Write-Json $compatibilityPath $expandedCompatibility
    Assert-FailedWith (Invoke-Gate $workingRoot $baseRef) "ordinary candidate 'EDGE-CANDIDATE-LEGACY'" 'Compatibility self-authorization'
    Invoke-Git $workingRoot @('checkout', '--', 'scripts/tests/edge-compatibility-inventory.json') | Out-Null

    $ordinaryGrowth = Get-Content $compatibilityPath -Raw | ConvertFrom-Json -Depth 32
    $ordinaryGrowth.candidateDispositions[0].occurrenceCount = 2
    $ordinaryGrowth.candidateDispositions[0].callEvidence[0].expectedOccurrences = 2
    $ordinaryGrowth.symbolCandidates[0].callEvidence[0].expectedOccurrences = 2
    Write-Json $compatibilityPath $ordinaryGrowth
    Set-Content (Join-Path $workingRoot 'src/Legacy.cs') 'sealed class LegacyThing { void LegacyCall() {} void Second() { LegacyCall(); } }' -Encoding utf8
    $ordinaryGrowthResult = Invoke-Gate $workingRoot $baseRef
    if ($ordinaryGrowthResult.ExitCode -ne 0) {
        throw "A machine-verified ordinary abstraction may gain a real caller. output=$($ordinaryGrowthResult.Output)"
    }
    Invoke-Git $workingRoot @('checkout', '--', 'scripts/tests/edge-compatibility-inventory.json', 'src/Legacy.cs') | Out-Null

    $newOrdinary = Get-Content $compatibilityPath -Raw | ConvertFrom-Json -Depth 32
    $ordinaryEvidence = @([pscustomobject][ordered]@{
        path = 'src/CurrentAdapter.cs'
        pattern = 'CurrentAdapterCall'
        expectedOccurrences = 1
    })
    $newOrdinary.candidateDispositions = @($newOrdinary.candidateDispositions) + @([pscustomobject][ordered]@{
        id = 'EDGE-CANDIDATE-CURRENT-ADAPTER'
        token = 'adapter'
        pathPattern = '^src/CurrentAdapter\.cs$'
        status = 'OrdinaryAbstraction'
        rationale = 'Current architecture port with executable callers; not a compatibility surface.'
        candidateCount = 1
        occurrenceCount = 1
        manifestSha256 = 'fixture-current-adapter'
        callEvidence = $ordinaryEvidence
    })
    $newOrdinary.symbolCandidates = @($newOrdinary.symbolCandidates) + @([pscustomobject][ordered]@{
        token = 'adapter'
        kind = 'type'
        symbol = 'CurrentAdapter'
        declarationPath = 'src/CurrentAdapter.cs'
        status = 'OrdinaryAbstraction'
        rationale = 'Current architecture port with executable callers; not a compatibility surface.'
        callEvidence = $ordinaryEvidence
    })
    Write-Json $compatibilityPath $newOrdinary
    Set-Content (Join-Path $workingRoot 'src/CurrentAdapter.cs') 'sealed class CurrentAdapter { void CurrentAdapterCall() {} }' -Encoding utf8
    $newOrdinaryResult = Invoke-Gate $workingRoot $baseRef
    if ($newOrdinaryResult.ExitCode -ne 0) {
        throw "A new machine-verified ordinary abstraction should not be frozen as compatibility. output=$($newOrdinaryResult.Output)"
    }
    Invoke-Git $workingRoot @('checkout', '--', 'scripts/tests/edge-compatibility-inventory.json') | Out-Null
    Remove-Item (Join-Path $workingRoot 'src/CurrentAdapter.cs') -Force

    $newMigrationCandidate = Get-Content $compatibilityPath -Raw | ConvertFrom-Json -Depth 32
    $newMigrationCandidate.candidateDispositions = @($newMigrationCandidate.candidateDispositions) + @([pscustomobject][ordered]@{
        id = 'EDGE-CANDIDATE-NEW-LEGACY'
        token = 'legacy'
        pathPattern = '^src/'
        status = 'MigrationWindow'
        rationale = 'A new compatibility surface is not authorized by the current batch.'
        candidateCount = 1
        occurrenceCount = 1
        manifestSha256 = 'fixture-new-legacy'
        callEvidence = $evidence
    })
    Write-Json $compatibilityPath $newMigrationCandidate
    Assert-FailedWith (Invoke-Gate $workingRoot $baseRef) 'EDGE-CANDIDATE-NEW-LEGACY' 'New migration candidate'
    Invoke-Git $workingRoot @('checkout', '--', 'scripts/tests/edge-compatibility-inventory.json') | Out-Null

    $newMigrationWindow = Get-Content $compatibilityPath -Raw | ConvertFrom-Json -Depth 32
    $newMigrationWindow.migrationWindows = @([pscustomobject][ordered]@{
        id = 'EDGE-MIGRATION-NEW-LEGACY'
        currentConsumers = @('fixture')
        callEvidence = $evidence
        latestRemovalBatch = 'fixture-next'
    })
    Write-Json $compatibilityPath $newMigrationWindow
    Assert-FailedWith (Invoke-Gate $workingRoot $baseRef) 'EDGE-MIGRATION-NEW-LEGACY' 'New migration window'
    Invoke-Git $workingRoot @('checkout', '--', 'scripts/tests/edge-compatibility-inventory.json') | Out-Null

    $global:LASTEXITCODE = 0
    Write-Host "EDGE_GOVERNANCE_BASELINE_MONOTONICITY_BEHAVIOR_OK bootstrap=$bootstrapRef firstRed=8 ordinaryGrowth=1 newOrdinary=1"
} finally {
    if (Test-Path $workingRoot) {
        Remove-Item $workingRoot -Recurse -Force
    }
}
