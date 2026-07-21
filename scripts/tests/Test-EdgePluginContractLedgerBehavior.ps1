[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$BaselinePath,
    [Parameter(Mandatory)][string]$AuthorityReceiptPath,
    [Parameter(Mandatory)][string]$AuthorityPublicKeySpkiBase64,
    [Parameter(Mandatory)][string]$AuthorityRunId,
    [Parameter(Mandatory)][string]$AuthorityChallengeBase64,
    [Parameter(Mandatory)][string]$AuthoritySourceRepositoryRoot,
    [Parameter(Mandatory)][string]$AuthorityBindingsBase64
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$pinnedChildProtocolModulePath = Join-Path $PSScriptRoot 'EdgePluginContractLedger.Protocol.psm1'
Import-Module $pinnedChildProtocolModulePath -Force
if (-not [bool](Initialize-EdgeAuthorityGitChildEnvironment)) {
    throw 'EDGE-SPLIT-AUTHORITY-CHILD-BINDING behavior authority mode requires the exact parent-controlled child binding.'
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}
else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

$validatorPath = Join-Path $RepositoryRoot 'scripts/tests/Test-EdgePluginContractLedger.ps1'
$generatorPath = Join-Path $RepositoryRoot 'eng/Generate-EdgePluginContractLedger.ps1'
$primitivesPath = Join-Path $RepositoryRoot 'scripts/tests/Test-EdgePluginContractLedgerPrimitives.ps1'
if ([string]::IsNullOrWhiteSpace($BaselinePath) -or
    [string]::IsNullOrWhiteSpace($AuthorityReceiptPath) -or
    [string]::IsNullOrWhiteSpace($AuthorityPublicKeySpkiBase64) -or
    [string]::IsNullOrWhiteSpace($AuthorityRunId) -or
    [string]::IsNullOrWhiteSpace($AuthorityChallengeBase64) -or
    [string]::IsNullOrWhiteSpace($AuthoritySourceRepositoryRoot) -or
    [string]::IsNullOrWhiteSpace($AuthorityBindingsBase64)) {
    throw 'EDGE-SPLIT-LEDGER-BEHAVIOR-AUTHORITY signed development authority inputs are required.'
}
$baselinePath = if ([IO.Path]::IsPathRooted($BaselinePath)) {
    [IO.Path]::GetFullPath($BaselinePath)
}
else {
    [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $BaselinePath))
}
$protocolModulePath = Join-Path $RepositoryRoot 'scripts/tests/EdgePluginContractLedger.Protocol.psm1'
Import-Module $protocolModulePath -Force
try {
    $bindingBytes = [Convert]::FromBase64String($AuthorityBindingsBase64)
    $bindingRaw = [Text.UTF8Encoding]::new($false, $true).GetString($bindingBytes)
    $authorityBindings = ConvertFrom-EdgeJsonText $bindingRaw
}
catch { throw 'EDGE-SPLIT-LEDGER-BEHAVIOR-AUTHORITY parent authority bindings are malformed.' }
[byte[]]$canonicalBindingBytes = @(
    ConvertTo-EdgeCanonicalBytes $authorityBindings)
