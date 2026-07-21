[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$canonicalLedgerRelativePath = 'eng/baselines/edge-plugin-contract-ledger.json'
$canonicalLedgerPath = Join-Path $RepositoryRoot $canonicalLedgerRelativePath
$formalResultSchemaPath = Join-Path $RepositoryRoot 'eng/edge-plugin-contract-formal-validation-result.schema.json'
$staticGuardModulePath = Join-Path $PSScriptRoot 'EdgePluginContractStaticGuard.psm1'
$protocolModulePath = Join-Path $PSScriptRoot 'EdgePluginContractLedger.Protocol.psm1'

Import-Module $staticGuardModulePath -Force
$staticGuardResult = Assert-EdgePluginContractStaticGuard -RepositoryRoot $RepositoryRoot -PassThru
$formalSourceBytes = [IO.File]::ReadAllBytes(
    (Join-Path $PSScriptRoot 'Invoke-EdgePluginContractFormalValidation.ps1'))
$formalSourceSha256 = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData($formalSourceBytes)).ToLowerInvariant()
if ($staticGuardResult.schemaVersion -ne 1 -or
    $staticGuardResult.owner -cne 'scripts/tests/EdgePluginContractStaticGuard.psm1' -or
    $staticGuardResult.scope -cne 'production' -or
    $staticGuardResult.passed -ne $true -or
    $staticGuardResult.sourceCount -ne 11 -or
    $staticGuardResult.sourceDigests.formal -cne $formalSourceSha256) {
    throw 'EDGE-SPLIT-AUTHORITY-FORMAL-STATIC formal entry rejected an invalid canonical static-guard result.'
}

Import-Module $protocolModulePath -Force
Assert-EdgeAuthorityGitEnvironment
$formalPowerShellPath = Resolve-EdgeFixedExecutable ([Environment]::ProcessPath)
$formalGitCommand = @(Get-Command git -CommandType Application -ErrorAction Stop)[0]
$formalGitPath = Assert-EdgeAuthorityFinalGitExecutablePath (
    [IO.Path]::GetFullPath([string]$formalGitCommand.Source))
$formalEmptyGitConfigPath = Assert-EdgeAuthorityEmptyGitConfig $RepositoryRoot
$formalGitChildEnvironment = New-EdgeAuthorityGitChildEnvironment `
    $formalEmptyGitConfigPath $formalGitPath
$formalPinnedPath = Get-EdgeAuthorityPinnedPath $formalGitPath
$formalMaximumCapturedBytes = 16777216

function ConvertFrom-FormalUtf8 {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes,
        [Parameter(Mandatory)][string]$ErrorCode
    )
    try { return [Text.UTF8Encoding]::new($false, $true).GetString($Bytes) }
    catch { throw "$ErrorCode native output is not strict UTF-8." }
}

function Invoke-FormalProcess {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [ValidateRange(1, 1800)][int]$TimeoutSeconds,
        [AllowNull()][byte[]]$InputBytes,
        [AllowNull()][Collections.IDictionary]$Environment
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardInput = $null -ne $InputBytes
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add([string]$argument) }
    if ($null -ne $Environment) {
        foreach ($name in $Environment.Keys) {
            $startInfo.Environment[[string]$name] = [string]$Environment[$name]
        }
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PROCESS child process did not start.'
        }
        $childProcessId = [int]$process.Id
        $processStartUtc = $process.StartTime.ToUniversalTime().ToString('O')
        $stdoutTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync(
            $process.StandardOutput.BaseStream, $script:formalMaximumCapturedBytes)
        $stderrTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync(
            $process.StandardError.BaseStream, $script:formalMaximumCapturedBytes)
        if ($null -ne $InputBytes) {
            $process.StandardInput.BaseStream.Write($InputBytes, 0, $InputBytes.Length)
            $process.StandardInput.BaseStream.Flush()
            $process.StandardInput.Close()
        }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
        while (-not $process.WaitForExit(100)) {
            if ($stdoutTask.IsFaulted -or $stderrTask.IsFaulted -or
                [DateTimeOffset]::UtcNow -ge $deadline) {
                try { $process.Kill($true) } catch { }
                [void]$process.WaitForExit(30000)
                throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PROCESS bounded child output/deadline failed.'
            }
        }
        try {
            $capture = Wait-EdgeBoundedCaptureTasks $stdoutTask $stderrTask $deadline `
                'EDGE-SPLIT-AUTHORITY-FORMAL-PROCESS'
        }
        catch {
            throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PROCESS child output was unbounded, held open, or incomplete.'
        }
        return [pscustomobject][ordered]@{
            exitCode = [int]$process.ExitCode
            pid = $childProcessId
            processStartUtc = $processStartUtc
            stdoutBytes = [byte[]]$capture.stdoutBytes
            stderrBytes = [byte[]]$capture.stderrBytes
        }
    }
    finally { $process.Dispose() }
}

