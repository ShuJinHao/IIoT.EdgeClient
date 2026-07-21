[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$PluginProject = 'src/Modules/IIoT.Edge.Module.Homogenization/IIoT.Edge.Module.Homogenization.csproj',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidateSet('EDGE-SPLIT-000', 'EDGE-SPLIT-010', 'EDGE-SPLIT-020', 'EDGE-SPLIT-030', 'EDGE-SPLIT-040', 'EDGE-SPLIT-050')]
    [string]$CurrentBatch = 'EDGE-SPLIT-000',
    [string]$ViewIdsTypeName = 'IIoT.Edge.Presentation.Navigation.PluginSystem.StandardModuleViewIds',
    [ValidateRange(60, 900)][int]$AuthorityTimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
}
else { $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot) }

$staticGuardModulePath = Join-Path $PSScriptRoot 'EdgePluginContractStaticGuard.psm1'
Import-Module $staticGuardModulePath -Force
$staticGuardResult = Assert-EdgePluginContractStaticGuard `
    -RepositoryRoot $RepositoryRoot -PassThru
$staticDevelopmentBytes = [IO.File]::ReadAllBytes(
    (Join-Path $PSScriptRoot 'Invoke-EdgePluginContractDevelopmentValidation.ps1'))
$staticDevelopmentDigest = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData($staticDevelopmentBytes)).ToLowerInvariant()
$staticFormalBytes = [IO.File]::ReadAllBytes(
    (Join-Path $PSScriptRoot 'Invoke-EdgePluginContractFormalValidation.ps1'))
$staticFormalDigest = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData($staticFormalBytes)).ToLowerInvariant()
if ($staticGuardResult.schemaVersion -ne 1 -or
    $staticGuardResult.owner -cne 'scripts/tests/EdgePluginContractStaticGuard.psm1' -or
    $staticGuardResult.scope -cne 'production' -or
    $staticGuardResult.passed -ne $true -or
    $staticGuardResult.sourceCount -ne 11 -or
    $staticGuardResult.sourceDigests.development -cne $staticDevelopmentDigest -or
    $staticGuardResult.sourceDigests.formal -cne $staticFormalDigest) {
    throw 'EDGE-SPLIT-AUTHORITY-STATIC-002 development entry rejected an invalid canonical static-guard result.'
}

$protocolModulePath = Join-Path $PSScriptRoot 'EdgePluginContractLedger.Protocol.psm1'
Import-Module $protocolModulePath -Force
Assert-EdgeAuthorityGitEnvironment
$powerShellPath = Resolve-EdgeFixedExecutable ([Environment]::ProcessPath)
$gitCommand = @(Get-Command git -CommandType Application -ErrorAction Stop)[0]
$gitPath = Resolve-EdgeFixedExecutable ([string]$gitCommand.Source)
$devMaximumCapturedBytes = 16777216
$devEmptyGitConfigPath = Assert-EdgeAuthorityEmptyGitConfig $RepositoryRoot
$devGitChildEnvironment = New-EdgeAuthorityGitChildEnvironment $devEmptyGitConfigPath $gitPath
$devPinnedPath = Get-EdgeAuthorityPinnedPath $gitPath

function Sort-DevOrdinalStrings {
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Values)
    $copy = [string[]]@($Values)
    [Array]::Sort($copy, [StringComparer]::Ordinal)
    return $copy
}

function ConvertFrom-DevUtf8 {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes,
        [Parameter(Mandatory)][string]$Code
    )
    try { return [Text.UTF8Encoding]::new($false, $true).GetString($Bytes) }
    catch { throw "$Code native output is not strict UTF-8." }
}

function Invoke-DevProcess {
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
            if ([string]::Equals([string]$name, 'TMPDIR', [StringComparison]::OrdinalIgnoreCase)) {
                throw 'EDGE-SPLIT-AUTHORITY-DEV-TEMP child environment overlay must not replace process TMPDIR.'
            }
            $startInfo.Environment[[string]$name] = [string]$Environment[$name]
        }
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw 'EDGE-SPLIT-AUTHORITY-DEV-PROCESS child process did not start.' }
        $childProcessId = [int]$process.Id
        $processStartUtc = $process.StartTime.ToUniversalTime().ToString('O')
        $stdoutTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync(
            $process.StandardOutput.BaseStream, $script:devMaximumCapturedBytes)
        $stderrTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync(
            $process.StandardError.BaseStream, $script:devMaximumCapturedBytes)
        if ($null -ne $InputBytes) {
            $process.StandardInput.BaseStream.Write($InputBytes, 0, $InputBytes.Length)
            $process.StandardInput.BaseStream.Flush()
            $process.StandardInput.Close()
        }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
        while (-not $process.WaitForExit(100)) {
            if ($stdoutTask.IsFaulted -or $stderrTask.IsFaulted) {
                try { $process.Kill($true) } catch { }
                [void]$process.WaitForExit(30000)
                throw 'EDGE-SPLIT-AUTHORITY-DEV-OUTPUT-LIMIT bounded child output exceeded 16 MiB per stream.'
            }
            if ([DateTimeOffset]::UtcNow -ge $deadline) {
                try { $process.Kill($true) } catch { }
                [void]$process.WaitForExit(30000)
                throw 'EDGE-SPLIT-AUTHORITY-DEV-TIMEOUT bounded child process timed out.'
            }
        }
        try {
            $capture = Wait-EdgeBoundedCaptureTasks $stdoutTask $stderrTask $deadline `
                'EDGE-SPLIT-AUTHORITY-DEV-OUTPUT-LIMIT'
            $stdoutBytes = $capture.stdoutBytes
            $stderrBytes = $capture.stderrBytes
        }
        catch {
            if (-not $process.HasExited) {
                try { $process.Kill($true) } catch { }
                [void]$process.WaitForExit(30000)
            }
            throw 'EDGE-SPLIT-AUTHORITY-DEV-OUTPUT-LIMIT bounded child output exceeded 16 MiB or an inherited pipe outlived the deadline.'
        }
        return [pscustomobject][ordered]@{
            exitCode = [int]$process.ExitCode
            pid = $childProcessId
            processStartUtc = $processStartUtc
            stdoutBytes = [byte[]]$stdoutBytes
            stderrBytes = [byte[]]$stderrBytes
        }
    }
    finally { $process.Dispose() }
}