if (-not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
        $bindingBytes, $canonicalBindingBytes)) {
    throw 'EDGE-SPLIT-LEDGER-BEHAVIOR-AUTHORITY parent authority bindings are not canonical bytes.'
}
if ([string]$authorityBindings.runId -cne $AuthorityRunId -or
    [string]$authorityBindings.challengeBase64 -cne $AuthorityChallengeBase64 -or
    -not (Test-EdgePathIdentity ([string]$authorityBindings.sourceRepositoryRoot) $AuthoritySourceRepositoryRoot) -or
    [string]$authorityBindings.receiptPath -cne $AuthorityReceiptPath) {
    throw 'EDGE-SPLIT-LEDGER-BEHAVIOR-AUTHORITY direct parent authority bindings differ from behavior arguments.'
}
$authorityEnvironment = [ordered]@{
    EDGE_PLUGIN_CONTRACT_AUTHORITY_RECEIPT = $AuthorityReceiptPath
    EDGE_PLUGIN_CONTRACT_AUTHORITY_PUBLIC_KEY = $AuthorityPublicKeySpkiBase64
    EDGE_PLUGIN_CONTRACT_AUTHORITY_RUN_ID = $AuthorityRunId
    EDGE_PLUGIN_CONTRACT_AUTHORITY_CHALLENGE = $AuthorityChallengeBase64
    EDGE_PLUGIN_CONTRACT_AUTHORITY_SOURCE_ROOT = $AuthoritySourceRepositoryRoot
    EDGE_PLUGIN_CONTRACT_AUTHORITY_HEAD = [string]$authorityBindings.authorityHead
    EDGE_PLUGIN_CONTRACT_AUTHORITY_TREE = [string]$authorityBindings.authorityTree
    EDGE_PLUGIN_CONTRACT_FORMAL_FINAL_HEAD = [string]$authorityBindings.formalFinalHead
    EDGE_PLUGIN_CONTRACT_FORMAL_FINAL_TREE = [string]$authorityBindings.formalFinalTree
    EDGE_PLUGIN_CONTRACT_SOURCE_BASE_HEAD = [string]$authorityBindings.sourceBaseHead
    EDGE_PLUGIN_CONTRACT_SOURCE_BASE_TREE = [string]$authorityBindings.sourceBaseTree
    EDGE_PLUGIN_CONTRACT_SOURCE_DIRTY_MANIFEST_SHA256 = [string]$authorityBindings.sourceDirtyManifestSha256
    EDGE_PLUGIN_CONTRACT_EPHEMERAL_SNAPSHOT_HEAD = [string]$authorityBindings.ephemeralSnapshotHead
    EDGE_PLUGIN_CONTRACT_EPHEMERAL_SNAPSHOT_TREE = [string]$authorityBindings.ephemeralSnapshotTree
    EDGE_PLUGIN_CONTRACT_IMPLEMENTATION_HEAD = [string]$authorityBindings.implementationHead
    EDGE_PLUGIN_CONTRACT_IMPLEMENTATION_TREE = [string]$authorityBindings.implementationTree
}
foreach ($name in $authorityEnvironment.Keys) {
    [Environment]::SetEnvironmentVariable([string]$name, [string]$authorityEnvironment[$name], 'Process')
}

$baselineRelativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $baselinePath).Replace('\', '/')
$positiveOutput = & ([Environment]::ProcessPath) -NoLogo -NoProfile -File $validatorPath `
    -RepositoryRoot $RepositoryRoot -LedgerPath $baselineRelativePath -RequireAuthorityReceipt 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or
    -not $positiveOutput.Contains('Edge plugin contract ledger fast receipt passed:', [StringComparison]::Ordinal)) {
    throw "EDGE-SPLIT-LEDGER-BEHAVIOR-AUTHORITY shared positive signed receipt failed.`n$positiveOutput"
}
$positiveLedger = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json -Depth 100
$deterministicTargetInputs = @($positiveLedger.msbuildCompilation.authorityInputs | Where-Object {
        [string]$_.path -ceq 'eng/EdgePluginContractDeterministicBuild.targets'
    })
if ($deterministicTargetInputs.Count -ne 1 -or
    [string]$deterministicTargetInputs[0].origin -cne 'tracked-repository' -or
    [string]$deterministicTargetInputs[0].representation -cne 'raw-sha256' -or
    (@($deterministicTargetInputs[0].roles) -join "`n") -cne
        "deterministic-build-targets`nevaluated-import") {
    throw 'EDGE-SPLIT-LEDGER-001 positive ledger lost the exact deterministic-build authority role binding.'
}

$primitiveOutput = & ([Environment]::ProcessPath) -NoLogo -NoProfile -File $primitivesPath `
    -RepositoryRoot $RepositoryRoot 2>&1 | Out-String
$primitiveExitCode = $LASTEXITCODE
if ($primitiveExitCode -ne 0 -or
    -not $primitiveOutput.Contains(
        'Edge plugin contract ledger primitive fixtures passed: 25/25.',
        [StringComparison]::Ordinal)) {
    throw "EDGE-SPLIT-LEDGER-001 primitive fixtures failed before behavior validation (exit=$primitiveExitCode).`n$primitiveOutput"
}
Write-Host $primitiveOutput.Trim()