function Invoke-FormalGitBytes {
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments)
    $fixedArguments = [string[]]@(
        @('-C', $script:RepositoryRoot, '-c',
            "core.hooksPath=$script:formalEmptyGitConfigPath") + $Arguments)
    $result = Invoke-FormalProcess -FileName $script:formalGitPath `
        -Arguments $fixedArguments -WorkingDirectory $script:RepositoryRoot `
        -TimeoutSeconds 300 -InputBytes $null -Environment $script:formalGitChildEnvironment
    if ($result.exitCode -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-FORMAL-GIT read-only git command failed; stderrSha256=$(Get-EdgeSha256Bytes $result.stderrBytes)."
    }
    return [byte[]]$result.stdoutBytes
}

function Invoke-FormalGitText {
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments)
    [byte[]]$bytes = @(Invoke-FormalGitBytes $Arguments)
    return (ConvertFrom-FormalUtf8 $bytes 'EDGE-SPLIT-AUTHORITY-FORMAL-GIT').Trim()
}

function Get-FormalLocalGitConfigDigest {
    $configPathValue = Invoke-FormalGitText @('rev-parse', '--git-path', 'config')
    $configPath = if ([IO.Path]::IsPathRooted($configPathValue)) {
        [IO.Path]::GetFullPath($configPathValue)
    }
    else { [IO.Path]::GetFullPath((Join-Path $script:RepositoryRoot $configPathValue)) }
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-GIT-CONFIG local Git config is missing.'
    }
    $item = Get-Item -LiteralPath $configPath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-GIT-CONFIG local Git config is indirect.'
    }
    return Get-EdgeSha256File $configPath
}

function Get-FormalRepositoryState {
    [byte[]]$statusBytes = @(Invoke-FormalGitBytes @(
            'status', '--porcelain=v1', '-z', '--untracked-files=all'))
    [byte[]]$indexInventoryBytes = @(Invoke-FormalGitBytes @(
            'ls-files', '--stage', '-z'))
    [byte[]]$ledgerBytes = @([IO.File]::ReadAllBytes($script:canonicalLedgerPath))
    return [pscustomobject][ordered]@{
        head = Invoke-FormalGitText @('rev-parse', 'HEAD')
        tree = Invoke-FormalGitText @('rev-parse', 'HEAD^{tree}')
        statusBytes = $statusBytes
        indexInventoryBytes = $indexInventoryBytes
        ledgerBytes = $ledgerBytes
        ledgerSha256 = Get-EdgeSha256Bytes $ledgerBytes
        localGitConfigSha256 = Get-FormalLocalGitConfigDigest
    }
}

function Assert-FormalStateEqual {
    param(
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)]$Actual,
        [Parameter(Mandatory)][string]$ErrorCode
    )
    if ([string]$Actual.head -cne [string]$Expected.head -or
        [string]$Actual.tree -cne [string]$Expected.tree -or
        [string]$Actual.ledgerSha256 -cne [string]$Expected.ledgerSha256 -or
        [string]$Actual.localGitConfigSha256 -cne [string]$Expected.localGitConfigSha256 -or
        -not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
            [byte[]]$Actual.statusBytes, [byte[]]$Expected.statusBytes) -or
        -not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
            [byte[]]$Actual.indexInventoryBytes, [byte[]]$Expected.indexInventoryBytes) -or
        -not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
            [byte[]]$Actual.ledgerBytes, [byte[]]$Expected.ledgerBytes)) {
        throw "$ErrorCode formal repository state changed."
    }
}

