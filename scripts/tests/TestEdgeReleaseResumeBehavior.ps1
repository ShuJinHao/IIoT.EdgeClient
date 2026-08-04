param(
    [string]$PluginRepositoryRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'EdgeLoopbackFakeCloud.TestSupport.ps1')

$scriptsRoot = Split-Path -Parent $PSScriptRoot
$sourceRepoRoot = Split-Path -Parent $scriptsRoot
$resolvedPluginSourceRepoRoot = if ([string]::IsNullOrWhiteSpace($PluginRepositoryRoot)) {
    ''
} else {
    [System.IO.Path]::GetFullPath($PluginRepositoryRoot)
}
$testTempRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    [System.IO.Path]::GetTempPath()
} else {
    [System.IO.Path]::GetFullPath($env:RUNNER_TEMP)
}
$testRoot = Join-Path $testTempRoot ("er-{0}" -f ([Guid]::NewGuid().ToString('N')))
$cloneRoot = Join-Path $testRoot 'r'
$remoteRoot = Join-Path $testRoot 'g.git'
$pluginCloneRoot = Join-Path $testRoot 'p'
$pluginRemoteRoot = Join-Path $testRoot 'pg.git'
$fakeCloudServer = $null
$oldEnvironment = @{
    Dispatch = $env:IIOT_EDGE_WORKSPACE_DISPATCH
    Target = $env:IIOT_EDGE_WORKSPACE_TARGET
    Invocation = $env:IIOT_EDGE_WORKSPACE_INVOCATION_ID
    DependencyReference = $env:IIOT_EDGE_DEPENDENCY_REFERENCE_PACKAGE
}
$passed = 0

function Invoke-GitChecked {
    param([Parameter(Mandatory = $true)][string]$Directory, [Parameter(Mandatory = $true)][string[]]$Arguments)
    & git -C $Directory @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' ')" }
}

function Set-Dispatch {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('EdgeHost', 'EdgePlugin')]
        [string]$Target,
        [string]$InvocationId = ''
    )
    $env:IIOT_EDGE_WORKSPACE_DISPATCH = '1'
    $env:IIOT_EDGE_WORKSPACE_TARGET = $Target
    $env:IIOT_EDGE_WORKSPACE_INVOCATION_ID = if ([string]::IsNullOrWhiteSpace($InvocationId)) {
        [Guid]::NewGuid().ToString('D')
    }
    else {
        $InvocationId
    }
}

function Assert-ThrowsContaining {
    param([Parameter(Mandatory = $true)][scriptblock]$Action, [Parameter(Mandatory = $true)][string[]]$Needles)
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw "Expected failure containing: $($Needles -join ', ')" }
    foreach ($needle in $Needles) {
        if (-not $caught.Exception.Message.Contains($needle, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Failure did not contain '$needle'. Actual: $($caught.Exception.Message)"
        }
    }
    $script:passed++
}

function Assert-State {
    param([Parameter(Mandatory = $true)][string]$ReleaseRoot, [Parameter(Mandatory = $true)][string]$Status)
    $statePath = Join-Path $ReleaseRoot 'edge-deployment-attempt.json'
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) { throw "Attempt state missing: $statePath" }
    $state = Get-Content -Raw -Encoding UTF8 -LiteralPath $statePath | ConvertFrom-Json
    if ([string]$state.status -ne $Status) { throw "Expected attempt status '$Status', got '$($state.status)'." }
    $script:passed++
}