$fixturePathRoot = Join-Path $RepositoryRoot '.artifacts/test-temp'
$fixtureRoot = Join-Path $fixturePathRoot "edge-contract-ledger-$([Guid]::NewGuid().ToString('N'))"
[void](New-Item -ItemType Directory -Path $fixtureRoot -Force)

function Set-PayloadDigest {
    param([Parameter(Mandatory = $true)]$Ledger)

    $Ledger.integrity.payloadSha256 = ''
    $json = ($Ledger | ConvertTo-Json -Depth 100) + "`n"
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
    $Ledger.integrity.payloadSha256 = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Assert-MutantRejected {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$ExpectedCode,
        [Parameter(Mandatory = $true)][scriptblock]$Mutate
    )

    $originalBytes = [IO.File]::ReadAllBytes($baselinePath)
    try {
        $ledger = [Text.UTF8Encoding]::new($false, $true).GetString($originalBytes) |
            ConvertFrom-Json -Depth 100
        & $Mutate $ledger
        Set-PayloadDigest $ledger
        [IO.File]::WriteAllText(
            $baselinePath,
            (($ledger | ConvertTo-Json -Depth 100) + "`n"),
            [Text.UTF8Encoding]::new($false))
        $relativePath = [IO.Path]::GetRelativePath($RepositoryRoot, $baselinePath).Replace('\', '/')
        $output = & ([Environment]::ProcessPath) -NoLogo -NoProfile -File $validatorPath `
            -RepositoryRoot $RepositoryRoot -LedgerPath $relativePath -RequireAuthorityReceipt 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0 -or -not $output.Contains($ExpectedCode, [StringComparison]::Ordinal)) {
            throw "EDGE-SPLIT-LEDGER-001 negative fixture '$Name' was not rejected for '$ExpectedCode'.`n$output"
        }
    }
    finally {
        [IO.File]::WriteAllBytes($baselinePath, $originalBytes)
    }
}

