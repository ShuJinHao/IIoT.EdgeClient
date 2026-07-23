param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptsRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $scriptsRoot
. (Join-Path $scriptsRoot 'EdgeDeployment.Common.ps1')
. (Join-Path $PSScriptRoot 'EdgeLoopbackFakeCloud.TestSupport.ps1')

$passed = 0
$temporaryRoots = [System.Collections.Generic.List[string]]::new()
$fakeCloudServer = $null

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

function Invoke-PreflightProcess {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [ValidateRange(1, 300)][int]$TimeoutSeconds = 30
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = (Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    $startInfo.WorkingDirectory = Split-Path -Parent (Split-Path -Parent $ScriptPath)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('-NoLogo', '-NoProfile', '-File', $ScriptPath) + $Arguments) {
        $startInfo.ArgumentList.Add([string]$argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $started = $false
    try {
        if (-not $process.Start()) {
            throw "Could not start deployment preflight fixture: $ScriptPath"
        }
        $started = $true
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $killError = ''
            try {
                $process.Kill($true)
            }
            catch {
                $killError = $_.Exception.Message
            }
            if (-not $process.WaitForExit(5000)) {
                throw "Deployment preflight fixture timed out after $TimeoutSeconds seconds and its process tree did not exit within the 5-second kill wait. killError=$killError script=$ScriptPath"
            }
            throw "Deployment preflight fixture timed out after $TimeoutSeconds seconds; the entire process tree was killed. script=$ScriptPath"
        }
        if (-not $stdoutTask.Wait(5000) -or -not $stderrTask.Wait(5000)) {
            throw "Deployment preflight fixture exited but redirected output did not close within 5 seconds. script=$ScriptPath"
        }
        $stdout = $stdoutTask.Result
        $stderr = $stderrTask.Result
        $output = ($stdout + [Environment]::NewLine + $stderr).Trim()
        $exitCode = $process.ExitCode

        return [PSCustomObject]@{
            ExitCode = $exitCode
            Output = $output
        }
    }
    finally {
        if ($started) {
            try {
                if (-not $process.HasExited) {
                    $process.Kill($true)
                    [void]$process.WaitForExit(5000)
                }
            }
            catch {
                # The bounded timeout path already reports the actionable process failure.
            }
        }
        $process.Dispose()
    }
}

function Assert-PreflightResult {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][int]$ExpectedExitCode,
        [Parameter(Mandatory = $true)][string[]]$RequiredText,
        [Parameter(Mandatory = $true)][string]$Scenario,
        [System.StringComparison]$TextComparison = [System.StringComparison]::OrdinalIgnoreCase
    )

    if ($Result.ExitCode -ne $ExpectedExitCode) {
        throw "$Scenario expected exit $ExpectedExitCode, got $($Result.ExitCode). Output: $($Result.Output)"
    }
    foreach ($required in $RequiredText) {
        if (-not $Result.Output.Contains($required, $TextComparison)) {
            throw "$Scenario output did not contain '$required'. Output: $($Result.Output)"
        }
    }
    $script:passed++
}