function Get-RequestCount {
    param([Parameter(Mandatory = $true)][string]$RequestLog)
    if (-not (Test-Path -LiteralPath $RequestLog -PathType Leaf)) { return 0 }
    return @(Get-Content -LiteralPath $RequestLog | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
}

function Assert-GitClean {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $dirty = @(& git -C $RepositoryRoot status --porcelain=v1)
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect $Description Git state." }
    if ($dirty.Count -gt 0) {
        throw "$Description fixture became dirty: $($dirty -join '; ')"
    }
    $script:passed++
}

function New-DependencyClosureFixtureArchive {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$LayoutRoot
    )

    $stagingRoot = Join-Path $testRoot ("dependency-{0}" -f ([Guid]::NewGuid().ToString('N')))
    try {
        $targetName = '.NETCoreApp,Version=v10.0/win-x64'
        foreach ($applicationName in @('IIoT.Edge.Launcher', 'IIoT.Edge.Shell')) {
            $applicationRoot = Join-Path $stagingRoot "$LayoutRoot/$applicationName"
            New-Item -ItemType Directory -Force -Path $applicationRoot | Out-Null
            $runtimeAssets = [ordered]@{
                'Velopack.dll' = @{}
                'IIoT.Edge.UI.Shared.dll' = @{}
                'IIoT.Edge.Module.Contracts.dll' = @{}
            }
            $target = [ordered]@{
                'ReleaseResumeFixture/1.0.0' = [ordered]@{
                    runtime = $runtimeAssets
                }
            }
            $targets = [ordered]@{}
            $targets[$targetName] = $target
            [ordered]@{
                runtimeTarget = [ordered]@{ name = $targetName }
                targets = $targets
            } | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8NoBOM -LiteralPath (
                Join-Path $applicationRoot "$applicationName.deps.json")

            foreach ($assemblyName in $runtimeAssets.Keys) {
                [IO.File]::WriteAllBytes(
                    (Join-Path $applicationRoot $assemblyName),
                    [byte[]](0x42, 0x30, 0x34))
            }
        }

        $archiveDirectory = Split-Path -Parent $ArchivePath
        New-Item -ItemType Directory -Force -Path $archiveDirectory | Out-Null
        if (Test-Path -LiteralPath $ArchivePath) {
            Remove-Item -LiteralPath $ArchivePath -Force
        }
        [IO.Compression.ZipFile]::CreateFromDirectory(
            $stagingRoot,
            $ArchivePath,
            [IO.Compression.CompressionLevel]::Fastest,
            $false)
    }
    finally {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

try {
    if (-not [string]::IsNullOrWhiteSpace($resolvedPluginSourceRepoRoot) -and
        -not (Test-Path -LiteralPath $resolvedPluginSourceRepoRoot -PathType Container)) {
        throw "Independent plugin repository was not found: $resolvedPluginSourceRepoRoot"
    }

    New-Item -ItemType Directory -Path $testRoot, $cloneRoot, $pluginCloneRoot | Out-Null
    & git init -q $cloneRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize isolated Edge test clone.' }
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('config', 'core.longpaths', 'true')
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('config', 'user.name', 'edge-release-contract-test')
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('config', 'user.email', 'edge-release-contract-test@example.invalid')
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('remote', 'add', 'source', $sourceRepoRoot)
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('fetch', '-q', 'source', 'HEAD')
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('checkout', '-q', '-B', 'main', 'FETCH_HEAD')
    foreach ($currentDeploymentScript in @(
        'EdgeDeployment.Common.ps1',
        'EdgeReleaseCredential.Common.ps1',
        'LocalPublishAndDeploy.ps1',
        'PublishEdgeClientInstallerArtifact.ps1',
        'PublishEdgePluginRelease.ps1',
        'SaveEdgeReleaseToken.ps1',
        'TestEdgeDependencyClosure.ps1',
        'TestEdgeDeploymentPreflight.ps1'
    )) {
        Copy-Item `
            -LiteralPath (Join-Path $scriptsRoot $currentDeploymentScript) `
            -Destination (Join-Path $cloneRoot "scripts/$currentDeploymentScript") `
            -Force
    }
    Copy-Item `
        -LiteralPath (Join-Path $scriptsRoot 'edge-dependency-removal-approvals.json') `
        -Destination (Join-Path $cloneRoot 'scripts/edge-dependency-removal-approvals.json') `
        -Force
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('add', 'scripts')
    Invoke-GitChecked -Directory $cloneRoot -Arguments @(
        'commit', '-q', '--allow-empty', '-m', 'test current deployment scripts')
    & git init --bare -q $remoteRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize isolated Edge test remote.' }
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('remote', 'add', 'origin', $remoteRoot)
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('push', '-q', '-u', 'origin', 'main')
    $head = (& git -C $cloneRoot rev-parse HEAD).Trim()

    & git init -q $pluginCloneRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize isolated Edge plugin test clone.' }
    Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @('config', 'core.longpaths', 'true')
    Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @('config', 'user.name', 'edge-release-contract-test')
    Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @('config', 'user.email', 'edge-release-contract-test@example.invalid')
    if ([string]::IsNullOrWhiteSpace($resolvedPluginSourceRepoRoot)) {
        Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @('checkout', '-q', '-B', 'main')
        $fixtureManifestRoot = Join-Path $pluginCloneRoot 'src/IIoT.Edge.Module.CP'
        New-Item -ItemType Directory -Force -Path (
            $fixtureManifestRoot,
            (Join-Path $pluginCloneRoot 'eng')) | Out-Null
        @'
{
  "moduleId": "CP",
  "displayName": "正极模切",
  "description": "Release resume behavior fixture",
  "iconKind": "ContentCut",
  "accentColor": "#2563EB",
  "version": "9.8.7",
  "hostApiVersion": "2.0.0",
  "minHostVersion": "2.0.0",
  "maxHostVersion": "2.0.0",
  "entryAssembly": "IIoT.Edge.Module.CP.dll",
  "entryType": "IIoT.Edge.Module.CP.DependencyInjection",
  "supportedProcessType": "CP",
  "dependencies": [],
  "ownedAssemblies": []
}
'@ | Set-Content -Encoding utf8NoBOM -LiteralPath (
            Join-Path $fixtureManifestRoot 'plugin.json')
        'artifacts/' | Set-Content -Encoding utf8NoBOM -LiteralPath (
            Join-Path $pluginCloneRoot '.gitignore')
        Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @(
            'add', '.gitignore', 'src/IIoT.Edge.Module.CP/plugin.json')
        Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @(
            'commit', '-q', '-m', 'seed isolated plugin release fixture')
    }
    else {
        Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @(
            'remote', 'add', 'source', $resolvedPluginSourceRepoRoot)
        Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @('fetch', '-q', 'source', 'HEAD')
        Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @(
            'checkout', '-q', '-B', 'main', 'FETCH_HEAD')
    }
    $pluginManifestPath = Join-Path $pluginCloneRoot 'src/IIoT.Edge.Module.CP/plugin.json'
    $pluginManifestJson = Get-Content -Raw -Encoding UTF8 -LiteralPath $pluginManifestPath
    $pluginManifest = $pluginManifestJson | ConvertFrom-Json
    $pluginVersion = [string]$pluginManifest.version
    if ([string]::IsNullOrWhiteSpace($pluginVersion)) { throw 'CP plugin manifest version is required.' }
    $pluginPackageMemory = [IO.MemoryStream]::new()
    $pluginPackageArchive = [IO.Compression.ZipArchive]::new(
        $pluginPackageMemory,
        [IO.Compression.ZipArchiveMode]::Create,
        $true)
    try {
        $manifestEntry = $pluginPackageArchive.CreateEntry('plugin.json')
        $manifestEntry.LastWriteTime = [DateTimeOffset]::Parse('2020-01-01T00:00:00Z')
        $manifestWriter = [IO.StreamWriter]::new(
            $manifestEntry.Open(),
            [Text.UTF8Encoding]::new($false))
        try {
            $manifestWriter.Write($pluginManifestJson)
        }
        finally {
            $manifestWriter.Dispose()
        }
        $assemblyEntry = $pluginPackageArchive.CreateEntry([string]$pluginManifest.entryAssembly)
        $assemblyEntry.LastWriteTime = [DateTimeOffset]::Parse('2020-01-01T00:00:00Z')
        $assemblyStream = $assemblyEntry.Open()
        try {
            $assemblyBytes = [Text.Encoding]::ASCII.GetBytes('release-contract-fixture')
            $assemblyStream.Write($assemblyBytes, 0, $assemblyBytes.Length)
        }
        finally {
            $assemblyStream.Dispose()
        }
    }
    finally {
        $pluginPackageArchive.Dispose()
    }
    $pluginPackageBytes = $pluginPackageMemory.ToArray()
    $pluginPackageMemory.Dispose()
    $pluginPackageSize = [int64]$pluginPackageBytes.Length
    $pluginPackageHash = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($pluginPackageBytes))
    [Convert]::ToBase64String($pluginPackageBytes) |
        Set-Content -Encoding ascii -NoNewline -LiteralPath (
            Join-Path $pluginCloneRoot 'eng/release-contract-package.b64')
    @'
param(
    [Parameter(Mandatory = $true)][string]$ModuleId,
    [string]$Configuration = 'Release',
    [string]$TargetRuntime = 'win-x64',
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [Parameter(Mandatory = $true)][string]$SourceCommit,
    [switch]$CleanOutput
)
$ErrorActionPreference = 'Stop'
if ($CleanOutput -and (Test-Path -LiteralPath $OutputRoot)) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$repoRoot = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "src/IIoT.Edge.Module.$ModuleId/plugin.json") | ConvertFrom-Json
$packageFileName = "IIoT.EdgePlugin.$ModuleId-$($manifest.version)-$TargetRuntime.zip"
$packagePath = Join-Path $OutputRoot $packageFileName
$packageBytes = [Convert]::FromBase64String((
    Get-Content -Raw -Encoding ASCII -LiteralPath (
        Join-Path $PSScriptRoot 'release-contract-package.b64')).Trim())
[IO.File]::WriteAllBytes($packagePath, $packageBytes)
$package = Get-Item -LiteralPath $packagePath
$packageHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $packagePath).Hash
@{
    packageSchemaVersion = 2
    moduleId = [string]$manifest.moduleId
    processType = $ModuleId
    displayName = $ModuleId
    version = [string]$manifest.version
    hostApiVersion = '2.0.0'
    minHostVersion = '2.0.0'
    maxHostVersion = '2.0.0'
    dependencies = @()
    targetRuntime = $TargetRuntime
    targetFramework = 'net10.0'
    packageFileName = $packageFileName
    packageSize = [int64]$package.Length
    sha256 = $packageHash
    signature = ''
    publisher = 'IIoT'
    sourceCommit = $SourceCommit
} | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8NoBOM -LiteralPath "$packagePath.json"
'@ | Set-Content -Encoding utf8NoBOM -LiteralPath (
        Join-Path $pluginCloneRoot 'eng/PackEdgePlugin.ps1')
    Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @(
        'add', 'eng/PackEdgePlugin.ps1', 'eng/release-contract-package.b64')
    Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @(
        'commit', '-q', '--allow-empty', '-m', 'add deterministic release contract pack fixture')
    & git init --bare -q $pluginRemoteRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize isolated Edge plugin test remote.' }
    Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @('remote', 'add', 'origin', $pluginRemoteRoot)
    Invoke-GitChecked -Directory $pluginCloneRoot -Arguments @('push', '-q', '-u', 'origin', 'main')
    $pluginHead = (& git -C $pluginCloneRoot rev-parse HEAD).Trim()

    $portFile = Join-Path $testRoot 'port.txt'
    $requestLog = Join-Path $testRoot 'requests.jsonl'
    $fakeCloudServer = Start-EdgeLoopbackFakeCloud `
        -PortFile $portFile `
        -RequestLog $requestLog `
        -PluginVersion $pluginVersion `
        -PluginSha256 $pluginPackageHash `
        -PluginPackageSize $pluginPackageSize `
        -HostSourceCommit $head
    $baseUrl = $fakeCloudServer.BaseUrl

    $hostReleaseRoot = Join-Path $testRoot 'host-release'
    $hostArtifactRoot = Join-Path $hostReleaseRoot 'edge-installer-artifacts/stable/9.9.9'
    $hostVelopackRoot = Join-Path $hostReleaseRoot 'edge-velopack'
    New-Item -ItemType Directory -Force -Path $hostArtifactRoot, $hostVelopackRoot | Out-Null
    @{
        schemaVersion = 2
        installerBindingSchemaVersion = 2
        channel = 'stable'
        version = '9.9.9'
        sourceCommit = $head
        releaseNotes = 'fake host release notes'
    } | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $hostArtifactRoot 'installer-artifact.json')
    '{}' | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $hostVelopackRoot 'releases.stable.json')
    $hostBundlePath = Join-Path $hostReleaseRoot 'edge-release-bundle-stable-9.9.9.zip'
    New-DependencyClosureFixtureArchive -ArchivePath $hostBundlePath -LayoutRoot 'installer'
    $dependencyReferencePath = Join-Path $testRoot 'edge-dependency-reference.nupkg'
    New-DependencyClosureFixtureArchive -ArchivePath $dependencyReferencePath -LayoutRoot 'lib/app'
    $env:IIOT_EDGE_DEPENDENCY_REFERENCE_PACKAGE = $dependencyReferencePath
    $hostInvocation = [Guid]::NewGuid().ToString('D')
    @{
        schemaVersion = 1
        target = 'EdgeHost'
        invocationId = $hostInvocation
        stage = 'uploading'
        status = 'failed'
        facts = @{ version = '9.9.9'; sourceCommit = $head }
    } | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 -LiteralPath (
        Join-Path $hostReleaseRoot 'edge-deployment-attempt.json')

    $pluginReleaseRoot = Join-Path $testRoot 'plugin-release'
    $pluginPackageRoot = Join-Path $pluginReleaseRoot 'package'
    New-Item -ItemType Directory -Force -Path $pluginPackageRoot | Out-Null
    $pluginFileName = "IIoT.EdgePlugin.CP-$pluginVersion-win-x64.zip"
    [IO.File]::WriteAllBytes(
        (Join-Path $pluginPackageRoot $pluginFileName),
        $pluginPackageBytes)
    @{
        packageSchemaVersion = 2
        moduleId = 'CP'
        processType = 'CP'
        displayName = '正极模切'
        version = $pluginVersion
        hostApiVersion = '2.0.0'
        minHostVersion = '2.0.0'
        maxHostVersion = '2.0.0'
        dependencies = @()
        targetRuntime = 'win-x64'
        targetFramework = 'net10.0'
        packageFileName = $pluginFileName
        packageSize = $pluginPackageSize
        sha256 = $pluginPackageHash
        signature = ''
        publisher = 'IIoT'
        sourceCommit = $pluginHead
    } | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $pluginPackageRoot "$pluginFileName.json")
    'preserved plugin wrapper' | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $pluginReleaseRoot "edge-plugin-release-CP-$pluginVersion-win-x64.zip")
    $pluginInvocation = [Guid]::NewGuid().ToString('D')
    @{
        schemaVersion = 1
        target = 'EdgePlugin'
        invocationId = $pluginInvocation
        stage = 'uploading'
        status = 'failed'
        facts = @{ moduleId = 'CP'; version = $pluginVersion; sourceCommit = $pluginHead }
    } | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $pluginReleaseRoot 'edge-deployment-attempt.json')

    Set-Dispatch -Target EdgeHost -InvocationId $hostInvocation
    & (Join-Path $cloneRoot 'scripts/LocalPublishAndDeploy.ps1') `
        -Version '9.9.9' -CloudApiBaseUrl "$baseUrl/api/v1" -ReleaseNotes 'fake host release notes' `
        -CloudToken 'fake-token' -ExpectedSha $head `
        -ResumeReleaseRoot $hostReleaseRoot -SkipVelopackValidation -SkipInstallerValidation
    Assert-State -ReleaseRoot $hostReleaseRoot -Status succeeded

    Set-Dispatch -Target EdgePlugin -InvocationId $pluginInvocation
    & (Join-Path $cloneRoot 'scripts/PublishEdgePluginRelease.ps1') `
        -ModuleId CP -PluginRepositoryRoot $pluginCloneRoot `
        -CloudApiBaseUrl "$baseUrl/api/v1" -ReleaseNotes 'fake plugin release notes' `
        -CloudToken 'fake-token' -ExpectedSha $pluginHead `
        -ResumeReleaseRoot $pluginReleaseRoot -SkipPackageValidation
    Assert-State -ReleaseRoot $pluginReleaseRoot -Status succeeded
    $initialPostCount = Get-RequestCount -RequestLog $requestLog
    if ($initialPostCount -ne 2) { throw "Expected two fake Cloud uploads, got $initialPostCount." }
    $passed++

    Set-Dispatch -Target EdgeHost -InvocationId $hostInvocation
    & (Join-Path $cloneRoot 'scripts/LocalPublishAndDeploy.ps1') `
        -Version '9.9.9' -CloudApiBaseUrl "$baseUrl/existing/api/v1" -ReleaseNotes 'fake host release notes' `
        -CloudToken 'fake-token' -ExpectedSha $head `
        -ResumeReleaseRoot $hostReleaseRoot -SkipVelopackValidation -SkipInstallerValidation
    Set-Dispatch -Target EdgePlugin -InvocationId $pluginInvocation
    & (Join-Path $cloneRoot 'scripts/PublishEdgePluginRelease.ps1') `
        -ModuleId CP -PluginRepositoryRoot $pluginCloneRoot `
        -CloudApiBaseUrl "$baseUrl/existing/api/v1" -ReleaseNotes 'fake plugin release notes' `
        -CloudToken 'fake-token' -ExpectedSha $pluginHead `
        -ResumeReleaseRoot $pluginReleaseRoot -SkipPackageValidation
    if ((Get-RequestCount -RequestLog $requestLog) -ne $initialPostCount) { throw 'Existing release reconciliation unexpectedly re-uploaded artifacts.' }
    $passed++

    $reconcileOutputRoot = Join-Path $testRoot 'from-zero-plugin-reconcile'
    Set-Dispatch -Target EdgePlugin
    $reconcileInvocation = $env:IIOT_EDGE_WORKSPACE_INVOCATION_ID
    & (Join-Path $cloneRoot 'scripts/PublishEdgePluginRelease.ps1') `
        -ModuleId CP -PluginRepositoryRoot $pluginCloneRoot `
        -CloudApiBaseUrl "$baseUrl/existing/api/v1" -ReleaseNotes 'from-zero reconciliation' `
        -CloudToken 'fake-token' -ExpectedSha $pluginHead `
        -OutputRoot $reconcileOutputRoot -ReconcileExistingRelease -SkipPackageValidation
    $reconcileReleaseRoot = Join-Path $reconcileOutputRoot "stable/CP/reconcile-$reconcileInvocation"
    Assert-State -ReleaseRoot $reconcileReleaseRoot -Status succeeded
    $reconcileState = Get-Content -Raw -Encoding UTF8 -LiteralPath (
        Join-Path $reconcileReleaseRoot 'edge-deployment-attempt.json') | ConvertFrom-Json
    if (-not [bool]$reconcileState.facts.rebuiltAndCompared -or
        -not [bool]$reconcileState.facts.alreadyPublished) {
        throw 'From-zero plugin reconciliation did not record rebuiltAndCompared/alreadyPublished evidence.'
    }
    if ((Get-RequestCount -RequestLog $requestLog) -ne $initialPostCount) {
        throw 'From-zero plugin reconciliation unexpectedly uploaded or replaced restored release bytes.'
    }
    $passed++

    Assert-GitClean -RepositoryRoot $cloneRoot -Description 'Edge release'
    Assert-GitClean -RepositoryRoot $pluginCloneRoot -Description 'Plugin release'

    Set-Dispatch -Target EdgeHost
    Assert-ThrowsContaining -Action {
        & (Join-Path $cloneRoot 'scripts/LocalPublishAndDeploy.ps1') `
            -Version '9.9.9' -CloudApiBaseUrl "$baseUrl/existing/api/v1" -ReleaseNotes 'new attempt' `
            -CloudToken 'fake-token' -ExpectedSha $head
    } -Needles @('already exists', 'No build was started')
    if (Test-Path -LiteralPath (Join-Path $cloneRoot 'publish/local-edge-release/stable/9.9.9/edge-runtime')) {
        throw 'Duplicate Host version unexpectedly started a runtime build.'
    }
    $passed++

    Set-Dispatch -Target EdgePlugin
    Assert-ThrowsContaining -Action {
        & (Join-Path $cloneRoot 'scripts/PublishEdgePluginRelease.ps1') `
            -ModuleId CP -PluginRepositoryRoot $pluginCloneRoot `
            -CloudApiBaseUrl "$baseUrl/existing/api/v1" -ReleaseNotes 'new attempt' `
            -CloudToken 'fake-token' -ExpectedSha $pluginHead
    } -Needles @('already exists', 'No package build was started')

    Set-Dispatch -Target EdgeHost -InvocationId $hostInvocation
    Assert-ThrowsContaining -Action {
        & (Join-Path $cloneRoot 'scripts/LocalPublishAndDeploy.ps1') `
            -Version '9.9.9' -CloudApiBaseUrl "$baseUrl/error/api/v1" -ReleaseNotes 'fake host release notes' `
            -CloudToken 'fake-token' -ExpectedSha $head `
            -ResumeReleaseRoot $hostReleaseRoot -SkipVelopackValidation -SkipInstallerValidation
    } -Needles @('httpStatus=413', 'bundle_too_large', 'Artifacts were preserved')
    Assert-State -ReleaseRoot $hostReleaseRoot -Status failed

    Set-Dispatch -Target EdgeHost -InvocationId $hostInvocation
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    Assert-ThrowsContaining -Action {
        & (Join-Path $cloneRoot 'scripts/LocalPublishAndDeploy.ps1') `
            -Version '9.9.9' -CloudApiBaseUrl "$baseUrl/slow-upload/api/v1" -ReleaseNotes 'fake host release notes' `
            -CloudToken 'fake-token' -ExpectedSha $head `
            -ResumeReleaseRoot $hostReleaseRoot -SkipVelopackValidation -SkipInstallerValidation `
            -UploadTimeoutSeconds 1 -LowSpeedTimeSeconds 1 -LowSpeedLimitBytesPerSecond 1
    } -Needles @('curlExit=28', 'Artifacts were preserved')
    $timer.Stop()
    if ($timer.Elapsed.TotalSeconds -ge 4) { throw "Full Host upload timeout was not fail-fast: $($timer.Elapsed.TotalSeconds)s" }
    Assert-State -ReleaseRoot $hostReleaseRoot -Status failed

    Set-Dispatch -Target EdgePlugin -InvocationId $pluginInvocation
    Assert-ThrowsContaining -Action {
        & (Join-Path $cloneRoot 'scripts/PublishEdgePluginRelease.ps1') `
            -ModuleId CP -PluginRepositoryRoot $pluginCloneRoot `
            -CloudApiBaseUrl "$baseUrl/catalog-error/api/v1" -ReleaseNotes 'fake plugin release notes' `
            -CloudToken 'fake-token' -ExpectedSha $pluginHead `
            -ResumeReleaseRoot $pluginReleaseRoot -SkipPackageValidation
    } -Needles @('httpStatus=500', 'catalog_unavailable', 'injected failure')

    Write-Host "Edge full release resume behavior tests passed: $passed"
    $global:LASTEXITCODE = 0
}
finally {
    Stop-EdgeLoopbackFakeCloud -Server $fakeCloudServer
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    if ($null -eq $oldEnvironment.Dispatch) { Remove-Item Env:IIOT_EDGE_WORKSPACE_DISPATCH -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_WORKSPACE_DISPATCH = $oldEnvironment.Dispatch }
    if ($null -eq $oldEnvironment.Target) { Remove-Item Env:IIOT_EDGE_WORKSPACE_TARGET -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_WORKSPACE_TARGET = $oldEnvironment.Target }
    if ($null -eq $oldEnvironment.Invocation) { Remove-Item Env:IIOT_EDGE_WORKSPACE_INVOCATION_ID -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_WORKSPACE_INVOCATION_ID = $oldEnvironment.Invocation }
    if ($null -eq $oldEnvironment.DependencyReference) { Remove-Item Env:IIOT_EDGE_DEPENDENCY_REFERENCE_PACKAGE -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_DEPENDENCY_REFERENCE_PACKAGE = $oldEnvironment.DependencyReference }
}
