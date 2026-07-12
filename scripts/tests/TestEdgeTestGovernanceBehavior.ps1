[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}
$policyPath = Join-Path $RepositoryRoot 'scripts/tests/TestEdgeTestGovernancePolicy.ps1'
$reviewedBaselinePath = Join-Path $RepositoryRoot 'scripts/tests/baselines/edge-test-governance.baseline.json'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "edge-test-governance-$([Guid]::NewGuid().ToString('N'))"
[void](New-Item $tempRoot -ItemType Directory -Force)

function Write-JsonFile {
    param(
        [Parameter(Mandatory)][object]$Value,
        [Parameter(Mandatory)][string]$Path
    )
    $json = $Value | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText($Path, "$json`n", [System.Text.UTF8Encoding]::new($false))
}

function New-Traits {
    param(
        [string]$TestKind,
        [string]$Capability,
        [string]$Runtime,
        [string]$Risk,
        [string]$Owner,
        [string]$RegressionId
    )
    $traits = [ordered]@{}
    if (-not [string]::IsNullOrWhiteSpace($TestKind)) { $traits.TestKind = @($TestKind) }
    if (-not [string]::IsNullOrWhiteSpace($Capability)) { $traits.Capability = @($Capability) }
    if (-not [string]::IsNullOrWhiteSpace($Runtime)) { $traits.Runtime = @($Runtime) }
    if (-not [string]::IsNullOrWhiteSpace($Risk)) { $traits.Risk = @($Risk) }
    if (-not [string]::IsNullOrWhiteSpace($Owner)) { $traits.Owner = @($Owner) }
    if (-not [string]::IsNullOrWhiteSpace($RegressionId)) { $traits.RegressionId = @($RegressionId) }
    return [pscustomobject]$traits
}

function Get-FixtureHash {
    param([Parameter(Mandatory)][string]$Value)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function New-TestRecord {
    param(
        [Parameter(Mandatory)][string]$Id,
        [string]$TypeName = 'Fixture.Tests.SampleTests',
        [string]$MethodName = 'Existing',
        [int]$InlineDataRows = 0,
        [string]$AttributeCategory = 'Fact',
        [string]$TestAttributeType = 'Xunit.FactAttribute',
        [AllowNull()][object]$Traits = $null,
        [bool]$Disabled = $false,
        [string[]]$ExecutionTypeNames = @()
    )
    if ($null -eq $Traits) { $Traits = [pscustomobject]@{} }
    if ($ExecutionTypeNames.Count -eq 0) { $ExecutionTypeNames = @($TypeName) }
    $physicalId = "edge-test-physical-v1:$(Get-FixtureHash "physical|$Id")"
    $logicalId = "edge-test-decl-v1:$(Get-FixtureHash "logical|$Id")"
    $executionTypes = @($ExecutionTypeNames | ForEach-Object {
        [pscustomobject]@{
            id = "edge-test-execution-v1:$(Get-FixtureHash "execution|$Id|$_")"
            name = $_
            traits = $Traits
        }
    })
    $rowProjection = if ($AttributeCategory -eq 'Theory' -and $InlineDataRows -gt 0) { $InlineDataRows } else { 1 }
    $inlineDataSignatures = if ($InlineDataRows -gt 0) { [string[]]@(1..$InlineDataRows | ForEach-Object { "fixture-inline-row-$_" }) } else { [string[]]@() }
    return [pscustomobject][ordered]@{
        id = $physicalId
        logicalId = $logicalId
        symbol = "$TypeName.$MethodName()"
        executionType = $TypeName
        declaringType = $TypeName
        methodName = $MethodName
        parameterSignature = ''
        attributeCategory = $AttributeCategory
        testAttributeType = $TestAttributeType
        testAttributePolicy = [pscustomobject]@{
            signature = "Skip=$(if ($Disabled) { 'disabled' } else { '' })|Explicit=False|SkipWhen=|SkipUnless=|SkipType=|Timeout=0"
            isDisabled = $Disabled
        }
        inlineDataRows = $InlineDataRows
        inlineDataSignatures = $inlineDataSignatures
        dynamicDataSources = [string[]]@()
        executionTypes = $executionTypes
        projectedCases = $rowProjection * $executionTypes.Count
        traits = $Traits
    }
}

function New-Baseline {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Tests,
        [ValidateSet('None', 'All', 'Types')][string]$FreezeMode = 'None',
        [string[]]$FrozenTypePatterns = @(),
        [string[]]$AllowedNewTestKinds = @(),
        [string[]]$ForbiddenNewTestKinds = @()
    )
    $executionTemplateCount = if ($Tests.Count -eq 0) { 0 } else { [int](($Tests | ForEach-Object { @($_.executionTypes).Count } | Measure-Object -Sum).Sum) }
    $projectedCaseCount = if ($Tests.Count -eq 0) { 0 } else { [int](($Tests | Measure-Object -Property projectedCases -Sum).Sum) }
    return [pscustomobject][ordered]@{
        schemaVersion = '1.0'
        ruleId = 'EDGE-TEST-GOV-001'
        allowedMetadata = [pscustomobject]@{
            testKinds = @('Architecture', 'Unit', 'Aggregate', 'Application', 'Contract', 'Conformance', 'Persistence', 'Workflow', 'Integration', 'EndToEnd', 'UI', 'GoldenEval', 'Deployment', 'Performance', 'SoakChaos', 'Security')
            runtimes = @('Pure', 'InProcess', 'Filesystem', 'SQLite', 'Postgres', 'Redis', 'RabbitMQ', 'Docker', 'Aspire', 'Avalonia', 'Browser', 'Windows', 'LiveExternal')
            risks = @('P0', 'P1', 'P2')
            owners = @('Edge.Architecture', 'Edge.Core', 'Edge.Application', 'Edge.Persistence', 'Edge.PLC', 'Edge.MES', 'Edge.Cloud', 'Edge.Modules', 'Edge.Shell', 'Edge.UI', 'Edge.Deployment', 'Edge.Security', 'Edge.Tests')
            capabilities = @('Architecture', 'Authentication', 'Authorization', 'Launcher', 'Installer', 'Shell', 'UI.Shared', 'Update', 'Modules', 'PLC', 'MES', 'Cloud', 'DataPipeline', 'Persistence', 'Deployment', 'Startup', 'Diagnostics', 'Configuration', 'Recipes', 'Capacity', 'Device', 'Logging', 'Runtime', 'TestGovernance')
        }
        projects = @([pscustomobject][ordered]@{
            projectPath = $ProjectPath
            projectName = $ProjectName
            freezeMode = $FreezeMode
            frozenTypePatterns = [string[]]$FrozenTypePatterns
            frozenSourceFiles = [string[]]@()
            allowedNewTestKinds = [string[]]$AllowedNewTestKinds
            allowedNewRuntimes = [string[]]@()
            forbiddenNewTestKinds = [string[]]$ForbiddenNewTestKinds
            protectBaselineRemovals = $true
            baselineDeclarations = @($Tests).Count
            baselineExecutionTemplates = $executionTemplateCount
            baselineProjectedCases = $projectedCaseCount
            tests = [object[]]$Tests
        })
    }
}

function New-Snapshot {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Tests
    )
    return [pscustomobject][ordered]@{
        projectPath = $ProjectPath
        projectName = $ProjectName
        tests = [object[]]$Tests
    }
}

function New-WaiverManifest {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Waivers)
    return [pscustomobject][ordered]@{
        schemaVersion = '1.0'
        ruleId = 'EDGE-TEST-GOV-001'
        waivers = [object[]]$Waivers
    }
}

function Invoke-SnapshotValidation {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][object]$WaiverManifest
    )

    $baselinePath = Join-Path $tempRoot "$Name.baseline.json"
    $snapshotPath = Join-Path $tempRoot "$Name.snapshot.json"
    $waiverPath = Join-Path $tempRoot "$Name.waivers.json"
    Write-JsonFile -Value $Baseline -Path $baselinePath
    Write-JsonFile -Value $Snapshot -Path $snapshotPath
    Write-JsonFile -Value $WaiverManifest -Path $waiverPath
    $output = & pwsh -NoLogo -NoProfile -File $policyPath `
        -Mode ValidateSnapshot `
        -RepositoryRoot $RepositoryRoot `
        -BaselinePath $baselinePath `
        -WaiverPath $waiverPath `
        -CurrentSnapshotPath $snapshotPath 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String).Trim()
    }
}

function Invoke-RepositorySnapshotValidation {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object[]]$Snapshots,
        [Parameter(Mandatory)][object]$WaiverManifest
    )

    $baselinePath = Join-Path $tempRoot "$Name.baseline.json"
    $snapshotPath = Join-Path $tempRoot "$Name.repository-snapshot.json"
    $waiverPath = Join-Path $tempRoot "$Name.waivers.json"
    Write-JsonFile -Value $Baseline -Path $baselinePath
    Write-JsonFile -Value ([pscustomobject]@{ snapshots = [object[]]$Snapshots }) -Path $snapshotPath
    Write-JsonFile -Value $WaiverManifest -Path $waiverPath
    $output = & pwsh -NoLogo -NoProfile -File $policyPath `
        -Mode ValidateRepositorySnapshot `
        -RepositoryRoot $RepositoryRoot `
        -BaselinePath $baselinePath `
        -WaiverPath $waiverPath `
        -CurrentSnapshotPath $snapshotPath 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String).Trim()
    }
}

function Assert-Accepted {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][object]$WaiverManifest
    )
    $result = Invoke-SnapshotValidation -Name $Name -Baseline $Baseline -Snapshot $Snapshot -WaiverManifest $WaiverManifest
    if ($result.ExitCode -ne 0) {
        throw "Fixture '$Name' should pass:`n$($result.Output)"
    }
    Write-Host "Accepted Edge test-governance fixture: $Name"
}

function Assert-Rejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][object]$Snapshot,
        [Parameter(Mandatory)][object]$WaiverManifest,
        [Parameter(Mandatory)][string]$ExpectedCode
    )
    $result = Invoke-SnapshotValidation -Name $Name -Baseline $Baseline -Snapshot $Snapshot -WaiverManifest $WaiverManifest
    if ($result.ExitCode -eq 0 -or -not $result.Output.Contains($ExpectedCode, [StringComparison]::Ordinal)) {
        throw "Fixture '$Name' should fail with $ExpectedCode; exit=$($result.ExitCode):`n$($result.Output)"
    }
    Write-Host "Rejected Edge test-governance fixture: $Name ($ExpectedCode)"
}

function Invoke-StaticPolicyValidation {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ValidationRoot,
        [Parameter(Mandatory)][string]$BaselinePath,
        [Parameter(Mandatory)][string]$WaiverPath
    )
    $output = & pwsh -NoLogo -NoProfile -File $policyPath `
        -Mode ValidateStatic `
        -RepositoryRoot $ValidationRoot `
        -BaselinePath $BaselinePath `
        -WaiverPath $WaiverPath `
        -Configuration Release 2>&1
    return [pscustomobject]@{
        Name = $Name
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String).Trim()
    }
}

function Invoke-BaselineAnchorValidation {
    param(
        [Parameter(Mandatory)][string]$ValidationRoot,
        [Parameter(Mandatory)][string]$BaselinePath,
        [Parameter(Mandatory)][string]$TrustedBaseRevision,
        [ValidateSet('BaseAncestorOfHead', 'HeadAncestorOfBase')]
        [string]$AnchorRelationship = 'BaseAncestorOfHead'
    )
    $output = & pwsh -NoLogo -NoProfile -File $policyPath `
        -Mode ValidateBaselineAnchor `
        -RepositoryRoot $ValidationRoot `
        -BaselinePath $BaselinePath `
        -TrustedBaseRevision $TrustedBaseRevision `
        -AnchorRelationship $AnchorRelationship 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String).Trim()
    }
}