function Invoke-DevGitBytes {
    param(
        [Parameter(Mandatory)][string]$GitRoot,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments
    )
    [void](Assert-EdgeAuthorityEmptyGitConfig $script:RepositoryRoot)
    $fixedArguments = [string[]]@(
        @('-c', "core.hooksPath=$script:devEmptyGitConfigPath") + $Arguments)
    $result = Invoke-DevProcess -FileName $gitPath -Arguments $fixedArguments -WorkingDirectory $GitRoot `
        -TimeoutSeconds 300 -InputBytes $null -Environment $script:devGitChildEnvironment
    if ($result.exitCode -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-DEV-GIT git command failed; stderrSha256=$(Get-EdgeSha256Bytes $result.stderrBytes)."
    }
    return [byte[]]$result.stdoutBytes
}

function Invoke-DevGitText {
    param(
        [Parameter(Mandatory)][string]$GitRoot,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Arguments
    )
    [byte[]]$bytes = @(Invoke-DevGitBytes $GitRoot $Arguments)
    return (ConvertFrom-DevUtf8 $bytes 'EDGE-SPLIT-AUTHORITY-DEV-GIT').Trim()
}

function Get-DevLocalGitConfigDigest {
    param([Parameter(Mandatory)][string]$Root)

    $configPathValue = Invoke-DevGitText $Root @('rev-parse', '--git-path', 'config')
    $configPath = if ([IO.Path]::IsPathRooted($configPathValue)) {
        [IO.Path]::GetFullPath($configPathValue)
    }
    else { Resolve-EdgeRepositoryPath $Root $configPathValue.Replace('\', '/') }
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-GIT-CONFIG snapshot local Git config is missing.'
    }
    $item = Get-Item -LiteralPath $configPath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-GIT-CONFIG snapshot local Git config is indirect.'
    }
    return Get-EdgeSha256File $configPath
}

function Resolve-DevPhysicalTempRoot {
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not (Test-Path -LiteralPath $temporaryRoot -PathType Container)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-TEMP process temporary directory is missing.'
    }
    $temporaryItem = Get-Item -LiteralPath $temporaryRoot -Force
    if (-not $temporaryItem.PSIsContainer -or
        ($temporaryItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$temporaryItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-TEMP process temporary directory must be a direct directory.'
    }

    $originalCurrentDirectory = [Environment]::CurrentDirectory
    $currentDirectoryFailure = $null
    try {
        [Environment]::CurrentDirectory = $temporaryRoot
        $physicalRoot = [IO.Path]::GetFullPath([Environment]::CurrentDirectory)
    }
    catch { $currentDirectoryFailure = $_ }
    finally {
        try {
            [Environment]::CurrentDirectory = $originalCurrentDirectory
            if (-not [string]::Equals(
                    [Environment]::CurrentDirectory, $originalCurrentDirectory,
                    [StringComparison]::Ordinal)) {
                throw 'EDGE-SPLIT-AUTHORITY-DEV-TEMP current directory restoration was not exact.'
            }
        }
        catch {
            if ($null -eq $currentDirectoryFailure) { $currentDirectoryFailure = $_ }
        }
    }
    if ($null -ne $currentDirectoryFailure) { throw $currentDirectoryFailure }

    if (-not (Test-Path -LiteralPath $physicalRoot -PathType Container)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-TEMP physical temporary directory is missing.'
    }
    $physicalItem = Get-Item -LiteralPath $physicalRoot -Force
    if (-not $physicalItem.PSIsContainer -or
        ($physicalItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$physicalItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-TEMP physical temporary directory must be a direct directory.'
    }
    return $physicalRoot
}

function Assert-DevRepositoryRoot {
    param([Parameter(Mandatory)][string]$Root)
    $fullPath = [IO.Path]::GetFullPath($Root)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-PATH repository root is missing.'
    }
    $item = Get-Item -LiteralPath $fullPath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-PATH repository root must not be a symlink/reparse point.'
    }
    $gitTopLevel = Invoke-DevGitText $fullPath @('rev-parse', '--show-toplevel')
    if (-not (Test-EdgePathIdentity $fullPath $gitTopLevel)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-PATH repository root must be the exact pinned git worktree top-level.'
    }
    return $fullPath
}

function Assert-DevSafePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$RelativePath,
        [switch]$MayBeMissing
    )
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath.Contains('\', [StringComparison]::Ordinal) -or
        [Text.RegularExpressions.Regex]::IsMatch(
            $RelativePath, '(^|/)\.\.(/|$)',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-PATH dirty manifest contains an unsafe repository path.'
    }
    if ($RelativePath -ceq '.artifacts' -or $RelativePath.StartsWith('.artifacts/', [StringComparison]::Ordinal) -or
        [Text.RegularExpressions.Regex]::IsMatch(
            $RelativePath,
            '(^|/)(?:\.env(?:\.[^/]*)?|secrets?|credentials?|logs?|cache)(?:/|$)|\.(?:log|db|db3|sqlite|sqlite3|pfx|p12|pem)$',
            [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        throw "EDGE-SPLIT-AUTHORITY-DEV-SENSITIVE dirty snapshot refuses ignored/artifact/secret/env/db/log/cache path: $RelativePath."
    }
    $fullPath = Resolve-EdgeRepositoryPath -RepositoryRoot $Root -RelativePath $RelativePath
    if (-not $MayBeMissing -and -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "EDGE-SPLIT-AUTHORITY-DEV-PATH dirty source is not a regular file: $RelativePath."
    }
    return $fullPath
}

function Get-DevCurrentFileMode {
    param([Parameter(Mandatory)][string]$Path)
    if ([OperatingSystem]::IsWindows()) { return '100644' }
    $mode = [IO.File]::GetUnixFileMode($Path)
    $executeMask = [IO.UnixFileMode]::UserExecute -bor [IO.UnixFileMode]::GroupExecute -bor [IO.UnixFileMode]::OtherExecute
    if (($mode -band $executeMask) -ne 0) {
        return '100755'
    }
    return '100644'
}

function Get-DevDirtyManifest {
    param([Parameter(Mandatory)][string]$Root)

    $rootPath = Assert-DevRepositoryRoot $Root
    $baseHead = Invoke-DevGitText $rootPath @('rev-parse', 'HEAD')
    $baseTree = Invoke-DevGitText $rootPath @('rev-parse', 'HEAD^{tree}')
    [byte[]]$trackedBytes = @(Invoke-DevGitBytes $rootPath @(
        '-c', 'core.quotePath=false', 'status', '--porcelain=v2', '-z', '--untracked-files=no'))
    [byte[]]$untrackedBytes = @(Invoke-DevGitBytes $rootPath @(
        '-c', 'core.quotePath=false', 'ls-files', '--others', '--exclude-standard', '-z'))
    $records = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $totalBytes = [long]0

    $trackedText = ConvertFrom-DevUtf8 $trackedBytes 'EDGE-SPLIT-AUTHORITY-DEV-MANIFEST'
    $trackedTokens = $trackedText.Split([char[]]@([char]0), [StringSplitOptions]::None)
    for ($index = 0; $index -lt $trackedTokens.Length; $index++) {
        $token = $trackedTokens[$index]
        if ([string]::IsNullOrEmpty($token)) { continue }
        $recordType = $token.Substring(0, 1)
        if ($recordType -notin @('1', '2')) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-MANIFEST unmerged/submodule/unknown tracked status is forbidden.'
        }
        $fieldCount = if ($recordType -ceq '1') { 9 } else { 10 }
        $fields = $token.Split([char[]]@(' '), $fieldCount, [StringSplitOptions]::None)
        if ($fields.Length -ne $fieldCount) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-MANIFEST malformed porcelain-v2 tracked record.'
        }
        $xy = [string]$fields[1]
        $submodule = [string]$fields[2]
        if ($xy.Length -ne 2 -or -not $submodule.StartsWith('N', [StringComparison]::Ordinal)) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-MANIFEST submodule/unmerged tracked state is forbidden.'
        }
        $path = [string]$fields[$fieldCount - 1]
        $oldPath = ''
        $renameScore = ''
        if ($recordType -ceq '2') {
            $renameScore = [string]$fields[8]
            $index++
            if ($index -ge $trackedTokens.Length -or [string]::IsNullOrEmpty($trackedTokens[$index])) {
                throw 'EDGE-SPLIT-AUTHORITY-DEV-MANIFEST porcelain-v2 rename lacks its original path.'
            }
            $oldPath = [string]$trackedTokens[$index]
            [void](Assert-DevSafePath -Root $rootPath -RelativePath $oldPath -MayBeMissing)
        }
        $headMode = [string]$fields[3]
        $indexMode = [string]$fields[4]
        $worktreeMode = [string]$fields[5]
        foreach ($mode in @($headMode, $indexMode, $worktreeMode)) {
            if ($mode -notmatch '^(?:000000|100644|100755)$') {
                throw 'EDGE-SPLIT-AUTHORITY-DEV-MANIFEST symlink/gitlink/special tracked modes are forbidden.'
            }
        }
        $missing = $worktreeMode -ceq '000000'
        $sourcePath = Assert-DevSafePath -Root $rootPath -RelativePath $path -MayBeMissing:$missing
        $size = [long]0
        $sha256 = ''
        if (-not $missing) {
            $sourceItem = Get-Item -LiteralPath $sourcePath -Force
            if (($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                -not [string]::IsNullOrWhiteSpace([string]$sourceItem.LinkTarget)) {
                throw 'EDGE-SPLIT-AUTHORITY-DEV-MANIFEST dirty source symlink/reparse points are forbidden.'
            }
            $size = [long]$sourceItem.Length
            if ($size -gt 67108864) { throw 'EDGE-SPLIT-AUTHORITY-DEV-LIMIT dirty file exceeds 64 MiB.' }
            $sha256 = Get-EdgeSha256File $sourcePath
            $totalBytes += $size
        }
        $changeKind = if ($recordType -ceq '2') { 'rename' }
        elseif ($headMode -ceq '000000') { 'tracked-add' }
        elseif ($missing) { 'delete' }
        elseif ($headMode -cne $worktreeMode) { 'mode-or-type-change' }
        else { 'modify' }
        $record = [pscustomobject][ordered]@{
            recordType = $recordType
            indexStatus = $xy.Substring(0, 1)
            worktreeStatus = $xy.Substring(1, 1)
            submoduleState = $submodule
            headMode = $headMode
            indexMode = $indexMode
            worktreeMode = $worktreeMode
            headObject = [string]$fields[6]
            indexObject = [string]$fields[7]
            renameScore = $renameScore
            changeKind = $changeKind
            oldPath = $oldPath
            path = $path
            size = $size
            sha256 = $sha256
        }
        if (-not $records.TryAdd($path, $record)) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-MANIFEST duplicate tracked path in dirty manifest.'
        }
    }

    $untrackedText = ConvertFrom-DevUtf8 $untrackedBytes 'EDGE-SPLIT-AUTHORITY-DEV-MANIFEST'
    foreach ($path in $untrackedText.Split([char[]]@([char]0), [StringSplitOptions]::RemoveEmptyEntries)) {
        $sourcePath = Assert-DevSafePath -Root $rootPath -RelativePath $path
        $sourceItem = Get-Item -LiteralPath $sourcePath -Force
        if (($sourceItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$sourceItem.LinkTarget)) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-MANIFEST untracked symlink/reparse points are forbidden.'
        }
        $size = [long]$sourceItem.Length
        if ($size -gt 67108864) { throw 'EDGE-SPLIT-AUTHORITY-DEV-LIMIT untracked file exceeds 64 MiB.' }
        $totalBytes += $size
        $record = [pscustomobject][ordered]@{
            recordType = '?'
            indexStatus = '?'
            worktreeStatus = '?'
            submoduleState = 'N...'
            headMode = '000000'
            indexMode = '000000'
            worktreeMode = Get-DevCurrentFileMode $sourcePath
            headObject = ('0' * 40)
            indexObject = ('0' * 40)
            renameScore = ''
            changeKind = 'untracked'
            oldPath = ''
            path = [string]$path
            size = $size
            sha256 = Get-EdgeSha256File $sourcePath
        }
        if (-not $records.TryAdd([string]$path, $record)) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-MANIFEST tracked/untracked path collision.'
        }
    }
    if ($records.Count -eq 0) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-MANIFEST development snapshot requires a non-empty dirty manifest.'
    }
    if ($records.Count -gt 4096 -or $totalBytes -gt 536870912) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-LIMIT dirty manifest exceeds file-count/total-byte limits.'
    }
    $orderedRecords = @(Sort-DevOrdinalStrings ([string[]]@($records.Keys)) | ForEach-Object { $records[$_] })
    $manifest = [pscustomobject][ordered]@{
        schemaVersion = 1
        sourceBaseHead = $baseHead
        sourceBaseTree = $baseTree
        trackedPorcelainV2ZLength = [long]$trackedBytes.Length
        trackedPorcelainV2ZSha256 = Get-EdgeSha256Bytes $trackedBytes
        untrackedExcludeStandardZLength = [long]$untrackedBytes.Length
        untrackedExcludeStandardZSha256 = Get-EdgeSha256Bytes $untrackedBytes
        recordCount = $records.Count
        totalRegularFileBytes = $totalBytes
        records = $orderedRecords
    }
    [byte[]]$manifestBytes = @(
        ConvertTo-EdgeCanonicalBytes $manifest)
    return [pscustomobject][ordered]@{
        value = $manifest
        bytes = $manifestBytes
        sha256 = Get-EdgeSha256Bytes $manifestBytes
    }
}

function Update-DevSnapshotIndexFromManifest {
    param(
        [Parameter(Mandatory)][string]$SnapshotRoot,
        [Parameter(Mandatory)]$Manifest
    )

    $snapshot = Assert-DevRepositoryRoot $SnapshotRoot
    $pathSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($record in @($Manifest.value.records)) {
        $missing = [string]$record.worktreeMode -ceq '000000'
        [void](Assert-DevSafePath -Root $snapshot -RelativePath ([string]$record.path) -MayBeMissing:$missing)
        [void]$pathSet.Add([string]$record.path)
        if (-not [string]::IsNullOrEmpty([string]$record.oldPath)) {
            [void](Assert-DevSafePath -Root $snapshot -RelativePath ([string]$record.oldPath) -MayBeMissing)
            [void]$pathSet.Add([string]$record.oldPath)
        }
    }
    $orderedPaths = [string[]]@(Sort-DevOrdinalStrings ([string[]]@($pathSet)))
    if ($orderedPaths.Length -eq 0 -or $orderedPaths.Length -gt 8192) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-INDEX exact dirty pathspec inventory is empty or exceeds its bound.'
    }
    $indexArguments = [string[]]@(
        @('update-index', '--add', '--remove', '--') + $orderedPaths)
    [byte[]]$indexUpdateBytes = @(
        Invoke-DevGitBytes $snapshot $indexArguments)
    if ($indexUpdateBytes -isnot [byte[]] -or $indexUpdateBytes.Length -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-INDEX exact path-level index update returned unexpected stdout.'
    }
    foreach ($record in @($Manifest.value.records | Where-Object {
                [string]$_.worktreeMode -ne '000000'
            })) {
        $chmod = if ([string]$record.worktreeMode -ceq '100755') { '--chmod=+x' }
        elseif ([string]$record.worktreeMode -ceq '100644') { '--chmod=-x' }
        else { throw 'EDGE-SPLIT-AUTHORITY-DEV-INDEX exact path-level index update received an invalid file mode.' }
        [void](Invoke-DevGitBytes $snapshot @(
                'update-index', $chmod, '--', [string]$record.path))
    }
}

function New-DevIndependentSnapshot {
    param(
        [Parameter(Mandatory)][string]$SourceRoot,
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)][string]$SnapshotRoot
    )

    [void](Invoke-DevGitBytes $SourceRoot @('clone', '--no-local', '--no-hardlinks', '--no-checkout', $SourceRoot, $SnapshotRoot))
    $snapshot = Assert-DevRepositoryRoot $SnapshotRoot
    [void](Invoke-DevGitBytes $snapshot @('checkout', '--detach', [string]$Manifest.value.sourceBaseHead))
    $gitDirectory = [IO.Path]::GetFullPath((Join-Path $snapshot (Invoke-DevGitText $snapshot @('rev-parse', '--git-dir'))))
    $commonDirectory = [IO.Path]::GetFullPath((Join-Path $snapshot (Invoke-DevGitText $snapshot @('rev-parse', '--git-common-dir'))))
    if (-not (Test-EdgePathIdentity $gitDirectory (Join-Path $snapshot '.git')) -or
        -not (Test-EdgePathIdentity $commonDirectory $gitDirectory) -or
        -not (Test-Path -LiteralPath (Join-Path $gitDirectory 'objects') -PathType Container) -or
        -not (Test-Path -LiteralPath (Join-Path $gitDirectory 'index') -PathType Leaf)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLONE snapshot refs/index/objects are not clone-local.'
    }
    $alternatesPath = Join-Path $gitDirectory 'objects/info/alternates'
    if (Test-Path -LiteralPath $alternatesPath -PathType Leaf) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLONE snapshot object database must not use alternates/shared objects.'
    }

    foreach ($record in @($Manifest.value.records)) {
        if (-not [string]::IsNullOrEmpty([string]$record.oldPath)) {
            $oldDestination = Assert-DevSafePath -Root $snapshot -RelativePath ([string]$record.oldPath) -MayBeMissing
            if (Test-Path -LiteralPath $oldDestination -PathType Leaf) { Remove-Item -LiteralPath $oldDestination -Force }
        }
        $destination = Assert-DevSafePath -Root $snapshot -RelativePath ([string]$record.path) -MayBeMissing
        if ([string]$record.worktreeMode -ceq '000000') {
            if (Test-Path -LiteralPath $destination -PathType Leaf) { Remove-Item -LiteralPath $destination -Force }
            continue
        }
        $source = Assert-DevSafePath -Root $SourceRoot -RelativePath ([string]$record.path)
        [void](New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force)
        [IO.File]::Copy($source, $destination, $true)
        if (-not [OperatingSystem]::IsWindows()) {
            $destinationMode = if ([string]$record.worktreeMode -ceq '100755') {
                [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor
                    [IO.UnixFileMode]::UserExecute -bor [IO.UnixFileMode]::GroupRead -bor
                    [IO.UnixFileMode]::GroupExecute -bor [IO.UnixFileMode]::OtherRead -bor
                    [IO.UnixFileMode]::OtherExecute
            }
            else {
                [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor
                    [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::OtherRead
            }
            [IO.File]::SetUnixFileMode($destination, $destinationMode)
        }
        if ((Get-DevCurrentFileMode $destination) -cne [string]$record.worktreeMode -or
            [long](Get-Item -LiteralPath $destination -Force).Length -ne [long]$record.size -or
            (Get-EdgeSha256File $destination) -cne [string]$record.sha256) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-CLONE copied dirty mode/bytes changed during snapshot construction.'
        }
    }
    Update-DevSnapshotIndexFromManifest -SnapshotRoot $snapshot -Manifest $Manifest
    [void](Invoke-DevGitBytes $snapshot @(
            '-c', 'user.name=Edge Authority Snapshot',
            '-c', 'user.email=edge-authority-snapshot@example.invalid',
            'commit', '--no-gpg-sign', '-m', 'ephemeral: bind development dirty snapshot'))
    $snapshotHead = Invoke-DevGitText $snapshot @('rev-parse', 'HEAD')
    $snapshotTree = Invoke-DevGitText $snapshot @('rev-parse', 'HEAD^{tree}')
    [byte[]]$cleanStatusBytes = @(
        Invoke-DevGitBytes $snapshot @('status', '--porcelain=v2', '-z', '--untracked-files=no'))
    if ($cleanStatusBytes.Length -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLONE ephemeral snapshot is not clean after its binding commit.'
    }
    $expectedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($record in @($Manifest.value.records)) {
        [void]$expectedPaths.Add([string]$record.path)
        if (-not [string]::IsNullOrEmpty([string]$record.oldPath)) { [void]$expectedPaths.Add([string]$record.oldPath) }
    }
    [byte[]]$diffBytes = @(Invoke-DevGitBytes $snapshot @(
        '-c', 'core.quotePath=false', 'diff', '--no-ext-diff', '--name-only', '--no-renames', '-z',
        [string]$Manifest.value.sourceBaseHead, $snapshotHead, '--'))
    $diffPaths = @(ConvertFrom-DevUtf8 $diffBytes 'EDGE-SPLIT-AUTHORITY-DEV-CLONE' |
        ForEach-Object { $_.Split([char[]]@([char]0), [StringSplitOptions]::RemoveEmptyEntries) })
    $expectedOrdered = @(Sort-DevOrdinalStrings ([string[]]@($expectedPaths)))
    $actualOrdered = @(Sort-DevOrdinalStrings ([string[]]$diffPaths))
    if (($expectedOrdered -join "`n") -cne ($actualOrdered -join "`n")) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLONE snapshot commit path set differs from the dirty manifest.'
    }
    foreach ($record in @($Manifest.value.records | Where-Object { [string]$_.worktreeMode -ne '000000' })) {
        $path = Assert-DevSafePath -Root $snapshot -RelativePath ([string]$record.path)
        $treeEntry = Invoke-DevGitText $snapshot @('ls-tree', $snapshotHead, '--', [string]$record.path)
        if (-not $treeEntry.StartsWith("$([string]$record.worktreeMode) blob ", [StringComparison]::Ordinal) -or
            [long](Get-Item -LiteralPath $path -Force).Length -ne [long]$record.size -or
            (Get-EdgeSha256File $path) -cne [string]$record.sha256) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-CLONE snapshot mode/size/hash differs from the dirty manifest.'
        }
    }

    $runtimeFixtureRoot = Join-Path $snapshot '.edge-byte-array-runtime'
    $runtimeFixtureFailure = $null
    try {
        if ([IO.Directory]::Exists($runtimeFixtureRoot)) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES runtime fixture root was not absent.'
        }
        [void][IO.Directory]::CreateDirectory($runtimeFixtureRoot)
        [byte[]]$fixtureCommandBytes = @(
            Invoke-DevGitBytes $snapshot @('init', '--quiet', $runtimeFixtureRoot))
        if ($fixtureCommandBytes -isnot [byte[]] -or $fixtureCommandBytes.Length -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES zero-byte Git stdout did not remain an empty byte array.'
        }
        $runtimeFixtureRoot = Assert-DevRepositoryRoot $runtimeFixtureRoot
        $oneBytePath = Join-Path $runtimeFixtureRoot 'one-byte.bin'
        $manyBytesPath = Join-Path $runtimeFixtureRoot 'many-bytes.bin'
        $trackedModifyPath = Join-Path $runtimeFixtureRoot 'tracked-modify.bin'
        $trackedDeletePath = Join-Path $runtimeFixtureRoot 'tracked-delete.bin'
        $trackedRenameSourcePath = Join-Path $runtimeFixtureRoot 'tracked-rename-source.bin'
        $trackedRenameDestinationPath = Join-Path $runtimeFixtureRoot 'tracked-rename-destination.bin'
        $modeChangePath = Join-Path $runtimeFixtureRoot 'mode-change.sh'
        [byte[]]$oneByteExpected = @(0x2A)
        [byte[]]$manyBytesExpected = @(0x00, 0x01, 0x7F, 0x80, 0xFF)
        [IO.File]::WriteAllBytes($oneBytePath, $oneByteExpected)
        [IO.File]::WriteAllBytes($manyBytesPath, $manyBytesExpected)
        [IO.File]::WriteAllBytes($trackedModifyPath, [byte[]]@(0x10))
        [IO.File]::WriteAllBytes($trackedDeletePath, [byte[]]@(0x20))
        [IO.File]::WriteAllBytes($trackedRenameSourcePath, [byte[]]@(0x30))
        [IO.File]::WriteAllBytes($modeChangePath, [byte[]]@(0x40))
        if (-not [OperatingSystem]::IsWindows()) {
            $nonExecutableMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor
                [IO.UnixFileMode]::GroupRead -bor [IO.UnixFileMode]::OtherRead
            [IO.File]::SetUnixFileMode($modeChangePath, $nonExecutableMode)
        }
        $fixtureCommandBytes = @(Invoke-DevGitBytes $runtimeFixtureRoot @(
                'add', '--', 'one-byte.bin', 'many-bytes.bin', 'tracked-modify.bin',
                'tracked-delete.bin', 'tracked-rename-source.bin', 'mode-change.sh'))
        if ($fixtureCommandBytes -isnot [byte[]] -or $fixtureCommandBytes.Length -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES fixture add stdout did not remain an empty byte array.'
        }
        $fixtureCommandBytes = @(Invoke-DevGitBytes $runtimeFixtureRoot @(
                '-c', 'user.name=Edge Byte Runtime',
                '-c', 'user.email=edge-byte-runtime@example.invalid',
                'commit', '--quiet', '--no-gpg-sign', '-m', 'ephemeral: byte-array runtime fixture'))
        if ($fixtureCommandBytes -isnot [byte[]] -or $fixtureCommandBytes.Length -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES fixture commit stdout did not remain an empty byte array.'
        }
        $runtimeFixtureBaseHead = Invoke-DevGitText $runtimeFixtureRoot @('rev-parse', 'HEAD')

        [byte[]]$oneByteActual = @(
            Invoke-DevGitBytes $runtimeFixtureRoot @('cat-file', 'blob', 'HEAD:one-byte.bin'))
        if ($oneByteActual -isnot [byte[]] -or $oneByteActual.Length -ne 1 -or
            $oneByteActual[0] -ne $oneByteExpected[0]) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES one-byte Git stdout lost its byte-array shape or value.'
        }
        [byte[]]$manyBytesActual = @(
            Invoke-DevGitBytes $runtimeFixtureRoot @('cat-file', 'blob', 'HEAD:many-bytes.bin'))
        if ($manyBytesActual -isnot [byte[]] -or
            -not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
                $manyBytesActual, $manyBytesExpected)) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES multi-byte Git stdout changed its byte order or shape.'
        }
        [byte[]]$runtimeCleanBytes = @(Invoke-DevGitBytes $runtimeFixtureRoot @(
                'status', '--porcelain=v2', '-z', '--untracked-files=no'))
        if ($runtimeCleanBytes -isnot [byte[]] -or $runtimeCleanBytes.Length -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES clean runtime fixture status was not an empty byte array.'
        }

        [IO.File]::WriteAllBytes($oneBytePath, [byte[]]@(0x2A, 0x2B))
        $trackedOnlyManifest = Get-DevDirtyManifest $runtimeFixtureRoot
        [byte[]]$trackedOnlyExpectedManifestBytes = @(
            ConvertTo-EdgeCanonicalBytes $trackedOnlyManifest.value)
        if ($trackedOnlyManifest.bytes -isnot [byte[]] -or
            $trackedOnlyManifest.bytes.Length -le 0 -or
            -not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
                [byte[]]$trackedOnlyManifest.bytes,
                $trackedOnlyExpectedManifestBytes) -or
            [string]$trackedOnlyManifest.sha256 -cne
                (Get-EdgeSha256Bytes $trackedOnlyExpectedManifestBytes)) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES dirty manifest canonical bytes lost their strong type or exact value.'
        }
        $trackedOnlyRecords = @($trackedOnlyManifest.value.records)
        if ([long]$trackedOnlyManifest.value.trackedPorcelainV2ZLength -le 0 -or
            [long]$trackedOnlyManifest.value.untrackedExcludeStandardZLength -ne 0 -or
            $trackedOnlyRecords.Count -ne 1 -or
            [string]$trackedOnlyRecords[0].recordType -cne '1' -or
            [string]$trackedOnlyRecords[0].path -cne 'one-byte.bin') {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES tracked-only dirty manifest lost its exact runtime shape.'
        }
        [IO.File]::WriteAllBytes($oneBytePath, $oneByteExpected)
        [byte[]]$restoredTrackedBytes = @(Invoke-DevGitBytes $runtimeFixtureRoot @(
                'status', '--porcelain=v2', '-z', '--untracked-files=no'))
        if ($restoredTrackedBytes -isnot [byte[]] -or $restoredTrackedBytes.Length -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES tracked-only fixture did not restore to clean bytes.'
        }

        $untrackedPath = Join-Path $runtimeFixtureRoot 'untracked.bin'
        [IO.File]::WriteAllBytes($untrackedPath, [byte[]]@(0x5A))
        $untrackedOnlyManifest = Get-DevDirtyManifest $runtimeFixtureRoot
        $untrackedOnlyRecords = @($untrackedOnlyManifest.value.records)
        if ([long]$untrackedOnlyManifest.value.trackedPorcelainV2ZLength -ne 0 -or
            [long]$untrackedOnlyManifest.value.untrackedExcludeStandardZLength -le 0 -or
            $untrackedOnlyRecords.Count -ne 1 -or
            [string]$untrackedOnlyRecords[0].recordType -cne '?' -or
            [string]$untrackedOnlyRecords[0].path -cne 'untracked.bin') {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES untracked-only dirty manifest lost its exact runtime shape.'
        }
        [IO.File]::Delete($untrackedPath)

        [IO.File]::WriteAllBytes($trackedModifyPath, [byte[]]@(0x10, 0x11))
        [IO.File]::Delete($trackedDeletePath)
        [byte[]]$fixtureCommandBytes = @(Invoke-DevGitBytes $runtimeFixtureRoot @(
                'mv', '--', 'tracked-rename-source.bin', 'tracked-rename-destination.bin'))
        if ($fixtureCommandBytes -isnot [byte[]] -or $fixtureCommandBytes.Length -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES fixture rename stdout did not remain an empty byte array.'
        }
        $modeChangeExpected = -not [OperatingSystem]::IsWindows()
        if ($modeChangeExpected) {
            $executableMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor
                [IO.UnixFileMode]::UserExecute -bor [IO.UnixFileMode]::GroupRead -bor
                [IO.UnixFileMode]::GroupExecute -bor [IO.UnixFileMode]::OtherRead -bor
                [IO.UnixFileMode]::OtherExecute
            [IO.File]::SetUnixFileMode($modeChangePath, $executableMode)
        }
        $mixedUntrackedPath = Join-Path $runtimeFixtureRoot 'mixed-untracked.bin'
        [IO.File]::WriteAllBytes($mixedUntrackedPath, [byte[]]@(0x50))
        $mixedManifest = Get-DevDirtyManifest $runtimeFixtureRoot
        $mixedRecords = @($mixedManifest.value.records)
        $expectedMixedCount = if ($modeChangeExpected) { 5 } else { 4 }
        $modifyRecord = @($mixedRecords | Where-Object { [string]$_.path -ceq 'tracked-modify.bin' })
        $deleteRecord = @($mixedRecords | Where-Object { [string]$_.path -ceq 'tracked-delete.bin' })
        $renameRecord = @($mixedRecords | Where-Object { [string]$_.path -ceq 'tracked-rename-destination.bin' })
        $untrackedRecord = @($mixedRecords | Where-Object { [string]$_.path -ceq 'mixed-untracked.bin' })
        $modeRecord = @($mixedRecords | Where-Object { [string]$_.path -ceq 'mode-change.sh' })
        if ($mixedRecords.Count -ne $expectedMixedCount -or
            $modifyRecord.Count -ne 1 -or [string]$modifyRecord[0].changeKind -cne 'modify' -or
            $deleteRecord.Count -ne 1 -or [string]$deleteRecord[0].changeKind -cne 'delete' -or
            [string]$deleteRecord[0].worktreeMode -cne '000000' -or
            $renameRecord.Count -ne 1 -or [string]$renameRecord[0].changeKind -cne 'rename' -or
            [string]$renameRecord[0].oldPath -cne 'tracked-rename-source.bin' -or
            $untrackedRecord.Count -ne 1 -or [string]$untrackedRecord[0].changeKind -cne 'untracked' -or
            ($modeChangeExpected -and
                ($modeRecord.Count -ne 1 -or [string]$modeRecord[0].changeKind -cne 'mode-or-type-change' -or
                    [string]$modeRecord[0].worktreeMode -cne '100755')) -or
            (-not $modeChangeExpected -and $modeRecord.Count -ne 0)) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-INDEX mixed tracked/untracked/delete/rename/mode manifest lost its exact runtime shape.'
        }
        Update-DevSnapshotIndexFromManifest -SnapshotRoot $runtimeFixtureRoot -Manifest $mixedManifest
        [byte[]]$mixedCommitBytes = @(Invoke-DevGitBytes $runtimeFixtureRoot @(
                '-c', 'user.name=Edge Byte Runtime',
                '-c', 'user.email=edge-byte-runtime@example.invalid',
                'commit', '--quiet', '--no-gpg-sign', '-m', 'ephemeral: mixed path-level index fixture'))
        if ($mixedCommitBytes -isnot [byte[]] -or $mixedCommitBytes.Length -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-INDEX mixed path-level index commit returned unexpected stdout.'
        }
        [byte[]]$mixedCleanBytes = @(Invoke-DevGitBytes $runtimeFixtureRoot @(
                'status', '--porcelain=v2', '-z', '--untracked-files=all'))
        if ($mixedCleanBytes -isnot [byte[]] -or $mixedCleanBytes.Length -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-INDEX mixed path-level index fixture was not clean after commit.'
        }
        $mixedHead = Invoke-DevGitText $runtimeFixtureRoot @('rev-parse', 'HEAD')
        [byte[]]$mixedDiffBytes = @(Invoke-DevGitBytes $runtimeFixtureRoot @(
                '-c', 'core.quotePath=false', 'diff', '--name-only', '--no-renames', '-z',
                $runtimeFixtureBaseHead, $mixedHead, '--'))
        $mixedDiffPaths = @(ConvertFrom-DevUtf8 $mixedDiffBytes 'EDGE-SPLIT-AUTHORITY-DEV-INDEX' |
            ForEach-Object { $_.Split([char[]]@([char]0), [StringSplitOptions]::RemoveEmptyEntries) })
        $mixedExpectedPaths = @(
            'mixed-untracked.bin', 'tracked-delete.bin', 'tracked-modify.bin',
            'tracked-rename-destination.bin', 'tracked-rename-source.bin')
        if ($modeChangeExpected) { $mixedExpectedPaths += 'mode-change.sh' }
        $mixedExpectedPaths = @(Sort-DevOrdinalStrings ([string[]]$mixedExpectedPaths))
        $mixedActualPaths = @(Sort-DevOrdinalStrings ([string[]]$mixedDiffPaths))
        $modeTreeEntry = Invoke-DevGitText $runtimeFixtureRoot @('ls-tree', $mixedHead, '--', 'mode-change.sh')
        $expectedModePrefix = if ($modeChangeExpected) { '100755 blob ' } else { '100644 blob ' }
        if (($mixedExpectedPaths -join "`n") -cne ($mixedActualPaths -join "`n") -or
            -not $modeTreeEntry.StartsWith($expectedModePrefix, [StringComparison]::Ordinal) -or
            -not [string]::IsNullOrEmpty((Invoke-DevGitText $runtimeFixtureRoot @(
                        'ls-tree', $mixedHead, '--', 'tracked-delete.bin'))) -or
            -not [string]::IsNullOrEmpty((Invoke-DevGitText $runtimeFixtureRoot @(
                        'ls-tree', $mixedHead, '--', 'tracked-rename-source.bin')))) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-INDEX mixed path-level index commit differs from its exact manifest paths/modes.'
        }

        [byte[]]$runtimeFinalCleanBytes = @(Invoke-DevGitBytes $runtimeFixtureRoot @(
                'status', '--porcelain=v2', '-z', '--untracked-files=all'))
        if ($runtimeFinalCleanBytes -isnot [byte[]] -or $runtimeFinalCleanBytes.Length -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES runtime fixture was not clean after byte-array regressions.'
        }
    }
    catch { $runtimeFixtureFailure = $_ }
    finally {
        try {
            if ([IO.Directory]::Exists($runtimeFixtureRoot)) {
                [IO.Directory]::Delete($runtimeFixtureRoot, $true)
            }
            if ([IO.Directory]::Exists($runtimeFixtureRoot)) {
                throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES runtime fixture cleanup was incomplete.'
            }
        }
        catch {
            if ($null -eq $runtimeFixtureFailure) { $runtimeFixtureFailure = $_ }
        }
    }
    if ($null -ne $runtimeFixtureFailure) { throw $runtimeFixtureFailure }

    [byte[]]$postRegressionCleanBytes = @(Invoke-DevGitBytes $snapshot @(
            'status', '--porcelain=v2', '-z', '--untracked-files=all'))
    if ($postRegressionCleanBytes -isnot [byte[]] -or $postRegressionCleanBytes.Length -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-BYTES snapshot was not clean after runtime fixture cleanup.'
    }
    return [pscustomobject][ordered]@{ root = $snapshot; head = $snapshotHead; tree = $snapshotTree }
}

$tmpDirectoryEnvironment = [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process)
$tmpDirectoryWasPresent = $tmpDirectoryEnvironment.Contains('TMPDIR')
$tmpDirectoryOriginalValue = if ($tmpDirectoryWasPresent) {
    [string]$tmpDirectoryEnvironment['TMPDIR']
}
else { $null }
$physicalTempRoot = Resolve-DevPhysicalTempRoot
$tmpDirectoryFailure = $null

try {
    [Environment]::SetEnvironmentVariable(
        'TMPDIR', $physicalTempRoot, [EnvironmentVariableTarget]::Process)
    $tmpDirectoryPinnedEnvironment =
        [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process)
    if (-not $tmpDirectoryPinnedEnvironment.Contains('TMPDIR') -or
        [string]$tmpDirectoryPinnedEnvironment['TMPDIR'] -cne $physicalTempRoot) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-TEMP process TMPDIR pin was not exact.'
    }

$RepositoryRoot = Assert-DevRepositoryRoot $RepositoryRoot
$runId = [Guid]::NewGuid().ToString('N')
$outerRunRoot = Join-Path $physicalTempRoot "edge-development-authority-$runId"
$snapshotRoot = Join-Path $outerRunRoot "snapshot-$runId"
$coordinatorRunRoot = Join-Path $outerRunRoot "coordinator-$runId"
$validatorWorktreePath = Join-Path $coordinatorRunRoot "validator-$runId"
$replayWorktreePath = Join-Path $coordinatorRunRoot "replay-$runId"
$outerMarkerPath = Join-Path $outerRunRoot '.edge-development-authority-run.json'
$outerMarker = [pscustomobject][ordered]@{
    schemaVersion = 2
    runId = $runId
    sourceRepositoryRoot = $RepositoryRoot
    authorityRepositoryRoot = $snapshotRoot
    snapshotRoot = $snapshotRoot
    coordinatorRunRoot = $coordinatorRunRoot
    validatorWorktreePath = $validatorWorktreePath
    replayWorktreePath = $replayWorktreePath
    fixedGitExecutablePath = $gitPath
    pinnedPathSha256 = Get-EdgeSha256Text $devPinnedPath
}
$startManifest = $null
$failure = $null
$evidence = $null
$coordinatorMarker = $null

try {
    $startManifest = Get-DevDirtyManifest $RepositoryRoot
    [void](New-Item -ItemType Directory -Path $outerRunRoot)
    foreach ($reservedPath in @($snapshotRoot, $coordinatorRunRoot, $validatorWorktreePath, $replayWorktreePath)) {
        if (Test-Path -LiteralPath $reservedPath) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-OWNERSHIP reserved child path was not absent before the parent marker was established.'
        }
    }
    [IO.File]::WriteAllBytes($outerMarkerPath, (ConvertTo-EdgeCanonicalBytes $outerMarker))
    $snapshot = New-DevIndependentSnapshot -SourceRoot $RepositoryRoot -Manifest $startManifest -SnapshotRoot $snapshotRoot
    $snapshotEmptyGitConfigPath = Assert-EdgeAuthorityEmptyGitConfig $snapshot.root
    $snapshotGitChildEnvironment = New-EdgeAuthorityGitChildEnvironment $snapshotEmptyGitConfigPath $gitPath
    $snapshotLocalConfigSha256 = Get-DevLocalGitConfigDigest $snapshot.root
    $coordinatorMarker = [pscustomobject][ordered]@{
        schemaVersion = 1
        runId = $runId
        sourceRepositoryRoot = $RepositoryRoot
        authorityRepositoryRoot = $snapshotRoot
        authorityHead = [string]$snapshot.head
        validatorWorktreePath = $validatorWorktreePath
        replayWorktreePath = $replayWorktreePath
    }

    $ledgerRelativePath = ".artifacts/edge-authority-$runId/development-ledger.json"
    $receiptRelativePath = ".artifacts/edge-authority-$runId/development-receipt.json"
    $generatorScript = Join-Path $snapshot.root 'eng/Generate-EdgePluginContractLedger.ps1'
    $generatorResult = Invoke-DevProcess -FileName $powerShellPath -WorkingDirectory $snapshot.root `
        -Arguments @(
            '-NoLogo', '-NoProfile', '-File', $generatorScript,
            '-PluginProject', $PluginProject,
            '-OutputPath', $ledgerRelativePath,
            '-Configuration', $Configuration,
            '-CurrentBatch', $CurrentBatch,
            '-ViewIdsTypeName', $ViewIdsTypeName) `
        -TimeoutSeconds 900 -InputBytes $null -Environment $snapshotGitChildEnvironment
    if ($generatorResult.exitCode -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-DEV-GENERATOR candidate generation failed; stderrSha256=$(Get-EdgeSha256Bytes $generatorResult.stderrBytes)."
    }
    if ((Get-DevLocalGitConfigDigest $snapshot.root) -cne $snapshotLocalConfigSha256) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-GIT-CONFIG candidate generator changed snapshot local Git config.'
    }
    $ledgerPath = Resolve-EdgeRepositoryPath $snapshot.root $ledgerRelativePath
    $ledger = Read-EdgeGeneratorLedger -Path $ledgerPath `
        -SchemaPath (Join-Path $snapshot.root 'eng/edge-plugin-contract-ledger.schema.json') -Name 'development'
    if ([string]$ledger.sourceState.head -cne [string]$snapshot.head -or
        [string]$ledger.sourceState.tree -cne [string]$snapshot.tree -or
        -not [bool]$ledger.sourceState.cleanObserved -or @($ledger.sourceState.dirtyPaths).Count -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-LEDGER development ledger is not an exact clean projection of the ephemeral snapshot.'
    }

    $challengeBase64 = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $request = [pscustomobject][ordered]@{
        schemaVersion = 1
        mode = 'development-snapshot'
        runId = $runId
        challengeBase64 = $challengeBase64
        sourceRepositoryRoot = $RepositoryRoot
        authorityRepositoryRoot = $snapshot.root
        authorityHead = [string]$snapshot.head
        authorityTree = [string]$snapshot.tree
        formalFinalHead = ''
        formalFinalTree = ''
        sourceBaseHead = [string]$startManifest.value.sourceBaseHead
        sourceBaseTree = [string]$startManifest.value.sourceBaseTree
        sourceDirtyManifestSha256 = [string]$startManifest.sha256
        ephemeralSnapshotHead = [string]$snapshot.head
        ephemeralSnapshotTree = [string]$snapshot.tree
        implementationHead = [string]$ledger.sourceState.head
        implementationTree = [string]$ledger.sourceState.tree
        ledgerPath = $ledgerRelativePath
        receiptPath = $receiptRelativePath
        runRoot = $coordinatorRunRoot
        validatorWorktreePath = $validatorWorktreePath
        replayWorktreePath = $replayWorktreePath
        pluginProject = [string]$ledger.msbuildCompilation.projectPath
        configuration = [string]$ledger.msbuildCompilation.configuration
        currentBatch = [string]$ledger.batchId
        viewIdsAssemblyPath = [string]$ledger.msbuildCompilation.viewIdsAssemblyPath
        viewIdsTypeName = [string]$ledger.msbuildCompilation.viewIdsTypeName
        timeoutSeconds = $AuthorityTimeoutSeconds
    }
    $requestBytes = ConvertTo-EdgeCanonicalBytes $request
    [void](Assert-EdgeStrictJson -RawBytes $requestBytes `
        -SchemaPath (Join-Path $snapshot.root 'eng/edge-plugin-contract-authority-request.schema.json') `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-DEV-REQUEST' -RequireCanonical)
    $coordinatorEnvironment = New-EdgeAuthorityCoordinatorParentEnvironment `
        -OuterMarker $outerMarker -ParentMarkerPath $outerMarkerPath `
        -FixedGitExecutablePath $gitPath -PinnedPath $devPinnedPath
    $coordinatorResult = Invoke-DevProcess -FileName $powerShellPath -WorkingDirectory $snapshot.root `
        -Arguments @('-NoLogo', '-NoProfile', '-File',
            (Join-Path $snapshot.root 'scripts/tests/Invoke-EdgePluginContractAuthorityCoordinator.ps1')) `
        -TimeoutSeconds ($AuthorityTimeoutSeconds + 60) -InputBytes $requestBytes `
        -Environment $coordinatorEnvironment
    if ($coordinatorResult.exitCode -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-DEV-COORDINATOR coordinator failed; stderrSha256=$(Get-EdgeSha256Bytes $coordinatorResult.stderrBytes)."
    }
    $receiptPath = Resolve-EdgeRepositoryPath $snapshot.root $receiptRelativePath
    $descriptorExpected = [pscustomobject][ordered]@{
        runId = $runId
        challengeBase64 = $challengeBase64
        coordinatorPid = [int]$coordinatorResult.pid
        processStartUtc = [string]$coordinatorResult.processStartUtc
        sourceRepositoryRoot = $RepositoryRoot
        authorityRepositoryRoot = $snapshot.root
        authorityHead = [string]$snapshot.head
        authorityTree = [string]$snapshot.tree
        formalFinalHead = ''
        formalFinalTree = ''
        sourceBaseHead = [string]$startManifest.value.sourceBaseHead
        sourceBaseTree = [string]$startManifest.value.sourceBaseTree
        sourceDirtyManifestSha256 = [string]$startManifest.sha256
        ephemeralSnapshotHead = [string]$snapshot.head
        ephemeralSnapshotTree = [string]$snapshot.tree
        implementationHead = [string]$ledger.sourceState.head
        implementationTree = [string]$ledger.sourceState.tree
        receiptPath = $receiptRelativePath
    }
    $descriptor = Assert-EdgeAuthorityDescriptor -RawBytes $coordinatorResult.stdoutBytes `
        -SchemaPath (Join-Path $snapshot.root 'eng/edge-plugin-contract-authority-descriptor.schema.json') `
        -Expected $descriptorExpected -ReceiptFullPath $receiptPath

    $receiptArguments = @{
        RepositoryRoot = $snapshot.root
        LedgerPath = $ledgerRelativePath
        ReceiptPath = $receiptPath
        PublicKeySpkiBase64 = [string]$descriptor.publicKeySpkiBase64
        ExpectedRunId = $runId
        ExpectedChallengeBase64 = $challengeBase64
        ExpectedSourceRepositoryRoot = $RepositoryRoot
        ExpectedAuthorityHead = [string]$snapshot.head
        ExpectedAuthorityTree = [string]$snapshot.tree
        ExpectedFormalFinalHead = ''
        ExpectedFormalFinalTree = ''
        ExpectedSourceBaseHead = [string]$startManifest.value.sourceBaseHead
        ExpectedSourceBaseTree = [string]$startManifest.value.sourceBaseTree
        ExpectedSourceDirtyManifestSha256 = [string]$startManifest.sha256
        ExpectedEphemeralSnapshotHead = [string]$snapshot.head
        ExpectedEphemeralSnapshotTree = [string]$snapshot.tree
        ExpectedImplementationHead = [string]$ledger.sourceState.head
        ExpectedImplementationTree = [string]$ledger.sourceState.tree
    }
    [void](Assert-EdgeAuthorityReceipt @receiptArguments)

    $fastEnvironment = [ordered]@{
        EDGE_PLUGIN_CONTRACT_AUTHORITY_RECEIPT = $receiptRelativePath
        EDGE_PLUGIN_CONTRACT_AUTHORITY_PUBLIC_KEY = [string]$descriptor.publicKeySpkiBase64
        EDGE_PLUGIN_CONTRACT_AUTHORITY_RUN_ID = $runId
        EDGE_PLUGIN_CONTRACT_AUTHORITY_CHALLENGE = $challengeBase64
        EDGE_PLUGIN_CONTRACT_AUTHORITY_SOURCE_ROOT = $RepositoryRoot
        EDGE_PLUGIN_CONTRACT_AUTHORITY_HEAD = [string]$snapshot.head
        EDGE_PLUGIN_CONTRACT_AUTHORITY_TREE = [string]$snapshot.tree
        EDGE_PLUGIN_CONTRACT_FORMAL_FINAL_HEAD = ''
        EDGE_PLUGIN_CONTRACT_FORMAL_FINAL_TREE = ''
        EDGE_PLUGIN_CONTRACT_SOURCE_BASE_HEAD = [string]$startManifest.value.sourceBaseHead
        EDGE_PLUGIN_CONTRACT_SOURCE_BASE_TREE = [string]$startManifest.value.sourceBaseTree
        EDGE_PLUGIN_CONTRACT_SOURCE_DIRTY_MANIFEST_SHA256 = [string]$startManifest.sha256
        EDGE_PLUGIN_CONTRACT_EPHEMERAL_SNAPSHOT_HEAD = [string]$snapshot.head
        EDGE_PLUGIN_CONTRACT_EPHEMERAL_SNAPSHOT_TREE = [string]$snapshot.tree
        EDGE_PLUGIN_CONTRACT_IMPLEMENTATION_HEAD = [string]$ledger.sourceState.head
        EDGE_PLUGIN_CONTRACT_IMPLEMENTATION_TREE = [string]$ledger.sourceState.tree
    }
    foreach ($gitEnvironmentName in $snapshotGitChildEnvironment.Keys) {
        $fastEnvironment[[string]$gitEnvironmentName] =
            [string]$snapshotGitChildEnvironment[$gitEnvironmentName]
    }
    $fastResult = Invoke-DevProcess -FileName $powerShellPath -WorkingDirectory $snapshot.root `
        -Arguments @('-NoLogo', '-NoProfile', '-File',
            (Join-Path $snapshot.root 'scripts/tests/Test-EdgePluginContractLedger.ps1'),
            '-RepositoryRoot', $snapshot.root, '-LedgerPath', $ledgerRelativePath, '-RequireAuthorityReceipt') `
        -TimeoutSeconds 120 -InputBytes $null -Environment $fastEnvironment
    if ($fastResult.exitCode -ne 0) {
        throw "EDGE-SPLIT-AUTHORITY-DEV-FAST signed receipt fast consumer failed; stderrSha256=$(Get-EdgeSha256Bytes $fastResult.stderrBytes)."
    }
    $behaviorBindingsBase64 = [Convert]::ToBase64String(
        (ConvertTo-EdgeCanonicalBytes $descriptorExpected))
    $behaviorResult = Invoke-DevProcess -FileName $powerShellPath -WorkingDirectory $snapshot.root `
        -Arguments @('-NoLogo', '-NoProfile', '-File',
            (Join-Path $snapshot.root 'scripts/tests/Test-EdgePluginContractLedgerBehavior.ps1'),
            '-RepositoryRoot', $snapshot.root,
            '-BaselinePath', $ledgerRelativePath,
            '-AuthorityReceiptPath', $receiptRelativePath,
            '-AuthorityPublicKeySpkiBase64', [string]$descriptor.publicKeySpkiBase64,
            '-AuthorityRunId', $runId,
            '-AuthorityChallengeBase64', $challengeBase64,
            '-AuthoritySourceRepositoryRoot', $RepositoryRoot,
            '-AuthorityBindingsBase64', $behaviorBindingsBase64
        ) -TimeoutSeconds 600 -InputBytes $null -Environment $fastEnvironment
    $behaviorFailed = $behaviorResult.exitCode -ne 0
    $behaviorStderrSha256 = Get-EdgeSha256Bytes $behaviorResult.stderrBytes
    [byte[]]$behaviorOutputBytes = if ($behaviorFailed) {
        [byte[]]$behaviorResult.stderrBytes
    }
    else { [byte[]]$behaviorResult.stdoutBytes }
    if ($behaviorOutputBytes.Length -eq 0) {
        $emptyStream = if ($behaviorFailed) { 'stderr' } else { 'stdout' }
        throw "EDGE-SPLIT-AUTHORITY-DEV-BEHAVIOR behavior $emptyStream is empty; stderrSha256=$behaviorStderrSha256."
    }
    try {
        $behaviorOutput = ConvertFrom-DevUtf8 $behaviorOutputBytes `
            'EDGE-SPLIT-AUTHORITY-DEV-BEHAVIOR'
    }
    catch {
        if ($behaviorFailed) {
            throw "EDGE-SPLIT-AUTHORITY-DEV-BEHAVIOR behavior stderr is not strict UTF-8; stderrSha256=$behaviorStderrSha256."
        }
        throw
    }
    if ($behaviorFailed) {
        $behaviorReceiptRaw = [Text.UTF8Encoding]::new($false, $true).GetString(
            [IO.File]::ReadAllBytes($receiptPath))
        $behaviorReceipt = ConvertFrom-EdgeJsonText $behaviorReceiptRaw
        $behaviorForbiddenDiagnosticValues = @(
            $challengeBase64,
            [string]$descriptor.publicKeySpkiBase64,
            [string]$behaviorReceipt.signatureBase64,
            $behaviorBindingsBase64,
            $behaviorReceiptRaw)
        foreach ($forbiddenDiagnosticValue in $behaviorForbiddenDiagnosticValues) {
            if (-not [string]::IsNullOrEmpty([string]$forbiddenDiagnosticValue) -and
                $behaviorOutput.Contains(
                    [string]$forbiddenDiagnosticValue, [StringComparison]::Ordinal)) {
                throw "EDGE-SPLIT-AUTHORITY-DEV-BEHAVIOR sensitive child stderr was rejected; stderrSha256=$behaviorStderrSha256."
            }
        }

        $behaviorDiagnosticRows = @(
            [pscustomobject]@{
                name = 'codes'
                pattern = '(?<![A-Z0-9-])EDGE-[A-Z0-9-]{3,96}(?![A-Z0-9-])'
            },
            [pscustomobject]@{
                name = 'fixtures'
                pattern = "(?i:(?:negative|commit-pair|replay misuse) fixture) '(?<value>[a-z0-9][a-z0-9-]{0,95})'"
            },
            [pscustomobject]@{
                name = 'errorIds'
                pattern = 'FullyQualifiedErrorId\s*:\s*(?<value>[A-Za-z][A-Za-z0-9.,_-]{0,127})'
            },
            [pscustomobject]@{
                name = 'lines'
                pattern = 'Test-EdgePluginContractLedgerBehavior\.ps1:(?<value>[0-9]{1,6})'
            },
            [pscustomobject]@{
                name = 'methods'
                pattern = '(?<![A-Za-z0-9_.])(?<value>FixedTimeEquals)(?![A-Za-z0-9_.])'
            },
            [pscustomobject]@{
                name = 'arguments'
                pattern = '(?<![A-Za-z0-9_.])(?<value>right)(?![A-Za-z0-9_.])'
            })
        $behaviorDiagnosticParts = [Collections.Generic.List[string]]::new()
        foreach ($diagnosticRow in $behaviorDiagnosticRows) {
            $diagnosticValues = [Collections.Generic.List[string]]::new()
            $diagnosticSeen = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::Ordinal)
            foreach ($diagnosticMatch in [Text.RegularExpressions.Regex]::Matches(
                    $behaviorOutput, [string]$diagnosticRow.pattern,
                    [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                $diagnosticValue = if ($diagnosticMatch.Groups['value'].Success) {
                    [string]$diagnosticMatch.Groups['value'].Value
                }
                else { [string]$diagnosticMatch.Value }
                if ($diagnosticSeen.Add($diagnosticValue)) {
                    $diagnosticValues.Add($diagnosticValue)
                    if ($diagnosticValues.Count -eq 8) { break }
                }
            }
            $diagnosticText = if ($diagnosticValues.Count -eq 0) {
                'none'
            }
            else { [string]::Join(',', $diagnosticValues) }
            $behaviorDiagnosticParts.Add(
                ([string]$diagnosticRow.name + '=' + $diagnosticText))
        }
        throw "EDGE-SPLIT-AUTHORITY-DEV-BEHAVIOR fast behavior fixtures failed; stderrSha256=$behaviorStderrSha256; $([string]::Join('; ', $behaviorDiagnosticParts))."
    }
    $behaviorMarker =
        'Edge plugin contract ledger behavior fixtures passed: 53/53; authorityLaunches=0; replayLaunches=0.'
    $behaviorMarkerCount = @(
        [Text.RegularExpressions.Regex]::Split($behaviorOutput, '\r?\n') |
            Where-Object { $_ -ceq $behaviorMarker }
    ).Count
    if ($behaviorMarkerCount -ne 1) {
        throw "EDGE-SPLIT-AUTHORITY-DEV-BEHAVIOR exact behavior marker is missing or duplicated; stdoutSha256=$(Get-EdgeSha256Bytes $behaviorResult.stdoutBytes)."
    }

    $endManifest = Get-DevDirtyManifest $RepositoryRoot
    $endHead = Invoke-DevGitText $RepositoryRoot @('rev-parse', 'HEAD')
    $endTree = Invoke-DevGitText $RepositoryRoot @('rev-parse', 'HEAD^{tree}')
    if ($endHead -cne [string]$startManifest.value.sourceBaseHead -or
        $endTree -cne [string]$startManifest.value.sourceBaseTree -or
        $endManifest.sha256 -cne $startManifest.sha256 -or
        -not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals($endManifest.bytes, $startManifest.bytes)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-DRIFT source HEAD/tree/dirty manifest changed during development validation.'
    }
    $completedUtc = [DateTime]::UtcNow.ToString('O')
    $evidence = [pscustomobject][ordered]@{
        schemaVersion = 1
        ruleId = 'EDGE-SPLIT-LEDGER-001'
        mode = 'development-snapshot-non-formal'
        completedUtc = $completedUtc
        sourceBaseHead = [string]$startManifest.value.sourceBaseHead
        sourceBaseTree = [string]$startManifest.value.sourceBaseTree
        sourceDirtyManifestSha256 = [string]$startManifest.sha256
        ephemeralSnapshotHead = [string]$snapshot.head
        ephemeralSnapshotTree = [string]$snapshot.tree
        ledgerSha256 = Get-EdgeSha256File $ledgerPath
        receiptSha256 = [string]$descriptor.receiptSha256
        publicKeySha256 = [string]$descriptor.publicKeySha256
        authorityCount = 1
        replayCount = 1
        behaviorAuthorityLaunchCount = 0
        behaviorReplayLaunchCount = 0
        behaviorFixtureCount = 53
        descriptorPidBoundToDirectChild = $true
        descriptorStartBoundToDirectChild = $true
    }
    $evidenceRelativePath = '.artifacts/edge-plugin-contract-authority/development-validation.json'
    $evidencePath = Resolve-EdgeRepositoryPath $RepositoryRoot $evidenceRelativePath
    [void](New-Item -ItemType Directory -Path (Split-Path $evidencePath -Parent) -Force)
    [IO.File]::WriteAllText($evidencePath, (ConvertTo-EdgeCanonicalJson $evidence), [Text.UTF8Encoding]::new($false))
}
catch { $failure = $_ }
finally {
    try {
        if ($null -ne $coordinatorMarker) {
            Remove-EdgeDevelopmentCoordinatorRunState `
                -GitExecutablePath $gitPath `
                -AuthorityRepositoryRoot $snapshotRoot `
                -RunRoot $coordinatorRunRoot `
                -ValidatorWorktreePath $validatorWorktreePath `
                -ReplayWorktreePath $replayWorktreePath `
                -RunId $runId `
                -ParentMarkerPath $outerMarkerPath `
                -ParentMarkerExpected $outerMarker `
                -CoordinatorMarkerExpected $coordinatorMarker
        }
        elseif (Test-Path -LiteralPath $coordinatorRunRoot) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP coordinator state appeared before its exact marker binding existed.'
        }
    }
    catch {
        if ($null -eq $failure) { $failure = $_ }
    }
    try {
        Remove-EdgeDevelopmentOuterRunRoot `
            -RunRoot $outerRunRoot `
            -SnapshotRoot $snapshotRoot `
            -CoordinatorRunRoot $coordinatorRunRoot `
            -RunId $runId `
            -MarkerPath $outerMarkerPath `
            -MarkerExpected $outerMarker
    }
    catch {
        if ($null -eq $failure) { $failure = $_ }
    }
}