function Invoke-FixtureGit {
    param(
        [Parameter(Mandatory = $true)][string]$GitRoot,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $output = & git -C $GitRoot @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Git fixture command failed: git $($Arguments -join ' ')`n$output"
    }
    return $output.Trim()
}

function New-CommitPairFixture {
    param([Parameter(Mandatory = $true)][string]$Name)

    $gitRoot = Join-Path $fixtureRoot $Name
    $canonicalDirectory = Join-Path $gitRoot 'eng/baselines'
    [void](New-Item -ItemType Directory -Path $canonicalDirectory -Force)
    [void](Invoke-FixtureGit $gitRoot @('init', '-b', 'main'))
    [void](Invoke-FixtureGit $gitRoot @('config', 'user.name', 'Edge Ledger Fixture'))
    [void](Invoke-FixtureGit $gitRoot @('config', 'user.email', 'edge-ledger-fixture@example.invalid'))
    [IO.File]::WriteAllText((Join-Path $gitRoot 'implementation.txt'), "implementation`n", [Text.UTF8Encoding]::new($false))
    [void](Invoke-FixtureGit $gitRoot @('add', 'implementation.txt'))
    [void](Invoke-FixtureGit $gitRoot @('commit', '-m', 'implementation'))
    $implementationHead = Invoke-FixtureGit $gitRoot @('rev-parse', 'HEAD')
    $ledger = [pscustomobject][ordered]@{
        sourceState = [pscustomobject][ordered]@{ head = $implementationHead }
    }
    $canonicalPath = Join-Path $canonicalDirectory 'edge-plugin-contract-ledger.json'
    [IO.File]::WriteAllText(
        $canonicalPath,
        (($ledger | ConvertTo-Json -Depth 10) + "`n"),
        [Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{
        root = $gitRoot
        canonicalPath = $canonicalPath
    }
}

function Assert-CommitPairFixtureRejected {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Arrange
    )

    $fixture = New-CommitPairFixture $Name
    & $Arrange $fixture
    $output = & pwsh -NoLogo -NoProfile -File $validatorPath `
        -RepositoryRoot $fixture.root -CommitPairGateOnly 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or -not $output.Contains('EDGE-SPLIT-LEDGER-001', [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-LEDGER-001 commit-pair fixture '$Name' was not rejected.`n$output"
    }
}

function New-ReplayGateFixture {
    param([Parameter(Mandatory = $true)][string]$Name)

    $gitRoot = Join-Path $fixtureRoot $Name
    $fixtureGeneratorPath = Join-Path $gitRoot 'eng/Generate-EdgePluginContractLedger.ps1'
    $fixtureProtocolModulePath = Join-Path $gitRoot 'scripts/tests/EdgePluginContractLedger.Protocol.psm1'
    $fixtureGlobalJsonPath = Join-Path $gitRoot 'global.json'
    $canonicalPath = Join-Path $gitRoot 'eng/baselines/edge-plugin-contract-ledger.json'
    [void](New-Item -ItemType Directory -Path (Split-Path $canonicalPath -Parent) -Force)
    [void](New-Item -ItemType Directory -Path (Split-Path $fixtureProtocolModulePath -Parent) -Force)
    [IO.File]::WriteAllBytes($fixtureGeneratorPath, [IO.File]::ReadAllBytes($generatorPath))
    [IO.File]::WriteAllBytes($fixtureProtocolModulePath, [IO.File]::ReadAllBytes($protocolModulePath))
    [IO.File]::WriteAllBytes(
        $fixtureGlobalJsonPath,
        [IO.File]::ReadAllBytes((Join-Path $RepositoryRoot 'global.json')))
    [void](Invoke-FixtureGit $gitRoot @('init', '-b', 'main'))
    [void](Invoke-FixtureGit $gitRoot @('config', 'user.name', 'Edge Replay Fixture'))
    [void](Invoke-FixtureGit $gitRoot @('config', 'user.email', 'edge-replay-fixture@example.invalid'))
    [IO.File]::WriteAllText((Join-Path $gitRoot 'implementation.txt'), "implementation`n", [Text.UTF8Encoding]::new($false))
    [void](Invoke-FixtureGit $gitRoot @(
            'add',
            'eng/Generate-EdgePluginContractLedger.ps1',
            'scripts/tests/EdgePluginContractLedger.Protocol.psm1',
            'global.json',
            'implementation.txt'))
    [void](Invoke-FixtureGit $gitRoot @('commit', '-m', 'implementation'))
    $implementationHead = Invoke-FixtureGit $gitRoot @('rev-parse', 'HEAD')
    $implementationTree = Invoke-FixtureGit $gitRoot @('rev-parse', 'HEAD^{tree}')
    return [pscustomobject]@{
        root = $gitRoot
        generatorPath = $fixtureGeneratorPath
        canonicalPath = $canonicalPath
        headArgument = $implementationHead
        treeArgument = $implementationTree
        outputPath = '.artifacts/replay.json'
    }
}

function Add-ReplayEvidenceCommit {
    param(
        [Parameter(Mandatory = $true)]$Fixture,
        [switch]$IncludeUnexpectedPath
    )

    [IO.File]::WriteAllText($Fixture.canonicalPath, "{}`n", [Text.UTF8Encoding]::new($false))
    [void](Invoke-FixtureGit $Fixture.root @('add', 'eng/baselines/edge-plugin-contract-ledger.json'))
    if ($IncludeUnexpectedPath) {
        [IO.File]::WriteAllText((Join-Path $Fixture.root 'unexpected.txt'), "unexpected`n", [Text.UTF8Encoding]::new($false))
        [void](Invoke-FixtureGit $Fixture.root @('add', 'unexpected.txt'))
    }
    [void](Invoke-FixtureGit $Fixture.root @('commit', '-m', 'evidence'))
}

function Assert-ReplayGateFixtureRejected {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Arrange,
        [Parameter(Mandatory = $true)][string]$ExpectedMessage
    )

    $fixture = New-ReplayGateFixture $Name
    & $Arrange $fixture
    Push-Location $fixture.root
    try {
        $output = & pwsh -NoLogo -NoProfile -File $fixture.generatorPath `
            -PluginProject 'missing.csproj' `
            -OutputPath $fixture.outputPath `
            -ValidationReplayImplementationHead $fixture.headArgument `
            -ValidationReplayImplementationTree $fixture.treeArgument `
            -ValidationReplayGateOnly 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $normalizedOutput = [Text.RegularExpressions.Regex]::Replace($output.Replace('|', ' '), '\s+', ' ')
    if ($exitCode -eq 0 -or -not $normalizedOutput.Contains($ExpectedMessage, [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-LEDGER-001 replay misuse fixture '$Name' was not rejected for the expected reason '$ExpectedMessage'.`n$output"
    }
}