function Assert-StaticRejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ValidationRoot,
        [Parameter(Mandatory)][object]$Baseline,
        [Parameter(Mandatory)][string]$WaiverPath,
        [Parameter(Mandatory)][string]$ExpectedCode
    )
    $baselinePath = Join-Path $tempRoot "$Name.static-baseline.json"
    Write-JsonFile -Value $Baseline -Path $baselinePath
    $result = Invoke-StaticPolicyValidation -Name $Name -ValidationRoot $ValidationRoot -BaselinePath $baselinePath -WaiverPath $WaiverPath
    if ($result.ExitCode -eq 0 -or -not $result.Output.Contains($ExpectedCode, [StringComparison]::Ordinal)) {
        throw "Static fixture '$Name' should fail with $ExpectedCode; exit=$($result.ExitCode):`n$($result.Output)"
    }
    Write-Host "Rejected Edge static-governance fixture: $Name ($ExpectedCode)"
}

function Assert-StaticMutationRejected {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ValidationRoot,
        [Parameter(Mandatory)][scriptblock]$Mutate,
        [Parameter(Mandatory)][scriptblock]$Restore,
        [Parameter(Mandatory)][string]$BaselinePath,
        [Parameter(Mandatory)][string]$WaiverPath,
        [Parameter(Mandatory)][string]$ExpectedCode
    )

    try {
        & $Mutate
        $result = Invoke-StaticPolicyValidation -Name $Name -ValidationRoot $ValidationRoot -BaselinePath $BaselinePath -WaiverPath $WaiverPath
        if ($result.ExitCode -eq 0 -or -not $result.Output.Contains($ExpectedCode, [StringComparison]::Ordinal)) {
            throw "Static mutation '$Name' should fail with $ExpectedCode; exit=$($result.ExitCode):`n$($result.Output)"
        }
        Write-Host "Rejected Edge static-governance mutation: $Name ($ExpectedCode)"
    } finally {
        & $Restore
    }
}