if ($null -ne $failure) { throw $failure }
}
catch { $tmpDirectoryFailure = $_ }
finally {
    try {
        if ($tmpDirectoryWasPresent) {
            [Environment]::SetEnvironmentVariable(
                'TMPDIR', $tmpDirectoryOriginalValue, [EnvironmentVariableTarget]::Process)
        }
        else {
            Remove-Item -LiteralPath 'Env:TMPDIR' -ErrorAction Stop
        }
        $tmpDirectoryRestorationEnvironment =
            [Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Process)
        if ($tmpDirectoryRestorationEnvironment.Contains('TMPDIR') -ne $tmpDirectoryWasPresent) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-TEMP TMPDIR restoration did not preserve the original presence.'
        }
        if ($tmpDirectoryWasPresent -and
            [string]$tmpDirectoryRestorationEnvironment['TMPDIR'] -cne $tmpDirectoryOriginalValue) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-TEMP TMPDIR restoration did not preserve the original value.'
        }
    }
    catch {
        if ($null -eq $tmpDirectoryFailure) { $tmpDirectoryFailure = $_ }
    }
}

if ($null -ne $tmpDirectoryFailure) { throw $tmpDirectoryFailure }
$developmentValidationResult = [pscustomobject][ordered]@{
    schemaVersion = 1
    owner = 'scripts/tests/Invoke-EdgePluginContractDevelopmentValidation.ps1'
    passed = $true
    staticGuardOwner = [string]$staticGuardResult.owner
    staticGuardScope = [string]$staticGuardResult.scope
    staticGuardDevelopmentSha256 = [string]$staticGuardResult.sourceDigests.development
    staticGuardFormalSha256 = [string]$staticGuardResult.sourceDigests.formal
    primitivesPassed = 25
    primitivesTotal = 25
    behaviorPassed = 53
    behaviorTotal = 53
    authorityLaunches = 1
    replayLaunches = 1
    behaviorAuthorityLaunches = 0
    behaviorReplayLaunches = 0
    formal = $false
}
Write-Output (ConvertTo-EdgeCanonicalJson $developmentValidationResult)