function Remove-FormalUnmarkedOuterRunRoot {
    param(
        [Parameter(Mandatory)][string]$TemporaryRoot,
        [Parameter(Mandatory)][string]$OuterRunRoot,
        [Parameter(Mandatory)][string]$PartialMarkerPath,
        [Parameter(Mandatory)][string]$RunId
    )
    if ($RunId -notmatch '^[0-9a-f]{32}$') {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP unmarked runId is malformed.'
    }
    $expectedOuter = Join-Path ([IO.Path]::GetFullPath($TemporaryRoot)) `
        "edge-formal-authority-$RunId"
    $expectedPartial = Join-Path $expectedOuter `
        ".edge-formal-authority-run.json.partial-$RunId"
    if (-not (Test-EdgePathIdentity $expectedOuter $OuterRunRoot) -or
        -not (Test-EdgePathIdentity $expectedPartial $PartialMarkerPath)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP unmarked cleanup target differs from the exact current-process run path.'
    }
    if (-not (Test-Path -LiteralPath $expectedOuter -PathType Container)) { return }
    $outerItem = Get-Item -LiteralPath $expectedOuter -Force
    if (($outerItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$outerItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP unmarked outer run root became indirect.'
    }
    $children = @(Get-ChildItem -LiteralPath $expectedOuter -Force)
    if ($children.Count -gt 1 -or
        ($children.Count -eq 1 -and
            -not (Test-EdgePathIdentity ([string]$children[0].FullName) $expectedPartial))) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP unmarked outer run root contains an unknown item.'
    }
    if ($children.Count -eq 1) {
        $partialItem = $children[0]
        if (-not (Test-Path -LiteralPath $expectedPartial -PathType Leaf) -or
            ($partialItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$partialItem.LinkTarget)) {
            throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP partial marker is not one direct regular file.'
        }
        Remove-Item -LiteralPath $expectedPartial -Force
    }
    if (@(Get-ChildItem -LiteralPath $expectedOuter -Force).Count -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP unmarked outer run root did not become empty.'
    }
    Remove-Item -LiteralPath $expectedOuter -Force
    if (Test-Path -LiteralPath $expectedOuter) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP unmarked outer run root survived non-recursive cleanup.'
    }
}

