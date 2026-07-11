param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptsRoot = Split-Path -Parent $PSScriptRoot
$sourceRepoRoot = Split-Path -Parent $scriptsRoot
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("edge-resume-test-{0}" -f ([Guid]::NewGuid().ToString('N')))
$cloneRoot = Join-Path $testRoot 'repo'
$remoteRoot = Join-Path $testRoot 'remote.git'
$fakeCloudProcess = $null
$oldEnvironment = @{
    Dispatch = $env:IIOT_EDGE_WORKSPACE_DISPATCH
    Target = $env:IIOT_EDGE_WORKSPACE_TARGET
    Invocation = $env:IIOT_EDGE_WORKSPACE_INVOCATION_ID
    Token = $env:IIOT_CLOUD_RELEASE_TOKEN
}
$passed = 0

function Invoke-GitChecked {
    param([Parameter(Mandatory = $true)][string]$Directory, [Parameter(Mandatory = $true)][string[]]$Arguments)
    & git -C $Directory @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git failed: $($Arguments -join ' ')" }
}

function Set-Dispatch {
    param([Parameter(Mandatory = $true)][ValidateSet('EdgeHost', 'EdgePlugin')][string]$Target)
    $env:IIOT_EDGE_WORKSPACE_DISPATCH = '1'
    $env:IIOT_EDGE_WORKSPACE_TARGET = $Target
    $env:IIOT_EDGE_WORKSPACE_INVOCATION_ID = [Guid]::NewGuid().ToString('D')
    $env:IIOT_CLOUD_RELEASE_TOKEN = 'fake-token'
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

try {
    New-Item -ItemType Directory -Path $testRoot, $cloneRoot | Out-Null
    & git init -q $cloneRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize isolated Edge test clone.' }
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('remote', 'add', 'source', $sourceRepoRoot)
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('fetch', '-q', 'source', 'HEAD')
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('checkout', '-q', '-b', 'edge-resume-test', 'FETCH_HEAD')
    & git init --bare -q $remoteRoot
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize isolated Edge test remote.' }
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('remote', 'add', 'origin', $remoteRoot)
    Invoke-GitChecked -Directory $cloneRoot -Arguments @('push', '-q', '-u', 'origin', 'edge-resume-test')
    $head = (& git -C $cloneRoot rev-parse HEAD).Trim()

    $portFile = Join-Path $testRoot 'port.txt'
    $requestLog = Join-Path $testRoot 'requests.jsonl'
    $serverScript = Join-Path $PSScriptRoot 'fake_edge_release_cloud.py'
    $python = Get-Command python3 -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $python) { $python = Get-Command python -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1 }
    if ($null -eq $python) { throw 'Python 3 is required for the loopback fake Cloud resume test.' }
    $fakeCloudProcess = Start-Process -FilePath $python.Source -ArgumentList @($serverScript, '--port-file', $portFile, '--request-log', $requestLog) -PassThru
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $portFile -PathType Leaf)) {
        if ([DateTimeOffset]::UtcNow -ge $deadline) { throw 'Fake Cloud did not start.' }
        Start-Sleep -Milliseconds 50
    }
    $baseUrl = "http://127.0.0.1:$([int](Get-Content -Raw -LiteralPath $portFile))"

    $hostReleaseRoot = Join-Path $testRoot 'host-release'
    $hostArtifactRoot = Join-Path $hostReleaseRoot 'edge-installer-artifacts/stable/9.9.9'
    $hostVelopackRoot = Join-Path $hostReleaseRoot 'edge-velopack'
    New-Item -ItemType Directory -Force -Path $hostArtifactRoot, $hostVelopackRoot | Out-Null
    @{
        schemaVersion = 2
        channel = 'stable'
        version = '9.9.9'
        sourceCommit = $head
        releaseNotes = 'fake host release notes'
    } | ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $hostArtifactRoot 'installer-artifact.json')
    '{}' | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $hostVelopackRoot 'releases.stable.json')
    'preserved host bundle' | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $hostReleaseRoot 'edge-release-bundle-stable-9.9.9.zip')

    $pluginReleaseRoot = Join-Path $testRoot 'plugin-release'
    $pluginPackageRoot = Join-Path $pluginReleaseRoot 'package'
    New-Item -ItemType Directory -Force -Path $pluginPackageRoot | Out-Null
    $pluginFileName = 'IIoT.EdgePlugin.Homogenization-1.0.0-win-x64.zip'
    'preserved plugin package' | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $pluginPackageRoot $pluginFileName)
    @{
        packageSchemaVersion = 1
        moduleId = 'Homogenization'
        processType = 'Homogenization'
        displayName = 'Homogenization'
        version = '1.0.0'
        hostApiVersion = '1.0.0'
        minHostVersion = '1.0.0'
        maxHostVersion = '99.0.0'
        dependencies = @()
        targetRuntime = 'win-x64'
        targetFramework = 'net10.0'
        packageFileName = $pluginFileName
        packageSize = 24
        sha256 = 'FAKE'
        signature = ''
        publisher = 'IIoT'
    } | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $pluginPackageRoot "$pluginFileName.json")
    'preserved plugin wrapper' | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $pluginReleaseRoot 'edge-plugin-release-Homogenization-1.0.0-win-x64.zip')
    @{
        schemaVersion = 1
        target = 'EdgePlugin'
        invocationId = [Guid]::NewGuid().ToString('D')
        stage = 'uploading'
        status = 'failed'
        facts = @{ moduleId = 'Homogenization'; version = '1.0.0'; sourceCommit = $head }
    } | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $pluginReleaseRoot 'edge-deployment-attempt.json')

    Set-Dispatch -Target EdgeHost
    & (Join-Path $cloneRoot 'scripts/LocalPublishAndDeploy.ps1') `
        -Version '9.9.9' -CloudApiBaseUrl "$baseUrl/api/v1" -ReleaseNotes 'fake host release notes' `
        -ResumeReleaseRoot $hostReleaseRoot -SkipVelopackValidation -SkipInstallerValidation
    Assert-State -ReleaseRoot $hostReleaseRoot -Status succeeded

    Set-Dispatch -Target EdgePlugin
    & (Join-Path $cloneRoot 'scripts/PublishEdgePluginRelease.ps1') `
        -ModuleId Homogenization -CloudApiBaseUrl "$baseUrl/api/v1" -ReleaseNotes 'fake plugin release notes' `
        -ResumeReleaseRoot $pluginReleaseRoot -SkipPackageValidation
    Assert-State -ReleaseRoot $pluginReleaseRoot -Status succeeded
    $initialPostCount = Get-RequestCount -RequestLog $requestLog
    if ($initialPostCount -ne 2) { throw "Expected two fake Cloud uploads, got $initialPostCount." }
    $passed++

    Set-Dispatch -Target EdgeHost
    & (Join-Path $cloneRoot 'scripts/LocalPublishAndDeploy.ps1') `
        -Version '9.9.9' -CloudApiBaseUrl "$baseUrl/existing/api/v1" -ReleaseNotes 'fake host release notes' `
        -ResumeReleaseRoot $hostReleaseRoot -SkipVelopackValidation -SkipInstallerValidation
    Set-Dispatch -Target EdgePlugin
    & (Join-Path $cloneRoot 'scripts/PublishEdgePluginRelease.ps1') `
        -ModuleId Homogenization -CloudApiBaseUrl "$baseUrl/existing/api/v1" -ReleaseNotes 'fake plugin release notes' `
        -ResumeReleaseRoot $pluginReleaseRoot -SkipPackageValidation
    if ((Get-RequestCount -RequestLog $requestLog) -ne $initialPostCount) { throw 'Existing release reconciliation unexpectedly re-uploaded artifacts.' }
    $passed++

    Set-Dispatch -Target EdgeHost
    Assert-ThrowsContaining -Action {
        & (Join-Path $cloneRoot 'scripts/LocalPublishAndDeploy.ps1') `
            -Version '9.9.9' -CloudApiBaseUrl "$baseUrl/existing/api/v1" -ReleaseNotes 'new attempt'
    } -Needles @('already exists', 'No build was started')
    if (Test-Path -LiteralPath (Join-Path $cloneRoot 'publish/local-edge-release/stable/9.9.9/edge-runtime')) {
        throw 'Duplicate Host version unexpectedly started a runtime build.'
    }
    $passed++

    Set-Dispatch -Target EdgePlugin
    Assert-ThrowsContaining -Action {
        & (Join-Path $cloneRoot 'scripts/PublishEdgePluginRelease.ps1') `
            -ModuleId Homogenization -CloudApiBaseUrl "$baseUrl/existing/api/v1" -ReleaseNotes 'new attempt'
    } -Needles @('already exists', 'No package build was started')

    Set-Dispatch -Target EdgeHost
    Assert-ThrowsContaining -Action {
        & (Join-Path $cloneRoot 'scripts/LocalPublishAndDeploy.ps1') `
            -Version '9.9.9' -CloudApiBaseUrl "$baseUrl/error/api/v1" -ReleaseNotes 'fake host release notes' `
            -ResumeReleaseRoot $hostReleaseRoot -SkipVelopackValidation -SkipInstallerValidation
    } -Needles @('httpStatus=413', 'bundle_too_large', 'Artifacts were preserved')
    Assert-State -ReleaseRoot $hostReleaseRoot -Status failed

    Set-Dispatch -Target EdgeHost
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    Assert-ThrowsContaining -Action {
        & (Join-Path $cloneRoot 'scripts/LocalPublishAndDeploy.ps1') `
            -Version '9.9.9' -CloudApiBaseUrl "$baseUrl/slow-upload/api/v1" -ReleaseNotes 'fake host release notes' `
            -ResumeReleaseRoot $hostReleaseRoot -SkipVelopackValidation -SkipInstallerValidation `
            -UploadTimeoutSeconds 1 -LowSpeedTimeSeconds 1 -LowSpeedLimitBytesPerSecond 1
    } -Needles @('curlExit=28', 'Artifacts were preserved')
    $timer.Stop()
    if ($timer.Elapsed.TotalSeconds -ge 4) { throw "Full Host upload timeout was not fail-fast: $($timer.Elapsed.TotalSeconds)s" }
    Assert-State -ReleaseRoot $hostReleaseRoot -Status failed

    Set-Dispatch -Target EdgePlugin
    Assert-ThrowsContaining -Action {
        & (Join-Path $cloneRoot 'scripts/PublishEdgePluginRelease.ps1') `
            -ModuleId Homogenization -CloudApiBaseUrl "$baseUrl/catalog-error/api/v1" -ReleaseNotes 'fake plugin release notes' `
            -ResumeReleaseRoot $pluginReleaseRoot -SkipPackageValidation
    } -Needles @('httpStatus=500', 'catalog_unavailable', 'injected failure')

    Write-Host "Edge full release resume behavior tests passed: $passed"
}
finally {
    if ($null -ne $fakeCloudProcess -and -not $fakeCloudProcess.HasExited) {
        Stop-Process -Id $fakeCloudProcess.Id -Force -ErrorAction SilentlyContinue
        $fakeCloudProcess.WaitForExit(5000) | Out-Null
    }
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
    if ($null -eq $oldEnvironment.Dispatch) { Remove-Item Env:IIOT_EDGE_WORKSPACE_DISPATCH -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_WORKSPACE_DISPATCH = $oldEnvironment.Dispatch }
    if ($null -eq $oldEnvironment.Target) { Remove-Item Env:IIOT_EDGE_WORKSPACE_TARGET -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_WORKSPACE_TARGET = $oldEnvironment.Target }
    if ($null -eq $oldEnvironment.Invocation) { Remove-Item Env:IIOT_EDGE_WORKSPACE_INVOCATION_ID -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_WORKSPACE_INVOCATION_ID = $oldEnvironment.Invocation }
    if ($null -eq $oldEnvironment.Token) { Remove-Item Env:IIOT_CLOUD_RELEASE_TOKEN -ErrorAction SilentlyContinue } else { $env:IIOT_CLOUD_RELEASE_TOKEN = $oldEnvironment.Token }
}