function New-PreflightWorkspaceFixture {
    $fixtureRoot = New-TestDirectory
    $workspaceRoot = Join-Path $fixtureRoot 'workspace'
    $canonicalRepository = Join-Path $workspaceRoot 'IIoT.EdgeClient'
    $linkedRepository = Join-Path $fixtureRoot 'linked-edge'
    $caseVariantRepository = Join-Path $fixtureRoot 'case-variant-edge'
    $wrongWorkspaceRoot = Join-Path $fixtureRoot 'wrong-workspace'
    $wrongCanonicalRepository = Join-Path $wrongWorkspaceRoot 'IIoT.EdgeClient'

    foreach ($directory in @(
        (Join-Path $workspaceRoot 'docs'),
        (Join-Path $workspaceRoot 'deploy'),
        (Join-Path $canonicalRepository 'scripts'),
        (Join-Path $canonicalRepository 'docs'),
        (Join-Path $canonicalRepository 'src/Edge/IIoT.Edge.Shell'),
        (Join-Path $wrongWorkspaceRoot 'docs'),
        (Join-Path $wrongWorkspaceRoot 'deploy'),
        $wrongCanonicalRepository
    )) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $workspaceRoot 'docs/上传部署总览.md') -Value '# isolated workspace deployment overview marker'
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $workspaceRoot 'deploy/Invoke-WorkspaceDeploy.ps1') -Value '# isolated marker; never executed'
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $wrongWorkspaceRoot 'docs/上传部署总览.md') -Value '# wrong workspace marker'
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $wrongWorkspaceRoot 'deploy/Invoke-WorkspaceDeploy.ps1') -Value '# wrong workspace marker; never executed'

    Copy-Item -LiteralPath (Join-Path $scriptsRoot 'TestEdgeDeploymentPreflight.ps1') -Destination (Join-Path $canonicalRepository 'scripts/TestEdgeDeploymentPreflight.ps1')
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $canonicalRepository 'scripts/EdgeReleaseCredential.Common.ps1') -Value '# isolated no-network credential stub'
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $canonicalRepository 'scripts/EdgeDeployment.Common.ps1') -Value '# isolated deployment guard stub'
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $canonicalRepository 'scripts/LocalPublishAndDeploy.ps1') -Value @'
# Stable Edge host releases must use -Transport http
# Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgeHost
# Enter-EdgeDeploymentLock
# ResumeReleaseRoot
# UploadTimeoutSeconds
'@
    foreach ($requiredScript in @(
        'PublishEdgeClientInstallerArtifact.ps1',
        'TestEdgeClientInstallerArtifact.ps1',
        'PackEdgeClientVelopack.ps1'
    )) {
        Set-Content -Encoding UTF8 -LiteralPath (Join-Path $canonicalRepository "scripts/$requiredScript") -Value '# isolated required host marker'
    }
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $canonicalRepository 'docs/客户端部署.md') -Value '# isolated Edge deployment guide'
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $canonicalRepository 'docs/Edge安装更新验收.md') -Value '# isolated Edge installer acceptance guide'
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $canonicalRepository 'src/Edge/IIoT.Edge.Shell/appsettings.fixture.json') -Value '{}'

    Invoke-Git -WorkingDirectory $canonicalRepository -Arguments @('init', '-q')
    Invoke-Git -WorkingDirectory $canonicalRepository -Arguments @('config', 'user.name', 'Edge Preflight Test')
    Invoke-Git -WorkingDirectory $canonicalRepository -Arguments @('config', 'user.email', 'edge-preflight-test@example.invalid')
    Invoke-Git -WorkingDirectory $canonicalRepository -Arguments @('add', 'scripts', 'docs', 'src')
    Invoke-Git -WorkingDirectory $canonicalRepository -Arguments @('commit', '-q', '-m', 'isolated preflight fixture')
    Invoke-Git -WorkingDirectory $canonicalRepository -Arguments @('worktree', 'add', '--detach', $linkedRepository, 'HEAD')
    if (-not $IsWindows) {
        Invoke-Git -WorkingDirectory $canonicalRepository -Arguments @('worktree', 'add', '--detach', $caseVariantRepository, 'HEAD')
    }

    Invoke-Git -WorkingDirectory $wrongCanonicalRepository -Arguments @('init', '-q')
    Invoke-Git -WorkingDirectory $wrongCanonicalRepository -Arguments @('config', 'user.name', 'Wrong Edge Owner Test')
    Invoke-Git -WorkingDirectory $wrongCanonicalRepository -Arguments @('config', 'user.email', 'wrong-edge-owner-test@example.invalid')
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $wrongCanonicalRepository 'tracked.txt') -Value 'independent repository owner'
    Invoke-Git -WorkingDirectory $wrongCanonicalRepository -Arguments @('add', 'tracked.txt')
    Invoke-Git -WorkingDirectory $wrongCanonicalRepository -Arguments @('commit', '-q', '-m', 'independent owner')

    return [PSCustomObject]@{
        WorkspaceRoot = $workspaceRoot
        CanonicalRepository = $canonicalRepository
        LinkedRepository = $linkedRepository
        CaseVariantRepository = $caseVariantRepository
        WrongWorkspaceRoot = $wrongWorkspaceRoot
        CanonicalPreflight = Join-Path $canonicalRepository 'scripts/TestEdgeDeploymentPreflight.ps1'
        LinkedPreflight = Join-Path $linkedRepository 'scripts/TestEdgeDeploymentPreflight.ps1'
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

    $preflightFixture = New-PreflightWorkspaceFixture
    $basePreflightArguments = @(
        '-Mode', 'Host',
        '-SkipCloud',
        '-AllowDirty',
        '-ReleaseNotes', 'isolated deployment preflight fixture'
    )

    $canonicalFixtureAuthFiles = [Collections.Generic.List[string]]::new()
    foreach ($authRoot in @(
        (Join-Path $preflightFixture.CanonicalRepository 'src/Edge/IIoT.Edge.Launcher/Services'),
        (Join-Path $preflightFixture.CanonicalRepository 'src/Infrastructure/IIoT.Edge.Infrastructure.Integration/Auth')
    )) {
        if (Test-Path -LiteralPath $authRoot -PathType Container) {
            foreach ($authFile in Get-ChildItem -LiteralPath $authRoot -Recurse -File -Filter '*.cs') {
                $canonicalFixtureAuthFiles.Add($authFile.FullName)
            }
        }
    }
    Assert-True -Condition ($canonicalFixtureAuthFiles.Count -eq 0) `
        -Message 'Canonical deployment preflight fixture must exercise the real zero-auth-file scanner contract.'

    $canonicalResult = Invoke-PreflightProcess -ScriptPath $preflightFixture.CanonicalPreflight -Arguments $basePreflightArguments
    Assert-PreflightResult -Result $canonicalResult -ExpectedExitCode 0 `
        -RequiredText @('Deployment preflight passed.') -Scenario 'canonical checkout default workspace root'

    $linkedWithoutRootResult = Invoke-PreflightProcess -ScriptPath $preflightFixture.LinkedPreflight -Arguments $basePreflightArguments
    Assert-PreflightResult -Result $linkedWithoutRootResult -ExpectedExitCode 1 `
        -RequiredText @('EDGE-DEPLOY-WORKSPACE-001', 'reason=linked-worktree-requires-explicit-root', '-WorkspaceRoot') `
        -Scenario 'linked worktree without explicit workspace root'

    $linkedArguments = $basePreflightArguments + @('-WorkspaceRoot', $preflightFixture.WorkspaceRoot)
    $linkedResult = Invoke-PreflightProcess -ScriptPath $preflightFixture.LinkedPreflight -Arguments $linkedArguments
    Assert-PreflightResult -Result $linkedResult -ExpectedExitCode 0 `
        -RequiredText @('Deployment preflight passed.') -Scenario 'linked worktree with explicit workspace root'

    $wrongOwnerArguments = $basePreflightArguments + @('-WorkspaceRoot', $preflightFixture.WrongWorkspaceRoot)
    $wrongOwnerResult = Invoke-PreflightProcess -ScriptPath $preflightFixture.LinkedPreflight -Arguments $wrongOwnerArguments
    Assert-PreflightResult -Result $wrongOwnerResult -ExpectedExitCode 1 `
        -RequiredText @('EDGE-DEPLOY-WORKSPACE-001', 'reason=repository-owner-mismatch') `
        -Scenario 'workspace root with markers but wrong repository owner'

    $testOnlyScriptDirectory = Join-Path $preflightFixture.LinkedRepository 'scripts/tests/fixtures'
    $sourceTestsDirectory = Join-Path $preflightFixture.LinkedRepository 'src/Tests/PreflightFixture'
    $sourceTestingDirectory = Join-Path $preflightFixture.LinkedRepository 'src/Testing/PreflightFixture'
    New-Item -ItemType Directory -Force -Path $testOnlyScriptDirectory, $sourceTestsDirectory, $sourceTestingDirectory | Out-Null
    $testOnlyScriptPath = Join-Path $testOnlyScriptDirectory 'TestOnlyPasswordHash.ps1'
    Set-Content -Encoding UTF8 -LiteralPath $testOnlyScriptPath -Value @'
param([string]$Password)
$algorithm = [Security.Cryptography.SHA256]::Create()
$bytes = [Text.Encoding]::UTF8.GetBytes($Password)
$algorithm.ComputeHash($bytes)
'@
    $testOnlyDebugSource = @'
namespace PreflightFixture;
internal static class TestOnlyDebugOutput
{
    internal static void Write() => System.Diagnostics.Debug.WriteLine("test-only negative fixture");
}
'@
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $sourceTestsDirectory 'TestOnlyDebugOutput.cs') -Value $testOnlyDebugSource
    Set-Content -Encoding UTF8 -LiteralPath (Join-Path $sourceTestingDirectory 'TestOnlyDebugOutput.cs') -Value $testOnlyDebugSource

    $securityScanArguments = $linkedArguments + @('-RunSourceSecurityScan')
    $testOwnerResult = Invoke-PreflightProcess -ScriptPath $preflightFixture.LinkedPreflight -Arguments $securityScanArguments
    Assert-PreflightResult -Result $testOwnerResult -ExpectedExitCode 0 `
        -RequiredText @('Deployment preflight passed.') -Scenario 'three test-owned negative fixture paths'

    if (-not $IsWindows) {
        $caseProbeDirectory = Join-Path $preflightFixture.CaseVariantRepository '.case-sensitive-coexistence-probe'
        New-Item -ItemType Directory -Path $caseProbeDirectory | Out-Null
        $caseProbeExactPath = Join-Path $caseProbeDirectory 'edge-path-owner'
        $caseProbeVariantPath = Join-Path $caseProbeDirectory 'Edge-Path-Owner'
        [IO.File]::WriteAllText($caseProbeExactPath, 'exact-owner')
        $caseProbeVariantWriteSucceeded = $true
        try {
            [IO.File]::WriteAllText($caseProbeVariantPath, 'case-variant')
        }
        catch {
            $caseProbeVariantWriteSucceeded = $false
        }
        $caseProbeNames = @([IO.Directory]::EnumerateFiles($caseProbeDirectory) | ForEach-Object { [IO.Path]::GetFileName($_) })
        $caseProbeHasExact = @($caseProbeNames | Where-Object {
            [string]::Equals($_, 'edge-path-owner', [System.StringComparison]::Ordinal)
        }).Count -eq 1
        $caseProbeHasVariant = @($caseProbeNames | Where-Object {
            [string]::Equals($_, 'Edge-Path-Owner', [System.StringComparison]::Ordinal)
        }).Count -eq 1
        $caseSensitiveCoexistenceSupported = $caseProbeVariantWriteSucceeded -and $caseProbeHasExact -and $caseProbeHasVariant

        $caseVariantOriginalPreflight = Join-Path $preflightFixture.CaseVariantRepository 'scripts/TestEdgeDeploymentPreflight.ps1'
        if ($caseSensitiveCoexistenceSupported) {
            $exactScriptTestsDirectory = Join-Path $preflightFixture.CaseVariantRepository 'scripts/tests/fixtures'
            $caseVariantScriptTestsDirectory = Join-Path $preflightFixture.CaseVariantRepository 'scripts/Tests/fixtures'
            $exactSourceTestsDirectory = Join-Path $preflightFixture.CaseVariantRepository 'src/Tests/PreflightFixture'
            $caseVariantSourceTestsDirectory = Join-Path $preflightFixture.CaseVariantRepository 'src/tests/PreflightFixture'
            New-Item -ItemType Directory -Force -Path `
                $exactScriptTestsDirectory, $caseVariantScriptTestsDirectory, `
                $exactSourceTestsDirectory, $caseVariantSourceTestsDirectory | Out-Null
            Copy-Item -LiteralPath $testOnlyScriptPath -Destination (Join-Path $exactScriptTestsDirectory 'ExactOwnerPasswordHash.ps1')
            Copy-Item -LiteralPath $testOnlyScriptPath -Destination (Join-Path $caseVariantScriptTestsDirectory 'CaseVariantPasswordHash.ps1')
            Set-Content -Encoding UTF8 -LiteralPath (Join-Path $exactSourceTestsDirectory 'ExactOwnerDebugOutput.cs') -Value $testOnlyDebugSource
            Set-Content -Encoding UTF8 -LiteralPath (Join-Path $caseVariantSourceTestsDirectory 'CaseVariantDebugOutput.cs') -Value $testOnlyDebugSource

            $caseVariantPreflight = Join-Path $preflightFixture.CaseVariantRepository 'scripts/TestEdgeDeploymentPreFlight.ps1'
            Copy-Item -LiteralPath $caseVariantOriginalPreflight -Destination $caseVariantPreflight

            $scriptDirectoryNames = [Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            foreach ($directoryPath in [IO.Directory]::EnumerateDirectories((Join-Path $preflightFixture.CaseVariantRepository 'scripts'))) {
                [void]$scriptDirectoryNames.Add([IO.Path]::GetFileName($directoryPath))
            }
            $sourceDirectoryNames = [Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            foreach ($directoryPath in [IO.Directory]::EnumerateDirectories((Join-Path $preflightFixture.CaseVariantRepository 'src'))) {
                [void]$sourceDirectoryNames.Add([IO.Path]::GetFileName($directoryPath))
            }
            $preflightFileNames = [Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            foreach ($filePath in [IO.Directory]::EnumerateFiles((Join-Path $preflightFixture.CaseVariantRepository 'scripts'))) {
                [void]$preflightFileNames.Add([IO.Path]::GetFileName($filePath))
            }
            Assert-True `
                -Condition ($scriptDirectoryNames.Contains('tests') -and $scriptDirectoryNames.Contains('Tests') -and
                    $sourceDirectoryNames.Contains('Tests') -and $sourceDirectoryNames.Contains('tests') -and
                    $preflightFileNames.Contains('TestEdgeDeploymentPreflight.ps1') -and
                    $preflightFileNames.Contains('TestEdgeDeploymentPreFlight.ps1')) `
                -Message 'Case-sensitive fixture did not preserve exact self/owner paths and their case variants simultaneously.'

            $caseVariantArguments = $basePreflightArguments + @(
                '-WorkspaceRoot', $preflightFixture.WorkspaceRoot,
                '-RunSourceSecurityScan'
            )
            $caseVariantResult = Invoke-PreflightProcess -ScriptPath $caseVariantPreflight -Arguments $caseVariantArguments
            Assert-PreflightResult -Result $caseVariantResult -ExpectedExitCode 1 `
                -RequiredText @(
                    'EDGE-DEPLOY-SECURITY-001',
                    'scripts/Tests/fixtures/CaseVariantPasswordHash.ps1',
                    'src/tests/PreflightFixture/CaseVariantDebugOutput.cs',
                    'scripts/TestEdgeDeploymentPreFlight.ps1'
                ) `
                -Scenario 'non-Windows case-variant paths remain production-owned' `
                -TextComparison ([System.StringComparison]::Ordinal)
        }
        else {
            $preflightSource = [IO.File]::ReadAllText($caseVariantOriginalPreflight)
            $pathComparisonContract = '\$pathComparison\s*=\s*if\s*\(\$IsWindows\)[\s\S]*?\[System\.StringComparison\]::OrdinalIgnoreCase[\s\S]*?\[System\.StringComparison\]::Ordinal'
            $pathComparerContract = '\$pathComparer\s*=\s*if\s*\(\$pathComparison\s+-eq\s+\[System\.StringComparison\]::OrdinalIgnoreCase\)[\s\S]*?\[System\.StringComparer\]::OrdinalIgnoreCase[\s\S]*?\[System\.StringComparer\]::Ordinal'
            Assert-True `
                -Condition ([regex]::IsMatch($preflightSource, $pathComparisonContract) -and [regex]::IsMatch($preflightSource, $pathComparerContract)) `
                -Message 'Case-sensitive path coexistence is unavailable and the non-Windows ordinal comparer source contract is missing.'
            Write-Host 'Case-sensitive path coexistence is unavailable; verified the non-Windows ordinal comparer source contract without claiming the coexistence behavior ran.'
            $passed++
        }
    }

    $productionScriptDirectory = Join-Path $preflightFixture.LinkedRepository 'scripts/production'
    New-Item -ItemType Directory -Force -Path $productionScriptDirectory | Out-Null
    $productionScriptPath = Join-Path $productionScriptDirectory 'TestOnlyPasswordHash.ps1'
    Copy-Item -LiteralPath $testOnlyScriptPath -Destination $productionScriptPath
    $productionResult = Invoke-PreflightProcess -ScriptPath $preflightFixture.LinkedPreflight -Arguments $securityScanArguments
    Assert-PreflightResult -Result $productionResult -ExpectedExitCode 1 `
        -RequiredText @('EDGE-DEPLOY-SECURITY-001', 'SHA256 password hash generation script content', 'scripts/production/TestOnlyPasswordHash.ps1') `
        -Scenario 'same-byte production security violation'

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
    $fakeCloudServer = Start-EdgeLoopbackFakeCloud -PortFile $portFile -RequestLog $requestLog
    $baseUrl = $fakeCloudServer.BaseUrl

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
    Stop-EdgeLoopbackFakeCloud -Server $fakeCloudServer
    foreach ($path in $temporaryRoots) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($null -eq $oldDispatch) { Remove-Item Env:IIOT_EDGE_WORKSPACE_DISPATCH -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_WORKSPACE_DISPATCH = $oldDispatch }
    if ($null -eq $oldTarget) { Remove-Item Env:IIOT_EDGE_WORKSPACE_TARGET -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_WORKSPACE_TARGET = $oldTarget }
    if ($null -eq $oldInvocation) { Remove-Item Env:IIOT_EDGE_WORKSPACE_INVOCATION_ID -ErrorAction SilentlyContinue } else { $env:IIOT_EDGE_WORKSPACE_INVOCATION_ID = $oldInvocation }
}