function Assert-FormalReceiptIdentity {
    param(
        [Parameter(Mandatory)][string]$ReceiptPath,
        [Parameter(Mandatory)][string]$ExpectedSha256
    )
    $expectedPath = Resolve-EdgeRepositoryPath `
        $script:RepositoryRoot $script:receiptRelativePath
    if (-not (Test-EdgePathIdentity $expectedPath $ReceiptPath) -or
        -not (Test-Path -LiteralPath $expectedPath -PathType Leaf)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-RECEIPT final receipt path/identity differs.'
    }
    $receiptItem = Get-Item -LiteralPath $expectedPath -Force
    if (($receiptItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$receiptItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-RECEIPT final receipt must be one direct regular file.'
    }
    $actualSha256 = Get-EdgeSha256File $expectedPath
    if ($actualSha256 -notmatch '^[0-9a-f]{64}$' -or
        $ExpectedSha256 -notmatch '^[0-9a-f]{64}$' -or
        -not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
            [Convert]::FromHexString($actualSha256),
            [Convert]::FromHexString($ExpectedSha256))) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-RECEIPT final receipt digest differs from the direct-child descriptor.'
    }
    return $actualSha256
}

function Assert-FormalValidationPreconditions {
    $rootItem = Get-Item -LiteralPath $script:RepositoryRoot -Force
    if (-not $rootItem.PSIsContainer -or
        ($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$rootItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION repository root must be one direct directory.'
    }
    $topLevel = Invoke-FormalGitText @('rev-parse', '--show-toplevel')
    if (-not (Test-EdgePathIdentity $topLevel $script:RepositoryRoot)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION script root is not the exact Git worktree root.'
    }
    $state = Get-FormalRepositoryState
    if ($state.statusBytes.Length -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION worktree must be completely clean.'
    }
    [void](Invoke-FormalGitBytes @(
            'diff', '--cached', '--quiet', '--no-ext-diff', 'HEAD', '--'))
    $ledger = Read-EdgeGeneratorLedger -Path $script:canonicalLedgerPath `
        -SchemaPath (Join-Path $script:RepositoryRoot 'eng/edge-plugin-contract-ledger.schema.json') `
        -Name 'formal-canonical'
    $implementationHead = [string]$ledger.sourceState.head
    $implementationTree = [string]$ledger.sourceState.tree
    if (-not [bool]$ledger.sourceState.cleanObserved -or
        @($ledger.sourceState.dirtyPaths).Count -ne 0 -or
        @($ledger.sourceState.excludedPaths).Count -ne 1 -or
        [string]$ledger.sourceState.excludedPaths[0] -cne $script:canonicalLedgerRelativePath) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION canonical ledger source-state cleanliness/exclusion binding differs.'
    }
    if ((Invoke-FormalGitText @('rev-parse', "$implementationHead^{tree}")) -cne $implementationTree) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION ledger implementation HEAD/tree binding is invalid.'
    }
    if ((Invoke-FormalGitText @('rev-list', '--count', "$implementationHead..$($state.head)")) -cne '1' -or
        (Invoke-FormalGitText @('rev-parse', "$($state.head)^")) -cne $implementationHead) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION final evidence commit must be the unique direct child of implementation HEAD.'
    }
    $parentRow = (Invoke-FormalGitText @('rev-list', '--parents', '-n', '1', [string]$state.head)) -split ' '
    if ($parentRow.Count -ne 2 -or $parentRow[0] -cne [string]$state.head -or
        $parentRow[1] -cne $implementationHead) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION final evidence commit parent inventory differs.'
    }
    [byte[]]$diffBytes = @(Invoke-FormalGitBytes @(
            'diff-tree', '--no-commit-id', '--name-only', '-r', '-z',
            $implementationHead, [string]$state.head))
    $diffPaths = @((ConvertFrom-FormalUtf8 $diffBytes 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION').Split(
            [char]0, [StringSplitOptions]::RemoveEmptyEntries))
    if ($diffPaths.Count -ne 1 -or $diffPaths[0] -cne $script:canonicalLedgerRelativePath) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION final evidence commit must change only the canonical ledger.'
    }
    $lsTree = Invoke-FormalGitText @('ls-tree', [string]$state.head, '--', $script:canonicalLedgerRelativePath)
    if ($lsTree -notmatch '^100644 blob [0-9a-f]{40}\teng/baselines/edge-plugin-contract-ledger\.json$') {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION canonical ledger committed mode/blob entry differs.'
    }
    [byte[]]$committedLedgerBytes = @(Invoke-FormalGitBytes @(
            'show', "$($state.head):$($script:canonicalLedgerRelativePath)"))
    if (-not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
            $committedLedgerBytes, [byte[]]$state.ledgerBytes)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION committed ledger blob differs from worktree bytes.'
    }
    return [pscustomobject][ordered]@{
        state = $state
        ledger = $ledger
        implementationHead = $implementationHead
        implementationTree = $implementationTree
    }
}

$initialPreconditions = Assert-FormalValidationPreconditions
$runId = [Guid]::NewGuid().ToString('N')
$challengeBase64 = [Convert]::ToBase64String(
    [Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryItem = Get-Item -LiteralPath $temporaryRoot -Force
if (-not $temporaryItem.PSIsContainer -or
    ($temporaryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
    -not [string]::IsNullOrWhiteSpace([string]$temporaryItem.LinkTarget)) {
    throw 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION temporary root must be one direct directory.'
}
$outerRunRoot = Join-Path $temporaryRoot "edge-formal-authority-$runId"
$coordinatorRunRoot = Join-Path $outerRunRoot "coordinator-$runId"
$validatorWorktreePath = Join-Path $coordinatorRunRoot "validator-$runId"
$replayWorktreePath = Join-Path $coordinatorRunRoot "replay-$runId"
$outerMarkerPath = Join-Path $outerRunRoot '.edge-formal-authority-run.json'
$partialOuterMarkerPath = "$outerMarkerPath.partial-$runId"
$receiptRelativePath = ".artifacts/edge-plugin-contract-authority/formal-$runId-receipt.json"
$receiptPath = Resolve-EdgeRepositoryPath $RepositoryRoot $receiptRelativePath
$outerMarker = [pscustomobject][ordered]@{
    schemaVersion = 3
    mode = 'formal-clean'
    runId = $runId
    sourceRepositoryRoot = $RepositoryRoot
    authorityRepositoryRoot = $RepositoryRoot
    outerRunRoot = $outerRunRoot
    coordinatorRunRoot = $coordinatorRunRoot
    validatorWorktreePath = $validatorWorktreePath
    replayWorktreePath = $replayWorktreePath
    fixedGitExecutablePath = $formalGitPath
    pinnedPathSha256 = Get-EdgeSha256Text $formalPinnedPath
}
$ledger = $initialPreconditions.ledger
$request = [pscustomobject][ordered]@{
    schemaVersion = 1
    mode = 'formal-clean'
    runId = $runId
    challengeBase64 = $challengeBase64
    sourceRepositoryRoot = $RepositoryRoot
    authorityRepositoryRoot = $RepositoryRoot
    authorityHead = [string]$initialPreconditions.state.head
    authorityTree = [string]$initialPreconditions.state.tree
    formalFinalHead = [string]$initialPreconditions.state.head
    formalFinalTree = [string]$initialPreconditions.state.tree
    sourceBaseHead = [string]$initialPreconditions.state.head
    sourceBaseTree = [string]$initialPreconditions.state.tree
    sourceDirtyManifestSha256 = ''
    ephemeralSnapshotHead = ''
    ephemeralSnapshotTree = ''
    implementationHead = [string]$initialPreconditions.implementationHead
    implementationTree = [string]$initialPreconditions.implementationTree
    ledgerPath = $canonicalLedgerRelativePath
    receiptPath = $receiptRelativePath
    runRoot = $coordinatorRunRoot
    validatorWorktreePath = $validatorWorktreePath
    replayWorktreePath = $replayWorktreePath
    pluginProject = [string]$ledger.msbuildCompilation.projectPath
    configuration = [string]$ledger.msbuildCompilation.configuration
    currentBatch = [string]$ledger.batchId
    viewIdsAssemblyPath = [string]$ledger.msbuildCompilation.viewIdsAssemblyPath
    viewIdsTypeName = [string]$ledger.msbuildCompilation.viewIdsTypeName
    timeoutSeconds = 900
}
$requestBytes = ConvertTo-EdgeCanonicalBytes $request
[void](Assert-EdgeStrictJson -RawBytes $requestBytes `
    -SchemaPath (Join-Path $RepositoryRoot 'eng/edge-plugin-contract-authority-request.schema.json') `
    -ErrorCode 'EDGE-SPLIT-AUTHORITY-FORMAL-REQUEST' -RequireCanonical)
$confirmedPreconditions = Assert-FormalValidationPreconditions
Assert-FormalStateEqual -Expected $initialPreconditions.state `
    -Actual $confirmedPreconditions.state `
    -ErrorCode 'EDGE-SPLIT-AUTHORITY-FORMAL-PRECONDITION'

$failure = $null
$descriptor = $null
$markerEstablished = $false
$outerCreatedByCurrentProcess = $false
try {
    if (Test-Path -LiteralPath $outerRunRoot) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-OWNERSHIP unique formal run root already exists.'
    }
    [void](New-Item -ItemType Directory -Path $outerRunRoot)
    $outerCreatedByCurrentProcess = $true
    foreach ($reservedPath in @($coordinatorRunRoot, $validatorWorktreePath, $replayWorktreePath)) {
        if (Test-Path -LiteralPath $reservedPath) {
            throw 'EDGE-SPLIT-AUTHORITY-FORMAL-OWNERSHIP reserved child path was not absent.'
        }
    }
    [IO.File]::WriteAllBytes(
        $partialOuterMarkerPath, (ConvertTo-EdgeCanonicalBytes $outerMarker))
    Move-Item -LiteralPath $partialOuterMarkerPath -Destination $outerMarkerPath
    $markerEstablished = $true
    $coordinatorEnvironment = New-EdgeAuthorityCoordinatorParentEnvironment `
        -OuterMarker $outerMarker -ParentMarkerPath $outerMarkerPath `
        -FixedGitExecutablePath $formalGitPath -PinnedPath $formalPinnedPath
    $coordinatorResult = Invoke-FormalProcess -FileName $formalPowerShellPath `
        -WorkingDirectory $RepositoryRoot `
        -Arguments @('-NoLogo', '-NoProfile', '-File',
            (Join-Path $PSScriptRoot 'Invoke-EdgePluginContractAuthorityCoordinator.ps1')) `
        -TimeoutSeconds 960 -InputBytes $requestBytes -Environment $coordinatorEnvironment
    if ($coordinatorResult.exitCode -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-FORMAL-COORDINATOR coordinator failed; stderrSha256=$(Get-EdgeSha256Bytes $coordinatorResult.stderrBytes)."
    }
    $descriptorExpected = [pscustomobject][ordered]@{
        runId = $runId
        challengeBase64 = $challengeBase64
        coordinatorPid = [int]$coordinatorResult.pid
        processStartUtc = [string]$coordinatorResult.processStartUtc
        sourceRepositoryRoot = $RepositoryRoot
        authorityRepositoryRoot = $RepositoryRoot
        authorityHead = [string]$initialPreconditions.state.head
        authorityTree = [string]$initialPreconditions.state.tree
        formalFinalHead = [string]$initialPreconditions.state.head
        formalFinalTree = [string]$initialPreconditions.state.tree
        sourceBaseHead = [string]$initialPreconditions.state.head
        sourceBaseTree = [string]$initialPreconditions.state.tree
        sourceDirtyManifestSha256 = ''
        ephemeralSnapshotHead = ''
        ephemeralSnapshotTree = ''
        implementationHead = [string]$initialPreconditions.implementationHead
        implementationTree = [string]$initialPreconditions.implementationTree
        receiptPath = $receiptRelativePath
    }
    $descriptor = Assert-EdgeAuthorityDescriptor `
        -RawBytes $coordinatorResult.stdoutBytes `
        -SchemaPath (Join-Path $RepositoryRoot 'eng/edge-plugin-contract-authority-descriptor.schema.json') `
        -Expected $descriptorExpected -ReceiptFullPath $receiptPath
    [void](Assert-EdgeAuthorityReceipt `
        -RepositoryRoot $RepositoryRoot `
        -LedgerPath $canonicalLedgerRelativePath `
        -ReceiptPath $receiptPath `
        -PublicKeySpkiBase64 ([string]$descriptor.publicKeySpkiBase64) `
        -ExpectedRunId $runId `
        -ExpectedChallengeBase64 $challengeBase64 `
        -ExpectedSourceRepositoryRoot $RepositoryRoot `
        -ExpectedAuthorityHead ([string]$initialPreconditions.state.head) `
        -ExpectedAuthorityTree ([string]$initialPreconditions.state.tree) `
        -ExpectedFormalFinalHead ([string]$initialPreconditions.state.head) `
        -ExpectedFormalFinalTree ([string]$initialPreconditions.state.tree) `
        -ExpectedSourceBaseHead ([string]$initialPreconditions.state.head) `
        -ExpectedSourceBaseTree ([string]$initialPreconditions.state.tree) `
        -ExpectedSourceDirtyManifestSha256 '' `
        -ExpectedEphemeralSnapshotHead '' `
        -ExpectedEphemeralSnapshotTree '' `
        -ExpectedImplementationHead ([string]$initialPreconditions.implementationHead) `
        -ExpectedImplementationTree ([string]$initialPreconditions.implementationTree) `
        -RequireFormal)
    $fastEnvironment = New-EdgeAuthorityGitChildEnvironment `
        $formalEmptyGitConfigPath $formalGitPath
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_AUTHORITY_RECEIPT = $receiptRelativePath
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_AUTHORITY_PUBLIC_KEY = [string]$descriptor.publicKeySpkiBase64
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_AUTHORITY_RUN_ID = $runId
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_AUTHORITY_CHALLENGE = $challengeBase64
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_AUTHORITY_SOURCE_ROOT = $RepositoryRoot
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_AUTHORITY_HEAD = [string]$initialPreconditions.state.head
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_AUTHORITY_TREE = [string]$initialPreconditions.state.tree
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_FORMAL_FINAL_HEAD = [string]$initialPreconditions.state.head
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_FORMAL_FINAL_TREE = [string]$initialPreconditions.state.tree
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_SOURCE_BASE_HEAD = [string]$initialPreconditions.state.head
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_SOURCE_BASE_TREE = [string]$initialPreconditions.state.tree
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_SOURCE_DIRTY_MANIFEST_SHA256 = ''
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_EPHEMERAL_SNAPSHOT_HEAD = ''
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_EPHEMERAL_SNAPSHOT_TREE = ''
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_IMPLEMENTATION_HEAD = [string]$initialPreconditions.implementationHead
    $fastEnvironment.EDGE_PLUGIN_CONTRACT_IMPLEMENTATION_TREE = [string]$initialPreconditions.implementationTree
    $fastResult = Invoke-FormalProcess -FileName $formalPowerShellPath `
        -WorkingDirectory $RepositoryRoot `
        -Arguments @('-NoLogo', '-NoProfile', '-File',
            (Join-Path $PSScriptRoot 'Test-EdgePluginContractLedger.ps1'),
            '-RepositoryRoot', $RepositoryRoot,
            '-LedgerPath', $canonicalLedgerRelativePath,
            '-RequireAuthorityReceipt', '-RequireFormalAuthorityReceipt') `
        -TimeoutSeconds 600 -InputBytes $null -Environment $fastEnvironment
    if ($fastResult.exitCode -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-FORMAL-CONSUMER fast formal consumer failed; stderrSha256=$(Get-EdgeSha256Bytes $fastResult.stderrBytes)."
    }
    $fastOutput = ConvertFrom-FormalUtf8 $fastResult.stdoutBytes 'EDGE-SPLIT-AUTHORITY-FORMAL-CONSUMER'
    if (@($fastOutput -split "`r?`n" | Where-Object {
                $_ -ceq "Edge plugin contract ledger fast receipt passed: run=$runId."
            }).Count -ne 1) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CONSUMER exact formal fast-consumer marker is missing or duplicated.'
    }
    $postValidationState = Get-FormalRepositoryState
    Assert-FormalStateEqual -Expected $initialPreconditions.state -Actual $postValidationState `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-FORMAL-POSTSTATE'
}
catch { $failure = $_ }
finally {
    try {
        if ($markerEstablished) {
            Remove-EdgeFormalAuthorityRunState `
                -GitExecutablePath $formalGitPath `
                -AuthorityRepositoryRoot $RepositoryRoot `
                -OuterRunRoot $outerRunRoot `
                -RunRoot $coordinatorRunRoot `
                -ValidatorWorktreePath $validatorWorktreePath `
                -ReplayWorktreePath $replayWorktreePath `
                -RunId $runId `
                -MarkerPath $outerMarkerPath `
                -MarkerExpected $outerMarker
        }
        elseif ($outerCreatedByCurrentProcess) {
            Remove-FormalUnmarkedOuterRunRoot `
                -TemporaryRoot $temporaryRoot `
                -OuterRunRoot $outerRunRoot `
                -PartialMarkerPath $partialOuterMarkerPath `
                -RunId $runId
        }
        elseif (Test-Path -LiteralPath $outerRunRoot) {
            throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP a pre-existing formal run root must never be removed.'
        }
    }
    catch {
        if ($null -eq $failure) { $failure = $_ }
    }
}
if ($null -ne $failure) { throw $failure }
if (Test-Path -LiteralPath $outerRunRoot) {
    throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP formal temporary run root survived cleanup.'
}
$finalState = Get-FormalRepositoryState
Assert-FormalStateEqual -Expected $initialPreconditions.state -Actual $finalState `
    -ErrorCode 'EDGE-SPLIT-AUTHORITY-FORMAL-POSTSTATE'
[void](Assert-FormalReceiptIdentity `
    -ReceiptPath $receiptPath -ExpectedSha256 ([string]$descriptor.receiptSha256))