function Copy-StaticFixtureRepository {
    param([Parameter(Mandatory)][string]$TargetRoot)

    $relativeFiles = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($relativePath in @(
        '.gitignore',
        '.gitattributes',
        'global.json',
        'Directory.Build.props',
        'Directory.Build.targets',
        'Directory.Packages.props',
        'NuGet.Config',
        'IIoT.EdgeClient.slnx',
        '.github/CODEOWNERS',
        '.github/workflows/edge-smoke-build.yml',
        '.github/workflows/edge-pack-modules.yml',
        'scripts/tests/TestEdgeTestGovernanceBehavior.ps1',
        'scripts/TestEdgePackageVulnerabilities.ps1',
        'scripts/tests/baselines/edge-test-governance.baseline.json',
        'scripts/tests/baselines/edge-test-governance.waivers.json',
        'src/Tests/Directory.Build.props',
        'src/Tests/xunit.runner.json',
        'src/Tests/IIoT.Edge.Shell.Tests/RepositoryHygieneTests.cs'
    )) {
        [void]$relativeFiles.Add($relativePath)
    }
    foreach ($projectFile in @(Get-ChildItem -Force $RepositoryRoot -Recurse -File | Where-Object {
        $_.Name -match '(?i)\.(?:cs|fs|vb)proj$' -and $_.FullName -notmatch '[/\\](?:\.git|bin|obj|node_modules)[/\\]'
    })) {
        [void]$relativeFiles.Add([IO.Path]::GetRelativePath($RepositoryRoot, $projectFile.FullName).Replace('\', '/'))
    }
    foreach ($sourceFile in @(Get-ChildItem -Force (Join-Path $RepositoryRoot 'src/Tests') -Recurse -Filter '*.cs' -File | Where-Object {
        $_.FullName -notmatch '[/\\](?:bin|obj)[/\\]'
    })) {
        [void]$relativeFiles.Add([IO.Path]::GetRelativePath($RepositoryRoot, $sourceFile.FullName).Replace('\', '/'))
    }
    foreach ($relativePath in @($relativeFiles | Sort-Object)) {
        $sourcePath = Join-Path $RepositoryRoot $relativePath
        $targetPath = Join-Path $TargetRoot $relativePath
        [void](New-Item (Split-Path $targetPath -Parent) -ItemType Directory -Force)
        Copy-Item $sourcePath $targetPath -Force
    }
}

try {
    $reviewedBaseline = Get-Content $reviewedBaselinePath -Raw | ConvertFrom-Json -Depth 100
    if (@($reviewedBaseline.projects).Count -ne 7 -or
        [int](($reviewedBaseline.projects | Measure-Object -Property baselineDeclarations -Sum).Sum) -ne 964 -or
        [int](($reviewedBaseline.projects | Measure-Object -Property baselineExecutionTemplates -Sum).Sum) -ne 1010 -or
        [int](($reviewedBaseline.projects | Measure-Object -Property baselineProjectedCases -Sum).Sum) -ne 1091 -or
        [int](($reviewedBaseline.projects | Measure-Object -Property baselineRunnerCases -Sum).Sum) -ne 1091) {
        throw 'Reviewed Edge baseline no longer contains 7 projects / 964 declarations / 1010 execution templates / 1091 projected and runner cases.'
    }
    $avaloniaAttributes = @($reviewedBaseline.projects.tests | Where-Object { $_.testAttributeType -in @('Avalonia.Headless.XUnit.AvaloniaFactAttribute', 'Avalonia.Headless.XUnit.AvaloniaTheoryAttribute') })
    if ($avaloniaAttributes.Count -ne 83) {
        throw "Reviewed Edge baseline should contain 83 Avalonia Fact/Theory-derived declarations; found $($avaloniaAttributes.Count)."
    }
    $reviewedInlineDataSignatures = @($reviewedBaseline.projects.tests.inlineDataSignatures | ForEach-Object { [string]$_ })
    if ($reviewedInlineDataSignatures.Count -ne 106 -or
        @($reviewedInlineDataSignatures | Where-Object { $_ -match 'EcmaCustomAttributeData|System\.Reflection\.CustomAttributeData' }).Count -gt 0 -or
        @($reviewedInlineDataSignatures | Sort-Object -Unique).Count -lt 20 -or
        @($reviewedInlineDataSignatures | Where-Object { $_ -notmatch '^Xunit\.InlineDataAttribute\|ctor=' }).Count -gt 0) {
        throw 'Reviewed Edge baseline must preserve 106 real InlineData constructor/named-argument payload signatures; reflection type-name collapse is forbidden.'
    }
    foreach ($project in @($reviewedBaseline.projects)) {
        if ([int]$project.baselineRunnerCases -lt [int]$project.baselineProjectedCases -or
            [string]$project.runnerCaseDigest -notmatch '^[0-9a-f]{64}$') {
            throw "Reviewed Edge baseline project $($project.projectName) lacks an exact normalized runner count/digest."
        }
    }
    $reviewedWaiverPath = Join-Path $RepositoryRoot 'scripts/tests/baselines/edge-test-governance.waivers.json'
    $currentStatic = Invoke-StaticPolicyValidation -Name 'current-repository-static-policy' -ValidationRoot $RepositoryRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($currentStatic.ExitCode -ne 0) {
        throw "Current repository static policy should pass:`n$($currentStatic.Output)"
    }
    Write-Host 'Accepted Edge static-governance fixture: current-repository-static-policy'
    $closedBootstrapAnchor = Invoke-BaselineAnchorValidation -ValidationRoot $RepositoryRoot -BaselinePath $reviewedBaselinePath -TrustedBaseRevision 'de5e38510e782c111b0a99bca6365bb94940c65e'
    if ($closedBootstrapAnchor.ExitCode -eq 0 -or -not $closedBootstrapAnchor.Output.Contains('EDGE-TEST-GOV-001-BASELINE', [StringComparison]::Ordinal)) {
        throw "Trusted revisions without a baseline must fail after Phase 0 bootstrap closes:`n$($closedBootstrapAnchor.Output)"
    }
    Write-Host 'Rejected Edge immutable baseline fixture: closed-bootstrap-base-without-baseline (EDGE-TEST-GOV-001-BASELINE)'
    $runnerNormalizationOutput = & pwsh -NoLogo -NoProfile -File $policyPath -Mode ValidateRunnerCaseNormalization 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Runner-case normalization fixture should pass:`n$(($runnerNormalizationOutput | Out-String).Trim())"
    }
    Write-Host 'Accepted Edge runner display-name normalization fixture'

    $projectPath = 'src/Tests/Fixture.Tests/Fixture.Tests.csproj'
    $projectName = 'Fixture.Tests'
    $existing = New-TestRecord -Id 'physical:existing'
    $classified = New-Traits -TestKind Unit -Capability TestGovernance -Runtime Pure -Risk P1 -Owner Edge.Tests
    $newTest = New-TestRecord -Id 'physical:new' -MethodName 'NewCase' -Traits $classified
    $emptyWaivers = New-WaiverManifest -Waivers @()

    Assert-Accepted -Name 'current-baseline' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -WaiverManifest $emptyWaivers

    Assert-Rejected -Name 'baseline-test-removal-needs-verified-migration' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @()) `
        -WaiverManifest $emptyWaivers `
        -ExpectedCode 'EDGE-TEST-GOV-001-FROZEN'

    $unclassifiedNew = New-TestRecord -Id 'physical:new' -MethodName 'NewCase'
    Assert-Rejected -Name 'new-test-without-metadata' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $unclassifiedNew)) `
        -WaiverManifest $emptyWaivers `
        -ExpectedCode 'EDGE-TEST-GOV-001-CLASSIFICATION'

    Assert-Accepted -Name 'new-classified-test' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $newTest)) `
        -WaiverManifest $emptyWaivers

    $frozenBaseline = New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing) -FreezeMode All
    $targetProjectPath = 'src/Tests/IIoT.Edge.UnitTests/IIoT.Edge.UnitTests.csproj'
    $targetBaseline = New-Baseline -ProjectPath $targetProjectPath -ProjectName 'IIoT.Edge.UnitTests' -Tests @()
    $frozenBaseline.projects = @($frozenBaseline.projects) + @($targetBaseline.projects)
    Assert-Rejected -Name 'frozen-bucket-addition' `
        -Baseline $frozenBaseline `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $newTest)) `
        -WaiverManifest $emptyWaivers `
        -ExpectedCode 'EDGE-TEST-GOV-001-FROZEN'

    Assert-Rejected -Name 'frozen-protected-removal' `
        -Baseline $frozenBaseline `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @()) `
        -WaiverManifest $emptyWaivers `
        -ExpectedCode 'EDGE-TEST-GOV-001-FROZEN'

    $validWaiver = [pscustomobject][ordered]@{
        id = 'EDGE-TEST-GOV-001-W001'
        projectPath = $projectPath
        symbol = $newTest.id
        changeKind = 'Add'
        regressionId = 'EDGE-REG-001'
        targetProject = $targetProjectPath
        testKind = 'Unit'
        owner = 'Edge.Tests'
        reason = 'Temporary blocking regression while the target project is created.'
        approvedBy = 'ShuJinHao'
        expiresOn = [DateTime]::UtcNow.AddDays(14).ToString('yyyy-MM-dd')
    }
    Assert-Accepted -Name 'exact-temporary-waiver' `
        -Baseline $frozenBaseline `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $newTest)) `
        -WaiverManifest (New-WaiverManifest -Waivers @($validWaiver))

    $expiredWaiver = $validWaiver.PSObject.Copy()
    $expiredWaiver.id = 'EDGE-TEST-GOV-001-W002'
    $expiredWaiver.expiresOn = [DateTime]::UtcNow.AddDays(-1).ToString('yyyy-MM-dd')
    Assert-Rejected -Name 'expired-waiver' `
        -Baseline $frozenBaseline `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $newTest)) `
        -WaiverManifest (New-WaiverManifest -Waivers @($expiredWaiver)) `
        -ExpectedCode 'EDGE-TEST-GOV-001-WAIVER'

    $wildcardWaiver = $validWaiver.PSObject.Copy()
    $wildcardWaiver.id = 'EDGE-TEST-GOV-001-W003'
    $wildcardWaiver.symbol = 'physical:*'
    Assert-Rejected -Name 'wildcard-waiver' `
        -Baseline $frozenBaseline `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $newTest)) `
        -WaiverManifest (New-WaiverManifest -Waivers @($wildcardWaiver)) `
        -ExpectedCode 'EDGE-TEST-GOV-001-WAIVER'

    $staleWaiver = $validWaiver.PSObject.Copy()
    $staleWaiver.id = 'EDGE-TEST-GOV-001-W004'
    $staleWaiver.symbol = 'physical:removed'
    Assert-Rejected -Name 'stale-waiver' `
        -Baseline $frozenBaseline `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -WaiverManifest (New-WaiverManifest -Waivers @($staleWaiver)) `
        -ExpectedCode 'EDGE-TEST-GOV-001-WAIVER'

    $ghostWaiver = $validWaiver.PSObject.Copy()
    $ghostWaiver.id = 'EDGE-TEST-GOV-001-W006'
    $ghostWaiver.projectPath = 'src/Tests/Ghost.Tests/Ghost.Tests.csproj'
    Assert-Rejected -Name 'waiver-source-project-must-be-reviewed' `
        -Baseline $frozenBaseline `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -WaiverManifest (New-WaiverManifest -Waivers @($ghostWaiver)) `
        -ExpectedCode 'EDGE-TEST-GOV-001-WAIVER'

    $theoryBaseline = New-TestRecord -Id 'physical:theory' -MethodName 'Rows' -InlineDataRows 1 -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute
    $theoryCurrent = New-TestRecord -Id 'physical:theory' -MethodName 'Rows' -InlineDataRows 2 -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -Traits $classified
    Assert-Rejected -Name 'frozen-inline-data-increase' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryBaseline) -FreezeMode All) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryCurrent)) `
        -WaiverManifest $emptyWaivers `
        -ExpectedCode 'EDGE-TEST-GOV-001-FROZEN'

    $theoryFourRows = New-TestRecord -Id 'physical:theory-decrease' -MethodName 'RowsCannotShrink' -InlineDataRows 4 -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute
    $theoryThreeRows = New-TestRecord -Id 'physical:theory-decrease' -MethodName 'RowsCannotShrink' -InlineDataRows 3 -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -Traits $classified
    Assert-Rejected -Name 'non-frozen-inline-data-decrease-needs-verified-migration' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryFourRows)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryThreeRows)) `
        -WaiverManifest $emptyWaivers `
        -ExpectedCode 'EDGE-TEST-GOV-001-FROZEN'

    $sameCountReplacement = New-TestRecord -Id 'physical:theory-decrease' -MethodName 'RowsCannotShrink' -InlineDataRows 4 -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -Traits $classified
    $sameCountReplacement.inlineDataSignatures[3] = 'fixture-inline-row-replacement'
    Assert-Rejected -Name 'same-count-inline-data-replacement-cannot-hide-removed-case' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryFourRows)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($sameCountReplacement)) `
        -WaiverManifest $emptyWaivers `
        -ExpectedCode 'EDGE-TEST-GOV-001-FROZEN'

    $migrationTargetPath = 'src/Tests/Fixture.TargetTests/Fixture.TargetTests.csproj'
    $targetBaselineTest = New-TestRecord -Id 'physical:migration-target' -MethodName 'ExistingTargetRows' -InlineDataRows 4 -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute
    $migrationBaseline = New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryFourRows)
    $migrationTargetBaseline = New-Baseline -ProjectPath $migrationTargetPath -ProjectName 'Fixture.TargetTests' -Tests @($targetBaselineTest)
    $migrationBaseline.projects = @($migrationBaseline.projects) + @($migrationTargetBaseline.projects)
    $decreaseSymbol = "edge-test-inline-removal-v1:$(Get-FixtureHash "$($theoryFourRows.id)|fixture-inline-row-4")"
    $migrationRegressionId = 'EDGE-REG-MIGRATION-001'
    $migrationWaiver = [pscustomobject][ordered]@{
        id = 'EDGE-TEST-GOV-001-W005'
        projectPath = $projectPath
        symbol = $decreaseSymbol
        changeKind = 'InlineDataRemoval'
        regressionId = $migrationRegressionId
        targetProject = $migrationTargetPath
        testKind = 'Unit'
        owner = 'Edge.Tests'
        reason = 'Move one concrete regression case into the classified target project.'
        approvedBy = 'ShuJinHao'
        expiresOn = [DateTime]::UtcNow.AddDays(14).ToString('yyyy-MM-dd')
    }
    $migrationTraits = New-Traits -TestKind Unit -Capability TestGovernance -Runtime Pure -Risk P1 -Owner Edge.Tests -RegressionId $migrationRegressionId
    $tagOnlyTarget = New-TestRecord -Id 'physical:migration-target' -MethodName 'ExistingTargetRows' -InlineDataRows 4 -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -Traits $migrationTraits
    $tagOnlyMigration = Invoke-RepositorySnapshotValidation -Name 'tag-only-target-cannot-prove-case-migration' `
        -Baseline $migrationBaseline `
        -Snapshots @(
            (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryThreeRows)),
            (New-Snapshot -ProjectPath $migrationTargetPath -ProjectName 'Fixture.TargetTests' -Tests @($tagOnlyTarget))
        ) `
        -WaiverManifest (New-WaiverManifest -Waivers @($migrationWaiver))
    if ($tagOnlyMigration.ExitCode -eq 0 -or -not $tagOnlyMigration.Output.Contains('EDGE-TEST-GOV-001-WAIVER', [StringComparison]::Ordinal)) {
        throw "Tag-only target must not prove a projected-case migration:`n$($tagOnlyMigration.Output)"
    }
    Write-Host 'Rejected Edge repository-governance fixture: tag-only-target-cannot-prove-case-migration (EDGE-TEST-GOV-001-WAIVER)'

    $expandedTarget = New-TestRecord -Id 'physical:migration-target' -MethodName 'ExistingTargetRows' -InlineDataRows 5 -AttributeCategory Theory -TestAttributeType Xunit.TheoryAttribute -Traits $migrationTraits
    $realMigration = Invoke-RepositorySnapshotValidation -Name 'new-target-case-proves-case-migration' `
        -Baseline $migrationBaseline `
        -Snapshots @(
            (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($theoryThreeRows)),
            (New-Snapshot -ProjectPath $migrationTargetPath -ProjectName 'Fixture.TargetTests' -Tests @($expandedTarget))
        ) `
        -WaiverManifest (New-WaiverManifest -Waivers @($migrationWaiver))
    if ($realMigration.ExitCode -ne 0) {
        throw "One genuinely added target case should prove the migration:`n$($realMigration.Output)"
    }
    Write-Host 'Accepted Edge repository-governance fixture: new-target-case-proves-case-migration'

    $architectureTraits = New-Traits -TestKind Architecture -Capability RepositoryPolicy -Runtime Filesystem -Risk P0 -Owner Edge.Architecture
    $architectureTest = New-TestRecord -Id 'physical:architecture' -MethodName 'SourceBoundary' -Traits $architectureTraits
    Assert-Rejected -Name 'architecture-routed-out-of-shell' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing) -ForbiddenNewTestKinds @('Architecture', 'Deployment')) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $architectureTest)) `
        -WaiverManifest $emptyWaivers `
        -ExpectedCode 'EDGE-TEST-GOV-001-ROUTE'

    Assert-Rejected -Name 'module-contract-allows-conformance-only' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing) -AllowedNewTestKinds @('Conformance')) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing, $newTest)) `
        -WaiverManifest $emptyWaivers `
        -ExpectedCode 'EDGE-TEST-GOV-001-ROUTE'

    $skippedCurrent = New-TestRecord -Id 'physical:existing' -Traits $classified -Disabled $true
    Assert-Rejected -Name 'required-test-cannot-be-skipped' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($existing)) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($skippedCurrent)) `
        -WaiverManifest $emptyWaivers `
        -ExpectedCode 'EDGE-TEST-GOV-001-DISABLED'

    $inheritedBaseline = New-TestRecord -Id 'physical:inherited' -TypeName 'Fixture.Tests.ContractBase' -ExecutionTypeNames @('Fixture.Tests.ExistingModule')
    $inheritedCurrent = New-TestRecord -Id 'physical:inherited' -TypeName 'Fixture.Tests.ContractBase' -ExecutionTypeNames @('Fixture.Tests.ExistingModule', 'Fixture.Tests.NewModule')
    Assert-Rejected -Name 'new-inherited-execution-needs-classification' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($inheritedBaseline) -AllowedNewTestKinds @('Conformance')) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($inheritedCurrent)) `
        -WaiverManifest $emptyWaivers `
        -ExpectedCode 'EDGE-TEST-GOV-001-CLASSIFICATION'

    Assert-Rejected -Name 'inherited-execution-cannot-silently-shrink' `
        -Baseline (New-Baseline -ProjectPath $projectPath -ProjectName $projectName -Tests @($inheritedCurrent) -AllowedNewTestKinds @('Conformance')) `
        -Snapshot (New-Snapshot -ProjectPath $projectPath -ProjectName $projectName -Tests @($inheritedBaseline)) `
        -WaiverManifest $emptyWaivers `
        -ExpectedCode 'EDGE-TEST-GOV-001-FROZEN'

    $tamperedCountBaseline = $reviewedBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
    $tamperedCountBaseline.projects[0].tests = @($tamperedCountBaseline.projects[0].tests) + @($newTest)
    Assert-StaticRejected -Name 'baseline-record-cannot-bypass-summary-counts' `
        -ValidationRoot $RepositoryRoot `
        -Baseline $tamperedCountBaseline `
        -WaiverPath $reviewedWaiverPath `
        -ExpectedCode 'EDGE-TEST-GOV-001-BASELINE'

    $tamperedCommandsBaseline = $reviewedBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
    $tamperedCommandsBaseline.ciRequirements[0].requiredCommandPrefixes = @()
    Assert-StaticRejected -Name 'required-command-order-cannot-be-cleared' `
        -ValidationRoot $RepositoryRoot `
        -Baseline $tamperedCommandsBaseline `
        -WaiverPath $reviewedWaiverPath `
        -ExpectedCode 'EDGE-TEST-GOV-001-BASELINE'

    $tamperedCeilingBaseline = $reviewedBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
    ($tamperedCeilingBaseline.projects | Where-Object { $_.projectName -eq 'IIoT.Edge.NonUiRegressionTests' }).discoveryCeilings = @()
    Assert-StaticRejected -Name 'legacy-discovery-ceiling-cannot-be-cleared' `
        -ValidationRoot $RepositoryRoot `
        -Baseline $tamperedCeilingBaseline `
        -WaiverPath $reviewedWaiverPath `
        -ExpectedCode 'EDGE-TEST-GOV-001-BASELINE'

    $tamperedSourceBaseline = $reviewedBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
    $nonUiProject = $tamperedSourceBaseline.projects | Where-Object { $_.projectName -eq 'IIoT.Edge.NonUiRegressionTests' }
    $nonUiProject.frozenSourceFiles = @($nonUiProject.frozenSourceFiles) + @('src/Tests/IIoT.Edge.NonUiRegressionTests/FutureBypass.cs')
    Assert-StaticRejected -Name 'frozen-source-manifest-cannot-grow' `
        -ValidationRoot $RepositoryRoot `
        -Baseline $tamperedSourceBaseline `
        -WaiverPath $reviewedWaiverPath `
        -ExpectedCode 'EDGE-TEST-GOV-001-BASELINE'

    $tamperedScannerBaseline = $reviewedBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
    $tamperedScannerBaseline.scanner.activeDotnetSdk = '0.0.0-fixture'
    Assert-StaticRejected -Name 'scanner-toolchain-drift-cannot-pass' `
        -ValidationRoot $RepositoryRoot `
        -Baseline $tamperedScannerBaseline `
        -WaiverPath $reviewedWaiverPath `
        -ExpectedCode 'EDGE-TEST-GOV-001-SCAN'

    $tamperedScannerHashBaseline = $reviewedBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100
    $tamperedScannerHashBaseline.scanner.metadataLoadContextSha256 = ('0' * 64)
    Assert-StaticRejected -Name 'unapproved-scanner-binary-hash-cannot-pass' `
        -ValidationRoot $RepositoryRoot `
        -Baseline $tamperedScannerHashBaseline `
        -WaiverPath $reviewedWaiverPath `
        -ExpectedCode 'EDGE-TEST-GOV-001-SCAN'

    $commentRoot = Join-Path $tempRoot 'comment-only-workflow'
    Copy-StaticFixtureRepository -TargetRoot $commentRoot

    $anchorRoot = Join-Path $tempRoot 'baseline-anchor'
    Copy-StaticFixtureRepository -TargetRoot $anchorRoot
    & git -C $anchorRoot init --initial-branch=main | Out-Null
    & git -C $anchorRoot config user.name 'Edge Governance Fixture'
    & git -C $anchorRoot config user.email 'edge-governance-fixture@example.invalid'
    & git -C $anchorRoot config core.autocrlf false
    & git -C $anchorRoot add --all
    & git -C $anchorRoot commit -m 'fixture baseline' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create trusted baseline fixture commit.' }
    $anchorBaseRevision = (& git -C $anchorRoot rev-parse HEAD | Out-String).Trim()
    $anchorBaselinePath = Join-Path $anchorRoot 'scripts/tests/baselines/edge-test-governance.baseline.json'
    [IO.File]::AppendAllText($anchorBaselinePath, "`n", [Text.UTF8Encoding]::new($false))
    & git -C $anchorRoot add --all
    & git -C $anchorRoot commit -m 'fixture candidate rebaseline' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create candidate rebaseline fixture commit.' }
    $anchorMutation = Invoke-BaselineAnchorValidation -ValidationRoot $anchorRoot -BaselinePath $anchorBaselinePath -TrustedBaseRevision $anchorBaseRevision
    if ($anchorMutation.ExitCode -eq 0 -or -not $anchorMutation.Output.Contains('EDGE-TEST-GOV-001-BASELINE', [StringComparison]::Ordinal)) {
        throw "Same-change rebaseline should fail against the trusted base:`n$($anchorMutation.Output)"
    }
    Write-Host 'Rejected Edge baseline-anchor fixture: same-change rebaseline (EDGE-TEST-GOV-001-BASELINE)'

    $releaseAnchorRoot = Join-Path $tempRoot 'release-baseline-anchor'
    Copy-StaticFixtureRepository -TargetRoot $releaseAnchorRoot
    & git -C $releaseAnchorRoot init --initial-branch=main | Out-Null
    & git -C $releaseAnchorRoot config user.name 'Edge Governance Fixture'
    & git -C $releaseAnchorRoot config user.email 'edge-governance-fixture@example.invalid'
    & git -C $releaseAnchorRoot config core.autocrlf false
    & git -C $releaseAnchorRoot add --all
    & git -C $releaseAnchorRoot commit -m 'reviewed release commit' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create reviewed release fixture commit.' }
    $reviewedReleaseRevision = (& git -C $releaseAnchorRoot rev-parse HEAD | Out-String).Trim()
    & git -C $releaseAnchorRoot commit --allow-empty -m 'protected main descendant' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create protected-main fixture commit.' }
    $protectedMainRevision = (& git -C $releaseAnchorRoot rev-parse HEAD | Out-String).Trim()
    & git -C $releaseAnchorRoot checkout --detach $reviewedReleaseRevision | Out-Null
    $reviewedReleaseAnchor = Invoke-BaselineAnchorValidation `
        -ValidationRoot $releaseAnchorRoot `
        -BaselinePath (Join-Path $releaseAnchorRoot 'scripts/tests/baselines/edge-test-governance.baseline.json') `
        -TrustedBaseRevision $protectedMainRevision `
        -AnchorRelationship HeadAncestorOfBase
    if ($reviewedReleaseAnchor.ExitCode -ne 0) {
        throw "Reviewed release commit reachable from protected main should pass:`n$($reviewedReleaseAnchor.Output)"
    }
    Write-Host 'Accepted Edge protected-main release anchor fixture'
    & git -C $releaseAnchorRoot checkout -b unreviewed-release | Out-Null
    [IO.File]::WriteAllText((Join-Path $releaseAnchorRoot 'unreviewed-release.txt'), 'unreviewed', [Text.UTF8Encoding]::new($false))
    & git -C $releaseAnchorRoot add --all
    & git -C $releaseAnchorRoot commit -m 'unreviewed release branch' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create unreviewed release fixture commit.' }
    $unreviewedReleaseAnchor = Invoke-BaselineAnchorValidation `
        -ValidationRoot $releaseAnchorRoot `
        -BaselinePath (Join-Path $releaseAnchorRoot 'scripts/tests/baselines/edge-test-governance.baseline.json') `
        -TrustedBaseRevision $protectedMainRevision `
        -AnchorRelationship HeadAncestorOfBase
    if ($unreviewedReleaseAnchor.ExitCode -eq 0 -or -not $unreviewedReleaseAnchor.Output.Contains('EDGE-TEST-GOV-001-BASELINE', [StringComparison]::Ordinal)) {
        throw "Release commit outside protected main ancestry should fail:`n$($unreviewedReleaseAnchor.Output)"
    }
    Write-Host 'Rejected Edge protected-main release anchor fixture: unreviewed branch (EDGE-TEST-GOV-001-BASELINE)'

    $trackedIgnoredAssetCases = @(
        [pscustomobject]@{ Name = 'bin-project'; RelativePath = 'src/Tests/bin/TrackedBin.Tests.csproj'; Content = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup></Project>'; ExpectedCode = 'EDGE-TEST-GOV-001-PROJECT'; ExpectIgnored = $true },
        [pscustomobject]@{ Name = 'obj-source'; RelativePath = 'src/Tests/obj/TrackedGeneratedTest.cs'; Content = 'public static class TrackedGeneratedTest { }'; ExpectedCode = 'EDGE-TEST-GOV-001-FROZEN'; ExpectIgnored = $true },
        [pscustomobject]@{ Name = 'artifacts-build-graph'; RelativePath = '.artifacts/Directory.Build.targets'; Content = '<Project />'; ExpectedCode = 'EDGE-TEST-GOV-001-BYPASS'; ExpectIgnored = $true },
        [pscustomobject]@{ Name = 'publish-project'; RelativePath = 'publish/TrackedPublish.Tests.csproj'; Content = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup></Project>'; ExpectedCode = 'EDGE-TEST-GOV-001-PROJECT'; ExpectIgnored = $true },
        [pscustomobject]@{ Name = 'dot-project'; RelativePath = '.hidden/TrackedDot.Tests.csproj'; Content = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup></Project>'; ExpectedCode = 'EDGE-TEST-GOV-001-PROJECT'; ExpectIgnored = $false }
    )
    foreach ($trackedCase in $trackedIgnoredAssetCases) {
        $trackedAssetRoot = Join-Path $tempRoot "tracked-$($trackedCase.Name)"
        Copy-StaticFixtureRepository -TargetRoot $trackedAssetRoot
        & git -C $trackedAssetRoot init --initial-branch=main | Out-Null
        & git -C $trackedAssetRoot config user.name 'Edge Governance Fixture'
        & git -C $trackedAssetRoot config user.email 'edge-governance-fixture@example.invalid'
        & git -C $trackedAssetRoot config core.autocrlf false
        & git -C $trackedAssetRoot add --all
        & git -C $trackedAssetRoot commit -m 'reviewed tracked assets' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not create tracked-asset fixture baseline '$($trackedCase.Name)'." }
        $assetPath = Join-Path $trackedAssetRoot $trackedCase.RelativePath
        [void](New-Item (Split-Path $assetPath -Parent) -ItemType Directory -Force)
        [IO.File]::WriteAllText($assetPath, [string]$trackedCase.Content, [Text.UTF8Encoding]::new($false))
        & git -C $trackedAssetRoot check-ignore --quiet -- $trackedCase.RelativePath
        $ignoreExitCode = $LASTEXITCODE
        if ([bool]$trackedCase.ExpectIgnored -and $ignoreExitCode -ne 0) {
            throw "Fixture '$($trackedCase.RelativePath)' should be ignored before git add -f."
        }
        if (-not [bool]$trackedCase.ExpectIgnored -and $ignoreExitCode -ne 1) {
            throw "Fixture '$($trackedCase.RelativePath)' should be a visible untracked dot-path before git add -f; git check-ignore exit=$ignoreExitCode."
        }
        & git -C $trackedAssetRoot add -f $trackedCase.RelativePath
        if ($LASTEXITCODE -ne 0) { throw "Could not force-track ignored-path fixture asset '$($trackedCase.RelativePath)'." }
        & git -C $trackedAssetRoot ls-files --error-unmatch -- $trackedCase.RelativePath | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Fixture asset '$($trackedCase.RelativePath)' was not force-tracked." }
        & git -C $trackedAssetRoot commit -m "force-track $($trackedCase.Name) asset" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not commit force-tracked ignored-path fixture '$($trackedCase.Name)'." }
        $trackedIgnoredResult = Invoke-StaticPolicyValidation `
            -Name "force-tracked-$($trackedCase.Name)-cannot-escape" `
            -ValidationRoot $trackedAssetRoot `
            -BaselinePath (Join-Path $trackedAssetRoot 'scripts/tests/baselines/edge-test-governance.baseline.json') `
            -WaiverPath (Join-Path $trackedAssetRoot 'scripts/tests/baselines/edge-test-governance.waivers.json')
        if ($trackedIgnoredResult.ExitCode -eq 0 -or -not $trackedIgnoredResult.Output.Contains([string]$trackedCase.ExpectedCode, [StringComparison]::Ordinal)) {
            throw "Force-tracked ignored-path fixture '$($trackedCase.Name)' should fail with $($trackedCase.ExpectedCode):`n$($trackedIgnoredResult.Output)"
        }
        Write-Host "Rejected Edge tracked-asset fixture: force-tracked-$($trackedCase.Name)-cannot-escape ($($trackedCase.ExpectedCode))"
    }

    $dotHiddenProject = Join-Path $commentRoot '.hidden/Stealth/Stealth.csproj'
    Assert-StaticMutationRejected -Name 'dot-directory-cannot-hide-test-project' -ValidationRoot $commentRoot `
        -Mutate {
            [void](New-Item (Split-Path $dotHiddenProject -Parent) -ItemType Directory -Force)
            [IO.File]::WriteAllText($dotHiddenProject, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup></Project>', [Text.UTF8Encoding]::new($false))
        } `
        -Restore { Remove-Item (Join-Path $commentRoot '.hidden') -Recurse -Force -ErrorAction SilentlyContinue } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-PROJECT'

    $ignoredNameProject = Join-Path $commentRoot 'src/Tests/bin/Hidden.Tests.csproj'
    Assert-StaticMutationRejected -Name 'tracked-bin-name-cannot-hide-test-project' -ValidationRoot $commentRoot `
        -Mutate {
            [void](New-Item (Split-Path $ignoredNameProject -Parent) -ItemType Directory -Force)
            [IO.File]::WriteAllText($ignoredNameProject, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup></Project>', [Text.UTF8Encoding]::new($false))
        } `
        -Restore { Remove-Item (Join-Path $commentRoot 'src/Tests/bin') -Recurse -Force -ErrorAction SilentlyContinue } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-PROJECT'

    $nestedSourceTargets = Join-Path $commentRoot 'src/Directory.Build.targets'
    Assert-StaticMutationRejected -Name 'nested-directory-build-targets-cannot-shadow-root' -ValidationRoot $commentRoot `
        -Mutate { [IO.File]::WriteAllText($nestedSourceTargets, '<Project />', [Text.UTF8Encoding]::new($false)) } `
        -Restore { Remove-Item $nestedSourceTargets -Force -ErrorAction SilentlyContinue } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-BYPASS'

    $domainProjectPath = Join-Path $commentRoot 'src/Core/IIoT.Edge.Domain/IIoT.Edge.Domain.csproj'
    $domainProjectOriginal = Get-Content $domainProjectPath -Raw
    Assert-StaticMutationRejected -Name 'case-insensitive-directory-build-override-cannot-bypass' -ValidationRoot $commentRoot `
        -Mutate {
            $mutated = $domainProjectOriginal.Replace('</Project>', '<PropertyGroup><directorybuildtargetspath>hidden.targets</directorybuildtargetspath></PropertyGroup></Project>')
            [IO.File]::WriteAllText($domainProjectPath, $mutated, [Text.UTF8Encoding]::new($false))
        } `
        -Restore { [IO.File]::WriteAllText($domainProjectPath, $domainProjectOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-BYPASS-AUTOIMPORT'

    $automaticImportTargetsPath = Join-Path $commentRoot 'src/evil.targets'
    Assert-StaticMutationRejected -Name 'automatic-msbuild-target-import-cannot-run-after-static-gate' -ValidationRoot $commentRoot `
        -Mutate {
            [IO.File]::WriteAllText($automaticImportTargetsPath, '<Project><Target Name="ExecuteUnreviewedBuildInput"><Exec Command="echo unreviewed" /></Target></Project>', [Text.UTF8Encoding]::new($false))
            $mutated = $domainProjectOriginal.Replace('</Project>', '<PropertyGroup><CustomAfterMicrosoftCommonTargets>../evil.targets</CustomAfterMicrosoftCommonTargets></PropertyGroup></Project>')
            [IO.File]::WriteAllText($domainProjectPath, $mutated, [Text.UTF8Encoding]::new($false))
        } `
        -Restore {
            [IO.File]::WriteAllText($domainProjectPath, $domainProjectOriginal, [Text.UTF8Encoding]::new($false))
            Remove-Item $automaticImportTargetsPath -Force -ErrorAction SilentlyContinue
        } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-BYPASS-AUTOIMPORT'

    $automaticResponsePath = Join-Path $commentRoot 'Directory.Build.rsp'
    Assert-StaticMutationRejected -Name 'automatic-msbuild-response-file-cannot-enter-repository' -ValidationRoot $commentRoot `
        -Mutate { [IO.File]::WriteAllText($automaticResponsePath, '/p:CustomAfterMicrosoftCommonTargets=evil.xml', [Text.UTF8Encoding]::new($false)) } `
        -Restore { Remove-Item $automaticResponsePath -Force -ErrorAction SilentlyContinue } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-BYPASS-RESPONSE'

    Assert-StaticMutationRejected -Name 'raw-compiler-analyzer-cannot-enter-project' -ValidationRoot $commentRoot `
        -Mutate {
            $mutated = $domainProjectOriginal.Replace('</Project>', '<ItemGroup><Analyzer Include="../unreviewed-analyzer.dll" /></ItemGroup></Project>')
            [IO.File]::WriteAllText($domainProjectPath, $mutated, [Text.UTF8Encoding]::new($false))
        } `
        -Restore { [IO.File]::WriteAllText($domainProjectPath, $domainProjectOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-BYPASS-ANALYZER'

    Assert-StaticMutationRejected -Name 'project-local-package-version-cannot-bypass-central-review' -ValidationRoot $commentRoot `
        -Mutate {
            $mutated = $domainProjectOriginal.Replace('</Project>', '<ItemGroup><PackageReference Include="Unreviewed.Build.Package" Version="1.0.0" /></ItemGroup></Project>')
            [IO.File]::WriteAllText($domainProjectPath, $mutated, [Text.UTF8Encoding]::new($false))
        } `
        -Restore { [IO.File]::WriteAllText($domainProjectPath, $domainProjectOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-BYPASS-PACKAGEVERSION'

    Assert-StaticMutationRejected -Name 'unreviewed-test-sdk-cannot-hide-in-production-project' -ValidationRoot $commentRoot `
        -Mutate {
            $mutated = $domainProjectOriginal.Replace('Sdk="Microsoft.NET.Sdk"', 'Sdk="MSTest.Sdk/3.8.3"')
            [IO.File]::WriteAllText($domainProjectPath, $mutated, [Text.UTF8Encoding]::new($false))
        } `
        -Restore { [IO.File]::WriteAllText($domainProjectPath, $domainProjectOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-BYPASS'

    Assert-StaticMutationRejected -Name 'raw-xunit-reference-cannot-hide-production-test' -ValidationRoot $commentRoot `
        -Mutate {
            $mutated = $domainProjectOriginal.Replace('</Project>', '<ItemGroup><Reference Include="xunit.core"><HintPath>fake/xunit.core.dll</HintPath></Reference></ItemGroup></Project>')
            [IO.File]::WriteAllText($domainProjectPath, $mutated, [Text.UTF8Encoding]::new($false))
        } `
        -Restore { [IO.File]::WriteAllText($domainProjectPath, $domainProjectOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-BYPASS'

    $launcherTestProjectPath = Join-Path $commentRoot 'src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj'
    $launcherTestProjectOriginal = Get-Content $launcherTestProjectPath -Raw
    Assert-StaticMutationRejected -Name 'conditional-is-test-project-cannot-disable-ci-gate' -ValidationRoot $commentRoot `
        -Mutate {
            $mutated = $launcherTestProjectOriginal.Replace('<IsTestProject>true</IsTestProject>', '<IsTestProject Condition="''$(CI)'' != ''true''">true</IsTestProject>')
            [IO.File]::WriteAllText($launcherTestProjectPath, $mutated, [Text.UTF8Encoding]::new($false))
        } `
        -Restore { [IO.File]::WriteAllText($launcherTestProjectPath, $launcherTestProjectOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-PROJECT'

    $nonUiBodyPath = Join-Path $commentRoot 'src/Tests/IIoT.Edge.NonUiRegressionTests/CapacityCloudQueryServiceBehaviorTests.cs'
    $nonUiBodyOriginal = Get-Content $nonUiBodyPath -Raw
    Assert-StaticMutationRejected -Name 'nonui-test-body-cannot-be-hollowed' -ValidationRoot $commentRoot `
        -Mutate { [IO.File]::WriteAllText($nonUiBodyPath, "$nonUiBodyOriginal`n// assertion-body mutation", [Text.UTF8Encoding]::new($false)) } `
        -Restore { [IO.File]::WriteAllText($nonUiBodyPath, $nonUiBodyOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-FROZEN'

    $launcherBodyPath = Join-Path $commentRoot 'src/Tests/IIoT.Edge.Launcher.Tests/LauncherMainViewModelTests.cs'
    $launcherBodyOriginal = Get-Content $launcherBodyPath -Raw
    Assert-StaticMutationRejected -Name 'reviewed-test-body-cannot-be-hollowed' -ValidationRoot $commentRoot `
        -Mutate { [IO.File]::WriteAllText($launcherBodyPath, "$launcherBodyOriginal`n// assertion-body mutation", [Text.UTF8Encoding]::new($false)) } `
        -Restore { [IO.File]::WriteAllText($launcherBodyPath, $launcherBodyOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-FROZEN'

    $codeOwnersFixturePath = Join-Path $commentRoot '.github/CODEOWNERS'
    $codeOwnersOriginal = Get-Content $codeOwnersFixturePath -Raw
    Assert-StaticMutationRejected -Name 'test-source-codeowner-rule-cannot-be-removed' -ValidationRoot $commentRoot `
        -Mutate { [IO.File]::WriteAllText($codeOwnersFixturePath, $codeOwnersOriginal.Replace('/src/Tests/**/*.cs @ShuJinHao', ''), [Text.UTF8Encoding]::new($false)) } `
        -Restore { [IO.File]::WriteAllText($codeOwnersFixturePath, $codeOwnersOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-CODEOWNER'

    $gitAttributesFixturePath = Join-Path $commentRoot '.gitattributes'
    $gitAttributesOriginal = Get-Content $gitAttributesFixturePath -Raw
    Assert-StaticMutationRejected -Name 'lf-normalization-policy-cannot-drift' -ValidationRoot $commentRoot `
        -Mutate { [IO.File]::WriteAllText($gitAttributesFixturePath, "$gitAttributesOriginal`n# mutation", [Text.UTF8Encoding]::new($false)) } `
        -Restore { [IO.File]::WriteAllText($gitAttributesFixturePath, $gitAttributesOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-CONFIG'

    $directoryPackagesFixturePath = Join-Path $commentRoot 'Directory.Packages.props'
    $directoryPackagesOriginal = Get-Content $directoryPackagesFixturePath -Raw
    Assert-StaticMutationRejected -Name 'central-test-runner-versions-cannot-drift' -ValidationRoot $commentRoot `
        -Mutate { [IO.File]::WriteAllText($directoryPackagesFixturePath, "$directoryPackagesOriginal`n<!-- mutation -->", [Text.UTF8Encoding]::new($false)) } `
        -Restore { [IO.File]::WriteAllText($directoryPackagesFixturePath, $directoryPackagesOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-CONFIG'

    $nugetConfigFixturePath = Join-Path $commentRoot 'NuGet.Config'
    $nugetConfigOriginal = Get-Content $nugetConfigFixturePath -Raw
    Assert-StaticMutationRejected -Name 'nuget-restore-policy-cannot-drift' -ValidationRoot $commentRoot `
        -Mutate { [IO.File]::WriteAllText($nugetConfigFixturePath, "$nugetConfigOriginal`n<!-- mutation -->", [Text.UTF8Encoding]::new($false)) } `
        -Restore { [IO.File]::WriteAllText($nugetConfigFixturePath, $nugetConfigOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-CONFIG'

    $vulnerabilityScannerFixturePath = Join-Path $commentRoot 'scripts/TestEdgePackageVulnerabilities.ps1'
    $vulnerabilityScannerOriginal = Get-Content $vulnerabilityScannerFixturePath -Raw
    Assert-StaticMutationRejected -Name 'package-vulnerability-scanner-cannot-be-hollowed' -ValidationRoot $commentRoot `
        -Mutate { [IO.File]::WriteAllText($vulnerabilityScannerFixturePath, "Write-Host 'no-op'`n", [Text.UTF8Encoding]::new($false)) } `
        -Restore { [IO.File]::WriteAllText($vulnerabilityScannerFixturePath, $vulnerabilityScannerOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-CONFIG'

    $dotnetStubRoot = Join-Path $tempRoot 'empty-vulnerability-report-dotnet'
    [void](New-Item $dotnetStubRoot -ItemType Directory -Force)
    if ($IsWindows) {
        [IO.File]::WriteAllText((Join-Path $dotnetStubRoot 'dotnet.cmd'), "@echo off`r`necho {}`r`n", [Text.UTF8Encoding]::new($false))
    } else {
        $dotnetStubPath = Join-Path $dotnetStubRoot 'dotnet'
        [IO.File]::WriteAllText($dotnetStubPath, "#!/bin/sh`nprintf '%s\n' '{}'`n", [Text.UTF8Encoding]::new($false))
        & chmod +x $dotnetStubPath
        if ($LASTEXITCODE -ne 0) { throw 'Could not make the empty vulnerability-report dotnet stub executable.' }
    }
    $originalPath = $env:PATH
    try {
        $env:PATH = "$dotnetStubRoot$([IO.Path]::PathSeparator)$originalPath"
        $emptyReportOutput = & pwsh -NoLogo -NoProfile -File $vulnerabilityScannerFixturePath 2>&1
        $emptyReportExitCode = $LASTEXITCODE
    } finally {
        $env:PATH = $originalPath
    }
    $emptyReportText = ($emptyReportOutput | Out-String).Trim()
    if ($emptyReportExitCode -eq 0 -or
        -not $emptyReportText.Contains('unsupported or incomplete', [StringComparison]::Ordinal)) {
        throw "Empty vulnerability-report stub should fail closed; exit=${emptyReportExitCode}:`n$emptyReportText"
    }
    Write-Host 'Rejected Edge package-vulnerability fixture: empty report cannot pass scanner coverage'

    $duplicateWorkflowPath = Join-Path $commentRoot '.github/workflows/duplicate-required-check.yml'
    Assert-StaticMutationRejected -Name 'workflow-roster-cannot-grow-with-shadow-check' -ValidationRoot $commentRoot `
        -Mutate { [IO.File]::WriteAllText($duplicateWorkflowPath, "name: Edge smoke build`non: { pull_request: {} }`njobs: { smoke-build: { runs-on: windows-latest, steps: [] } }`n", [Text.UTF8Encoding]::new($false)) } `
        -Restore { Remove-Item $duplicateWorkflowPath -Force -ErrorAction SilentlyContinue } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-CI'

    $dotRunnerPath = Join-Path $commentRoot 'src/Tests/.hidden.xunit.runner.json'
    Assert-StaticMutationRejected -Name 'dot-runner-config-cannot-disable-fail-skips' -ValidationRoot $commentRoot `
        -Mutate { [IO.File]::WriteAllText($dotRunnerPath, '{"failSkips":false}', [Text.UTF8Encoding]::new($false)) } `
        -Restore { Remove-Item $dotRunnerPath -Force -ErrorAction SilentlyContinue } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-DISABLED'

    $pullRequestTargetWorkflow = Join-Path $commentRoot '.github/workflows/edge-smoke-build.yml'
    $pullRequestTargetOriginal = Get-Content $pullRequestTargetWorkflow -Raw
    Assert-StaticMutationRejected -Name 'pull-request-target-cannot-enter-required-workflow' -ValidationRoot $commentRoot `
        -Mutate { [IO.File]::WriteAllText($pullRequestTargetWorkflow, $pullRequestTargetOriginal.Replace("on:`n", "on:`n  pull_request_target: {}`n"), [Text.UTF8Encoding]::new($false)) } `
        -Restore { [IO.File]::WriteAllText($pullRequestTargetWorkflow, $pullRequestTargetOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-CI'

    $movableActionWorkflow = Join-Path $commentRoot '.github/workflows/edge-smoke-build.yml'
    $movableActionOriginal = Get-Content $movableActionWorkflow -Raw
    Assert-StaticMutationRejected -Name 'movable-action-tag-cannot-enter-required-workflow' -ValidationRoot $commentRoot `
        -Mutate {
            $mutated = $movableActionOriginal.Replace('actions/checkout@9c091bb21b7c1c1d1991bb908d89e4e9dddfe3e0 # v7', 'actions/checkout@v7')
            [IO.File]::WriteAllText($movableActionWorkflow, $mutated, [Text.UTF8Encoding]::new($false))
        } `
        -Restore { [IO.File]::WriteAllText($movableActionWorkflow, $movableActionOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-CI'

    $reviewedTrustPreflight = @'
      - name: Validate reviewed restore and build inputs
        shell: pwsh
        run: ./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateStatic -Configuration Release

      - name: Restore Edge solution
        shell: pwsh
        run: dotnet restore IIoT.EdgeClient.slnx -p:RestoreDisableParallel=true --disable-build-servers -noAutoResponse
'@
    $restoreBeforeTrustPreflight = @'
      - name: Restore Edge solution
        shell: pwsh
        run: dotnet restore IIoT.EdgeClient.slnx -p:RestoreDisableParallel=true --disable-build-servers -noAutoResponse

      - name: Validate reviewed restore and build inputs
        shell: pwsh
        run: ./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateStatic -Configuration Release
'@
    Assert-StaticMutationRejected -Name 'restore-cannot-run-before-reviewed-input-gate' -ValidationRoot $commentRoot `
        -Mutate {
            if (-not $movableActionOriginal.Contains($reviewedTrustPreflight, [StringComparison]::Ordinal)) {
                throw 'Restore-order fixture could not locate the reviewed preflight and restore steps.'
            }
            [IO.File]::WriteAllText($movableActionWorkflow, $movableActionOriginal.Replace($reviewedTrustPreflight, $restoreBeforeTrustPreflight), [Text.UTF8Encoding]::new($false))
        } `
        -Restore { [IO.File]::WriteAllText($movableActionWorkflow, $movableActionOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-CI'

    Assert-StaticMutationRejected -Name 'required-workflow-cannot-enable-msbuild-auto-response' -ValidationRoot $commentRoot `
        -Mutate {
            $reviewedRestore = 'dotnet restore IIoT.EdgeClient.slnx -p:RestoreDisableParallel=true --disable-build-servers -noAutoResponse'
            if (-not $movableActionOriginal.Contains($reviewedRestore, [StringComparison]::Ordinal)) {
                throw 'MSBuild response-file workflow fixture could not locate the reviewed restore command.'
            }
            [IO.File]::WriteAllText($movableActionWorkflow, $movableActionOriginal.Replace($reviewedRestore, $reviewedRestore.Replace(' -noAutoResponse', '')), [Text.UTF8Encoding]::new($false))
        } `
        -Restore { [IO.File]::WriteAllText($movableActionWorkflow, $movableActionOriginal, [Text.UTF8Encoding]::new($false)) } `
        -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath -ExpectedCode 'EDGE-TEST-GOV-001-BYPASS-RESPONSE'

    foreach ($workflowPath in @('.github/workflows/edge-smoke-build.yml', '.github/workflows/edge-pack-modules.yml')) {
        $workflow = (Get-Content (Join-Path $RepositoryRoot $workflowPath) -Raw).Replace('        run: dotnet test ', '        # run: dotnet test ')
        [IO.File]::WriteAllText((Join-Path $commentRoot $workflowPath), $workflow, [Text.UTF8Encoding]::new($false))
    }
    $commentResult = Invoke-StaticPolicyValidation -Name 'workflow-comments-do-not-schedule-tests' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($commentResult.ExitCode -eq 0 -or -not $commentResult.Output.Contains('EDGE-TEST-GOV-001-CI', [StringComparison]::Ordinal)) {
        throw "Comment-only workflow fixture should fail with EDGE-TEST-GOV-001-CI:`n$($commentResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: workflow-comments-do-not-schedule-tests (EDGE-TEST-GOV-001-CI)'

    foreach ($workflowPath in @('.github/workflows/edge-smoke-build.yml', '.github/workflows/edge-pack-modules.yml')) {
        Copy-Item (Join-Path $RepositoryRoot $workflowPath) (Join-Path $commentRoot $workflowPath) -Force
    }
    $smokeWorkflowPath = Join-Path $commentRoot '.github/workflows/edge-smoke-build.yml'
    $smokeWorkflow = Get-Content $smokeWorkflowPath -Raw
    $reviewedRunBlock = @'
        run: |
          ./scripts/tests/TestEdgeTestGovernanceBehavior.ps1
          ./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateRunnerCaseNormalization
          ./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateStatic -Configuration Release
'@
    $deadRunBlock = @'
        run: |
          if ($false) {
            ./scripts/tests/TestEdgeTestGovernanceBehavior.ps1
            ./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateRunnerCaseNormalization
            ./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateStatic -Configuration Release
          }
'@
    if (-not $smokeWorkflow.Contains($reviewedRunBlock, [StringComparison]::Ordinal)) {
        throw 'Dead-shell-block fixture could not locate the reviewed governance run block.'
    }
    [IO.File]::WriteAllText($smokeWorkflowPath, $smokeWorkflow.Replace($reviewedRunBlock, $deadRunBlock), [Text.UTF8Encoding]::new($false))
    $deadShellResult = Invoke-StaticPolicyValidation -Name 'dead-shell-block-cannot-satisfy-required-ci-command' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($deadShellResult.ExitCode -eq 0 -or -not $deadShellResult.Output.Contains('EDGE-TEST-GOV-001-CI', [StringComparison]::Ordinal)) {
        throw "Dead shell block should fail with EDGE-TEST-GOV-001-CI:`n$($deadShellResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: dead-shell-block-cannot-satisfy-required-ci-command (EDGE-TEST-GOV-001-CI)'
    Copy-Item (Join-Path $RepositoryRoot '.github/workflows/edge-smoke-build.yml') $smokeWorkflowPath -Force

    $smokeWorkflow = Get-Content $smokeWorkflowPath -Raw
    $reviewedLauncherStep = @'
      - name: Run launcher tests
        shell: pwsh
        run: dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj -c Release --no-build --no-restore -p:BuildInParallel=false --disable-build-servers --nologo -noAutoResponse
'@
    $shadowedLauncherStep = @'
      - name: Run launcher tests
        shell: pwsh
        run: Write-Host "disabled"
        env:
          run: dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj -c Release --no-build --no-restore -p:BuildInParallel=false --disable-build-servers --nologo -noAutoResponse
'@
    if (-not $smokeWorkflow.Contains($reviewedLauncherStep, [StringComparison]::Ordinal)) {
        throw 'Nested env.run shadow fixture could not locate the reviewed launcher step.'
    }
    [IO.File]::WriteAllText($smokeWorkflowPath, $smokeWorkflow.Replace($reviewedLauncherStep, $shadowedLauncherStep), [Text.UTF8Encoding]::new($false))
    $nestedRunResult = Invoke-StaticPolicyValidation -Name 'nested-env-run-cannot-shadow-step-run' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($nestedRunResult.ExitCode -eq 0 -or -not $nestedRunResult.Output.Contains('EDGE-TEST-GOV-001-CI', [StringComparison]::Ordinal)) {
        throw "Nested env.run shadow should fail with EDGE-TEST-GOV-001-CI:`n$($nestedRunResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: nested-env-run-cannot-shadow-step-run (EDGE-TEST-GOV-001-CI)'
    Copy-Item (Join-Path $RepositoryRoot '.github/workflows/edge-smoke-build.yml') $smokeWorkflowPath -Force

    $smokeWorkflow = Get-Content $smokeWorkflowPath -Raw
    $timeoutShadowWorkflow = $smokeWorkflow.Replace("env:`n  DOTNET_NOLOGO: true", "env:`n  DOTNET_NOLOGO: true`n  timeout-minutes: 25")
    $timeoutShadowWorkflow = $timeoutShadowWorkflow.Replace('    timeout-minutes: 25', '    timeout-minutes: 30')
    [IO.File]::WriteAllText($smokeWorkflowPath, $timeoutShadowWorkflow, [Text.UTF8Encoding]::new($false))
    $timeoutShadowResult = Invoke-StaticPolicyValidation -Name 'top-level-timeout-cannot-shadow-required-job-budget' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($timeoutShadowResult.ExitCode -eq 0 -or -not $timeoutShadowResult.Output.Contains('EDGE-TEST-GOV-001-CI', [StringComparison]::Ordinal)) {
        throw "Timeout shadow should fail with EDGE-TEST-GOV-001-CI:`n$($timeoutShadowResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: top-level-timeout-cannot-shadow-required-job-budget (EDGE-TEST-GOV-001-CI)'
    Copy-Item (Join-Path $RepositoryRoot '.github/workflows/edge-smoke-build.yml') $smokeWorkflowPath -Force

    $smokeWorkflow = Get-Content $smokeWorkflowPath -Raw
    $continuedLauncherStep = $reviewedLauncherStep.Replace("        shell: pwsh", "        shell: pwsh`n        continue-on-error: `${{ 1 == 1 }}")
    [IO.File]::WriteAllText($smokeWorkflowPath, $smokeWorkflow.Replace($reviewedLauncherStep, $continuedLauncherStep), [Text.UTF8Encoding]::new($false))
    $continueExpressionResult = Invoke-StaticPolicyValidation -Name 'continue-on-error-expression-cannot-soften-required-step' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($continueExpressionResult.ExitCode -eq 0 -or -not $continueExpressionResult.Output.Contains('EDGE-TEST-GOV-001-CI', [StringComparison]::Ordinal)) {
        throw "continue-on-error expression should fail with EDGE-TEST-GOV-001-CI:`n$($continueExpressionResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: continue-on-error-expression-cannot-soften-required-step (EDGE-TEST-GOV-001-CI)'
    Copy-Item (Join-Path $RepositoryRoot '.github/workflows/edge-smoke-build.yml') $smokeWorkflowPath -Force

    $smokeWorkflow = Get-Content $smokeWorkflowPath -Raw
    $disabledJobWorkflow = $smokeWorkflow.Replace("  smoke-build:`n    runs-on: windows-latest", "  smoke-build:`n    if: `${{ 1 == 0 }}`n    runs-on: windows-latest")
    [IO.File]::WriteAllText($smokeWorkflowPath, $disabledJobWorkflow, [Text.UTF8Encoding]::new($false))
    $disabledJobResult = Invoke-StaticPolicyValidation -Name 'job-if-expression-cannot-disable-required-validation' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($disabledJobResult.ExitCode -eq 0 -or -not $disabledJobResult.Output.Contains('EDGE-TEST-GOV-001-CI', [StringComparison]::Ordinal)) {
        throw "Job if expression should fail with EDGE-TEST-GOV-001-CI:`n$($disabledJobResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: job-if-expression-cannot-disable-required-validation (EDGE-TEST-GOV-001-CI)'
    Copy-Item (Join-Path $RepositoryRoot '.github/workflows/edge-smoke-build.yml') $smokeWorkflowPath -Force

    $smokeWorkflow = Get-Content $smokeWorkflowPath -Raw
    $reviewedValidationBlock = @'
      - name: Validate Edge test repository and legacy discovery ceilings
        shell: pwsh
        run: |
          ./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateRepository -Configuration Release
          ./scripts/tests/TestEdgeTestGovernancePolicy.ps1 -Mode ValidateDiscovery -Configuration Release
'@
    $mutatingValidationBlock = $reviewedValidationBlock + @'

      - name: Mutate validated test outputs
        shell: pwsh
        run: Set-Content src/Tests/IIoT.Edge.Launcher.Tests/bin/Release/net10.0/IIoT.Edge.Launcher.Tests.xunit.runner.json '{"failSkips":false}'
'@
    if (-not $smokeWorkflow.Contains($reviewedValidationBlock, [StringComparison]::Ordinal)) {
        throw 'Post-governance mutation fixture could not locate the reviewed repository/discovery step.'
    }
    [IO.File]::WriteAllText($smokeWorkflowPath, $smokeWorkflow.Replace($reviewedValidationBlock, $mutatingValidationBlock), [Text.UTF8Encoding]::new($false))
    $postGovernanceMutationResult = Invoke-StaticPolicyValidation -Name 'post-governance-mutation-step-cannot-enter-required-job' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($postGovernanceMutationResult.ExitCode -eq 0 -or -not $postGovernanceMutationResult.Output.Contains('EDGE-TEST-GOV-001-CI', [StringComparison]::Ordinal)) {
        throw "Post-governance mutation step should fail with EDGE-TEST-GOV-001-CI:`n$($postGovernanceMutationResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: post-governance-mutation-step-cannot-enter-required-job (EDGE-TEST-GOV-001-CI)'
    Copy-Item (Join-Path $RepositoryRoot '.github/workflows/edge-smoke-build.yml') $smokeWorkflowPath -Force

    $escapedTestRoot = Join-Path $commentRoot 'escape'
    [void](New-Item $escapedTestRoot -ItemType Directory -Force)
    [IO.File]::WriteAllText((Join-Path $escapedTestRoot 'TestPackages.props'), @'
<Project>
  <PropertyGroup>
    <SdkPackage>Microsoft.NET.Test.Sdk</SdkPackage>
    <XunitPackage>xunit.v3</XunitPackage>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$(SdkPackage)" Version="17.14.1" />
    <PackageReference Include="$(XunitPackage)" Version="3.2.2" />
  </ItemGroup>
</Project>
'@, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $escapedTestRoot 'Hidden.csproj'), @'
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="TestPackages.props" />
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
'@, [Text.UTF8Encoding]::new($false))
    $externalResult = Invoke-StaticPolicyValidation -Name 'external-imported-test-project-cannot-escape' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($externalResult.ExitCode -eq 0 -or -not $externalResult.Output.Contains('EDGE-TEST-GOV-001-BYPASS', [StringComparison]::Ordinal)) {
        throw "External imported test-project fixture should fail with EDGE-TEST-GOV-001-BYPASS:`n$($externalResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: external-imported-test-project-cannot-escape (EDGE-TEST-GOV-001-BYPASS)'
    Remove-Item $escapedTestRoot -Recurse -Force

    $insideEscapedRoot = Join-Path $commentRoot 'src/Tests/Escaped.Tests'
    [void](New-Item $insideEscapedRoot -ItemType Directory -Force)
    [IO.File]::WriteAllText((Join-Path $insideEscapedRoot 'TestProject.props'), @'
<Project>
  <PropertyGroup>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
  </ItemGroup>
</Project>
'@, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $insideEscapedRoot 'Escaped.Tests.csproj'), @'
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="TestProject.props" />
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
'@, [Text.UTF8Encoding]::new($false))
    $insideImportResult = Invoke-StaticPolicyValidation -Name 'inside-imported-test-project-cannot-escape' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($insideImportResult.ExitCode -eq 0 -or -not $insideImportResult.Output.Contains('EDGE-TEST-GOV-001-BYPASS', [StringComparison]::Ordinal)) {
        throw "Inside imported test-project fixture should fail with EDGE-TEST-GOV-001-BYPASS:`n$($insideImportResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: inside-imported-test-project-cannot-escape (EDGE-TEST-GOV-001-BYPASS)'
    Remove-Item $insideEscapedRoot -Recurse -Force

    $propertyEscapedRoot = Join-Path $commentRoot 'src/Tests/PropertyEscaped.Tests'
    [void](New-Item $propertyEscapedRoot -ItemType Directory -Force)
    [IO.File]::WriteAllText((Join-Path $propertyEscapedRoot 'PropertyEscaped.Tests.csproj'), @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <TestFlag>true</TestFlag>
    <IsTestProject>$(TestFlag)</IsTestProject>
    <TestSdkPackage>Microsoft.NET.Test.Sdk</TestSdkPackage>
    <XunitPackage>xunit.v3</XunitPackage>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="$(TestSdkPackage)" Version="17.14.1" />
    <PackageReference Include="$(XunitPackage)" Version="3.2.2" />
  </ItemGroup>
</Project>
'@, [Text.UTF8Encoding]::new($false))
    $propertyIndirectionResult = Invoke-StaticPolicyValidation -Name 'property-indirected-test-project-cannot-escape' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($propertyIndirectionResult.ExitCode -eq 0 -or
        (-not $propertyIndirectionResult.Output.Contains('EDGE-TEST-GOV-001-PROJECT', [StringComparison]::Ordinal) -and
         -not $propertyIndirectionResult.Output.Contains('EDGE-TEST-GOV-001-BYPASS', [StringComparison]::Ordinal))) {
        throw "Property-indirected test-project fixture should fail closed:`n$($propertyIndirectionResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: property-indirected-test-project-cannot-escape (PROJECT/BYPASS)'
    Remove-Item $propertyEscapedRoot -Recurse -Force

    $fsharpEscapedRoot = Join-Path $commentRoot 'src/Tests/Escaped.FSharp.Tests'
    [void](New-Item $fsharpEscapedRoot -ItemType Directory -Force)
    [IO.File]::WriteAllText((Join-Path $fsharpEscapedRoot 'Escaped.FSharp.Tests.fsproj'), @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
  </ItemGroup>
</Project>
'@, [Text.UTF8Encoding]::new($false))
    $fsharpProjectResult = Invoke-StaticPolicyValidation -Name 'non-csharp-test-project-cannot-escape' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($fsharpProjectResult.ExitCode -eq 0 -or -not $fsharpProjectResult.Output.Contains('EDGE-TEST-GOV-001-BYPASS', [StringComparison]::Ordinal)) {
        throw "Non-C# test project should fail with EDGE-TEST-GOV-001-BYPASS:`n$($fsharpProjectResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: non-csharp-test-project-cannot-escape (EDGE-TEST-GOV-001-BYPASS)'
    Remove-Item $fsharpEscapedRoot -Recurse -Force

    $overrideProjectPath = Join-Path $commentRoot ([string]$reviewedBaseline.projects[0].projectPath)
    $runSettingsProjectDirectory = Split-Path $overrideProjectPath -Parent
    $runSettingsPath = Join-Path $runSettingsProjectDirectory 'override.runsettings'
    [IO.File]::WriteAllText($runSettingsPath, @'
<RunSettings>
  <xUnit>
    <FailSkips>false</FailSkips>
  </xUnit>
</RunSettings>
'@, [Text.UTF8Encoding]::new($false))
    $runSettingsProject = Get-Content $overrideProjectPath -Raw
    $runSettingsProject = $runSettingsProject.Replace('</Project>', @'
  <PropertyGroup>
    <RunSettingsFilePath>override.runsettings</RunSettingsFilePath>
  </PropertyGroup>
</Project>
'@)
    [IO.File]::WriteAllText($overrideProjectPath, $runSettingsProject, [Text.UTF8Encoding]::new($false))
    $runSettingsResult = Invoke-StaticPolicyValidation -Name 'runsettings-cannot-override-fail-skips' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($runSettingsResult.ExitCode -eq 0 -or -not $runSettingsResult.Output.Contains('EDGE-TEST-GOV-001-DISABLED', [StringComparison]::Ordinal)) {
        throw "VSTest runsettings fixture should fail with EDGE-TEST-GOV-001-DISABLED:`n$($runSettingsResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: runsettings-cannot-override-fail-skips (EDGE-TEST-GOV-001-DISABLED)'
    Remove-Item $runSettingsPath -Force
    Copy-Item (Join-Path $RepositoryRoot ([string]$reviewedBaseline.projects[0].projectPath)) $overrideProjectPath -Force

    $overrideProject = Get-Content $overrideProjectPath -Raw
    $overrideProject = $overrideProject.Replace('</Project>', @'
  <ItemGroup>
    <None Update="$(MSBuildThisFileDirectory)..\xunit.runner.json" CopyToOutputDirectory="Never" />
  </ItemGroup>
</Project>
'@)
    [IO.File]::WriteAllText($overrideProjectPath, $overrideProject, [Text.UTF8Encoding]::new($false))
    $runnerOverrideResult = Invoke-StaticPolicyValidation -Name 'project-runner-config-override-cannot-disable-fail-skips' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($runnerOverrideResult.ExitCode -eq 0 -or -not $runnerOverrideResult.Output.Contains('EDGE-TEST-GOV-001-DISABLED', [StringComparison]::Ordinal)) {
        throw "Runner-config override fixture should fail with EDGE-TEST-GOV-001-DISABLED:`n$($runnerOverrideResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: project-runner-config-override-cannot-disable-fail-skips (EDGE-TEST-GOV-001-DISABLED)'
    Copy-Item (Join-Path $RepositoryRoot ([string]$reviewedBaseline.projects[0].projectPath)) $overrideProjectPath -Force

    $overrideProjectDirectory = Split-Path $overrideProjectPath -Parent
    $assemblySpecificSourceConfig = Join-Path $overrideProjectDirectory 'Synthetic.Tests.xunit.runner.json'
    [IO.File]::WriteAllText($assemblySpecificSourceConfig, '{"failSkips":false}', [Text.UTF8Encoding]::new($false))
    $wildcardProject = Get-Content $overrideProjectPath -Raw
    $wildcardProject = $wildcardProject.Replace('</Project>', @'
  <ItemGroup>
    <None Update="*.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
'@)
    [IO.File]::WriteAllText($overrideProjectPath, $wildcardProject, [Text.UTF8Encoding]::new($false))
    $assemblySpecificStaticResult = Invoke-StaticPolicyValidation -Name 'assembly-specific-runner-config-cannot-override-fail-skips' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($assemblySpecificStaticResult.ExitCode -eq 0 -or -not $assemblySpecificStaticResult.Output.Contains('EDGE-TEST-GOV-001-DISABLED', [StringComparison]::Ordinal)) {
        throw "Assembly-specific runner configuration fixture should fail with EDGE-TEST-GOV-001-DISABLED:`n$($assemblySpecificStaticResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: assembly-specific-runner-config-cannot-override-fail-skips (EDGE-TEST-GOV-001-DISABLED)'
    Remove-Item $assemblySpecificSourceConfig -Force
    Copy-Item (Join-Path $RepositoryRoot ([string]$reviewedBaseline.projects[0].projectPath)) $overrideProjectPath -Force

    $unreviewedTargetProject = Get-Content $overrideProjectPath -Raw
    $unreviewedTargetProject = $unreviewedTargetProject.Replace('</Project>', @'
  <Target Name="RewriteValidatedRunnerConfiguration" AfterTargets="Build">
    <WriteLinesToFile File="$(TargetDir)Synthetic.Tests.xunit.runner.json" Lines="{&quot;failSkips&quot;:false}" Overwrite="true" />
  </Target>
</Project>
'@)
    [IO.File]::WriteAllText($overrideProjectPath, $unreviewedTargetProject, [Text.UTF8Encoding]::new($false))
    $unreviewedTargetResult = Invoke-StaticPolicyValidation -Name 'unreviewed-test-project-target-cannot-change-output' -ValidationRoot $commentRoot -BaselinePath $reviewedBaselinePath -WaiverPath $reviewedWaiverPath
    if ($unreviewedTargetResult.ExitCode -eq 0 -or -not $unreviewedTargetResult.Output.Contains('EDGE-TEST-GOV-001-BYPASS', [StringComparison]::Ordinal)) {
        throw "Unreviewed test-project target should fail with EDGE-TEST-GOV-001-BYPASS:`n$($unreviewedTargetResult.Output)"
    }
    Write-Host 'Rejected Edge static-governance fixture: unreviewed-test-project-target-cannot-change-output (EDGE-TEST-GOV-001-BYPASS)'
    Copy-Item (Join-Path $RepositoryRoot ([string]$reviewedBaseline.projects[0].projectPath)) $overrideProjectPath -Force

    $runnerConfigFixtureDirectory = Join-Path $tempRoot 'runner-config-output'
    [void](New-Item $runnerConfigFixtureDirectory -ItemType Directory -Force)
    $runnerConfigFixture = Join-Path $runnerConfigFixtureDirectory 'xunit.runner.json'
    Copy-Item (Join-Path $RepositoryRoot 'src/Tests/xunit.runner.json') $runnerConfigFixture
    $runnerPolicyArguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $policyPath, '-Mode', 'ValidateRunnerConfiguration', '-RunnerConfigPath', $runnerConfigFixture)
    $validRunnerOutput = & pwsh @runnerPolicyArguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Canonical built runner configuration should pass:`n$(($validRunnerOutput | Out-String).Trim())"
    }
    $assemblySpecificOutputConfig = Join-Path $runnerConfigFixtureDirectory 'Synthetic.Tests.xunit.runner.json'
    [IO.File]::WriteAllText($assemblySpecificOutputConfig, '{"failSkips":false}', [Text.UTF8Encoding]::new($false))
    $assemblySpecificOutput = & pwsh @runnerPolicyArguments 2>&1
    if ($LASTEXITCODE -eq 0 -or -not (($assemblySpecificOutput | Out-String).Contains('EDGE-TEST-GOV-001-DISABLED', [StringComparison]::Ordinal))) {
        throw "Assembly-specific built runner configuration should fail with EDGE-TEST-GOV-001-DISABLED:`n$(($assemblySpecificOutput | Out-String).Trim())"
    }
    Remove-Item $assemblySpecificOutputConfig -Force
    [IO.File]::WriteAllText($runnerConfigFixture, '{"failSkips":false}', [Text.UTF8Encoding]::new($false))
    $tamperedRunnerOutput = & pwsh @runnerPolicyArguments 2>&1
    if ($LASTEXITCODE -eq 0 -or -not (($tamperedRunnerOutput | Out-String).Contains('EDGE-TEST-GOV-001-DISABLED', [StringComparison]::Ordinal))) {
        throw "Tampered built runner configuration should fail with EDGE-TEST-GOV-001-DISABLED:`n$(($tamperedRunnerOutput | Out-String).Trim())"
    }
    Remove-Item $runnerConfigFixture -Force
    $missingRunnerOutput = & pwsh @runnerPolicyArguments 2>&1
    if ($LASTEXITCODE -eq 0 -or -not (($missingRunnerOutput | Out-String).Contains('EDGE-TEST-GOV-001-DISABLED', [StringComparison]::Ordinal))) {
        throw "Missing built runner configuration should fail with EDGE-TEST-GOV-001-DISABLED:`n$(($missingRunnerOutput | Out-String).Trim())"
    }
    Write-Host 'Rejected Edge output-governance fixtures: tampered/missing xunit.runner.json (EDGE-TEST-GOV-001-DISABLED)'

    $skipFixtureRoot = Join-Path $tempRoot 'runtime-skip-must-fail'
    [void](New-Item $skipFixtureRoot -ItemType Directory -Force)
    Copy-Item (Join-Path $RepositoryRoot 'src/Tests/xunit.runner.json') (Join-Path $skipFixtureRoot 'xunit.runner.json')
    [IO.File]::WriteAllText((Join-Path $skipFixtureRoot 'RuntimeSkipFixture.csproj'), @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
    <None Update="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
'@, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $skipFixtureRoot 'RuntimeSkipFixture.cs'), @'
using Xunit;

public sealed class RuntimeSkipFixture
{
    [Fact]
    public void RuntimeSkipMustFailRequiredLane() => Assert.Skip("synthetic runtime skip");
}
'@, [Text.UTF8Encoding]::new($false))
    $skipOutput = & dotnet test (Join-Path $skipFixtureRoot 'RuntimeSkipFixture.csproj') -c Release -p:RestoreDisableParallel=true --disable-build-servers --nologo -noAutoResponse 2>&1
    if ($LASTEXITCODE -eq 0) {
        throw "Runtime Assert.Skip fixture must produce a non-zero test exit when failSkips=true:`n$(($skipOutput | Out-String).Trim())"
    }
    Write-Host 'Rejected Edge runtime-governance fixture: Assert.Skip produces non-zero exit (failSkips=true)'

    Write-Host 'Edge test-governance behavior fixtures passed.'
} finally {
    Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
