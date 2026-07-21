[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$protocolModulePath = Join-Path $PSScriptRoot 'EdgePluginContractLedger.Protocol.psm1'
Import-Module $protocolModulePath -Force
$coordinatorParentBinding = Initialize-EdgeAuthorityCoordinatorParentEnvironment
$coordinatorPowerShellPath = Resolve-EdgeFixedExecutable ([Environment]::ProcessPath)
$coordinatorGitPath = Assert-EdgeAuthorityFinalGitExecutablePath `
    ([string]$coordinatorParentBinding.fixedGitExecutablePath)
$coordinatorMaximumCapturedBytes = 16777216
$coordinatorCodeRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent | Split-Path -Parent))
if (-not (Test-EdgePathIdentity $coordinatorCodeRoot `
        ([string]$coordinatorParentBinding.authorityRepositoryRoot))) {
    throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING fixed coordinator code root differs from the canonical parent binding.'
}
$coordinatorEmptyGitConfigPath = Assert-EdgeAuthorityEmptyGitConfig $coordinatorCodeRoot
$coordinatorGitChildEnvironment = New-EdgeAuthorityGitChildEnvironment `
    $coordinatorEmptyGitConfigPath $coordinatorGitPath

function Invoke-CoordinatorGit {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string[]]$Arguments
    )
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $script:coordinatorGitPath
    $startInfo.WorkingDirectory = $RepositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('-C', $RepositoryRoot, '-c',
            "core.hooksPath=$script:coordinatorEmptyGitConfigPath") + $Arguments) {
        $startInfo.ArgumentList.Add([string]$argument)
    }
    foreach ($name in $script:coordinatorGitChildEnvironment.Keys) {
        $startInfo.Environment[[string]$name] = [string]$script:coordinatorGitChildEnvironment[$name]
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'EDGE-SPLIT-AUTHORITY-GIT-001 fixed git process did not start.'
        }
        $stdoutTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync(
            $process.StandardOutput.BaseStream, $script:coordinatorMaximumCapturedBytes)
        $stderrTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync(
            $process.StandardError.BaseStream, $script:coordinatorMaximumCapturedBytes)
        $deadline = [DateTimeOffset]::UtcNow.AddMinutes(5)
        while (-not $process.WaitForExit(100)) {
            if ($stdoutTask.IsFaulted -or $stderrTask.IsFaulted) {
                try { $process.Kill($true) } catch { }
                [void]$process.WaitForExit(30000)
                throw 'EDGE-SPLIT-AUTHORITY-GIT-001 fixed git output exceeded 16 MiB per stream.'
            }
            if ([DateTimeOffset]::UtcNow -ge $deadline) {
                try { $process.Kill($true) } catch { }
                [void]$process.WaitForExit(30000)
                throw 'EDGE-SPLIT-AUTHORITY-GIT-001 fixed git command exceeded five minutes.'
            }
        }
        try {
            $capture = Wait-EdgeBoundedCaptureTasks $stdoutTask $stderrTask $deadline `
                'EDGE-SPLIT-AUTHORITY-GIT-001'
            $stdout = [Text.UTF8Encoding]::new($false, $true).GetString($capture.stdoutBytes)
            $stderr = [Text.UTF8Encoding]::new($false, $true).GetString($capture.stderrBytes)
        }
        catch { throw 'EDGE-SPLIT-AUTHORITY-GIT-001 fixed git output was unbounded, held open, or not strict UTF-8.' }
        $exitCode = [int]$process.ExitCode
    }
    finally { $process.Dispose() }
    if ($exitCode -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-GIT-001 git command failed; inspect the coordinator child log."
    }
    return $stdout.Trim()
}

function Get-CoordinatorLocalGitConfigDigest {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $configPathValue = Invoke-CoordinatorGit $RepositoryRoot @('rev-parse', '--git-path', 'config')
    $configPath = if ([IO.Path]::IsPathRooted($configPathValue)) {
        [IO.Path]::GetFullPath($configPathValue)
    }
    else { Resolve-EdgeRepositoryPath $RepositoryRoot $configPathValue.Replace('\', '/') }
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        throw 'EDGE-SPLIT-AUTHORITY-GIT-CONFIG authority local Git config is missing.'
    }
    $item = Get-Item -LiteralPath $configPath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-GIT-CONFIG authority local Git config is indirect.'
    }
    return Get-EdgeSha256File $configPath
}