[void](Assert-EdgeAuthorityReceipt `
    -RepositoryRoot $RepositoryRoot `
    -LedgerPath $canonicalLedgerRelativePath `
    -ReceiptPath $receiptPath `
    -PublicKeySpkiBase64 ([string]$descriptor.publicKeySpkiBase64) `
    -ExpectedRunId $runId `
    -ExpectedChallengeBase64 $challengeBase64 `
    -ExpectedSourceRepositoryRoot $RepositoryRoot `
    -ExpectedAuthorityHead ([string]$initialPreconditions.state.head) `
    -ExpectedAuthorityTree ([string]$initialPreconditions.state.tree) `
    -ExpectedFormalFinalHead ([string]$initialPreconditions.state.head) `
    -ExpectedFormalFinalTree ([string]$initialPreconditions.state.tree) `
    -ExpectedSourceBaseHead ([string]$initialPreconditions.state.head) `
    -ExpectedSourceBaseTree ([string]$initialPreconditions.state.tree) `
    -ExpectedSourceDirtyManifestSha256 '' `
    -ExpectedEphemeralSnapshotHead '' `
    -ExpectedEphemeralSnapshotTree '' `
    -ExpectedImplementationHead ([string]$initialPreconditions.implementationHead) `
    -ExpectedImplementationTree ([string]$initialPreconditions.implementationTree) `
    -RequireFormal)
$finalReceiptSha256 = Assert-FormalReceiptIdentity `
    -ReceiptPath $receiptPath -ExpectedSha256 ([string]$descriptor.receiptSha256)
$formalResult = [pscustomobject][ordered]@{
    schemaVersion = 1
    ruleId = 'EDGE-SPLIT-LEDGER-001'
    mode = 'formal-clean'
    formal = $true
    passed = $true
    completedUtc = [DateTime]::UtcNow.ToString('O')
    formalFinalHead = [string]$initialPreconditions.state.head
    formalFinalTree = [string]$initialPreconditions.state.tree
    implementationHead = [string]$initialPreconditions.implementationHead
    implementationTree = [string]$initialPreconditions.implementationTree
    ledgerPath = $canonicalLedgerRelativePath
    ledgerSha256 = [string]$initialPreconditions.state.ledgerSha256
    receiptPath = $receiptRelativePath
    receiptSha256 = $finalReceiptSha256
    publicKeySha256 = [string]$descriptor.publicKeySha256
    authorityCount = 1
    replayCount = 1
    descriptorPidBoundToDirectChild = $true
    descriptorStartBoundToDirectChild = $true
    fastConsumerRequireAuthorityReceipt = $true
    fastConsumerRequireFormalAuthorityReceipt = $true
    postStateStable = $true
    cleanupComplete = $true
}
$formalResultBytes = ConvertTo-EdgeCanonicalBytes $formalResult
[void](Assert-EdgeStrictJson -RawBytes $formalResultBytes `
    -SchemaPath $formalResultSchemaPath `
    -ErrorCode 'EDGE-SPLIT-AUTHORITY-FORMAL-RESULT' -RequireCanonical)
Write-Output (ConvertTo-EdgeCanonicalJson $formalResult)