try {
    $mutants = [ordered]@{
        'decisions-required-field' = {
            param($ledger)
            $ledger.decisions.PSObject.Properties.Remove('targetHostApiVersion')
        }
        'decisions-unknown-field' = {
            param($ledger)
            $ledger.decisions | Add-Member -NotePropertyName schemaBypass -NotePropertyValue $true
        }
        'test-inventory-required-field' = {
            param($ledger)
            $ledger.testInventory.PSObject.Properties.Remove('inventorySha256')
        }
        'test-inventory-unknown-field' = {
            param($ledger)
            $ledger.testInventory | Add-Member -NotePropertyName schemaBypass -NotePropertyValue $true
        }
        'plugin-manifest-required-field' = {
            param($ledger)
            $ledger.pluginManifest.PSObject.Properties.Remove('value')
        }
        'plugin-manifest-unknown-field' = {
            param($ledger)
            $ledger.pluginManifest | Add-Member -NotePropertyName schemaBypass -NotePropertyValue $true
        }
        'published-composition-required-field' = {
            param($ledger)
            $ledger.publishedComposition.PSObject.Properties.Remove('byteVerification')
        }
        'published-composition-unknown-field' = {
            param($ledger)
            $ledger.publishedComposition | Add-Member -NotePropertyName schemaBypass -NotePropertyValue $true
        }
        'msbuild-compilation-required-field' = {
            param($ledger)
            $ledger.msbuildCompilation.PSObject.Properties.Remove('pluginSources')
        }
        'msbuild-compilation-unknown-field' = {
            param($ledger)
            $ledger.msbuildCompilation | Add-Member -NotePropertyName schemaBypass -NotePropertyValue $true
        }
        'deterministic-target-role-deleted' = {
            param($ledger)
            $targetInput = @($ledger.msbuildCompilation.authorityInputs | Where-Object {
                    [string]$_.path -ceq 'eng/EdgePluginContractDeterministicBuild.targets'
                })[0]
            $targetInput.roles = @($targetInput.roles | Where-Object {
                    [string]$_ -cne 'deterministic-build-targets'
                })
        }
        'deterministic-target-role-illegal' = {
            param($ledger)
            $targetInput = @($ledger.msbuildCompilation.authorityInputs | Where-Object {
                    [string]$_.path -ceq 'eng/EdgePluginContractDeterministicBuild.targets'
                })[0]
            $targetInput.roles = @($targetInput.roles | ForEach-Object {
                    if ([string]$_ -ceq 'deterministic-build-targets') {
                        'deterministic-build-target'
                    }
                    else { $_ }
                })
        }
        'view-inventory-required-field' = {
            param($ledger)
            $ledger.viewInventory[0].PSObject.Properties.Remove('viewId')
        }
        'view-inventory-unknown-field' = {
            param($ledger)
            $ledger.viewInventory[0] | Add-Member -NotePropertyName schemaBypass -NotePropertyValue $true
        }
        'page-inventory-required-field' = {
            param($ledger)
            $ledger.pageInventory[0].PSObject.Properties.Remove('sourcePath')
        }
        'page-inventory-unknown-field' = {
            param($ledger)
            $ledger.pageInventory[0] | Add-Member -NotePropertyName schemaBypass -NotePropertyValue $true
        }
        'resource-inventory-required-field' = {
            param($ledger)
            $ledger.resourceInventory[0].PSObject.Properties.Remove('key')
        }
        'resource-inventory-unknown-field' = {
            param($ledger)
            $ledger.resourceInventory[0] | Add-Member -NotePropertyName schemaBypass -NotePropertyValue $true
        }
        'reference-assembly-required-field' = {
            param($ledger)
            $ledger.referenceAssemblies[0].PSObject.Properties.Remove('sha256')
        }
        'reference-assembly-unknown-field' = {
            param($ledger)
            $ledger.referenceAssemblies[0] | Add-Member -NotePropertyName schemaBypass -NotePropertyValue $true
        }
        'analyzed-input-digest-forged' = {
            param($ledger)
            $ledger.integrity.analyzedInputsSha256 = ('0' * 64)
        }
        'plugin-source-digest-forged' = {
            param($ledger)
            $ledger.msbuildCompilation.pluginSources[0].sha256 = ('0' * 64)
        }
        'view-fact-forged' = {
            param($ledger)
            $ledger.viewInventory[0].viewId += 'Forged'
        }
        'view-set-item-deleted' = {
            param($ledger)
            $ledger.viewInventory = @($ledger.viewInventory | Select-Object -Skip 1)
        }
        'view-set-item-injected' = {
            param($ledger)
            $forged = [pscustomobject][ordered]@{
                viewId = 'Homogenization.ForgedView'
                propertyName = 'ForgedView'
                registrationSource = [string]$ledger.viewInventory[0].registrationSource
                viewOwner = 'host-standard-page'
            }
            $ledger.viewInventory = @($ledger.viewInventory + $forged)
        }
        'resource-fact-forged' = {
            param($ledger)
            $ledger.resourceInventory[0].key += '_Forged'
        }
        'msbuild-source-count-forged' = {
            param($ledger)
            $ledger.msbuildCompilation.msbuildCompileSourceCount++
        }
        'project-items-required' = {
            param($ledger)
            $ledger.dependencyLayers.evaluatedProjectReferences.PSObject.Properties.Remove('items')
        }
        'project-layer-unknown-property' = {
            param($ledger)
            $ledger.dependencyLayers.evaluatedProjectReferences |
                Add-Member -NotePropertyName schemaBypass -NotePropertyValue $true
        }
        'project-total-count-forged' = {
            param($ledger)
            $ledger.dependencyLayers.evaluatedProjectReferences.totalCount++
        }
        'roslyn-inputs-required' = {
            param($ledger)
            $ledger.dependencyLayers.roslynForbiddenSymbols.PSObject.Properties.Remove('inputs')
        }
        'roslyn-forbidden-count-forged' = {
            param($ledger)
            $ledger.dependencyLayers.roslynForbiddenSymbols.forbiddenUsageCount++
        }
        'pe-inputs-required' = {
            param($ledger)
            $ledger.dependencyLayers.peAssemblyReferences.PSObject.Properties.Remove('inputs')
        }
        'pe-total-count-forged' = {
            param($ledger)
            $ledger.dependencyLayers.peAssemblyReferences.totalCount++
        }
        'package-na-cannot-masquerade-as-evaluated' = {
            param($ledger)
            $ledger.dependencyLayers.packagedAssemblies.status = 'evaluated'
        }
        'package-entry-count-forged' = {
            param($ledger)
            $package = $ledger.dependencyLayers.packagedAssemblies
            $package.status = 'evaluated'
            $package.packagePath = '.artifacts/review/fake-package.zip'
            $package.packageSha256 = ('0' * 64)
            $package.entries = @(
                [pscustomobject][ordered]@{
                    path = 'plugin.json'
                    size = 1
                    sha256 = ('0' * 64)
                    category = 'plugin-manifest'
                    owner = 'Homogenization'
                    allowed = $true
                })
            $package.totalEntryCount = 2
            $package.assemblies = @(
                [pscustomobject][ordered]@{
                    path = 'IIoT.Edge.Module.Homogenization.dll'
                    assemblyName = 'IIoT.Edge.Module.Homogenization'
                    assemblyVersion = '1.0.0.0'
                    culture = 'neutral'
                    publicKeyToken = 'none'
                    mvid = '00000000-0000-0000-0000-000000000000'
                    size = 1
                    sha256 = ('0' * 64)
                    ownerFamily = 'PluginOwned'
                    declaredPluginOwned = $true
                    byteMatchVerifiedInput = $true
                    forbiddenForPackageLayer = $false
                })
            $package.totalAssemblyCount = 1
            $package.forbiddenCount = 0
            $package.forbiddenCountByOwnerFamily = @()
            $package.unknownAssemblyCount = 0
            $package.forbiddenFileCount = 0
            $package.unclassifiedFileCount = 0
        }
        'summary-four-layer-count-forged' = {
            param($ledger)
            $ledger.summary.evaluatedProjectReferenceForbiddenCount++
        }
        'summary-four-layer-field-required' = {
            param($ledger)
            $ledger.summary.PSObject.Properties.Remove('peForbiddenAssemblyReferenceCount')
        }
        'clean-observation-forged' = {
            param($ledger)
            $ledger.sourceState.cleanObserved = -not [bool]$ledger.sourceState.cleanObserved
        }
        'carry-count-growth' = {
            param($ledger)
            $ledger.carrySets.'EDGE-SPLIT-020'.currentItems[0].count++
        }
        'carry-lifecycle-forged' = {
            param($ledger)
            $ledger.carrySets.'EDGE-SPLIT-020'.lifecycleStatus = 'closed'
        }
        'unknown-owner-family' = {
            param($ledger)
            $ledger.externalSymbolUsages[0].ownerFamily = 'Unknown'
        }
        'credentialed-historical-url' = {
            param($ledger)
            $ledger.publishedComposition.plugin.artifact.url =
                'https://forbidden:forbidden@example.invalid/artifact.zip'
        }
        'unverified-old-host' = {
            param($ledger)
            $ledger.publishedComposition.host.artifact.verified = $false
        }
        'blank-symbol-path' = {
            param($ledger)
            $ledger.externalSymbolUsages[0].sourcePath = ' '
        }
    }
    $expectedCodes = [ordered]@{
        'decisions-required-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'decisions-unknown-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'test-inventory-required-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'test-inventory-unknown-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'plugin-manifest-required-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'plugin-manifest-unknown-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'published-composition-required-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'published-composition-unknown-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'msbuild-compilation-required-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'msbuild-compilation-unknown-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'deterministic-target-role-deleted' = 'EDGE-SPLIT-LEDGER-FAST-FACT-MSBUILDCORE'
        'deterministic-target-role-illegal' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'view-inventory-required-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'view-inventory-unknown-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'page-inventory-required-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'page-inventory-unknown-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'resource-inventory-required-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'resource-inventory-unknown-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'reference-assembly-required-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'reference-assembly-unknown-field' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'analyzed-input-digest-forged' = 'EDGE-SPLIT-LEDGER-FAST-ANALYZED-INPUTS'
        'plugin-source-digest-forged' = 'EDGE-SPLIT-LEDGER-FAST-PLUGIN-SOURCES'
        'view-fact-forged' = 'EDGE-SPLIT-LEDGER-FAST-VIEWS'
        'view-set-item-deleted' = 'EDGE-SPLIT-LEDGER-FAST-SUMMARY'
        'view-set-item-injected' = 'EDGE-SPLIT-LEDGER-FAST-SUMMARY'
        'resource-fact-forged' = 'EDGE-SPLIT-LEDGER-FAST-RESOURCES'
        'msbuild-source-count-forged' = 'EDGE-SPLIT-LEDGER-FAST-MSBUILD-COUNTS'
        'project-items-required' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'project-layer-unknown-property' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'project-total-count-forged' = 'EDGE-SPLIT-LEDGER-FAST-PROJECT-COUNTS'
        'roslyn-inputs-required' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'roslyn-forbidden-count-forged' = 'EDGE-SPLIT-LEDGER-FAST-ROSLYN-COUNTS'
        'pe-inputs-required' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'pe-total-count-forged' = 'EDGE-SPLIT-LEDGER-FAST-PE-COUNTS'
        'package-na-cannot-masquerade-as-evaluated' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'package-entry-count-forged' = 'EDGE-SPLIT-LEDGER-FAST-PACKAGE-COUNTS'
        'summary-four-layer-count-forged' = 'EDGE-SPLIT-LEDGER-FAST-SUMMARY'
        'summary-four-layer-field-required' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'clean-observation-forged' = 'EDGE-SPLIT-LEDGER-FAST-SOURCE-CLEAN'
        'carry-count-growth' = 'EDGE-SPLIT-LEDGER-FAST-CARRY-COUNT'
        'carry-lifecycle-forged' = 'EDGE-SPLIT-LEDGER-FAST-CARRY-LIFECYCLE'
        'unknown-owner-family' = 'EDGE-SPLIT-LEDGER-FAST-USAGE-OWNER'
        'credentialed-historical-url' = 'EDGE-SPLIT-LEDGER-FAST-PUBLISHED-URI'
        'unverified-old-host' = 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
        'blank-symbol-path' = 'EDGE-SPLIT-LEDGER-FAST-PATH'
    }
    if ($mutants.Count -ne 45 -or $expectedCodes.Count -ne $mutants.Count -or
        @($mutants.Keys | Where-Object { -not $expectedCodes.Contains($_) }).Count -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-001 behavior mutant/error-code inventory must remain exact at 45.'
    }

    Assert-CommitPairFixtureRejected -Name 'commit-distance-zero-dirty-ledger' -Arrange {
        param($fixture)
        # The canonical ledger remains uncommitted at the implementation HEAD.
    }
    Assert-CommitPairFixtureRejected -Name 'distance-one-nonledger-and-dirty-tree' -Arrange {
        param($fixture)
        [IO.File]::WriteAllText((Join-Path $fixture.root 'unexpected.txt'), "unexpected`n", [Text.UTF8Encoding]::new($false))
        [void](Invoke-FixtureGit $fixture.root @('add', 'eng/baselines/edge-plugin-contract-ledger.json', 'unexpected.txt'))
        [void](Invoke-FixtureGit $fixture.root @('commit', '-m', 'invalid evidence'))
        [IO.File]::AppendAllText($fixture.canonicalPath, "`n", [Text.UTF8Encoding]::new($false))
    }
    Assert-ReplayGateFixtureRejected -Name 'replay-canonical-output-forbidden' -ExpectedMessage 'noncanonical output path' -Arrange {
        param($fixture)
        Add-ReplayEvidenceCommit $fixture
        $fixture.outputPath = 'eng/baselines/edge-plugin-contract-ledger.json'
    }
    Assert-ReplayGateFixtureRejected -Name 'replay-distance-zero-forbidden' -ExpectedMessage 'exact ledger-only evidence commit' -Arrange {
        param($fixture)
    }
    Assert-ReplayGateFixtureRejected -Name 'replay-arbitrary-ancestor-distance-forbidden' -ExpectedMessage 'exact ledger-only evidence commit' -Arrange {
        param($fixture)
        Add-ReplayEvidenceCommit $fixture
        [void](Invoke-FixtureGit $fixture.root @('commit', '--allow-empty', '-m', 'extra commit'))
    }
    Assert-ReplayGateFixtureRejected -Name 'replay-nonledger-diff-forbidden' -ExpectedMessage 'exact ledger-only evidence commit' -Arrange {
        param($fixture)
        Add-ReplayEvidenceCommit $fixture -IncludeUnexpectedPath
    }
    Assert-ReplayGateFixtureRejected -Name 'replay-dirty-final-tree-forbidden' -ExpectedMessage 'completely clean final worktree' -Arrange {
        param($fixture)
        Add-ReplayEvidenceCommit $fixture
        [IO.File]::AppendAllText((Join-Path $fixture.root 'implementation.txt'), "dirty`n", [Text.UTF8Encoding]::new($false))
    }
    Assert-ReplayGateFixtureRejected -Name 'replay-head-tree-mismatch-forbidden' -ExpectedMessage 'HEAD/tree mismatch' -Arrange {
        param($fixture)
        Add-ReplayEvidenceCommit $fixture
        $fixture.treeArgument = ('0' * 40)
    }
    foreach ($entry in $mutants.GetEnumerator()) {
        Assert-MutantRejected -Name $entry.Key -ExpectedCode ([string]$expectedCodes[$entry.Key]) -Mutate $entry.Value
    }
    $fixtureCount = $mutants.Count + 8
    Write-Host "Edge plugin contract ledger behavior fixtures passed: $fixtureCount/$fixtureCount; authorityLaunches=0; replayLaunches=0."
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot -PathType Container) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