function Assert-CoordinatorRoot {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Label)
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
        throw "EDGE-SPLIT-AUTHORITY-PATH-001 $Label does not exist."
    }
    $item = Get-Item -LiteralPath $fullPath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)) {
        throw "EDGE-SPLIT-AUTHORITY-PATH-001 $Label must not be a symlink/reparse point."
    }
    $gitTopLevel = Invoke-CoordinatorGit $fullPath @('rev-parse', '--show-toplevel')
    if (-not (Test-EdgePathIdentity $fullPath $gitTopLevel)) {
        throw "EDGE-SPLIT-AUTHORITY-PATH-001 $Label must be the exact pinned git worktree top-level."
    }
    return $fullPath
}

function Read-CoordinatorStandardInputBytes {
    $input = [Console]::OpenStandardInput()
    $memory = [IO.MemoryStream]::new()
    try {
        $buffer = [byte[]]::new(8192)
        while (($read = $input.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $memory.Write($buffer, 0, $read)
            if ($memory.Length -gt 1048576) {
                throw 'EDGE-SPLIT-AUTHORITY-REQUEST-001 request exceeds the one-megabyte protocol limit.'
            }
        }
        return $memory.ToArray()
    }
    finally { $memory.Dispose() }
}

function Assert-RunScopedPath {
    param(
        [Parameter(Mandatory)][string]$RunRoot,
        [Parameter(Mandatory)][string]$Candidate,
        [Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)][string]$Label,
        [switch]$AllowIdentity
    )
    $run = [IO.Path]::GetFullPath($RunRoot).TrimEnd(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
    $path = [IO.Path]::GetFullPath($Candidate)
    $comparison = if ([OperatingSystem]::IsWindows()) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $inside = $path.StartsWith("$run$([IO.Path]::DirectorySeparatorChar)", $comparison)
    if (($AllowIdentity -and [string]::Equals($path, $run, $comparison)) -or $inside) {
        if (-not $path.Contains($RunId, [StringComparison]::Ordinal)) {
            throw "EDGE-SPLIT-AUTHORITY-PATH-001 $Label is not bound to runId."
        }
        return $path
    }
    throw "EDGE-SPLIT-AUTHORITY-PATH-001 $Label escapes the explicit run root."
}

function Start-AuthorityChild {
    param(
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$Name
    )
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $script:coordinatorPowerShellPath
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    foreach ($argument in @('-NoLogo', '-NoProfile') + $Arguments) {
        $startInfo.ArgumentList.Add([string]$argument)
    }
    foreach ($environmentName in $script:coordinatorGitChildEnvironment.Keys) {
        $startInfo.Environment[[string]$environmentName] =
            [string]$script:coordinatorGitChildEnvironment[$environmentName]
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "EDGE-SPLIT-AUTHORITY-PROCESS-001 could not start the fixed $Name child."
    }
    return [pscustomobject]@{
        name = $Name
        process = $process
        stdoutTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync(
            $process.StandardOutput.BaseStream, $script:coordinatorMaximumCapturedBytes)
        stderrTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync(
            $process.StandardError.BaseStream, $script:coordinatorMaximumCapturedBytes)
        stopwatch = [Diagnostics.Stopwatch]::StartNew()
    }
}

function Stop-AuthorityChildTree {
    param([AllowNull()]$Child)
    if ($null -eq $Child -or $null -eq $Child.process) { return }
    try {
        if (-not $Child.process.HasExited) {
            $Child.process.Kill($true)
            [void]$Child.process.WaitForExit(30000)
        }
    }
    catch {
        # The outer cleanup still removes only the two explicit run worktrees.
    }
}

function Wait-AuthorityChildExit {
    param(
        [Parameter(Mandatory)]$Child,
        [Parameter(Mandatory)][DateTimeOffset]$DeadlineUtc
    )

    while (-not $Child.process.HasExited) {
        if ($Child.stdoutTask.IsFaulted -or $Child.stderrTask.IsFaulted) {
            Stop-AuthorityChildTree $Child
            throw 'EDGE-SPLIT-AUTHORITY-OUTPUT-LIMIT authority/replay output exceeded 16 MiB per stream.'
        }
        if ([DateTimeOffset]::UtcNow -ge $DeadlineUtc) {
            Stop-AuthorityChildTree $Child
            throw 'EDGE-SPLIT-AUTHORITY-TIMEOUT-001 authority/replay exceeded the bounded coordinator deadline.'
        }
        Start-Sleep -Milliseconds 200
    }
}

function Complete-AuthorityChild {
    param(
        [Parameter(Mandatory)]$Child,
        [Parameter(Mandatory)][string]$RunRoot,
        [Parameter(Mandatory)][DateTimeOffset]$CaptureDeadlineUtc
    )
    $Child.stopwatch.Stop()
    try {
        $capture = Wait-EdgeBoundedCaptureTasks $Child.stdoutTask $Child.stderrTask $CaptureDeadlineUtc `
            'EDGE-SPLIT-AUTHORITY-OUTPUT-LIMIT'
        $stdout = [Text.UTF8Encoding]::new($false, $true).GetString($capture.stdoutBytes)
        $stderr = [Text.UTF8Encoding]::new($false, $true).GetString($capture.stderrBytes)
    }
    catch {
        throw "EDGE-SPLIT-AUTHORITY-OUTPUT-LIMIT fixed $($Child.name) child output exceeded 16 MiB per stream or was not strict UTF-8."
    }
    $log = [pscustomobject][ordered]@{
        name = [string]$Child.name
        pid = [int]$Child.process.Id
        elapsedMilliseconds = [long]$Child.stopwatch.ElapsedMilliseconds
        exitCode = [int]$Child.process.ExitCode
        stdout = [string]$stdout
        stderr = [string]$stderr
    }
    $logPath = Join-Path $RunRoot "$($Child.name).json"
    [IO.File]::WriteAllText($logPath, (ConvertTo-EdgeCanonicalJson $log), [Text.UTF8Encoding]::new($false))
    if ($log.exitCode -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-CHILD-001 fixed $($Child.name) child failed; inspect its run-scoped log."
    }
    return $log
}

function Remove-ExplicitAuthorityWorktree {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$RunRoot,
        [Parameter(Mandatory)][string]$WorktreePath,
        [Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)]$ExpectedMarker
    )
    $validatedPath = Assert-RunScopedPath -RunRoot $RunRoot -Candidate $WorktreePath -RunId $RunId -Label 'worktree'
    $runItem = Get-Item -LiteralPath $RunRoot -Force
    if (($runItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$runItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-CLEANUP-001 run root became a symlink/reparse point.'
    }
    $markerPath = Join-Path $RunRoot '.edge-authority-run.json'
    $markerItem = Get-Item -LiteralPath $markerPath -Force
    [byte[]]$expectedMarkerBytes = @(ConvertTo-EdgeCanonicalBytes $ExpectedMarker)
    if (($markerItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$markerItem.LinkTarget) -or
        -not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
            [IO.File]::ReadAllBytes($markerPath), $expectedMarkerBytes)) {
        throw 'EDGE-SPLIT-AUTHORITY-CLEANUP-001 run marker is indirect, stale, or noncanonical.'
    }
    $repositoryItem = Get-Item -LiteralPath $RepositoryRoot -Force
    if (($repositoryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$repositoryItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-CLEANUP-001 authority repository became a symlink/reparse point.'
    }
    if (Test-Path -LiteralPath $validatedPath) {
        $worktreeItem = Get-Item -LiteralPath $validatedPath -Force
        if (($worktreeItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$worktreeItem.LinkTarget)) {
            throw 'EDGE-SPLIT-AUTHORITY-CLEANUP-001 worktree target became a symlink/reparse point.'
        }
    }
    $listed = @((Invoke-CoordinatorGit $RepositoryRoot @('worktree', 'list', '--porcelain')) -split "`r?`n")
    $registered = @($listed | Where-Object { $_.StartsWith('worktree ', [StringComparison]::Ordinal) } |
        ForEach-Object { [IO.Path]::GetFullPath($_.Substring(9)) } |
        Where-Object { Test-EdgePathIdentity $_ $validatedPath })
    if ($registered.Count -gt 1) {
        throw 'EDGE-SPLIT-AUTHORITY-CLEANUP-001 explicit worktree registration is ambiguous.'
    }
    if ($registered.Count -eq 1) {
        [void](Invoke-CoordinatorGit $RepositoryRoot @('worktree', 'remove', '--force', $validatedPath))
    }
    elseif (Test-Path -LiteralPath $validatedPath) {
        throw 'EDGE-SPLIT-AUTHORITY-CLEANUP-001 unregistered explicit worktree directory remains; refusing broad deletion.'
    }
}

$coordinatorProcess = [Diagnostics.Process]::GetCurrentProcess()
$coordinatorStartUtc = $coordinatorProcess.StartTime.ToUniversalTime().ToString('O')
$totalStopwatch = [Diagnostics.Stopwatch]::StartNew()
$request = $null
$requestBytes = [byte[]]::new(0)
$authorityChild = $null
$replayChild = $null
$privateKey = $null
$descriptor = $null
$runRoot = ''
$validatorWorktree = ''
$replayWorktree = ''
$authorityRoot = ''
$exitCode = 1
$cleanupFailure = $null

try {
    $requestBytes = Read-CoordinatorStandardInputBytes
    $requestSchemaPath = Join-Path (Split-Path $PSScriptRoot -Parent | Split-Path -Parent) `
        'eng/edge-plugin-contract-authority-request.schema.json'
    $request = Assert-EdgeStrictJson -RawBytes $requestBytes -SchemaPath $requestSchemaPath `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-REQUEST-001' -RequireCanonical
    Assert-EdgeAuthorityCoordinatorParentRequest -Binding $coordinatorParentBinding -Request $request
    $challengeBytes = [Convert]::FromBase64String([string]$request.challengeBase64)
    if ($challengeBytes.Length -ne 32) {
        throw 'EDGE-SPLIT-AUTHORITY-REQUEST-001 challenge must be exactly 256 bits.'
    }
    $authorityRoot = Assert-CoordinatorRoot ([string]$request.authorityRepositoryRoot) 'authority repository root'
    $sourceRoot = Assert-CoordinatorRoot ([string]$request.sourceRepositoryRoot) 'source repository root'
    if (-not (Test-EdgePathIdentity $authorityRoot $coordinatorCodeRoot)) {
        throw 'EDGE-SPLIT-AUTHORITY-PATH-001 authority repository must be the exact repository that owns the fixed coordinator code.'
    }
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
    $runRoot = [IO.Path]::GetFullPath([string]$request.runRoot)
    $comparison = if ([OperatingSystem]::IsWindows()) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $runRoot.StartsWith("$tempRoot$([IO.Path]::DirectorySeparatorChar)", $comparison) -or
        -not $runRoot.Contains([string]$request.runId, [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-PATH-001 run root must be a unique child of the OS temporary root.'
    }
    $validatorWorktree = Assert-RunScopedPath -RunRoot $runRoot -Candidate ([string]$request.validatorWorktreePath) `
        -RunId ([string]$request.runId) -Label 'validator worktree'
    $replayWorktree = Assert-RunScopedPath -RunRoot $runRoot -Candidate ([string]$request.replayWorktreePath) `
        -RunId ([string]$request.runId) -Label 'replay worktree'
    if (Test-EdgePathIdentity $validatorWorktree $replayWorktree) {
        throw 'EDGE-SPLIT-AUTHORITY-PATH-001 validator/replay worktrees must be distinct.'
    }
    if (Test-Path -LiteralPath $runRoot) {
        throw 'EDGE-SPLIT-AUTHORITY-PATH-001 unique run root already exists; stale state is never reused.'
    }
    [void](New-Item -ItemType Directory -Path $runRoot)
    $marker = [pscustomobject][ordered]@{
        schemaVersion = 1
        runId = [string]$request.runId
        sourceRepositoryRoot = $sourceRoot
        authorityRepositoryRoot = $authorityRoot
        authorityHead = [string]$request.authorityHead
        validatorWorktreePath = $validatorWorktree
        replayWorktreePath = $replayWorktree
    }
    $markerPath = Join-Path $runRoot '.edge-authority-run.json'
    $partialMarkerPath = "$markerPath.partial-$([string]$request.runId)"
    [IO.File]::WriteAllBytes($partialMarkerPath, (ConvertTo-EdgeCanonicalBytes $marker))
    Move-Item -LiteralPath $partialMarkerPath -Destination $markerPath

    $authorityHead = Invoke-CoordinatorGit $authorityRoot @('rev-parse', 'HEAD')
    $authorityTree = Invoke-CoordinatorGit $authorityRoot @('rev-parse', 'HEAD^{tree}')
    $sourceBaseHead = Invoke-CoordinatorGit $sourceRoot @('rev-parse', 'HEAD')
    $sourceBaseTree = Invoke-CoordinatorGit $sourceRoot @('rev-parse', "$([string]$request.sourceBaseHead)^{tree}")
    $implementationTree = Invoke-CoordinatorGit $authorityRoot @(
        'rev-parse', "$([string]$request.implementationHead)^{tree}")
    if ($authorityHead -cne [string]$request.authorityHead -or
        $authorityTree -cne [string]$request.authorityTree -or
        $sourceBaseHead -cne [string]$request.sourceBaseHead -or
        $sourceBaseTree -cne [string]$request.sourceBaseTree -or
        $implementationTree -cne [string]$request.implementationTree) {
        throw 'EDGE-SPLIT-AUTHORITY-STATE-001 request HEAD/tree bindings differ from the repositories.'
    }
    $isFormal = [string]$request.mode -ceq 'formal-clean'
    if ($isFormal) {
        if (-not (Test-EdgePathIdentity $sourceRoot $authorityRoot) -or
            [string]$request.formalFinalHead -cne $authorityHead -or
            [string]$request.formalFinalTree -cne $authorityTree -or
            [string]$request.sourceBaseHead -cne $authorityHead -or
            [string]$request.sourceBaseTree -cne $authorityTree -or
            -not [string]::IsNullOrEmpty([string]$request.sourceDirtyManifestSha256) -or
            -not [string]::IsNullOrEmpty([string]$request.ephemeralSnapshotHead) -or
            -not [string]::IsNullOrEmpty([string]$request.ephemeralSnapshotTree)) {
            throw 'EDGE-SPLIT-AUTHORITY-MODE-001 formal request fields are inconsistent.'
        }
        $status = Invoke-CoordinatorGit $authorityRoot @('status', '--porcelain=v1', '--untracked-files=all')
        if (-not [string]::IsNullOrWhiteSpace($status)) {
            throw 'EDGE-SPLIT-AUTHORITY-STATE-001 formal authority repository must be completely clean.'
        }
    }
    else {
        if (-not [string]::IsNullOrEmpty([string]$request.formalFinalHead) -or
            -not [string]::IsNullOrEmpty([string]$request.formalFinalTree) -or
            [string]::IsNullOrWhiteSpace([string]$request.sourceDirtyManifestSha256) -or
            [string]$request.ephemeralSnapshotHead -cne $authorityHead -or
            [string]$request.ephemeralSnapshotTree -cne $authorityTree -or
            [string]$request.implementationHead -cne $authorityHead -or
            [string]$request.implementationTree -cne $authorityTree) {
            throw 'EDGE-SPLIT-AUTHORITY-MODE-001 development snapshot request fields are inconsistent.'
        }
    }
    $authorityConfigSha256 = if ($isFormal) { '' } else { Get-CoordinatorLocalGitConfigDigest $authorityRoot }
    $ledgerFullPath = Resolve-EdgeRepositoryPath $authorityRoot ([string]$request.ledgerPath)
    $receiptFullPath = Resolve-EdgeRepositoryPath $authorityRoot ([string]$request.receiptPath)
    if (-not ([string]$request.ledgerPath).StartsWith('.artifacts/', [StringComparison]::Ordinal) -and -not $isFormal) {
        throw 'EDGE-SPLIT-AUTHORITY-PATH-001 development ledger must remain under ignored .artifacts.'
    }
    if (-not ([string]$request.receiptPath).StartsWith('.artifacts/', [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-PATH-001 receipt must remain under ignored .artifacts.'
    }
    if (-not (Test-Path -LiteralPath $ledgerFullPath -PathType Leaf)) {
        throw 'EDGE-SPLIT-AUTHORITY-LEDGER-001 candidate ledger is missing.'
    }
    $ledger = ConvertFrom-EdgeJsonText (Get-Content -LiteralPath $ledgerFullPath -Raw)
    if ([string]$ledger.sourceState.head -cne [string]$request.implementationHead -or
        [string]$ledger.sourceState.tree -cne [string]$request.implementationTree) {
        throw 'EDGE-SPLIT-AUTHORITY-LEDGER-001 ledger implementation HEAD/tree differs from the request.'
    }

    [void](Invoke-CoordinatorGit $authorityRoot @('worktree', 'add', '--detach', $validatorWorktree, $authorityHead))
    [void](Invoke-CoordinatorGit $authorityRoot @('worktree', 'add', '--detach', $replayWorktree, $authorityHead))
    $validatorLedgerRelativePath = [string]$request.ledgerPath
    if (-not $isFormal) {
        $validatorLedgerPath = Resolve-EdgeRepositoryPath $validatorWorktree $validatorLedgerRelativePath
        [void](New-Item -ItemType Directory -Path (Split-Path $validatorLedgerPath -Parent) -Force)
        [IO.File]::Copy($ledgerFullPath, $validatorLedgerPath, $false)
    }
    $authorityResultRelativePath = ".artifacts/edge-authority-$([string]$request.runId)/authority-result.json"
    $replayRelativePath = ".artifacts/edge-authority-$([string]$request.runId)/replay-ledger.json"
    $validatorScript = Join-Path $validatorWorktree 'scripts/tests/Test-EdgePluginContractLedger.ps1'
    $generatorScript = Join-Path $replayWorktree 'eng/Generate-EdgePluginContractLedger.ps1'
    $authorityArguments = [string[]]@(
        '-File', $validatorScript,
        '-RepositoryRoot', $validatorWorktree,
        '-LedgerPath', $validatorLedgerRelativePath,
        '-AuthorityRebuildOnly',
        '-AuthorityResultPath', $authorityResultRelativePath
    )
    $replayArguments = [Collections.Generic.List[string]]::new()
    foreach ($argument in @(
            '-File', $generatorScript,
            '-PluginProject', [string]$request.pluginProject,
            '-OutputPath', $replayRelativePath,
            '-Configuration', [string]$request.configuration,
            '-CurrentBatch', [string]$request.currentBatch,
            '-ViewIdsAssemblyPath', [string]$request.viewIdsAssemblyPath,
            '-ViewIdsTypeName', [string]$request.viewIdsTypeName)) {
        $replayArguments.Add([string]$argument)
    }
    if ($isFormal) {
        $replayArguments.Add('-ValidationReplayImplementationHead')
        $replayArguments.Add([string]$request.implementationHead)
        $replayArguments.Add('-ValidationReplayImplementationTree')
        $replayArguments.Add([string]$request.implementationTree)
    }
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds([int]$request.timeoutSeconds)
    $authorityChild = Start-AuthorityChild -WorkingDirectory $validatorWorktree -Arguments $authorityArguments -Name 'authority'
    Wait-AuthorityChildExit -Child $authorityChild -DeadlineUtc $deadline
    $authorityLog = Complete-AuthorityChild -Child $authorityChild -RunRoot $runRoot `
        -CaptureDeadlineUtc $deadline
    $replayChild = Start-AuthorityChild -WorkingDirectory $replayWorktree -Arguments $replayArguments.ToArray() -Name 'replay'
    Wait-AuthorityChildExit -Child $replayChild -DeadlineUtc $deadline
    $replayLog = Complete-AuthorityChild -Child $replayChild -RunRoot $runRoot `
        -CaptureDeadlineUtc $deadline
    $authorityResultPath = Resolve-EdgeRepositoryPath $validatorWorktree $authorityResultRelativePath
    $replayLedgerPath = Resolve-EdgeRepositoryPath $replayWorktree $replayRelativePath
    $authorityResultSchemaPath = Join-Path $authorityRoot 'eng/edge-plugin-contract-authority-result.schema.json'
    $authorityResult = Assert-EdgeStrictJson -RawBytes ([IO.File]::ReadAllBytes($authorityResultPath)) `
        -SchemaPath $authorityResultSchemaPath `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-RESULT-001' -RequireCanonical
    Assert-EdgeFactGroupSet @($authorityResult.factGroups)
    if ([string]$authorityResult.ledgerSha256 -cne (Get-EdgeSha256File $ledgerFullPath) -or
        [string]$authorityResult.authorityCodeSha256 -cne (Get-EdgeAuthorityCodeDigest $authorityRoot)) {
        throw 'EDGE-SPLIT-AUTHORITY-RESULT-001 independent authority result is not bound to the candidate/code.'
    }
    Assert-EdgeReplayEquivalent `
        -CanonicalLedgerPath $ledgerFullPath `
        -ReplayLedgerPath $replayLedgerPath `
        -LedgerSchemaPath (Join-Path $authorityRoot 'eng/edge-plugin-contract-ledger.schema.json') `
        -CanonicalOutputRelativePath ([string]$request.ledgerPath) `
        -ReplayOutputRelativePath $replayRelativePath
    $currentHead = Invoke-CoordinatorGit $authorityRoot @('rev-parse', 'HEAD')
    $currentTree = Invoke-CoordinatorGit $authorityRoot @('rev-parse', 'HEAD^{tree}')
    if ($currentHead -cne $authorityHead -or $currentTree -cne $authorityTree -or
        (Get-EdgeSha256File $ledgerFullPath) -cne [string]$authorityResult.ledgerSha256) {
        throw 'EDGE-SPLIT-AUTHORITY-STATE-001 authority state changed during validation.'
    }
    if (-not $isFormal -and
        (Get-CoordinatorLocalGitConfigDigest $authorityRoot) -cne $authorityConfigSha256) {
        throw 'EDGE-SPLIT-AUTHORITY-GIT-CONFIG authority local Git config changed during rebuild/replay.'
    }

    $curve = [Security.Cryptography.ECCurve]::CreateFromFriendlyName('nistP256')
    $privateKey = [Security.Cryptography.ECDsa]::Create($curve)
    if ($privateKey.KeySize -ne 256) {
        throw 'EDGE-SPLIT-AUTHORITY-SIGNATURE-001 coordinator did not create an ECDSA P-256 key.'
    }
    $publicBytes = $privateKey.ExportSubjectPublicKeyInfo()
    $publicKeyBase64 = [Convert]::ToBase64String($publicBytes)
    $publicKeySha256 = Get-EdgeSha256Bytes $publicBytes
    $issued = [DateTimeOffset]::UtcNow
    $payload = [pscustomobject][ordered]@{
        schemaVersion = 1
        mode = [string]$request.mode
        formal = $isFormal
        runId = [string]$request.runId
        challengeBase64 = [string]$request.challengeBase64
        issuedUtc = $issued.UtcDateTime.ToString('O')
        expiresUtc = $issued.AddMinutes(20).UtcDateTime.ToString('O')
        sourceRepositoryRoot = [IO.Path]::GetFullPath([string]$request.sourceRepositoryRoot)
        authorityRepositoryRoot = $authorityRoot
        authorityHead = [string]$request.authorityHead
        authorityTree = [string]$request.authorityTree
        formalFinalHead = [string]$request.formalFinalHead
        formalFinalTree = [string]$request.formalFinalTree
        sourceBaseHead = [string]$request.sourceBaseHead
        sourceBaseTree = [string]$request.sourceBaseTree
        sourceDirtyManifestSha256 = [string]$request.sourceDirtyManifestSha256
        ephemeralSnapshotHead = [string]$request.ephemeralSnapshotHead
        ephemeralSnapshotTree = [string]$request.ephemeralSnapshotTree
        implementationHead = [string]$request.implementationHead
        implementationTree = [string]$request.implementationTree
        ledgerPath = [string]$request.ledgerPath
        ledgerSha256 = [string]$authorityResult.ledgerSha256
        ledgerPayloadSha256 = [string]$authorityResult.ledgerPayloadSha256
        analyzedInputsSha256 = [string]$authorityResult.analyzedInputsSha256
        authorityCodeSha256 = [string]$authorityResult.authorityCodeSha256
        publicKeySha256 = $publicKeySha256
        authorityCount = 1
        replayCount = 1
        factGroups = @($authorityResult.factGroups)
    }
    $receipt = New-EdgeSignedAuthorityReceipt -Payload $payload -PrivateKey $privateKey
    $receiptRaw = ConvertTo-EdgeCanonicalJson $receipt
    $receiptSchemaPath = Join-Path $authorityRoot 'eng/edge-plugin-contract-authority-receipt.schema.json'
    $receiptBytes = [Text.UTF8Encoding]::new($false).GetBytes($receiptRaw)
    [void](Assert-EdgeStrictJson -RawBytes $receiptBytes -SchemaPath $receiptSchemaPath `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-RECEIPT-001' -RequireCanonical)
    [void](New-Item -ItemType Directory -Path (Split-Path $receiptFullPath -Parent) -Force)
    $partialReceiptPath = "$receiptFullPath.partial-$([string]$request.runId)"
    [IO.File]::WriteAllText($partialReceiptPath, $receiptRaw, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $partialReceiptPath -Destination $receiptFullPath -Force
    $totalStopwatch.Stop()
    $descriptor = [pscustomobject][ordered]@{
        schemaVersion = 1
        runId = [string]$request.runId
        challengeBase64 = [string]$request.challengeBase64
        coordinatorPid = [int]$coordinatorProcess.Id
        processStartUtc = $coordinatorStartUtc
        sourceRepositoryRoot = [IO.Path]::GetFullPath([string]$request.sourceRepositoryRoot)
        authorityRepositoryRoot = $authorityRoot
        authorityHead = [string]$request.authorityHead
        authorityTree = [string]$request.authorityTree
        formalFinalHead = [string]$request.formalFinalHead
        formalFinalTree = [string]$request.formalFinalTree
        sourceBaseHead = [string]$request.sourceBaseHead
        sourceBaseTree = [string]$request.sourceBaseTree
        sourceDirtyManifestSha256 = [string]$request.sourceDirtyManifestSha256
        ephemeralSnapshotHead = [string]$request.ephemeralSnapshotHead
        ephemeralSnapshotTree = [string]$request.ephemeralSnapshotTree
        implementationHead = [string]$request.implementationHead
        implementationTree = [string]$request.implementationTree
        receiptPath = [string]$request.receiptPath
        receiptSha256 = Get-EdgeSha256File $receiptFullPath
        publicKeySpkiBase64 = $publicKeyBase64
        publicKeySha256 = $publicKeySha256
        authorityCount = 1
        replayCount = 1
        authorityElapsedMilliseconds = [long]$authorityLog.elapsedMilliseconds
        replayElapsedMilliseconds = [long]$replayLog.elapsedMilliseconds
        totalElapsedMilliseconds = [long]$totalStopwatch.ElapsedMilliseconds
    }
    $exitCode = 0
}
catch {
    [Console]::Error.WriteLine("EDGE-SPLIT-AUTHORITY-COORDINATOR-001 $($_.Exception.Message)")
    $exitCode = 1
}
finally {
    Stop-AuthorityChildTree $authorityChild
    Stop-AuthorityChildTree $replayChild
    if ($null -ne $privateKey) { $privateKey.Dispose() }
    if (-not [string]::IsNullOrWhiteSpace($authorityRoot) -and
        -not [string]::IsNullOrWhiteSpace($runRoot) -and
        $null -ne $request) {
        try {
            foreach ($path in @($validatorWorktree, $replayWorktree)) {
                if (-not [string]::IsNullOrWhiteSpace($path)) {
                    Remove-ExplicitAuthorityWorktree -RepositoryRoot $authorityRoot -RunRoot $runRoot `
                        -WorktreePath $path -RunId ([string]$request.runId) -ExpectedMarker $marker
                }
            }
        }
        catch {
            $cleanupFailure = $_
            [Console]::Error.WriteLine("EDGE-SPLIT-AUTHORITY-CLEANUP-001 $($_.Exception.Message)")
            $exitCode = 1
        }
        if ($null -eq $cleanupFailure -and (Test-Path -LiteralPath $runRoot -PathType Container)) {
            $validatedRunRoot = Assert-RunScopedPath -RunRoot $runRoot -Candidate $runRoot `
                -RunId ([string]$request.runId) -Label 'run root' -AllowIdentity
            $runItem = Get-Item -LiteralPath $validatedRunRoot -Force
            $markerPath = Join-Path $validatedRunRoot '.edge-authority-run.json'
            $markerItem = Get-Item -LiteralPath $markerPath -Force
            [byte[]]$expectedRunMarkerBytes = @(ConvertTo-EdgeCanonicalBytes $marker)
            if (($runItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                -not [string]::IsNullOrWhiteSpace([string]$runItem.LinkTarget) -or
                ($markerItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                -not [string]::IsNullOrWhiteSpace([string]$markerItem.LinkTarget) -or
                -not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
                    [IO.File]::ReadAllBytes($markerPath), $expectedRunMarkerBytes)) {
                throw 'EDGE-SPLIT-AUTHORITY-CLEANUP-001 run root/marker changed before recursive cleanup.'
            }
            Remove-Item -LiteralPath $validatedRunRoot -Recurse -Force
        }
    }
}

if ($exitCode -eq 0 -and $null -ne $descriptor) {
    $descriptorSchemaPath = Join-Path $authorityRoot 'eng/edge-plugin-contract-authority-descriptor.schema.json'
    $descriptorRaw = ConvertTo-EdgeCanonicalJson $descriptor
    $descriptorBytes = [Text.UTF8Encoding]::new($false).GetBytes($descriptorRaw)
    [void](Assert-EdgeStrictJson -RawBytes $descriptorBytes -SchemaPath $descriptorSchemaPath `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-DESCRIPTOR-001' -RequireCanonical)
    $stdout = [Console]::OpenStandardOutput()
    $stdout.Write($descriptorBytes, 0, $descriptorBytes.Length)
    $stdout.Flush()
}
exit $exitCode
