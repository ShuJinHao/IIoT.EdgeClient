param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptsRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptsRoot
. (Join-Path $scriptsRoot 'EdgeDeployment.Common.ps1')

$passed = 0
$temporaryRoots = [System.Collections.Generic.List[string]]::new()
$fakeCloudProcess = $null

function Assert-True {
    param([Parameter(Mandatory = $true)][bool]$Condition, [Parameter(Mandatory = $true)][string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-ThrowsContaining {
    param([Parameter(Mandatory = $true)][scriptblock]$Action, [Parameter(Mandatory = $true)][string[]]$Needles)
    $caught = $null
    try {
        & $Action
    }
    catch {
        $caught = $_
    }
    if ($null -eq $caught) {
        throw "Expected action to fail containing: $($Needles -join ', ')"
    }
    foreach ($needle in $Needles) {
        if (-not $caught.Exception.Message.Contains($needle, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Failure did not contain '$needle'. Actual: $($caught.Exception.Message)"
        }
    }
    $script:passed++
}

function Assert-Passes {
    param([Parameter(Mandatory = $true)][scriptblock]$Action)
    & $Action
    $script:passed++
}

function New-TestDirectory {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("edge-deploy-test-{0}" -f ([Guid]::NewGuid().ToString('N')))
    New-Item -ItemType Directory -Path $path | Out-Null
    $script:temporaryRoots.Add($path) | Out-Null
    return $path
}

function Invoke-Git {
    param([Parameter(Mandatory = $true)][string]$WorkingDirectory, [Parameter(Mandatory = $true)][string[]]$Arguments)
    & git -C $WorkingDirectory @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "git failed: $($Arguments -join ' ')"
    }
}

try {
    $oldDispatch = $env:IIOT_EDGE_WORKSPACE_DISPATCH
    $oldTarget = $env:IIOT_EDGE_WORKSPACE_TARGET
    $oldInvocation = $env:IIOT_EDGE_WORKSPACE_INVOCATION_ID
    Remove-Item Env:IIOT_EDGE_WORKSPACE_DISPATCH -ErrorAction SilentlyContinue
    Remove-Item Env:IIOT_EDGE_WORKSPACE_TARGET -ErrorAction SilentlyContinue
    Remove-Item Env:IIOT_EDGE_WORKSPACE_INVOCATION_ID -ErrorAction SilentlyContinue
    Assert-ThrowsContaining -Action { Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgeHost | Out-Null } -Needles @('workspace root')

    $env:IIOT_EDGE_WORKSPACE_DISPATCH = '1'
    $env:IIOT_EDGE_WORKSPACE_TARGET = 'EdgePlugin'
    $env:IIOT_EDGE_WORKSPACE_INVOCATION_ID = [Guid]::NewGuid().ToString('D')
    Assert-ThrowsContaining -Action { Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgeHost | Out-Null } -Needles @('target mismatch')
    Assert-Passes -Action { Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgePlugin | Out-Null }

    $gitFixtureRoot = New-TestDirectory
    $gitRepo = Join-Path $gitFixtureRoot 'repo'
    $bareRemote = Join-Path $gitFixtureRoot 'remote.git'
    New-Item -ItemType Directory -Path $gitRepo | Out-Null
    Invoke-Git -WorkingDirectory $gitRepo -Arguments @('init', '-q')
    Invoke-Git -WorkingDirectory $gitRepo -Arguments @('config', 'user.name', 'Edge Deploy Test')
    Invoke-Git -WorkingDirectory $gitRepo -Arguments @('config', 'user.email', 'edge-deploy-test@example.invalid')
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $gitRepo 'tracked.txt') -Value 'baseline'
    Invoke-Git -WorkingDirectory $gitRepo -Arguments @('add', 'tracked.txt')
    Invoke-Git -WorkingDirectory $gitRepo -Arguments @('commit', '-q', '-m', 'baseline')
    & git init --bare -q $bareRemote
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize fake bare remote.' }
    Invoke-Git -WorkingDirectory $gitRepo -Arguments @('remote', 'add', 'origin', $bareRemote)
    Invoke-Git -WorkingDirectory $gitRepo -Arguments @('push', '-q', '-u', 'origin', 'HEAD:main')
    Assert-Passes -Action { Assert-EdgeReleaseGitState -RepoRoot $gitRepo | Out-Null }

    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $gitRepo 'dirty.txt') -Value 'dirty'
    Assert-ThrowsContaining -Action { Assert-EdgeReleaseGitState -RepoRoot $gitRepo | Out-Null } -Needles @('clean work tree')
    Remove-Item -LiteralPath (Join-Path $gitRepo 'dirty.txt')
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $gitRepo 'tracked.txt') -Value 'unpushed'
    Invoke-Git -WorkingDirectory $gitRepo -Arguments @('add', 'tracked.txt')
    Invoke-Git -WorkingDirectory $gitRepo -Arguments @('commit', '-q', '-m', 'unpushed')
    Assert-ThrowsContaining -Action { Assert-EdgeReleaseGitState -RepoRoot $gitRepo | Out-Null } -Needles @('pushed')
    Invoke-Git -WorkingDirectory $gitRepo -Arguments @('push', '-q', 'origin', 'HEAD:main')

    $firstInvocation = [Guid]::NewGuid().ToString('D')
    $lock = Enter-EdgeDeploymentLock -RepoRoot $gitRepo -InvocationId $firstInvocation -Target EdgeHost
    Assert-ThrowsContaining -Action {
        Enter-EdgeDeploymentLock -RepoRoot $gitRepo -InvocationId ([Guid]::NewGuid().ToString('D')) -Target EdgePlugin | Out-Null
    } -Needles @('Another Edge release is active', 'EdgeHost')
    Exit-EdgeDeploymentLock -Lock $lock
    $staleLockPath = Get-EdgeDeploymentLockPath -RepoRoot $gitRepo
    New-Item -ItemType Directory -Force -Path $staleLockPath | Out-Null
    @{ invocationId = [Guid]::NewGuid().ToString('D'); target = 'EdgeHost'; pid = 2147483647; startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') } |
        ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $staleLockPath 'owner.json')
    $replacementLock = Enter-EdgeDeploymentLock -RepoRoot $gitRepo -InvocationId ([Guid]::NewGuid().ToString('D')) -Target EdgePlugin
    Assert-True -Condition (Test-Path -LiteralPath $replacementLock.Path) -Message 'Stale Edge release lock was not replaced.'
    Exit-EdgeDeploymentLock -Lock $replacementLock
    $passed++

    $cloudRoot = New-TestDirectory
    $portFile = Join-Path $cloudRoot 'port.txt'
    $requestLog = Join-Path $cloudRoot 'requests.jsonl'
    $serverScript = Join-Path $PSScriptRoot 'fake_edge_release_cloud.py'
    $python = Get-Command python3 -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $python) {
        $python = Get-Command python -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if ($null -eq $python) {
        throw 'Python 3 is required for the loopback fake Cloud behavior test.'
    }
    $fakeCloudProcess = Start-Process -FilePath $python.Source -ArgumentList @($serverScript, '--port-file', $portFile, '--request-log', $requestLog) -PassThru
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    while (-not (Test-Path -LiteralPath $portFile -PathType Leaf)) {
        if ([DateTimeOffset]::UtcNow -ge $deadline) { throw 'Fake Cloud did not start.' }
        Start-Sleep -Milliseconds 50
    }
    $port = [int](Get-Content -Raw -LiteralPath $portFile)
    $baseUrl = "http://127.0.0.1:$port"

    $json = Invoke-EdgeCurlJsonGet -Uri "$baseUrl/ok-json" -Token 'fake-token' -RequestTimeoutSeconds 5
    Assert-True -Condition ($null -ne $json.host) -Message 'Fake Cloud JSON success was not parsed.'
    $passed++
    Assert-ThrowsContaining -Action {
        Invoke-EdgeCurlJsonGet -Uri "$baseUrl/error-json" -Token 'fake-token' -RequestTimeoutSeconds 5 | Out-Null
    } -Needles @('httpStatus=409', 'duplicate', 'already exists')
    $redactionFailure = $null
    try { Invoke-EdgeCurlJsonGet -Uri "$baseUrl/error-json" -Token 'fake-token' -RequestTimeoutSeconds 5 | Out-Null } catch { $redactionFailure = $_ }
    Assert-True -Condition ($null -ne $redactionFailure) -Message 'Expected redaction request to fail.'
    Assert-True -Condition (-not $redactionFailure.Exception.Message.Contains('secret-token-must-not-leak')) -Message 'HTTP diagnostic leaked an access token.'
    Assert-True -Condition $redactionFailure.Exception.Message.Contains('<redacted>') -Message 'HTTP diagnostic did not mark the redacted token.'
    $passed++

    $uploadFile = Join-Path $cloudRoot 'tiny.zip'
    Set-Content -Encoding UTF8 -LiteralPath $uploadFile -Value 'fake zip body'
    $uploadResponse = Join-Path $cloudRoot 'upload-response.json'
    Assert-Passes -Action {
        Invoke-EdgeCurlRequest -Method POST -Uri "$baseUrl/upload" -Token 'fake-token' -UploadFile $uploadFile `
            -ResponsePath $uploadResponse -RequestTimeoutSeconds 5 -LowSpeedTimeSeconds 2 -LowSpeedLimitBytesPerSecond 1 | Out-Null
    }
    Assert-ThrowsContaining -Action {
        Invoke-EdgeCurlRequest -Method POST -Uri "$baseUrl/upload-error" -Token 'fake-token' -UploadFile $uploadFile `
            -ResponsePath $uploadResponse -RequestTimeoutSeconds 5 -LowSpeedTimeSeconds 2 -LowSpeedLimitBytesPerSecond 1 | Out-Null
    } -Needles @('httpStatus=413', 'bundle_too_large', 'limit')
    Assert-Passes -Action { Test-EdgeCurlUrl -Uri "$baseUrl/download" -RequestTimeoutSeconds 5 }
    Assert-ThrowsContaining -Action { Test-EdgeCurlUrl -Uri "$baseUrl/missing" -RequestTimeoutSeconds 5 } -Needles @('httpStatus=404')

    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    Assert-ThrowsContaining -Action {
        Invoke-EdgeCurlJsonGet -Uri "$baseUrl/slow" -RequestTimeoutSeconds 1 -LowSpeedTimeSeconds 1 -LowSpeedLimitBytesPerSecond 1 | Out-Null
    } -Needles @('curlExit=28')
    $timer.Stop()
    Assert-True -Condition ($timer.Elapsed.TotalSeconds -lt 4) -Message "HTTP timeout was not fail-fast: $($timer.Elapsed.TotalSeconds)s"
    $passed++

    $hostText = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $scriptsRoot 'LocalPublishAndDeploy.ps1')
    $pluginText = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $scriptsRoot 'PublishEdgePluginRelease.ps1')
    Assert-True -Condition $hostText.Contains('Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgeHost') -Message 'Host route dispatch guard is missing.'
    Assert-True -Condition $pluginText.Contains('Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgePlugin') -Message 'Plugin route dispatch guard is missing.'
    Assert-True -Condition (-not $hostText.Contains('PublishEdgePluginRelease.ps1')) -Message 'Host publisher routes to plugin publisher.'
    Assert-True -Condition (-not $pluginText.Contains('LocalPublishAndDeploy.ps1')) -Message 'Plugin publisher routes to host publisher.'
    $passed++

    $stateRoot = New-TestDirectory
    $statePath = Write-EdgeDeploymentAttemptState -ReleaseRoot $stateRoot -Target EdgeHost `
        -InvocationId ([Guid]::NewGuid().ToString('D')) -Stage 'uploading' -Status failed -Facts @{ error = 'fake'; sourceCommit = 'abc' }
    $state = Get-Content -Raw -Encoding UTF8 -LiteralPath $statePath | ConvertFrom-Json
    Assert-True -Condition ($state.status -eq 'failed' -and $state.stage -eq 'uploading') -Message 'Deployment attempt state was not written atomically.'
    $passed++

    Write-Host "Edge deployment behavior tests passed: $passed"
    $global:LASTEXITCODE = 0
}
finally {
    if ($null -ne $fakeCloudProcess -and -not $fakeCloudProcess.HasExited) {
        Stop-Process -Id $fakeCloudProcess.Id -Force -ErrorAction SilentlyContinue
        $fakeCloudProcess.WaitForExit(5000) | Out-Null
    }
    foreach ($path in $temporaryRoots) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($null -eq $oldDispatch) { Remove-Item Env:IIOT_EDGE_WORKSPACE_DISPATCH -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_WORKSPACE_DISPATCH = $oldDispatch }
    if ($null -eq $oldTarget) { Remove-Item Env:IIOT_EDGE_WORKSPACE_TARGET -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_WORKSPACE_TARGET = $oldTarget }
    if ($null -eq $oldInvocation) { Remove-Item Env:IIOT_EDGE_WORKSPACE_INVOCATION_ID -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_WORKSPACE_INVOCATION_ID = $oldInvocation }
}
