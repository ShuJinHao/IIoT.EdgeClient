Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not ('EdgeAuthorityBoundedStreamCapture' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Threading.Tasks;

public static class EdgeAuthorityBoundedStreamCapture
{
    public static async Task<byte[]> ReadAsync(Stream source, int maximumBytes)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        using var destination = new MemoryStream(Math.Min(maximumBytes, 65536));
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > maximumBytes)
                throw new InvalidDataException("EDGE-SPLIT-AUTHORITY-OUTPUT-LIMIT captured child stream exceeded its byte limit.");
            destination.Write(buffer, 0, read);
        }
        return destination.ToArray();
    }
}
'@
}

function Wait-EdgeBoundedCaptureTasks {
    param(
        [Parameter(Mandatory)][Threading.Tasks.Task]$StdoutTask,
        [Parameter(Mandatory)][Threading.Tasks.Task]$StderrTask,
        [Parameter(Mandatory)][DateTimeOffset]$DeadlineUtc,
        [Parameter(Mandatory)][string]$ErrorCode
    )

    while (-not $StdoutTask.IsCompleted -or -not $StderrTask.IsCompleted) {
        if ($StdoutTask.IsFaulted -or $StderrTask.IsFaulted -or
            $StdoutTask.IsCanceled -or $StderrTask.IsCanceled) {
            throw "$ErrorCode bounded child stream capture faulted."
        }
        if ([DateTimeOffset]::UtcNow -ge $DeadlineUtc) {
            throw "$ErrorCode child exited but an inherited stdout/stderr pipe remained open past the deadline."
        }
        Start-Sleep -Milliseconds 25
    }
    if (-not $StdoutTask.IsCompletedSuccessfully -or -not $StderrTask.IsCompletedSuccessfully) {
        throw "$ErrorCode bounded child stream capture did not complete successfully."
    }
    return [pscustomobject][ordered]@{
        stdoutBytes = [byte[]]$StdoutTask.GetAwaiter().GetResult()
        stderrBytes = [byte[]]$StderrTask.GetAwaiter().GetResult()
    }
}

function Get-EdgeSha256Bytes {
    param([Parameter(Mandatory)][AllowEmptyCollection()][byte[]]$Bytes)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
}

function Get-EdgeSha256Text {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    return Get-EdgeSha256Bytes ([Text.UTF8Encoding]::new($false).GetBytes($Text))
}

function Get-EdgeSha256File {
    param([Parameter(Mandatory)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Sort-EdgeOrdinalStrings {
    param([Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Values)
    $copy = [string[]]@($Values)
    [Array]::Sort($copy, [StringComparer]::Ordinal)
    return $copy
}

function Test-EdgeByteArrayEqual {
    param([Parameter(Mandatory)][byte[]]$Left, [Parameter(Mandatory)][byte[]]$Right)
    if ($Left.Length -ne $Right.Length) { return $false }
    return [Security.Cryptography.CryptographicOperations]::FixedTimeEquals($Left, $Right)
}

function Test-EdgeAuthorityCriticalGitEnvironmentName {
    param([Parameter(Mandatory)][string]$Name)

    $criticalNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($criticalName in @(
            'GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_OBJECT_DIRECTORY',
            'GIT_ALTERNATE_OBJECT_DIRECTORIES', 'GIT_COMMON_DIR', 'GIT_CONFIG',
            'GIT_CONFIG_SYSTEM', 'GIT_CONFIG_GLOBAL', 'GIT_CONFIG_NOSYSTEM',
            'GIT_CONFIG_COUNT', 'GIT_CONFIG_PARAMETERS', 'GIT_CEILING_DIRECTORIES',
            'GIT_DISCOVERY_ACROSS_FILESYSTEM', 'GIT_NAMESPACE', 'GIT_REPLACE_REF_BASE',
            'GIT_NO_REPLACE_OBJECTS', 'GIT_SHALLOW_FILE', 'GIT_GRAFT_FILE',
            'GIT_EXEC_PATH', 'GIT_DEFAULT_HASH', 'GIT_LITERAL_PATHSPECS',
            'GIT_GLOB_PATHSPECS', 'GIT_NOGLOB_PATHSPECS', 'GIT_ICASE_PATHSPECS',
            'GIT_EXTERNAL_DIFF', 'GIT_DIFF_OPTS', 'GIT_ATTR_NOSYSTEM',
            'GIT_QUARANTINE_PATH')) {
        [void]$criticalNames.Add($criticalName)
    }
    return $criticalNames.Contains($Name) -or
        $Name.StartsWith('GIT_CONFIG_KEY_', [StringComparison]::OrdinalIgnoreCase) -or
        $Name.StartsWith('GIT_CONFIG_VALUE_', [StringComparison]::OrdinalIgnoreCase)
}

function Assert-EdgeAuthorityNoCriticalGitEnvironment {
    # Git environment names are case-insensitive on Windows.  Reject mixed-case
    # spellings on every platform so this guard has one conservative contract.
    foreach ($entry in [Environment]::GetEnvironmentVariables('Process').GetEnumerator()) {
        $name = [string]$entry.Key
        $value = [string]$entry.Value
        if ([string]::IsNullOrEmpty($value)) { continue }
        if (Test-EdgeAuthorityCriticalGitEnvironmentName $name) {
            throw "EDGE-SPLIT-AUTHORITY-GIT-ENV authority-critical git environment override is forbidden: $name."
        }
    }
}

function Assert-EdgeAuthorityGitEnvironment {
    Assert-EdgeAuthorityNoCriticalGitEnvironment
    foreach ($entry in [Environment]::GetEnvironmentVariables('Process').GetEnumerator()) {
        $name = [string]$entry.Key
        $value = [string]$entry.Value
        if ([string]::IsNullOrEmpty($value)) { continue }
        if ($name.StartsWith('EDGE_AUTHORITY_COORDINATOR_', [StringComparison]::OrdinalIgnoreCase) -or
            $name.StartsWith('EDGE_AUTHORITY_CHILD_', [StringComparison]::OrdinalIgnoreCase)) {
            throw "EDGE-SPLIT-AUTHORITY-GIT-ENV reserved authority binding is forbidden at external ingress: $name."
        }
    }
}

function Assert-EdgeAuthorityEmptyGitConfig {
    param([Parameter(Mandatory)][string]$RepositoryRoot)

    $path = Resolve-EdgeRepositoryPath $RepositoryRoot 'eng/edge-authority-empty.gitconfig'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'EDGE-SPLIT-AUTHORITY-GIT-CONFIG fixed empty Git config is missing.'
    }
    $expected = [Text.UTF8Encoding]::new($false).GetBytes(
        "# Intentionally contains no Git settings; authority processes pin this file.`n")
    if (-not (Test-EdgeByteArrayEqual ([IO.File]::ReadAllBytes($path)) $expected)) {
        throw 'EDGE-SPLIT-AUTHORITY-GIT-CONFIG fixed empty Git config bytes changed.'
    }
    return $path
}

function New-EdgeAuthorityGitChildEnvironment {
    param(
        [Parameter(Mandatory)][string]$EmptyConfigPath,
        [Parameter(Mandatory)][string]$FixedGitExecutablePath
    )

    $path = [IO.Path]::GetFullPath($EmptyConfigPath)
    $item = Get-Item -LiteralPath $path -Force
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-GIT-CONFIG fixed empty Git config became missing or indirect.'
    }
    $gitPath = Assert-EdgeAuthorityFinalGitExecutablePath $FixedGitExecutablePath
    $pinnedPath = Get-EdgeAuthorityPinnedPath $gitPath
    $binding = [pscustomobject][ordered]@{
        schemaVersion = 1
        fixedGitExecutablePath = $gitPath
        pinnedPath = $pinnedPath
        pinnedPathSha256 = Get-EdgeSha256Text $pinnedPath
        emptyGitConfigPath = $path
        emptyGitConfigSha256 = Get-EdgeSha256File $path
    }
    return [ordered]@{
        EDGE_AUTHORITY_COORDINATOR_PARENT_BINDING_BASE64 = ''
        EDGE_AUTHORITY_CHILD_BINDING_BASE64 = [Convert]::ToBase64String(
            (ConvertTo-EdgeCanonicalBytes $binding))
        GIT_CONFIG_GLOBAL = $path
        GIT_CONFIG_SYSTEM = $path
        GIT_CONFIG_NOSYSTEM = '1'
        GIT_CONFIG_COUNT = '1'
        GIT_CONFIG_KEY_0 = 'core.hooksPath'
        GIT_CONFIG_VALUE_0 = $path
        PATH = $pinnedPath
    }
}

function Assert-EdgeAuthorityFinalGitExecutablePath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $resolvedPath = Resolve-EdgeFixedExecutable $fullPath
    $comparison = if ([OperatingSystem]::IsWindows()) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else { [StringComparison]::Ordinal }
    if (-not [string]::Equals($Path, $fullPath, $comparison) -or
        -not [string]::Equals($resolvedPath, $fullPath, $comparison)) {
        throw 'EDGE-SPLIT-AUTHORITY-GIT-BINDING fixed Git must be an absolute normalized final executable path.'
    }
    $item = Get-Item -LiteralPath $fullPath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-GIT-BINDING fixed Git must be a direct regular file.'
    }
    return $fullPath
}

function Get-EdgeAuthorityPinnedPath {
    param([Parameter(Mandatory)][string]$FixedGitExecutablePath)

    $gitPath = Assert-EdgeAuthorityFinalGitExecutablePath $FixedGitExecutablePath
    $fixedGitDirectory = Split-Path $gitPath -Parent
    $existingPath = [Environment]::GetEnvironmentVariable('PATH', 'Process')
    if ([string]::IsNullOrEmpty($existingPath)) { return $fixedGitDirectory }
    return "$fixedGitDirectory$([IO.Path]::PathSeparator)$existingPath"
}

function Assert-EdgeAuthorityExactPropertyNames {
    param(
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string[]]$ExpectedNames,
        [Parameter(Mandatory)][string]$ErrorCode
    )

    if ($Value -isnot [Management.Automation.PSCustomObject]) {
        throw "$ErrorCode binding must be a JSON object."
    }
    $actualNames = [string[]]@($Value.PSObject.Properties.Name)
    if ($actualNames.Length -ne $ExpectedNames.Length) {
        throw "$ErrorCode binding property inventory differs."
    }
    for ($index = 0; $index -lt $ExpectedNames.Length; $index++) {
        if ($actualNames[$index] -cne $ExpectedNames[$index]) {
            throw "$ErrorCode binding property inventory/order differs."
        }
    }
}

function ConvertFrom-EdgeAuthorityCanonicalEnvironmentBinding {
    param(
        [Parameter(Mandatory)][string]$Base64,
        [Parameter(Mandatory)][string]$ErrorCode
    )

    try { $bytes = [Convert]::FromBase64String($Base64) }
    catch { throw "$ErrorCode binding is not valid base64." }
    $raw = ConvertFrom-EdgeStrictUtf8Bytes -RawBytes $bytes -ErrorCode $ErrorCode -MaximumBytes 65536
    try { $binding = ConvertFrom-EdgeJsonText $raw }
    catch { throw "$ErrorCode binding is not strict JSON." }
    if (-not (Test-EdgeByteArrayEqual $bytes (ConvertTo-EdgeCanonicalBytes $binding))) {
        throw "$ErrorCode binding is not exact canonical JSON bytes."
    }
    return $binding
}

function New-EdgeAuthorityCoordinatorParentEnvironment {
    param(
        [Parameter(Mandatory)]$OuterMarker,
        [Parameter(Mandatory)][string]$ParentMarkerPath,
        [Parameter(Mandatory)][string]$FixedGitExecutablePath,
        [Parameter(Mandatory)][string]$PinnedPath
    )

    $gitPath = Assert-EdgeAuthorityFinalGitExecutablePath $FixedGitExecutablePath
    $normalizedMarkerPath = [IO.Path]::GetFullPath($ParentMarkerPath)
    $pinnedPathSha256 = Get-EdgeSha256Text $PinnedPath
    $isDevelopmentBinding = [int]$OuterMarker.schemaVersion -eq 2
    $isFormalBinding = [int]$OuterMarker.schemaVersion -eq 3 -and
        [string]$OuterMarker.mode -ceq 'formal-clean'
    if ((-not $isDevelopmentBinding -and -not $isFormalBinding) -or
        [string]$OuterMarker.fixedGitExecutablePath -cne $gitPath -or
        [string]$OuterMarker.pinnedPathSha256 -cne $pinnedPathSha256) {
        throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING outer marker does not bind the final Git/PATH identity.'
    }
    Assert-EdgeExactCanonicalMarker -MarkerPath $normalizedMarkerPath -Expected $OuterMarker `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING'
    $binding = if ($isFormalBinding) {
        [pscustomobject][ordered]@{
            schemaVersion = 2
            mode = 'formal-clean'
            runId = [string]$OuterMarker.runId
            sourceRepositoryRoot = [string]$OuterMarker.sourceRepositoryRoot
            authorityRepositoryRoot = [string]$OuterMarker.authorityRepositoryRoot
            outerRunRoot = [string]$OuterMarker.outerRunRoot
            coordinatorRunRoot = [string]$OuterMarker.coordinatorRunRoot
            validatorWorktreePath = [string]$OuterMarker.validatorWorktreePath
            replayWorktreePath = [string]$OuterMarker.replayWorktreePath
            parentMarkerPath = $normalizedMarkerPath
            parentMarkerSha256 = Get-EdgeSha256File $normalizedMarkerPath
            fixedGitExecutablePath = $gitPath
            pinnedPath = $PinnedPath
            pinnedPathSha256 = $pinnedPathSha256
        }
    }
    else {
        [pscustomobject][ordered]@{
            schemaVersion = 1
            runId = [string]$OuterMarker.runId
            sourceRepositoryRoot = [string]$OuterMarker.sourceRepositoryRoot
            authorityRepositoryRoot = [string]$OuterMarker.authorityRepositoryRoot
            snapshotRoot = [string]$OuterMarker.snapshotRoot
            coordinatorRunRoot = [string]$OuterMarker.coordinatorRunRoot
            validatorWorktreePath = [string]$OuterMarker.validatorWorktreePath
            replayWorktreePath = [string]$OuterMarker.replayWorktreePath
            parentMarkerPath = $normalizedMarkerPath
            parentMarkerSha256 = Get-EdgeSha256File $normalizedMarkerPath
            fixedGitExecutablePath = $gitPath
            pinnedPath = $PinnedPath
            pinnedPathSha256 = $pinnedPathSha256
        }
    }
    return [ordered]@{
        EDGE_AUTHORITY_COORDINATOR_PARENT_BINDING_BASE64 = [Convert]::ToBase64String(
            (ConvertTo-EdgeCanonicalBytes $binding))
        EDGE_AUTHORITY_CHILD_BINDING_BASE64 = ''
        PATH = $PinnedPath
    }
}

function Initialize-EdgeAuthorityCoordinatorParentEnvironment {
    Assert-EdgeAuthorityNoCriticalGitEnvironment
    foreach ($entry in [Environment]::GetEnvironmentVariables('Process').GetEnumerator()) {
        $name = [string]$entry.Key
        $value = [string]$entry.Value
        if ($name.StartsWith('EDGE_AUTHORITY_COORDINATOR_', [StringComparison]::OrdinalIgnoreCase) -and
            $name -cne 'EDGE_AUTHORITY_COORDINATOR_PARENT_BINDING_BASE64' -and
            -not [string]::IsNullOrEmpty($value)) {
            throw "EDGE-SPLIT-AUTHORITY-PARENT-BINDING noncanonical/extra coordinator binding is forbidden: $name."
        }
        if ($name.StartsWith('EDGE_AUTHORITY_CHILD_', [StringComparison]::OrdinalIgnoreCase) -and
            ($name -cne 'EDGE_AUTHORITY_CHILD_BINDING_BASE64' -or
                -not [string]::IsNullOrEmpty($value))) {
            throw "EDGE-SPLIT-AUTHORITY-PARENT-BINDING child binding is forbidden at coordinator ingress: $name."
        }
    }
    $childBinding = [Environment]::GetEnvironmentVariable(
        'EDGE_AUTHORITY_CHILD_BINDING_BASE64', 'Process')
    if (-not [string]::IsNullOrEmpty($childBinding)) {
        throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING child binding is forbidden at coordinator ingress.'
    }
    $bindingBase64 = [Environment]::GetEnvironmentVariable(
        'EDGE_AUTHORITY_COORDINATOR_PARENT_BINDING_BASE64', 'Process')
    if ([string]::IsNullOrWhiteSpace($bindingBase64)) {
        throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING canonical parent binding is missing.'
    }
    $binding = ConvertFrom-EdgeAuthorityCanonicalEnvironmentBinding $bindingBase64 `
        'EDGE-SPLIT-AUTHORITY-PARENT-BINDING'
    $bindingSchemaVersion = [int]$binding.schemaVersion
    if ($bindingSchemaVersion -eq 1) {
        Assert-EdgeAuthorityExactPropertyNames $binding @(
            'authorityRepositoryRoot', 'coordinatorRunRoot', 'fixedGitExecutablePath',
            'parentMarkerPath', 'parentMarkerSha256', 'pinnedPath', 'pinnedPathSha256',
            'replayWorktreePath', 'runId', 'schemaVersion', 'snapshotRoot',
            'sourceRepositoryRoot', 'validatorWorktreePath') 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING'
    }
    elseif ($bindingSchemaVersion -eq 2) {
        Assert-EdgeAuthorityExactPropertyNames $binding @(
            'authorityRepositoryRoot', 'coordinatorRunRoot', 'fixedGitExecutablePath',
            'mode', 'outerRunRoot', 'parentMarkerPath', 'parentMarkerSha256',
            'pinnedPath', 'pinnedPathSha256', 'replayWorktreePath', 'runId',
            'schemaVersion', 'sourceRepositoryRoot', 'validatorWorktreePath') `
            'EDGE-SPLIT-AUTHORITY-PARENT-BINDING'
    }
    else {
        throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING canonical parent binding version is unsupported.'
    }
    if ([string]$binding.runId -notmatch '^[0-9a-f]{32}$' -or
        [string]$binding.parentMarkerSha256 -notmatch '^[0-9a-f]{64}$' -or
        [string]$binding.pinnedPathSha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING canonical parent binding values are malformed.'
    }
    $gitPath = Assert-EdgeAuthorityFinalGitExecutablePath ([string]$binding.fixedGitExecutablePath)
    $pinnedPath = [string]$binding.pinnedPath
    if ((Get-EdgeSha256Text $pinnedPath) -cne [string]$binding.pinnedPathSha256) {
        throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING pinned PATH digest differs.'
    }
    $comparison = if ([OperatingSystem]::IsWindows()) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else { [StringComparison]::Ordinal }
    $pathSegments = [string[]]$pinnedPath.Split([IO.Path]::PathSeparator)
    if ($pathSegments.Length -eq 0 -or
        -not [string]::Equals($pathSegments[0], (Split-Path $gitPath -Parent), $comparison)) {
        throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING fixed Git directory is not first in the parent-bound PATH.'
    }
    $expectedMarker = if ($bindingSchemaVersion -eq 2) {
        [pscustomobject][ordered]@{
            schemaVersion = 3
            mode = 'formal-clean'
            runId = [string]$binding.runId
            sourceRepositoryRoot = [string]$binding.sourceRepositoryRoot
            authorityRepositoryRoot = [string]$binding.authorityRepositoryRoot
            outerRunRoot = [string]$binding.outerRunRoot
            coordinatorRunRoot = [string]$binding.coordinatorRunRoot
            validatorWorktreePath = [string]$binding.validatorWorktreePath
            replayWorktreePath = [string]$binding.replayWorktreePath
            fixedGitExecutablePath = $gitPath
            pinnedPathSha256 = [string]$binding.pinnedPathSha256
        }
    }
    else {
        [pscustomobject][ordered]@{
            schemaVersion = 2
            runId = [string]$binding.runId
            sourceRepositoryRoot = [string]$binding.sourceRepositoryRoot
            authorityRepositoryRoot = [string]$binding.authorityRepositoryRoot
            snapshotRoot = [string]$binding.snapshotRoot
            coordinatorRunRoot = [string]$binding.coordinatorRunRoot
            validatorWorktreePath = [string]$binding.validatorWorktreePath
            replayWorktreePath = [string]$binding.replayWorktreePath
            fixedGitExecutablePath = $gitPath
            pinnedPathSha256 = [string]$binding.pinnedPathSha256
        }
    }
    $parentMarkerPath = [IO.Path]::GetFullPath([string]$binding.parentMarkerPath)
    Assert-EdgeExactCanonicalMarker -MarkerPath $parentMarkerPath -Expected $expectedMarker `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING'
    if ((Get-EdgeSha256File $parentMarkerPath) -cne [string]$binding.parentMarkerSha256) {
        throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING outer marker digest differs.'
    }
    if ($bindingSchemaVersion -eq 2) {
        $outer = Assert-EdgeExactTemporaryRunPath -RunRoot ([string]$binding.outerRunRoot) `
            -Candidate ([string]$binding.outerRunRoot) -RunId ([string]$binding.runId) -AllowRunRoot
        $coordinator = Assert-EdgeExactTemporaryRunPath -RunRoot $outer `
            -Candidate ([string]$binding.coordinatorRunRoot) -RunId ([string]$binding.runId)
        [void](Assert-EdgeExactTemporaryRunPath -RunRoot $coordinator `
                -Candidate ([string]$binding.validatorWorktreePath) -RunId ([string]$binding.runId))
        [void](Assert-EdgeExactTemporaryRunPath -RunRoot $coordinator `
                -Candidate ([string]$binding.replayWorktreePath) -RunId ([string]$binding.runId))
        $expectedMarkerPath = Join-Path $outer '.edge-formal-authority-run.json'
        if ([string]$binding.mode -cne 'formal-clean' -or
            -not (Test-EdgePathIdentity $expectedMarkerPath $parentMarkerPath) -or
            -not (Test-EdgePathIdentity ([string]$binding.sourceRepositoryRoot) `
                ([string]$binding.authorityRepositoryRoot))) {
            throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING formal outer marker path/root bindings differ.'
        }
    }
    else {
        $expectedMarkerPath = Join-Path (Split-Path $parentMarkerPath -Parent) `
            '.edge-development-authority-run.json'
        if (-not (Test-EdgePathIdentity $expectedMarkerPath $parentMarkerPath) -or
            -not (Test-EdgePathIdentity ([string]$binding.snapshotRoot) `
                ([string]$binding.authorityRepositoryRoot))) {
            throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING development outer marker path/root bindings differ.'
        }
    }

    # pwsh prepends PSHOME while starting. Restore the exact parent-owned PATH and
    # prove command discovery still resolves to the separately bound final Git.
    [Environment]::SetEnvironmentVariable('PATH', $pinnedPath, 'Process')
    $resolvedGit = @(Get-Command git -CommandType Application -ErrorAction Stop)[0]
    if (-not [string]::Equals(
            [IO.Path]::GetFullPath([string]$resolvedGit.Source), $gitPath, $comparison)) {
        throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING PATH does not resolve to the parent-bound Git executable.'
    }
    return $binding
}

function Assert-EdgeAuthorityCoordinatorParentRequest {
    param(
        [Parameter(Mandatory)]$Binding,
        [Parameter(Mandatory)]$Request
    )

    $bindingMode = if ([int]$Binding.schemaVersion -eq 2) {
        [string]$Binding.mode
    }
    else { 'development-snapshot' }
    if ($bindingMode -cne [string]$Request.mode -or
        [string]$Binding.runId -cne [string]$Request.runId -or
        -not (Test-EdgePathIdentity ([string]$Binding.sourceRepositoryRoot) ([string]$Request.sourceRepositoryRoot)) -or
        -not (Test-EdgePathIdentity ([string]$Binding.authorityRepositoryRoot) ([string]$Request.authorityRepositoryRoot)) -or
        -not (Test-EdgePathIdentity ([string]$Binding.coordinatorRunRoot) ([string]$Request.runRoot)) -or
        -not (Test-EdgePathIdentity ([string]$Binding.validatorWorktreePath) ([string]$Request.validatorWorktreePath)) -or
        -not (Test-EdgePathIdentity ([string]$Binding.replayWorktreePath) ([string]$Request.replayWorktreePath))) {
        throw 'EDGE-SPLIT-AUTHORITY-PARENT-BINDING request differs from the exact canonical outer parent binding.'
    }
}

function Initialize-EdgeAuthorityGitChildEnvironment {
    foreach ($entry in [Environment]::GetEnvironmentVariables('Process').GetEnumerator()) {
        $name = [string]$entry.Key
        $value = [string]$entry.Value
        if ($name.StartsWith('EDGE_AUTHORITY_COORDINATOR_', [StringComparison]::OrdinalIgnoreCase) -and
            ($name -cne 'EDGE_AUTHORITY_COORDINATOR_PARENT_BINDING_BASE64' -or
                -not [string]::IsNullOrEmpty($value))) {
            throw "EDGE-SPLIT-AUTHORITY-CHILD-BINDING coordinator parent binding leaked into an authority child: $name."
        }
        if ($name.StartsWith('EDGE_AUTHORITY_CHILD_', [StringComparison]::OrdinalIgnoreCase) -and
            $name -cne 'EDGE_AUTHORITY_CHILD_BINDING_BASE64' -and
            -not [string]::IsNullOrEmpty($value)) {
            throw "EDGE-SPLIT-AUTHORITY-CHILD-BINDING noncanonical/extra child binding is forbidden: $name."
        }
    }
    $coordinatorBinding = [Environment]::GetEnvironmentVariable(
        'EDGE_AUTHORITY_COORDINATOR_PARENT_BINDING_BASE64', 'Process')
    if (-not [string]::IsNullOrEmpty($coordinatorBinding)) {
        throw 'EDGE-SPLIT-AUTHORITY-CHILD-BINDING coordinator parent binding leaked into an authority child.'
    }
    $bindingBase64 = [Environment]::GetEnvironmentVariable(
        'EDGE_AUTHORITY_CHILD_BINDING_BASE64', 'Process')
    if ([string]::IsNullOrEmpty($bindingBase64)) { return $false }
    $binding = ConvertFrom-EdgeAuthorityCanonicalEnvironmentBinding $bindingBase64 `
        'EDGE-SPLIT-AUTHORITY-CHILD-BINDING'
    Assert-EdgeAuthorityExactPropertyNames $binding @(
        'emptyGitConfigPath', 'emptyGitConfigSha256', 'fixedGitExecutablePath',
        'pinnedPath', 'pinnedPathSha256', 'schemaVersion') 'EDGE-SPLIT-AUTHORITY-CHILD-BINDING'
    if ([int]$binding.schemaVersion -ne 1 -or
        [string]$binding.pinnedPathSha256 -notmatch '^[0-9a-f]{64}$' -or
        [string]$binding.emptyGitConfigSha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'EDGE-SPLIT-AUTHORITY-CHILD-BINDING canonical child binding values are malformed.'
    }
    $gitPath = Assert-EdgeAuthorityFinalGitExecutablePath ([string]$binding.fixedGitExecutablePath)
    $configPath = [IO.Path]::GetFullPath([string]$binding.emptyGitConfigPath)
    $comparison = if ([OperatingSystem]::IsWindows()) {
        [StringComparison]::OrdinalIgnoreCase
    }
    else { [StringComparison]::Ordinal }
    if (-not [string]::Equals([string]$binding.emptyGitConfigPath, $configPath, $comparison) -or
        -not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        throw 'EDGE-SPLIT-AUTHORITY-CHILD-BINDING fixed empty Git config path is not a direct absolute file.'
    }
    $configItem = Get-Item -LiteralPath $configPath -Force
    if (($configItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$configItem.LinkTarget) -or
        (Get-EdgeSha256File $configPath) -cne [string]$binding.emptyGitConfigSha256) {
        throw 'EDGE-SPLIT-AUTHORITY-CHILD-BINDING fixed empty Git config identity differs.'
    }
    $expectedConfigBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        "# Intentionally contains no Git settings; authority processes pin this file.`n")
    if (-not (Test-EdgeByteArrayEqual ([IO.File]::ReadAllBytes($configPath)) $expectedConfigBytes)) {
        throw 'EDGE-SPLIT-AUTHORITY-CHILD-BINDING fixed empty Git config bytes changed.'
    }
    $allowed = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    $allowed.Add('GIT_CONFIG_GLOBAL', $configPath)
    $allowed.Add('GIT_CONFIG_SYSTEM', $configPath)
    $allowed.Add('GIT_CONFIG_NOSYSTEM', '1')
    $allowed.Add('GIT_CONFIG_COUNT', '1')
    $allowed.Add('GIT_CONFIG_KEY_0', 'core.hooksPath')
    $allowed.Add('GIT_CONFIG_VALUE_0', $configPath)
    foreach ($entry in [Environment]::GetEnvironmentVariables('Process').GetEnumerator()) {
        $name = [string]$entry.Key
        $value = [string]$entry.Value
        if ([string]::IsNullOrEmpty($value) -or -not (Test-EdgeAuthorityCriticalGitEnvironmentName $name)) {
            continue
        }
        if (-not $allowed.ContainsKey($name) -or
            $name -cne @($allowed.Keys | Where-Object { $_ -ieq $name })[0] -or
            $value -cne $allowed[$name]) {
            throw "EDGE-SPLIT-AUTHORITY-CHILD-BINDING unbound or altered Git environment item is forbidden: $name."
        }
    }
    foreach ($name in $allowed.Keys) {
        if ([Environment]::GetEnvironmentVariable($name, 'Process') -cne $allowed[$name]) {
            throw "EDGE-SPLIT-AUTHORITY-CHILD-BINDING exact controlled Git environment item is missing: $name."
        }
    }
    $pinnedPath = [string]$binding.pinnedPath
    if ((Get-EdgeSha256Text $pinnedPath) -cne [string]$binding.pinnedPathSha256) {
        throw 'EDGE-SPLIT-AUTHORITY-CHILD-BINDING pinned PATH digest differs.'
    }
    $pathSegments = [string[]]$pinnedPath.Split([IO.Path]::PathSeparator)
    if ($pathSegments.Length -eq 0 -or
        -not [string]::Equals($pathSegments[0], (Split-Path $gitPath -Parent), $comparison)) {
        throw 'EDGE-SPLIT-AUTHORITY-CHILD-BINDING fixed Git directory is not first in the child-bound PATH.'
    }
    [Environment]::SetEnvironmentVariable('PATH', $pinnedPath, 'Process')
    $resolvedGit = @(Get-Command git -CommandType Application -ErrorAction Stop)[0]
    if (-not [string]::Equals(
            [IO.Path]::GetFullPath([string]$resolvedGit.Source), $gitPath, $comparison)) {
        throw 'EDGE-SPLIT-AUTHORITY-CHILD-BINDING PATH does not resolve to the child-bound Git executable.'
    }
    return $true
}

function Resolve-EdgeFixedExecutable {
    param([Parameter(Mandatory)][string]$Path)
    $comparison = if ([OperatingSystem]::IsWindows()) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
    $seen = [Collections.Generic.HashSet[string]]::new($comparison)
    $current = [IO.Path]::GetFullPath($Path)
    for ($depth = 0; $depth -lt 16; $depth++) {
        if (-not $seen.Add($current)) {
            throw 'EDGE-SPLIT-AUTHORITY-EXECUTABLE-001 executable symlink/reparse chain contains a loop.'
        }
        if (-not (Test-Path -LiteralPath $current -PathType Leaf)) {
            throw 'EDGE-SPLIT-AUTHORITY-EXECUTABLE-001 executable target is missing or not a regular file.'
        }
        $item = Get-Item -LiteralPath $current -Force
        $isLink = ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)
        if (-not $isLink) { return $current }
        $target = [string]$item.LinkTarget
        if ([string]::IsNullOrWhiteSpace($target)) {
            try { $target = [string]$item.ResolveLinkTarget($false).FullName }
            catch { throw 'EDGE-SPLIT-AUTHORITY-EXECUTABLE-001 executable reparse target cannot be resolved.' }
        }
        $current = if ([IO.Path]::IsPathRooted($target)) {
            [IO.Path]::GetFullPath($target)
        }
        else { [IO.Path]::GetFullPath((Join-Path (Split-Path $current -Parent) $target)) }
    }
    throw 'EDGE-SPLIT-AUTHORITY-EXECUTABLE-001 executable symlink/reparse chain exceeds 16 hops.'
}

function ConvertFrom-EdgeStrictUtf8Bytes {
    param(
        [Parameter(Mandatory)][byte[]]$RawBytes,
        [Parameter(Mandatory)][string]$ErrorCode,
        [long]$MaximumBytes = 1048576
    )

    if ($RawBytes.Length -eq 0) {
        throw "$ErrorCode JSON document is empty."
    }
    if ($RawBytes.Length -gt $MaximumBytes) {
        throw "$ErrorCode JSON document exceeds the bounded byte limit."
    }
    if ($RawBytes.Length -ge 3 -and $RawBytes[0] -eq 0xEF -and $RawBytes[1] -eq 0xBB -and $RawBytes[2] -eq 0xBF) {
        throw "$ErrorCode UTF-8 BOM is forbidden."
    }
    try { $raw = [Text.UTF8Encoding]::new($false, $true).GetString($RawBytes) }
    catch { throw "$ErrorCode JSON bytes are not strict UTF-8." }
    if ($raw.Contains("`r", [StringComparison]::Ordinal)) {
        throw "$ErrorCode CR/CRLF bytes are forbidden."
    }
    return $raw
}

function ConvertFrom-EdgeJsonElement {
    param([Parameter(Mandatory)][Text.Json.JsonElement]$Element)

    switch ($Element.ValueKind) {
        ([Text.Json.JsonValueKind]::Object) {
            $result = [ordered]@{}
            foreach ($property in $Element.EnumerateObject()) {
                if ($result.Contains([string]$property.Name)) {
                    throw "EDGE-SPLIT-AUTHORITY-CANONICAL-001 duplicate JSON property is forbidden: $($property.Name)."
                }
                $result[[string]$property.Name] = ConvertFrom-EdgeJsonElement $property.Value
            }
            return [pscustomobject]$result
        }
        ([Text.Json.JsonValueKind]::Array) {
            $result = [Collections.Generic.List[object]]::new()
            foreach ($item in $Element.EnumerateArray()) {
                $result.Add((ConvertFrom-EdgeJsonElement $item))
            }
            return ,([object[]]$result.ToArray())
        }
        ([Text.Json.JsonValueKind]::String) { return [string]$Element.GetString() }
        ([Text.Json.JsonValueKind]::Number) {
            $signed = 0L
            if ($Element.TryGetInt64([ref]$signed)) { return $signed }
            $unsigned = 0UL
            if ($Element.TryGetUInt64([ref]$unsigned)) { return $unsigned }
            throw 'EDGE-SPLIT-AUTHORITY-CANONICAL-001 non-integral or out-of-range JSON number is forbidden.'
        }
        ([Text.Json.JsonValueKind]::True) { return $true }
        ([Text.Json.JsonValueKind]::False) { return $false }
        ([Text.Json.JsonValueKind]::Null) { return $null }
        default { throw 'EDGE-SPLIT-AUTHORITY-CANONICAL-001 unsupported JSON token.' }
    }
}

function ConvertFrom-EdgeJsonText {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Json)

    $options = [Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $false
    $options.CommentHandling = [Text.Json.JsonCommentHandling]::Disallow
    $options.MaxDepth = 128
    try { $document = [Text.Json.JsonDocument]::Parse($Json, $options) }
    catch { throw "EDGE-SPLIT-AUTHORITY-CANONICAL-001 JSON parsing failed: $($_.Exception.Message)" }
    try { return ConvertFrom-EdgeJsonElement $document.RootElement }
    finally { $document.Dispose() }
}

function ConvertTo-EdgeCanonicalValue {
    param([AllowNull()]$Value)

    if ($null -eq $Value) { return $null }
    if ($Value -is [string] -or $Value -is [bool] -or
        $Value -is [byte] -or $Value -is [sbyte] -or
        $Value -is [int16] -or $Value -is [uint16] -or
        $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64]) {
        return $Value
    }
    if ($Value -is [Collections.IDictionary]) {
        $result = [ordered]@{}
        $names = Sort-EdgeOrdinalStrings ([string[]]@($Value.Keys | ForEach-Object { [string]$_ }))
        foreach ($name in $names) {
            $result[$name] = ConvertTo-EdgeCanonicalValue $Value[$name]
        }
        return $result
    }
    if ($Value -is [Management.Automation.PSCustomObject]) {
        $result = [ordered]@{}
        $names = Sort-EdgeOrdinalStrings ([string[]]@($Value.PSObject.Properties | ForEach-Object Name))
        foreach ($name in $names) {
            $result[$name] = ConvertTo-EdgeCanonicalValue $Value.PSObject.Properties[$name].Value
        }
        return $result
    }
    if ($Value -is [Collections.IEnumerable] -and $Value -isnot [byte[]]) {
        return @($Value | ForEach-Object { ConvertTo-EdgeCanonicalValue $_ })
    }
    throw "EDGE-SPLIT-AUTHORITY-CANONICAL-001 unsupported canonical JSON value type: $($Value.GetType().FullName)."
}

function ConvertTo-EdgeCanonicalJson {
    param([AllowNull()]$Value)

    $canonical = ConvertTo-EdgeCanonicalValue $Value
    return ($canonical | ConvertTo-Json -Depth 100 -Compress -EscapeHandling Default)
}

function ConvertTo-EdgeCanonicalBytes {
    param([AllowNull()]$Value)
    return [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-EdgeCanonicalJson $Value))
}

function Assert-EdgeStrictJson {
    param(
        [Parameter(Mandatory)][byte[]]$RawBytes,
        [Parameter(Mandatory)][string]$SchemaPath,
        [Parameter(Mandatory)][string]$ErrorCode,
        [switch]$RequireCanonical
    )

    $raw = ConvertFrom-EdgeStrictUtf8Bytes -RawBytes $RawBytes -ErrorCode $ErrorCode -MaximumBytes 1048576
    $schemaRaw = Get-Content -LiteralPath $SchemaPath -Raw
    try {
        if (-not ($raw | Test-Json -Schema $schemaRaw -ErrorAction Stop)) {
            throw 'schema validation returned false'
        }
    }
    catch {
        throw "$ErrorCode strict JSON schema validation failed: $($_.Exception.Message)"
    }
    try { $value = ConvertFrom-EdgeJsonText $raw }
    catch { throw "$ErrorCode JSON parsing failed: $($_.Exception.Message)" }
    if ($RequireCanonical -and -not (Test-EdgeByteArrayEqual $RawBytes (ConvertTo-EdgeCanonicalBytes $value))) {
        throw "$ErrorCode JSON bytes are not in the canonical UTF-8 representation."
    }
    return $value
}

function Test-EdgePathIdentity {
    param([Parameter(Mandatory)][string]$Left, [Parameter(Mandatory)][string]$Right)
    $comparison = if ([OperatingSystem]::IsWindows()) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    return [string]::Equals([IO.Path]::GetFullPath($Left), [IO.Path]::GetFullPath($Right), $comparison)
}

function Resolve-EdgeRepositoryPath {
    param([Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][string]$RelativePath)
    $rootFullPath = [IO.Path]::GetFullPath($RepositoryRoot)
    if (-not (Test-Path -LiteralPath $rootFullPath -PathType Container)) {
        throw 'EDGE-SPLIT-LEDGER-FAST-PATH repository root does not exist.'
    }
    $rootItem = Get-Item -LiteralPath $rootFullPath -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$rootItem.LinkTarget)) {
        throw 'EDGE-SPLIT-LEDGER-FAST-PATH repository root must not be a symlink/reparse point.'
    }
    if ([IO.Path]::IsPathRooted($RelativePath) -or $RelativePath.Contains('\', [StringComparison]::Ordinal)) {
        throw "EDGE-SPLIT-LEDGER-FAST-PATH repository path is rooted or uses a backslash: $RelativePath"
    }
    $fullPath = [IO.Path]::GetFullPath((Join-Path $rootFullPath $RelativePath))
    $relative = [IO.Path]::GetRelativePath($rootFullPath, $fullPath)
    if ($relative -eq '..' -or $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal) -or
        [IO.Path]::IsPathRooted($relative)) {
        throw "EDGE-SPLIT-LEDGER-FAST-PATH repository path escapes the root: $RelativePath"
    }
    $current = $rootFullPath
    foreach ($segment in $relative.Split(
            [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
            [StringSplitOptions]::RemoveEmptyEntries)) {
        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current)) { break }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)) {
            throw "EDGE-SPLIT-LEDGER-FAST-PATH repository path traverses a symlink/reparse point: $RelativePath"
        }
    }
    return $fullPath
}

function Assert-EdgeExactCanonicalMarker {
    param(
        [Parameter(Mandatory)][string]$MarkerPath,
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)][string]$ErrorCode
    )
    if (-not (Test-Path -LiteralPath $MarkerPath -PathType Leaf)) {
        throw "$ErrorCode exact cleanup marker is missing."
    }
    $item = Get-Item -LiteralPath $MarkerPath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$item.LinkTarget)) {
        throw "$ErrorCode cleanup marker must not be a symlink/reparse point."
    }
    $actualBytes = [IO.File]::ReadAllBytes($MarkerPath)
    $expectedBytes = ConvertTo-EdgeCanonicalBytes $Expected
    if (-not (Test-EdgeByteArrayEqual $actualBytes $expectedBytes)) {
        throw "$ErrorCode cleanup marker is not the exact canonical parent binding."
    }
}

function Assert-EdgeExactTemporaryRunPath {
    param(
        [Parameter(Mandatory)][string]$RunRoot,
        [Parameter(Mandatory)][string]$Candidate,
        [Parameter(Mandatory)][string]$RunId,
        [switch]$AllowRunRoot
    )
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
    $run = [IO.Path]::GetFullPath($RunRoot).TrimEnd(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar))
    $path = [IO.Path]::GetFullPath($Candidate)
    $comparison = if ([OperatingSystem]::IsWindows()) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $run.StartsWith("$tempRoot$([IO.Path]::DirectorySeparatorChar)", $comparison) -or
        -not $run.Contains($RunId, [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP run root is not a runId-bound child of the OS temporary root.'
    }
    $identity = [string]::Equals($path, $run, $comparison)
    if ((-not $AllowRunRoot -and $identity) -or
        (-not $identity -and -not $path.StartsWith("$run$([IO.Path]::DirectorySeparatorChar)", $comparison)) -or
        -not $path.Contains($RunId, [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP cleanup target escapes the exact run root/runId.'
    }
    return $path
}

function Invoke-EdgeCleanupGit {
    param(
        [Parameter(Mandatory)][string]$GitExecutablePath,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$EmptyConfigPath,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $environment = New-EdgeAuthorityGitChildEnvironment $EmptyConfigPath $GitExecutablePath
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $GitExecutablePath
    $startInfo.WorkingDirectory = $RepositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @('-C', $RepositoryRoot, '-c', "core.hooksPath=$EmptyConfigPath") + $Arguments) {
        $startInfo.ArgumentList.Add([string]$argument)
    }
    foreach ($name in $environment.Keys) {
        $startInfo.Environment[[string]$name] = [string]$environment[$name]
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP exact git cleanup process did not start.'
        }
        $stdoutTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync($process.StandardOutput.BaseStream, 4194304)
        $stderrTask = [EdgeAuthorityBoundedStreamCapture]::ReadAsync($process.StandardError.BaseStream, 4194304)
        $deadline = [DateTimeOffset]::UtcNow.AddMinutes(5)
        while (-not $process.WaitForExit(100)) {
            if ($stdoutTask.IsFaulted -or $stderrTask.IsFaulted -or [DateTimeOffset]::UtcNow -ge $deadline) {
                try { $process.Kill($true) } catch { }
                [void]$process.WaitForExit(30000)
                throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP exact git cleanup output/deadline bound failed.'
            }
        }
        try {
            $capture = Wait-EdgeBoundedCaptureTasks $stdoutTask $stderrTask $deadline `
                'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP'
            $output = [Text.UTF8Encoding]::new($false, $true).GetString($capture.stdoutBytes)
            [void][Text.UTF8Encoding]::new($false, $true).GetString($capture.stderrBytes)
        }
        catch { throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP exact git cleanup output was unbounded, held open, or invalid UTF-8.' }
        $exitCode = [int]$process.ExitCode
    }
    finally { $process.Dispose() }
    if ($exitCode -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP exact git worktree cleanup command failed.'
    }
    return $output.Trim()
}

function Remove-EdgeDevelopmentCoordinatorRunState {
    param(
        [Parameter(Mandatory)][string]$GitExecutablePath,
        [Parameter(Mandatory)][string]$AuthorityRepositoryRoot,
        [Parameter(Mandatory)][string]$RunRoot,
        [Parameter(Mandatory)][string]$ValidatorWorktreePath,
        [Parameter(Mandatory)][string]$ReplayWorktreePath,
        [Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)][string]$ParentMarkerPath,
        [Parameter(Mandatory)]$ParentMarkerExpected,
        [Parameter(Mandatory)]$CoordinatorMarkerExpected
    )

    Assert-EdgeAuthorityGitEnvironment
    $parentRunRoot = [IO.Path]::GetFullPath((Split-Path $ParentMarkerPath -Parent))
    $expectedParentMarkerPath = Join-Path $parentRunRoot '.edge-development-authority-run.json'
    try {
        if (-not (Test-EdgePathIdentity $expectedParentMarkerPath $ParentMarkerPath) -or
            [string]$ParentMarkerExpected.runId -cne $RunId -or
            -not (Test-EdgePathIdentity ([string]$ParentMarkerExpected.authorityRepositoryRoot) $AuthorityRepositoryRoot) -or
            -not (Test-EdgePathIdentity ([string]$ParentMarkerExpected.snapshotRoot) $AuthorityRepositoryRoot) -or
            -not (Test-EdgePathIdentity ([string]$ParentMarkerExpected.coordinatorRunRoot) $RunRoot) -or
            -not (Test-EdgePathIdentity ([string]$ParentMarkerExpected.validatorWorktreePath) $ValidatorWorktreePath) -or
            -not (Test-EdgePathIdentity ([string]$ParentMarkerExpected.replayWorktreePath) $ReplayWorktreePath) -or
            [string]$CoordinatorMarkerExpected.runId -cne $RunId -or
            -not (Test-EdgePathIdentity ([string]$CoordinatorMarkerExpected.authorityRepositoryRoot) $AuthorityRepositoryRoot) -or
            -not (Test-EdgePathIdentity ([string]$CoordinatorMarkerExpected.validatorWorktreePath) $ValidatorWorktreePath) -or
            -not (Test-EdgePathIdentity ([string]$CoordinatorMarkerExpected.replayWorktreePath) $ReplayWorktreePath)) {
            throw 'binding mismatch'
        }
    }
    catch {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP cleanup arguments are not the exact parent/coordinator marker bindings.'
    }
    Assert-EdgeExactCanonicalMarker -MarkerPath $ParentMarkerPath -Expected $ParentMarkerExpected `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP'
    $gitFullPath = [IO.Path]::GetFullPath($GitExecutablePath)
    $gitItem = Get-Item -LiteralPath $gitFullPath -Force
    if (-not (Test-Path -LiteralPath $gitFullPath -PathType Leaf) -or
        ($gitItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$gitItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP cleanup git executable is missing or indirect.'
    }
    $parent = Assert-EdgeExactTemporaryRunPath -RunRoot $parentRunRoot -Candidate $parentRunRoot `
        -RunId $RunId -AllowRunRoot
    $parentItem = Get-Item -LiteralPath $parent -Force
    if (($parentItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$parentItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP parent run root became a symlink/reparse point.'
    }
    $authorityRoot = Assert-EdgeExactTemporaryRunPath -RunRoot $parent `
        -Candidate $AuthorityRepositoryRoot -RunId $RunId
    if (-not (Test-Path -LiteralPath $authorityRoot -PathType Container)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP authority repository vanished before exact worktree cleanup.'
    }
    $authorityItem = Get-Item -LiteralPath $authorityRoot -Force
    if (($authorityItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$authorityItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP authority repository became a symlink/reparse point.'
    }
    $emptyGitConfigPath = Assert-EdgeAuthorityEmptyGitConfig $authorityRoot
    $run = Assert-EdgeExactTemporaryRunPath -RunRoot $parent -Candidate $RunRoot -RunId $RunId
    $targets = [string[]]@(
        (Assert-EdgeExactTemporaryRunPath -RunRoot $run -Candidate $ValidatorWorktreePath -RunId $RunId),
        (Assert-EdgeExactTemporaryRunPath -RunRoot $run -Candidate $ReplayWorktreePath -RunId $RunId))
    foreach ($target in $targets) {
        if (Test-Path -LiteralPath $target) {
            $targetItem = Get-Item -LiteralPath $target -Force
            if (($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                -not [string]::IsNullOrWhiteSpace([string]$targetItem.LinkTarget)) {
                throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP worktree cleanup target became a symlink/reparse point.'
            }
        }
    }
    $listed = @((Invoke-EdgeCleanupGit $gitFullPath $authorityRoot $emptyGitConfigPath `
                @('worktree', 'list', '--porcelain')) -split "`r?`n")
    foreach ($target in $targets) {
        $registrations = @($listed | Where-Object { $_.StartsWith('worktree ', [StringComparison]::Ordinal) } |
            ForEach-Object { [IO.Path]::GetFullPath($_.Substring(9)) } |
            Where-Object { Test-EdgePathIdentity $_ $target })
        if ($registrations.Count -gt 1) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP exact worktree registration is ambiguous.'
        }
        if ($registrations.Count -eq 1) {
            [void](Invoke-EdgeCleanupGit $gitFullPath $authorityRoot $emptyGitConfigPath `
                @('worktree', 'remove', '--force', $target))
        }
    }
    $listedAfter = @((Invoke-EdgeCleanupGit $gitFullPath $authorityRoot $emptyGitConfigPath `
                @('worktree', 'list', '--porcelain')) -split "`r?`n")
    foreach ($target in $targets) {
        if (@($listedAfter | Where-Object { $_.StartsWith('worktree ', [StringComparison]::Ordinal) } |
                ForEach-Object { [IO.Path]::GetFullPath($_.Substring(9)) } |
                Where-Object { Test-EdgePathIdentity $_ $target }).Count -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP exact worktree registration survived cleanup.'
        }
    }
    if (Test-Path -LiteralPath $run -PathType Container) {
        $runItem = Get-Item -LiteralPath $run -Force
        if (($runItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$runItem.LinkTarget)) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP coordinator run root became a symlink/reparse point.'
        }
        $coordinatorMarkerPath = Join-Path $run '.edge-authority-run.json'
        if (Test-Path -LiteralPath $coordinatorMarkerPath) {
            Assert-EdgeExactCanonicalMarker -MarkerPath $coordinatorMarkerPath -Expected $CoordinatorMarkerExpected `
                -ErrorCode 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP'
        }
        Remove-Item -LiteralPath $run -Recurse -Force
    }
    if (Test-Path -LiteralPath $run) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP coordinator run root survived exact cleanup.'
    }
}

function Remove-EdgeDevelopmentOuterRunRoot {
    param(
        [Parameter(Mandatory)][string]$RunRoot,
        [Parameter(Mandatory)][string]$SnapshotRoot,
        [Parameter(Mandatory)][string]$CoordinatorRunRoot,
        [Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)][string]$MarkerPath,
        [Parameter(Mandatory)]$MarkerExpected
    )
    try {
        if ([string]$MarkerExpected.runId -cne $RunId -or
            -not (Test-EdgePathIdentity ([string]$MarkerExpected.snapshotRoot) $SnapshotRoot) -or
            -not (Test-EdgePathIdentity ([string]$MarkerExpected.authorityRepositoryRoot) $SnapshotRoot) -or
            -not (Test-EdgePathIdentity ([string]$MarkerExpected.coordinatorRunRoot) $CoordinatorRunRoot)) {
            throw 'binding mismatch'
        }
    }
    catch {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP outer cleanup arguments are not the exact parent marker bindings.'
    }
    $run = Assert-EdgeExactTemporaryRunPath -RunRoot $RunRoot -Candidate $RunRoot -RunId $RunId -AllowRunRoot
    if (-not (Test-Path -LiteralPath $run -PathType Container)) { return }
    $runItem = Get-Item -LiteralPath $run -Force
    if (($runItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$runItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP outer run root became a symlink/reparse point.'
    }
    Assert-EdgeExactCanonicalMarker -MarkerPath $MarkerPath -Expected $MarkerExpected `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP'
    if (-not (Test-EdgePathIdentity $MarkerPath (Join-Path $run '.edge-development-authority-run.json'))) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP outer cleanup marker path is not the exact parent marker location.'
    }
    $snapshot = Assert-EdgeExactTemporaryRunPath -RunRoot $run -Candidate $SnapshotRoot -RunId $RunId
    if (Test-Path -LiteralPath $snapshot) {
        $snapshotItem = Get-Item -LiteralPath $snapshot -Force
        if (($snapshotItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$snapshotItem.LinkTarget)) {
            throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP snapshot root became a symlink/reparse point.'
        }
    }
    $coordinator = Assert-EdgeExactTemporaryRunPath -RunRoot $run `
        -Candidate $CoordinatorRunRoot -RunId $RunId
    if (Test-Path -LiteralPath $coordinator) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP coordinator run root must be gone before outer cleanup.'
    }
    Remove-Item -LiteralPath $run -Recurse -Force
    if (Test-Path -LiteralPath $run) {
        throw 'EDGE-SPLIT-AUTHORITY-DEV-CLEANUP outer run root survived exact cleanup.'
    }
}

function Remove-EdgeFormalAuthorityRunState {
    param(
        [Parameter(Mandatory)][string]$GitExecutablePath,
        [Parameter(Mandatory)][string]$AuthorityRepositoryRoot,
        [Parameter(Mandatory)][string]$OuterRunRoot,
        [Parameter(Mandatory)][string]$RunRoot,
        [Parameter(Mandatory)][string]$ValidatorWorktreePath,
        [Parameter(Mandatory)][string]$ReplayWorktreePath,
        [Parameter(Mandatory)][string]$RunId,
        [Parameter(Mandatory)][string]$MarkerPath,
        [Parameter(Mandatory)]$MarkerExpected
    )

    Assert-EdgeAuthorityGitEnvironment
    try {
        if ([int]$MarkerExpected.schemaVersion -ne 3 -or
            [string]$MarkerExpected.mode -cne 'formal-clean' -or
            [string]$MarkerExpected.runId -cne $RunId -or
            -not (Test-EdgePathIdentity ([string]$MarkerExpected.sourceRepositoryRoot) `
                $AuthorityRepositoryRoot) -or
            -not (Test-EdgePathIdentity ([string]$MarkerExpected.authorityRepositoryRoot) `
                $AuthorityRepositoryRoot) -or
            -not (Test-EdgePathIdentity ([string]$MarkerExpected.outerRunRoot) $OuterRunRoot) -or
            -not (Test-EdgePathIdentity ([string]$MarkerExpected.coordinatorRunRoot) $RunRoot) -or
            -not (Test-EdgePathIdentity ([string]$MarkerExpected.validatorWorktreePath) `
                $ValidatorWorktreePath) -or
            -not (Test-EdgePathIdentity ([string]$MarkerExpected.replayWorktreePath) `
                $ReplayWorktreePath)) {
            throw 'binding mismatch'
        }
    }
    catch {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP cleanup arguments are not the exact formal parent marker bindings.'
    }
    $outer = Assert-EdgeExactTemporaryRunPath -RunRoot $OuterRunRoot `
        -Candidate $OuterRunRoot -RunId $RunId -AllowRunRoot
    if (-not (Test-Path -LiteralPath $outer -PathType Container)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP formal outer run root vanished before exact cleanup.'
    }
    $outerItem = Get-Item -LiteralPath $outer -Force
    if (($outerItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$outerItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP formal outer run root became indirect.'
    }
    $expectedMarkerPath = Join-Path $outer '.edge-formal-authority-run.json'
    if (-not (Test-EdgePathIdentity $expectedMarkerPath $MarkerPath)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP marker path is not the exact formal parent marker location.'
    }
    Assert-EdgeExactCanonicalMarker -MarkerPath $MarkerPath -Expected $MarkerExpected `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP'

    $authorityRoot = [IO.Path]::GetFullPath($AuthorityRepositoryRoot)
    if (-not (Test-Path -LiteralPath $authorityRoot -PathType Container)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP real authority repository is missing.'
    }
    $authorityItem = Get-Item -LiteralPath $authorityRoot -Force
    if (($authorityItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        -not [string]::IsNullOrWhiteSpace([string]$authorityItem.LinkTarget)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP real authority repository became indirect.'
    }
    $authorityRelativeToOuter = [IO.Path]::GetRelativePath($outer, $authorityRoot)
    if ($authorityRelativeToOuter -ne '..' -and
        -not $authorityRelativeToOuter.StartsWith(
            "..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP real authority repository must remain outside the deletable formal run root.'
    }
    $run = Assert-EdgeExactTemporaryRunPath -RunRoot $outer `
        -Candidate $RunRoot -RunId $RunId
    $targets = [string[]]@(
        (Assert-EdgeExactTemporaryRunPath -RunRoot $run `
            -Candidate $ValidatorWorktreePath -RunId $RunId),
        (Assert-EdgeExactTemporaryRunPath -RunRoot $run `
            -Candidate $ReplayWorktreePath -RunId $RunId))
    foreach ($target in $targets) {
        if (Test-Path -LiteralPath $target) {
            $targetItem = Get-Item -LiteralPath $target -Force
            if (($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                -not [string]::IsNullOrWhiteSpace([string]$targetItem.LinkTarget)) {
                throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP formal worktree target became indirect.'
            }
        }
    }
    $gitFullPath = Assert-EdgeAuthorityFinalGitExecutablePath (
        [IO.Path]::GetFullPath($GitExecutablePath))
    $emptyGitConfigPath = Assert-EdgeAuthorityEmptyGitConfig $authorityRoot
    $listed = @((Invoke-EdgeCleanupGit $gitFullPath $authorityRoot $emptyGitConfigPath `
                @('worktree', 'list', '--porcelain')) -split "`r?`n")
    foreach ($target in $targets) {
        $registrations = @($listed | Where-Object {
                $_.StartsWith('worktree ', [StringComparison]::Ordinal)
            } | ForEach-Object {
                [IO.Path]::GetFullPath($_.Substring(9))
            } | Where-Object { Test-EdgePathIdentity $_ $target })
        if ($registrations.Count -gt 1) {
            throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP exact formal worktree registration is ambiguous.'
        }
        if ($registrations.Count -eq 1) {
            [void](Invoke-EdgeCleanupGit $gitFullPath $authorityRoot $emptyGitConfigPath `
                @('worktree', 'remove', '--force', $target))
        }
    }
    $listedAfter = @((Invoke-EdgeCleanupGit $gitFullPath $authorityRoot $emptyGitConfigPath `
                @('worktree', 'list', '--porcelain')) -split "`r?`n")
    foreach ($target in $targets) {
        if (@($listedAfter | Where-Object {
                    $_.StartsWith('worktree ', [StringComparison]::Ordinal)
                } | ForEach-Object {
                    [IO.Path]::GetFullPath($_.Substring(9))
                } | Where-Object { Test-EdgePathIdentity $_ $target }).Count -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP exact formal worktree registration survived cleanup.'
        }
        if (Test-Path -LiteralPath $target) {
            throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP an unregistered formal worktree path survived; preserve it for diagnosis.'
        }
    }
    if (Test-Path -LiteralPath $run) {
        if (-not (Test-Path -LiteralPath $run -PathType Container)) {
            throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP coordinator run root is not a directory.'
        }
        $runItem = Get-Item -LiteralPath $run -Force
        if (($runItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            -not [string]::IsNullOrWhiteSpace([string]$runItem.LinkTarget)) {
            throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP coordinator run root became indirect.'
        }
        $allowedRunFiles = [string[]]@(
            (Join-Path $run '.edge-authority-run.json'),
            (Join-Path $run ".edge-authority-run.json.partial-$RunId"),
            (Join-Path $run 'authority.json'),
            (Join-Path $run 'replay.json'))
        $runChildren = @(Get-ChildItem -LiteralPath $run -Force)
        foreach ($child in $runChildren) {
            $matchesAllowedFile = @($allowedRunFiles | Where-Object {
                    Test-EdgePathIdentity $_ ([string]$child.FullName)
                }).Count -eq 1
            if (-not $matchesAllowedFile -or
                -not (Test-Path -LiteralPath ([string]$child.FullName) -PathType Leaf) -or
                ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
                -not [string]::IsNullOrWhiteSpace([string]$child.LinkTarget)) {
                throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP coordinator run root contains an unknown or indirect item; preserve it for diagnosis.'
            }
        }
        foreach ($allowedFile in $allowedRunFiles) {
            if (Test-Path -LiteralPath $allowedFile -PathType Leaf) {
                Remove-Item -LiteralPath $allowedFile -Force
            }
        }
        if (@(Get-ChildItem -LiteralPath $run -Force).Count -ne 0) {
            throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP coordinator run root did not become empty after exact file cleanup.'
        }
        Remove-Item -LiteralPath $run -Force
        if (Test-Path -LiteralPath $run) {
            throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP coordinator run root survived non-recursive cleanup.'
        }
    }
    Assert-EdgeExactCanonicalMarker -MarkerPath $MarkerPath -Expected $MarkerExpected `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP'
    $outerChildren = @(Get-ChildItem -LiteralPath $outer -Force)
    if ($outerChildren.Count -ne 1 -or
        -not (Test-EdgePathIdentity ([string]$outerChildren[0].FullName) $MarkerPath)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP formal outer run root contains an unknown item; preserve it for diagnosis.'
    }
    Remove-Item -LiteralPath $MarkerPath -Force
    if (@(Get-ChildItem -LiteralPath $outer -Force).Count -ne 0) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP formal outer run root did not become empty.'
    }
    Remove-Item -LiteralPath $outer -Force
    if (Test-Path -LiteralPath $outer) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP formal outer run root survived exact cleanup.'
    }
    if (-not (Test-Path -LiteralPath $authorityRoot -PathType Container)) {
        throw 'EDGE-SPLIT-AUTHORITY-FORMAL-CLEANUP real authority repository was affected by cleanup.'
    }
}

function Get-EdgeLedgerFactGroupValues {
    param([Parameter(Mandatory)]$Ledger)

    $compilationCore = [ordered]@{}
    foreach ($name in @(Sort-EdgeOrdinalStrings ([string[]]@($Ledger.msbuildCompilation.PSObject.Properties | ForEach-Object Name)))) {
        if ($name -cne 'pluginSources') { $compilationCore[$name] = $Ledger.msbuildCompilation.PSObject.Properties[$name].Value }
    }
    $integrityAuthority = [ordered]@{}
    foreach ($name in @(Sort-EdgeOrdinalStrings ([string[]]@($Ledger.integrity.PSObject.Properties | ForEach-Object Name)))) {
        if ($name -notin @('payloadSha256', 'analyzedInputsSha256')) {
            $integrityAuthority[$name] = $Ledger.integrity.PSObject.Properties[$name].Value
        }
    }
    return [ordered]@{
        sourceState = $Ledger.sourceState
        frozenSource = $Ledger.frozenPhase0Source
        decisions = $Ledger.decisions
        solutionInventory = $Ledger.solutionInventory
        testInventory = $Ledger.testInventory
        pluginManifest = $Ledger.pluginManifest
        publishedComposition = $Ledger.publishedComposition
        msbuildCore = $compilationCore
        pluginSources = @($Ledger.msbuildCompilation.pluginSources)
        views = @($Ledger.viewInventory)
        pages = @($Ledger.pageInventory)
        resources = @($Ledger.resourceInventory)
        references = @($Ledger.referenceAssemblies)
        usages = @($Ledger.externalSymbolUsages)
        projectLayer = $Ledger.dependencyLayers.evaluatedProjectReferences
        roslynLayer = $Ledger.dependencyLayers.roslynForbiddenSymbols
        peLayer = $Ledger.dependencyLayers.peAssemblyReferences
        contractLayer = $Ledger.dependencyLayers.sdkUiContractClosures
        packageLayer = $Ledger.dependencyLayers.packagedAssemblies
        carry020 = $Ledger.carrySets.'EDGE-SPLIT-020'
        carry030 = $Ledger.carrySets.'EDGE-SPLIT-030'
        summary = $Ledger.summary
        integrityAuthority = $integrityAuthority
        analyzedInputs = [string]$Ledger.integrity.analyzedInputsSha256
    }
}

function Get-EdgeFactAuthorityKind {
    param([Parameter(Mandatory)][string]$Name)
    if ($Name -in @('frozenSource', 'decisions', 'testInventory', 'publishedComposition', 'integrityAuthority')) {
        return 'validated-authority-input'
    }
    if ($Name -in @('sourceState', 'carry020', 'carry030', 'summary')) {
        return 'validated-cross-field'
    }
    return 'independent-recompute'
}

function Get-EdgeRequiredFactGroupNames {
    return [string[]]@(
        'analyzedInputs',
        'carry020',
        'carry030',
        'contractLayer',
        'decisions',
        'frozenSource',
        'integrityAuthority',
        'msbuildCore',
        'packageLayer',
        'pages',
        'peLayer',
        'pluginManifest',
        'pluginSources',
        'projectLayer',
        'publishedComposition',
        'references',
        'resources',
        'roslynLayer',
        'solutionInventory',
        'sourceState',
        'summary',
        'testInventory',
        'usages',
        'views'
    )
}

function Assert-EdgeFactGroupSet {
    param([Parameter(Mandatory)][object[]]$FactGroups)
    $actualNames = @($FactGroups | ForEach-Object { [string]$_.name })
    $expectedNames = @(Get-EdgeRequiredFactGroupNames)
    $nameSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in $actualNames) { [void]$nameSet.Add($name) }
    if ($actualNames.Count -ne $expectedNames.Count -or
        $nameSet.Count -ne $actualNames.Count -or
        ($actualNames -join "`n") -cne ($expectedNames -join "`n")) {
        throw 'EDGE-SPLIT-AUTHORITY-FACT-SET fact groups must have the exact unique required-name set in ordinal order.'
    }
}

function New-EdgeCandidateFactGroups {
    param([Parameter(Mandatory)]$Ledger)
    $recorded = Get-EdgeLedgerFactGroupValues $Ledger
    $result = [Collections.Generic.List[object]]::new()
    foreach ($name in @(Sort-EdgeOrdinalStrings ([string[]]@($recorded.Keys)))) {
        $result.Add([pscustomobject][ordered]@{
                name = [string]$name
                sha256 = Get-EdgeSha256Bytes (ConvertTo-EdgeCanonicalBytes $recorded[$name])
                authority = Get-EdgeFactAuthorityKind ([string]$name)
            })
    }
    $groups = @($result.ToArray())
    Assert-EdgeFactGroupSet $groups
    return $groups
}

function New-EdgeAuthorityFactGroups {
    param(
        [Parameter(Mandatory)]$Ledger,
        [Parameter(Mandatory)][Collections.IDictionary]$IndependentOverrides
    )

    $recorded = Get-EdgeLedgerFactGroupValues $Ledger
    $requiredOverrideNames = @($recorded.Keys | Where-Object {
            (Get-EdgeFactAuthorityKind ([string]$_)) -ceq 'independent-recompute'
        } | ForEach-Object { [string]$_ })
    $requiredOverrideNames = Sort-EdgeOrdinalStrings ([string[]]$requiredOverrideNames)
    $actualOverrideNames = Sort-EdgeOrdinalStrings ([string[]]@($IndependentOverrides.Keys | ForEach-Object { [string]$_ }))
    if (($requiredOverrideNames -join "`n") -cne ($actualOverrideNames -join "`n")) {
        throw 'EDGE-SPLIT-AUTHORITY-FACT-SET independent authority overrides must contain exactly every independent-recompute group.'
    }
    $result = [Collections.Generic.List[object]]::new()
    foreach ($name in @(Sort-EdgeOrdinalStrings ([string[]]@($recorded.Keys)))) {
        $value = $recorded[$name]
        if ($IndependentOverrides.Contains($name)) {
            $actualValue = $IndependentOverrides[$name]
            $recordedDigest = Get-EdgeSha256Bytes (ConvertTo-EdgeCanonicalBytes $value)
            $actualDigest = Get-EdgeSha256Bytes (ConvertTo-EdgeCanonicalBytes $actualValue)
            if ($recordedDigest -cne $actualDigest) {
                throw "EDGE-SPLIT-AUTHORITY-FACT-$($name.ToUpperInvariant()) recorded ledger differs from the independently recomputed fact projection."
            }
            $value = $actualValue
        }
        $result.Add([pscustomobject][ordered]@{
                name = [string]$name
                sha256 = Get-EdgeSha256Bytes (ConvertTo-EdgeCanonicalBytes $value)
                authority = Get-EdgeFactAuthorityKind ([string]$name)
            })
    }
    $groups = @($result.ToArray())
    Assert-EdgeFactGroupSet $groups
    return $groups
}

function Assert-EdgeCanonicalEqual {
    param(
        [AllowNull()]$Expected,
        [AllowNull()]$Actual,
        [Parameter(Mandatory)][string]$ErrorCode,
        [Parameter(Mandatory)][string]$Message
    )
    if ((ConvertTo-EdgeCanonicalJson $Expected) -cne (ConvertTo-EdgeCanonicalJson $Actual)) {
        throw "$ErrorCode $Message"
    }
}

function Get-EdgeCountMap {
    param([Parameter(Mandatory)][object[]]$Items, [Parameter(Mandatory)][string]$Property)
    $counts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($item in $Items) {
        $name = [string]$item.$Property
        if ($counts.ContainsKey($name)) { $counts[$name]++ } else { $counts.Add($name, 1) }
    }
    return @(Sort-EdgeOrdinalStrings ([string[]]@($counts.Keys)) | ForEach-Object {
            [pscustomobject][ordered]@{ name = [string]$_; count = [int]$counts[$_] }
        })
}

function Assert-EdgeCheapLedgerSemantics {
    param([Parameter(Mandatory)]$Ledger)

    $usages = @($Ledger.externalSymbolUsages)
    foreach ($path in @(
            @($Ledger.sourceState.dirtyPaths) + @($Ledger.sourceState.excludedPaths) +
            @($Ledger.sourceState.pluginSourceDriftFromHead) + @($usages | ForEach-Object sourcePath) +
            @($Ledger.pageInventory | ForEach-Object sourcePath) + @($Ledger.resourceInventory | ForEach-Object sourcePath))) {
        if ([string]::IsNullOrWhiteSpace([string]$path) -or [IO.Path]::IsPathRooted([string]$path) -or
            ([string]$path).Contains('\', [StringComparison]::Ordinal) -or
            ([string]$path) -match '(^|/)\.\.(/|$)') {
            throw "EDGE-SPLIT-LEDGER-FAST-PATH ledger contains a non-repository path."
        }
    }
    if ([bool]$Ledger.sourceState.cleanObserved -ne (@($Ledger.sourceState.dirtyPaths).Count -eq 0)) {
        throw 'EDGE-SPLIT-LEDGER-FAST-SOURCE-CLEAN cleanObserved is not derived from dirtyPaths.'
    }
    if (@($Ledger.sourceState.excludedPaths).Count -ne 1) {
        throw 'EDGE-SPLIT-LEDGER-FAST-SOURCE-EXCLUSION exactly one output ledger path must be excluded.'
    }
    foreach ($usage in $usages) {
        if ([string]$usage.ownerFamily -ceq 'Unknown' -or [string]$usage.classification -ceq 'unclassified') {
            throw 'EDGE-SPLIT-LEDGER-FAST-USAGE-OWNER external usages may not retain Unknown/unclassified ownership.'
        }
    }
    foreach ($uriText in @(
            $Ledger.publishedComposition.catalogApiBaseUrl,
            $Ledger.publishedComposition.host.manifest.url,
            $Ledger.publishedComposition.host.artifact.url,
            $Ledger.publishedComposition.plugin.artifact.url)) {
        $uri = $null
        if (-not [Uri]::TryCreate([string]$uriText, [UriKind]::Absolute, [ref]$uri) -or
            $uri.Scheme -notin @('http', 'https') -or -not [string]::IsNullOrWhiteSpace($uri.UserInfo) -or
            -not [string]::IsNullOrWhiteSpace($uri.Query) -or -not [string]::IsNullOrWhiteSpace($uri.Fragment)) {
            throw 'EDGE-SPLIT-LEDGER-FAST-PUBLISHED-URI historical artifact URI is not immutable and credential-free.'
        }
    }
    if (-not [bool]$Ledger.publishedComposition.host.manifest.verified -or
        -not [bool]$Ledger.publishedComposition.host.artifact.verified -or
        -not [bool]$Ledger.publishedComposition.plugin.artifact.verified) {
        throw 'EDGE-SPLIT-LEDGER-FAST-PUBLISHED-VERIFIED historical artifact bytes are not all verified.'
    }

    $compilation = $Ledger.msbuildCompilation
    if ([int]$compilation.emittedGeneratorSourceCount -ne @($compilation.generatedSources).Count -or
        [int]$compilation.compilationInputCount -ne @($compilation.compilationInputs).Count -or
        [int]$compilation.compilationInputCount -ne
            ([int]$compilation.msbuildCompileSourceCount + [int]$compilation.emittedGeneratorSourceCount) -or
        [int]$compilation.metadataReferenceCount -ne @($Ledger.referenceAssemblies).Count -or
        [int]$compilation.msbuildCompileSourceCount -le 0) {
        throw 'EDGE-SPLIT-LEDGER-FAST-MSBUILD-COUNTS MSBuild compilation counts are internally inconsistent.'
    }
    $project = $Ledger.dependencyLayers.evaluatedProjectReferences
    $projectItems = @($project.items)
    $projectForbidden = @($projectItems | Where-Object forbiddenForSourceLayer)
    if ([int]$project.totalCount -ne $projectItems.Count -or
        [int]$project.forbiddenCount -ne $projectForbidden.Count -or
        [int]$project.unknownAssemblyCount -ne @($projectItems | Where-Object ownerFamily -eq 'Unknown').Count) {
        throw 'EDGE-SPLIT-LEDGER-FAST-PROJECT-COUNTS evaluated ProjectReference counts are internally inconsistent.'
    }
    $roslyn = $Ledger.dependencyLayers.roslynForbiddenSymbols
    $roslynForbidden = @($usages | Where-Object forbiddenForSourceLayer)
    if ([int]$roslyn.totalExternalUsageCount -ne $usages.Count -or
        [int]$roslyn.forbiddenUsageCount -ne $roslynForbidden.Count -or
        [int]$roslyn.unclassifiedSymbolCount -ne @($usages | Where-Object classification -eq 'unclassified').Count) {
        throw 'EDGE-SPLIT-LEDGER-FAST-ROSLYN-COUNTS Roslyn layer counts are internally inconsistent.'
    }
    $pe = $Ledger.dependencyLayers.peAssemblyReferences
    $peItems = @($pe.items)
    $peForbidden = @($peItems | Where-Object forbiddenForSourceLayer)
    if ([int]$pe.totalCount -ne $peItems.Count -or [int]$pe.forbiddenCount -ne $peForbidden.Count -or
        [int]$pe.unknownAssemblyCount -ne @($peItems | Where-Object ownerFamily -eq 'Unknown').Count) {
        throw 'EDGE-SPLIT-LEDGER-FAST-PE-COUNTS PE AssemblyRef counts are internally inconsistent.'
    }
    $package = $Ledger.dependencyLayers.packagedAssemblies
    $packageAssemblies = @($package.assemblies)
    $packageEntries = @($package.entries)
    if ([int]$package.totalEntryCount -ne $packageEntries.Count -or
        [int]$package.totalAssemblyCount -ne $packageAssemblies.Count -or
        [int]$package.forbiddenCount -ne @($packageAssemblies | Where-Object forbiddenForPackageLayer).Count -or
        [int]$package.unknownAssemblyCount -ne @($packageAssemblies | Where-Object ownerFamily -eq 'Unknown').Count -or
        [int]$package.forbiddenFileCount -ne @($packageEntries | Where-Object { -not [bool]$_.allowed }).Count -or
        [int]$package.unclassifiedFileCount -ne @($packageEntries | Where-Object category -eq 'unclassified').Count) {
        throw 'EDGE-SPLIT-LEDGER-FAST-PACKAGE-COUNTS package layer counts are internally inconsistent.'
    }
    $rank = [array]::IndexOf(
        @('EDGE-SPLIT-000', 'EDGE-SPLIT-010', 'EDGE-SPLIT-020', 'EDGE-SPLIT-030', 'EDGE-SPLIT-040', 'EDGE-SPLIT-050'),
        [string]$Ledger.batchId)
    if ($rank -lt 4) {
        if ([string]$package.status -cne 'not-applicable-before-EDGE-SPLIT-040' -or
            -not [string]::IsNullOrWhiteSpace([string]$package.packagePath) -or
            -not [string]::IsNullOrWhiteSpace([string]$package.packageSha256) -or
            $packageEntries.Count -ne 0 -or $packageAssemblies.Count -ne 0) {
            throw 'EDGE-SPLIT-LEDGER-FAST-PACKAGE-NA a pre-Phase-4 package layer cannot masquerade as evaluated.'
        }
    }
    foreach ($carryId in @('EDGE-SPLIT-020', 'EDGE-SPLIT-030')) {
        $carry = $Ledger.carrySets.$carryId
        $baselineItems = @($carry.baselineItems)
        $currentItems = @($carry.currentItems)
        $baselineSum = ($baselineItems | Measure-Object -Property count -Sum).Sum
        $currentSum = ($currentItems | Measure-Object -Property count -Sum).Sum
        if ($null -eq $baselineSum) { $baselineSum = 0 }
        if ($null -eq $currentSum) { $currentSum = 0 }
        if ([int]$carry.baselineItemCount -ne $baselineItems.Count -or
            [int]$carry.currentItemCount -ne $currentItems.Count -or
            [int]$carry.baselineOccurrenceCount -ne [int]$baselineSum -or
            [int]$carry.currentOccurrenceCount -ne [int]$currentSum) {
            throw "EDGE-SPLIT-LEDGER-FAST-CARRY-COUNT $carryId counts are internally inconsistent."
        }
        $removalRank = if ($carryId -ceq 'EDGE-SPLIT-020') { 2 } else { 3 }
        $expectedStatus = if ($rank -eq 0) { 'frozen' } elseif ($rank -lt $removalRank) { 'retained-exact' } else { 'closed' }
        if ([string]$carry.lifecycleStatus -cne $expectedStatus) {
            throw "EDGE-SPLIT-LEDGER-FAST-CARRY-LIFECYCLE $carryId lifecycle is inconsistent with the batch."
        }
    }
    $summary = $Ledger.summary
    $uniqueUsageSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($usage in $usages) { [void]$uniqueUsageSet.Add("$($usage.ownerAssembly)|$($usage.symbol)") }
    $uniqueUsageCount = $uniqueUsageSet.Count
    if ([int]$summary.externalSymbolUsageCount -ne $usages.Count -or
        [int]$summary.uniqueExternalSymbolCount -ne $uniqueUsageCount -or
        [int]$summary.viewCount -ne @($Ledger.viewInventory).Count -or
        [int]$summary.pageCount -ne @($Ledger.pageInventory).Count -or
        [int]$summary.resourceKeyOccurrenceCount -ne @($Ledger.resourceInventory).Count -or
        [int]$summary.evaluatedProjectReferenceForbiddenCount -ne $projectForbidden.Count -or
        [int]$summary.roslynForbiddenSymbolCount -ne $roslynForbidden.Count -or
        [int]$summary.peForbiddenAssemblyReferenceCount -ne $peForbidden.Count -or
        [int]$summary.packagedForbiddenAssemblyCount -ne @($packageAssemblies | Where-Object forbiddenForPackageLayer).Count -or
        [int]$summary.contractSurfaceForbiddenReferenceCount -ne [int]$Ledger.dependencyLayers.sdkUiContractClosures.forbiddenReferenceCount -or
        [int]$summary.packagedForbiddenFileCount -ne [int]$package.forbiddenFileCount -or
        [int]$summary.packagedUnclassifiedFileCount -ne [int]$package.unclassifiedFileCount -or
        [int]$summary.unknownAssemblyCount -ne 0 -or [int]$summary.unclassifiedSymbolCount -ne 0) {
        throw 'EDGE-SPLIT-LEDGER-FAST-SUMMARY summary counts are not exact cross-field projections.'
    }
    if ([int]$summary.carrySet020ItemCount -ne @($Ledger.carrySets.'EDGE-SPLIT-020'.currentItems).Count -or
        [int]$summary.carrySet030ItemCount -ne @($Ledger.carrySets.'EDGE-SPLIT-030'.currentItems).Count -or
        [int]$summary.carrySet020OccurrenceCount -ne [int]$Ledger.carrySets.'EDGE-SPLIT-020'.currentOccurrenceCount -or
        [int]$summary.carrySet030OccurrenceCount -ne [int]$Ledger.carrySets.'EDGE-SPLIT-030'.currentOccurrenceCount) {
        throw 'EDGE-SPLIT-LEDGER-FAST-SUMMARY-CARRY summary carry counts are not exact cross-field projections.'
    }
}

function Get-EdgeFactErrorCode {
    param([Parameter(Mandatory)][string]$Name)
    $codes = @{
        analyzedInputs = 'EDGE-SPLIT-LEDGER-FAST-ANALYZED-INPUTS'
        pluginSources = 'EDGE-SPLIT-LEDGER-FAST-PLUGIN-SOURCES'
        views = 'EDGE-SPLIT-LEDGER-FAST-VIEWS'
        resources = 'EDGE-SPLIT-LEDGER-FAST-RESOURCES'
        projectLayer = 'EDGE-SPLIT-LEDGER-FAST-PROJECT-FACTS'
        roslynLayer = 'EDGE-SPLIT-LEDGER-FAST-ROSLYN-FACTS'
        peLayer = 'EDGE-SPLIT-LEDGER-FAST-PE-FACTS'
        pages = 'EDGE-SPLIT-LEDGER-FAST-PAGES'
        references = 'EDGE-SPLIT-LEDGER-FAST-REFERENCES'
        usages = 'EDGE-SPLIT-LEDGER-FAST-USAGES'
        packageLayer = 'EDGE-SPLIT-LEDGER-FAST-PACKAGE-FACTS'
        publishedComposition = 'EDGE-SPLIT-LEDGER-FAST-PUBLISHED-FACTS'
        sourceState = 'EDGE-SPLIT-LEDGER-FAST-SOURCE-FACTS'
        carry020 = 'EDGE-SPLIT-LEDGER-FAST-CARRY020-FACTS'
        carry030 = 'EDGE-SPLIT-LEDGER-FAST-CARRY030-FACTS'
    }
    if ($codes.ContainsKey($Name)) { return [string]$codes[$Name] }
    return "EDGE-SPLIT-LEDGER-FAST-FACT-$($Name.ToUpperInvariant())"
}

function Assert-EdgeReceiptFactGroups {
    param([Parameter(Mandatory)]$Ledger, [Parameter(Mandatory)][object[]]$ExpectedFactGroups)

    $actualGroups = @(New-EdgeCandidateFactGroups -Ledger $Ledger)
    Assert-EdgeFactGroupSet $ExpectedFactGroups
    Assert-EdgeFactGroupSet $actualGroups
    for ($index = 0; $index -lt $actualGroups.Count; $index++) {
        $expected = $ExpectedFactGroups[$index]
        $actual = $actualGroups[$index]
        if ([string]$expected.sha256 -cne [string]$actual.sha256 -or
            [string]$expected.authority -cne [string]$actual.authority) {
            $code = Get-EdgeFactErrorCode ([string]$actual.name)
            throw "$code ledger fact differs from the signed independent authority projection: $($actual.name)."
        }
    }
}

function Get-EdgeAuthorityCodePaths {
    return [string[]]@(
        '.github/workflows/edge-pack-modules.yml',
        '.github/workflows/edge-smoke-build.yml',
        'IIoT.EdgeClient.slnx',
        'NuGet.Config',
        'eng/EdgePluginContractLedger.Roslyn.cs',
        'eng/EdgePluginContractDeterministicBuild.targets',
        'eng/Generate-EdgePluginContractLedger.ps1',
        'eng/baselines/edge-split-phase0-inputs.json',
        'eng/edge-phase-close-evidence.schema.json',
        'eng/edge-plugin-contract-authority-descriptor.schema.json',
        'eng/edge-plugin-contract-authority-receipt.schema.json',
        'eng/edge-plugin-contract-authority-request.schema.json',
        'eng/edge-plugin-contract-authority-result.schema.json',
        'eng/edge-plugin-contract-formal-validation-result.schema.json',
        'eng/edge-plugin-contract-ledger.schema.json',
        'eng/edge-split-phase0-inputs.schema.json',
        'global.json',
        'scripts/tests/Confirm-EdgeRequiredTestResults.ps1',
        'scripts/tests/EdgePluginContractLedger.Protocol.psm1',
        'scripts/tests/EdgePluginContractLedger.ValidatorRoslyn.cs',
        'scripts/tests/EdgePluginContractStaticGuard.psm1',
        'scripts/tests/Invoke-EdgePluginContractAuthorityCoordinator.ps1',
        'scripts/tests/Invoke-EdgePluginContractAuthorityFixtureChild.ps1',
        'scripts/tests/Invoke-EdgePluginContractDevelopmentValidation.ps1',
        'scripts/tests/Invoke-EdgePluginContractFormalValidation.ps1',
        'scripts/tests/Invoke-EdgeRequiredTests.ps1',
        'scripts/tests/Test-EdgePhaseCloseEvidence.ps1',
        'scripts/tests/Test-EdgePluginContractAuthorityProtocol.ps1',
        'eng/edge-authority-empty.gitconfig',
        'scripts/tests/Test-EdgePluginContractLedger.ps1',
        'scripts/tests/Test-EdgePluginContractLedgerBehavior.ps1',
        'scripts/tests/Test-EdgePluginContractLedgerPrimitives.ps1',
        'scripts/tests/Test-EdgePluginContractStaticGuard.ps1',
        'scripts/tests/discovered-test-inventory.json',
        'scripts/tests/edge-test-inventory.json',
        'scripts/tests/required-test-counts.json',
        'src/Tests/IIoT.Edge.Architecture.Tests/EdgePluginContractLedgerTests.cs',
        'src/Tests/IIoT.Edge.Architecture.Tests/IIoT.Edge.Architecture.Tests.csproj'
    )
}

function Get-EdgeAuthorityCodeDigest {
    param([Parameter(Mandatory)][string]$RepositoryRoot)
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($relativePath in @(Sort-EdgeOrdinalStrings (Get-EdgeAuthorityCodePaths))) {
        $fullPath = Resolve-EdgeRepositoryPath -RepositoryRoot $RepositoryRoot -RelativePath $relativePath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "EDGE-SPLIT-AUTHORITY-CODE-001 authority code input is missing: $relativePath"
        }
        $lines.Add("$relativePath|$(Get-EdgeSha256File $fullPath)")
    }
    return Get-EdgeSha256Text ((@($lines.ToArray()) -join "`n") + "`n")
}

function New-EdgeSignedAuthorityReceipt {
    param(
        [Parameter(Mandatory)]$Payload,
        [Parameter(Mandatory)][Security.Cryptography.ECDsa]$PrivateKey
    )
    Assert-EdgeFactGroupSet @($Payload.factGroups)
    $isFormal = [string]$Payload.mode -ceq 'formal-clean'
    $formalFieldsInvalid = -not [string]::IsNullOrEmpty([string]$Payload.sourceDirtyManifestSha256) -or
        -not [string]::IsNullOrEmpty([string]$Payload.ephemeralSnapshotHead) -or
        -not [string]::IsNullOrEmpty([string]$Payload.ephemeralSnapshotTree) -or
        [string]$Payload.formalFinalHead -cne [string]$Payload.authorityHead -or
        [string]$Payload.formalFinalTree -cne [string]$Payload.authorityTree
    $developmentFieldsInvalid = [string]::IsNullOrWhiteSpace([string]$Payload.sourceDirtyManifestSha256) -or
        [string]::IsNullOrWhiteSpace([string]$Payload.ephemeralSnapshotHead) -or
        [string]::IsNullOrWhiteSpace([string]$Payload.ephemeralSnapshotTree) -or
        -not [string]::IsNullOrEmpty([string]$Payload.formalFinalHead) -or
        -not [string]::IsNullOrEmpty([string]$Payload.formalFinalTree) -or
        [string]$Payload.ephemeralSnapshotHead -cne [string]$Payload.authorityHead -or
        [string]$Payload.ephemeralSnapshotTree -cne [string]$Payload.authorityTree
    if ($isFormal -ne [bool]$Payload.formal -or
        ($isFormal -and $formalFieldsInvalid) -or
        (-not $isFormal -and $developmentFieldsInvalid)) {
        throw 'EDGE-SPLIT-AUTHORITY-MODE-001 receipt mode/formal/source-snapshot fields are inconsistent.'
    }
    $payloadBytes = ConvertTo-EdgeCanonicalBytes $Payload
    $signature = $PrivateKey.SignData(
        $payloadBytes,
        [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)
    if ($signature.Length -ne 64) {
        throw 'EDGE-SPLIT-AUTHORITY-SIGNATURE-001 ECDSA P-256 P1363 signature must be exactly 64 bytes.'
    }
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        signatureAlgorithm = 'ECDSA-P256-SHA256-IEEE-P1363'
        payload = $Payload
        signatureBase64 = [Convert]::ToBase64String($signature)
    }
}

function Read-EdgeLedgerDocument {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$SchemaPath,
        [Parameter(Mandatory)][string]$ErrorCode
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$ErrorCode ledger file is missing."
    }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $raw = ConvertFrom-EdgeStrictUtf8Bytes -RawBytes $bytes -ErrorCode $ErrorCode -MaximumBytes 67108864
    try {
        if (-not ($raw | Test-Json -Schema (Get-Content -LiteralPath $SchemaPath -Raw) -ErrorAction Stop)) {
            throw 'schema validation returned false'
        }
    }
    catch { throw "$ErrorCode ledger schema validation failed: $($_.Exception.Message)" }
    try { $ledger = ConvertFrom-EdgeJsonText $raw }
    catch { throw "$ErrorCode ledger JSON parsing failed: $($_.Exception.Message)" }
    return [pscustomobject][ordered]@{ bytes = $bytes; raw = $raw; ledger = $ledger }
}

function Read-EdgeGeneratorLedger {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$SchemaPath,
        [Parameter(Mandatory)][string]$Name
    )

    $document = Read-EdgeLedgerDocument -Path $Path -SchemaPath $SchemaPath `
        -ErrorCode "EDGE-SPLIT-AUTHORITY-REPLAY-SCHEMA-$($Name.ToUpperInvariant())"
    $serializedBytes = [Text.UTF8Encoding]::new($false).GetBytes(
        (($document.ledger | ConvertTo-Json -Depth 100) + "`n"))
    if (-not (Test-EdgeByteArrayEqual $document.bytes $serializedBytes)) {
        throw "EDGE-SPLIT-AUTHORITY-REPLAY-SERIALIZER $Name ledger bytes are not the exact generator pretty-JSON/LF/no-BOM representation."
    }
    $expectedPayload = [string]$document.ledger.integrity.payloadSha256
    $document.ledger.integrity.payloadSha256 = ''
    $actualPayload = Get-EdgeSha256Text (($document.ledger | ConvertTo-Json -Depth 100) + "`n")
    $document.ledger.integrity.payloadSha256 = $expectedPayload
    if ($actualPayload -cne $expectedPayload) {
        throw "EDGE-SPLIT-AUTHORITY-REPLAY-PAYLOAD $Name ledger payload self-hash is invalid."
    }
    return $document.ledger
}

function Assert-EdgeAuthorityReceipt {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$LedgerPath,
        [Parameter(Mandatory)][string]$ReceiptPath,
        [Parameter(Mandatory)][AllowEmptyString()][string]$PublicKeySpkiBase64,
        [Parameter(Mandatory)][AllowEmptyString()][string]$ExpectedRunId,
        [Parameter(Mandatory)][AllowEmptyString()][string]$ExpectedChallengeBase64,
        [Parameter(Mandatory)][string]$ExpectedSourceRepositoryRoot,
        [Parameter(Mandatory)][string]$ExpectedAuthorityHead,
        [Parameter(Mandatory)][string]$ExpectedAuthorityTree,
        [Parameter(Mandatory)][AllowEmptyString()][string]$ExpectedFormalFinalHead,
        [Parameter(Mandatory)][AllowEmptyString()][string]$ExpectedFormalFinalTree,
        [Parameter(Mandatory)][string]$ExpectedSourceBaseHead,
        [Parameter(Mandatory)][string]$ExpectedSourceBaseTree,
        [Parameter(Mandatory)][AllowEmptyString()][string]$ExpectedSourceDirtyManifestSha256,
        [Parameter(Mandatory)][AllowEmptyString()][string]$ExpectedEphemeralSnapshotHead,
        [Parameter(Mandatory)][AllowEmptyString()][string]$ExpectedEphemeralSnapshotTree,
        [Parameter(Mandatory)][string]$ExpectedImplementationHead,
        [Parameter(Mandatory)][string]$ExpectedImplementationTree,
        [switch]$RequireFormal
    )

    # Parent anchors are checked before any child-provided receipt state is trusted.
    if ([string]::IsNullOrWhiteSpace($PublicKeySpkiBase64)) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-PUBLIC-KEY parent in-memory public-key anchor is missing.'
    }
    if ([string]::IsNullOrWhiteSpace($ExpectedRunId)) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-RUN parent runId binding is missing.'
    }
    if ([string]::IsNullOrWhiteSpace($ExpectedChallengeBase64)) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-CHALLENGE parent challenge binding is missing.'
    }

    # Preserve semantic subcodes: candidate schema and cheap semantics precede receipt
    # crypto, fact bindings, payload bindings, and only then the generic byte hash.
    $ledgerFullPath = Resolve-EdgeRepositoryPath $RepositoryRoot $LedgerPath
    $ledgerSchemaPath = Resolve-EdgeRepositoryPath $RepositoryRoot 'eng/edge-plugin-contract-ledger.schema.json'
    $ledgerDocument = Read-EdgeLedgerDocument -Path $ledgerFullPath -SchemaPath $ledgerSchemaPath `
        -ErrorCode 'EDGE-SPLIT-LEDGER-FAST-SCHEMA'
    $ledger = $ledgerDocument.ledger
    Assert-EdgeCheapLedgerSemantics $ledger

    if (-not (Test-Path -LiteralPath $ReceiptPath -PathType Leaf)) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-MISSING signed authority receipt is missing.'
    }
    $receiptSchemaPath = Resolve-EdgeRepositoryPath $RepositoryRoot 'eng/edge-plugin-contract-authority-receipt.schema.json'
    $receipt = Assert-EdgeStrictJson -RawBytes ([IO.File]::ReadAllBytes($ReceiptPath)) `
        -SchemaPath $receiptSchemaPath -ErrorCode 'EDGE-SPLIT-LEDGER-RECEIPT-SCHEMA' -RequireCanonical
    $publicBytes = $null
    try { $publicBytes = [Convert]::FromBase64String($PublicKeySpkiBase64) }
    catch { throw 'EDGE-SPLIT-LEDGER-RECEIPT-PUBLIC-KEY parent public-key anchor is not valid base64.' }
    $publicKeySha256 = Get-EdgeSha256Bytes $publicBytes
    if ([string]$receipt.payload.publicKeySha256 -cne $publicKeySha256) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-PUBLIC-KEY receipt public-key digest differs from the parent anchor.'
    }
    $signature = $null
    try { $signature = [Convert]::FromBase64String([string]$receipt.signatureBase64) }
    catch { throw 'EDGE-SPLIT-LEDGER-RECEIPT-SIGNATURE receipt signature is not valid base64.' }
    if ($signature.Length -ne 64) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-SIGNATURE receipt P1363 signature is not exactly 64 bytes.'
    }
    $publicKey = [Security.Cryptography.ECDsa]::Create()
    try {
        $bytesRead = 0
        [void]$publicKey.ImportSubjectPublicKeyInfo($publicBytes, [ref]$bytesRead)
        if ($bytesRead -ne $publicBytes.Length) {
            throw 'EDGE-SPLIT-LEDGER-RECEIPT-PUBLIC-KEY parent public key contains trailing bytes.'
        }
        if ($publicKey.KeySize -ne 256) {
            throw 'EDGE-SPLIT-LEDGER-RECEIPT-PUBLIC-KEY parent public key is not ECDSA P-256.'
        }
        if (-not $publicKey.VerifyData(
                (ConvertTo-EdgeCanonicalBytes $receipt.payload),
                $signature,
                [Security.Cryptography.HashAlgorithmName]::SHA256,
                [Security.Cryptography.DSASignatureFormat]::IeeeP1363FixedFieldConcatenation)) {
            throw 'EDGE-SPLIT-LEDGER-RECEIPT-SIGNATURE receipt signature does not verify against the parent anchor.'
        }
    }
    finally { $publicKey.Dispose() }
    if ([string]$receipt.payload.runId -cne $ExpectedRunId) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-RUN receipt runId differs from the parent run.'
    }
    if ([string]$receipt.payload.challengeBase64 -cne $ExpectedChallengeBase64) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-CHALLENGE receipt challenge differs from the parent challenge.'
    }
    $parentBindings = [ordered]@{
        authorityHead = $ExpectedAuthorityHead
        authorityTree = $ExpectedAuthorityTree
        formalFinalHead = $ExpectedFormalFinalHead
        formalFinalTree = $ExpectedFormalFinalTree
        sourceBaseHead = $ExpectedSourceBaseHead
        sourceBaseTree = $ExpectedSourceBaseTree
        sourceDirtyManifestSha256 = $ExpectedSourceDirtyManifestSha256
        ephemeralSnapshotHead = $ExpectedEphemeralSnapshotHead
        ephemeralSnapshotTree = $ExpectedEphemeralSnapshotTree
        implementationHead = $ExpectedImplementationHead
        implementationTree = $ExpectedImplementationTree
    }
    foreach ($bindingName in $parentBindings.Keys) {
        if ([string]$receipt.payload.$bindingName -cne [string]$parentBindings[$bindingName]) {
            throw "EDGE-SPLIT-LEDGER-RECEIPT-PARENT-BINDING signed receipt differs from parent-owned binding: $bindingName."
        }
    }
    if (-not (Test-EdgePathIdentity ([string]$receipt.payload.sourceRepositoryRoot) $ExpectedSourceRepositoryRoot) -or
        -not (Test-EdgePathIdentity ([string]$receipt.payload.authorityRepositoryRoot) $RepositoryRoot)) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-ROOT receipt repository binding is stale or cross-root.'
    }
    if ($RequireFormal -and (-not [bool]$receipt.payload.formal -or
            [string]$receipt.payload.mode -cne 'formal-clean')) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-FORMAL a development snapshot receipt cannot satisfy a formal consumer.'
    }
    $isFormalReceipt = [string]$receipt.payload.mode -ceq 'formal-clean'
    $formalReceiptFieldsInvalid = -not [string]::IsNullOrEmpty([string]$receipt.payload.sourceDirtyManifestSha256) -or
        -not [string]::IsNullOrEmpty([string]$receipt.payload.ephemeralSnapshotHead) -or
        -not [string]::IsNullOrEmpty([string]$receipt.payload.ephemeralSnapshotTree) -or
        [string]$receipt.payload.formalFinalHead -cne [string]$receipt.payload.authorityHead -or
        [string]$receipt.payload.formalFinalTree -cne [string]$receipt.payload.authorityTree
    $developmentReceiptFieldsInvalid = [string]::IsNullOrWhiteSpace([string]$receipt.payload.sourceDirtyManifestSha256) -or
        [string]$receipt.payload.ephemeralSnapshotHead -cne [string]$receipt.payload.authorityHead -or
        [string]$receipt.payload.ephemeralSnapshotTree -cne [string]$receipt.payload.authorityTree -or
        -not [string]::IsNullOrEmpty([string]$receipt.payload.formalFinalHead) -or
        -not [string]::IsNullOrEmpty([string]$receipt.payload.formalFinalTree)
    if ($isFormalReceipt -ne [bool]$receipt.payload.formal -or
        ($isFormalReceipt -and $formalReceiptFieldsInvalid) -or
        (-not $isFormalReceipt -and $developmentReceiptFieldsInvalid)) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-MODE receipt mode/formal/source-snapshot fields are inconsistent.'
    }
    try {
        $issued = [DateTimeOffset]::ParseExact(
            [string]$receipt.payload.issuedUtc, 'O', [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal)
        $expires = [DateTimeOffset]::ParseExact(
            [string]$receipt.payload.expiresUtc, 'O', [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal)
    }
    catch { throw 'EDGE-SPLIT-LEDGER-RECEIPT-TIME receipt timestamps are not exact round-trip UTC values.' }
    $now = [DateTimeOffset]::UtcNow
    if ($issued.Offset -ne [TimeSpan]::Zero -or $expires.Offset -ne [TimeSpan]::Zero -or
        $issued -gt $now.AddMinutes(2) -or $expires -le $issued -or
        ($expires - $issued) -gt [TimeSpan]::FromMinutes(30) -or $expires -lt $now) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-TIME receipt is future-issued, expired, or has an invalid lifetime.'
    }
    if ([int]$receipt.payload.authorityCount -ne 1 -or [int]$receipt.payload.replayCount -ne 1) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-COUNT receipt does not prove exactly one authority and one replay.'
    }
    $gitCommand = @(Get-Command git -CommandType Application -ErrorAction Stop)[0]
    $gitPath = Resolve-EdgeFixedExecutable ([string]$gitCommand.Source)
    $emptyGitConfigPath = Assert-EdgeAuthorityEmptyGitConfig $RepositoryRoot
    $head = Invoke-EdgeCleanupGit $gitPath $RepositoryRoot $emptyGitConfigPath @('rev-parse', 'HEAD')
    $tree = Invoke-EdgeCleanupGit $gitPath $RepositoryRoot $emptyGitConfigPath @('rev-parse', 'HEAD^{tree}')
    if ([string]$receipt.payload.authorityHead -cne $head) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-HEAD receipt authority HEAD differs from the current authority checkout.'
    }
    if ([string]$receipt.payload.authorityTree -cne $tree) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-TREE receipt authority tree differs from the current authority checkout.'
    }
    if ([string]$receipt.payload.ledgerPath -cne $LedgerPath) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-LEDGER-PATH receipt ledger path differs from the candidate path.'
    }
    if ([string]$receipt.payload.implementationHead -cne [string]$ledger.sourceState.head -or
        [string]$receipt.payload.implementationTree -cne [string]$ledger.sourceState.tree) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-IMPLEMENTATION receipt implementation HEAD/tree differs from the ledger source state.'
    }
    if ([string]$receipt.payload.authorityCodeSha256 -cne (Get-EdgeAuthorityCodeDigest $RepositoryRoot)) {
        throw 'EDGE-SPLIT-LEDGER-RECEIPT-CODE authority code/input digest differs from the signed run.'
    }
    Assert-EdgeReceiptFactGroups -Ledger $ledger -ExpectedFactGroups @($receipt.payload.factGroups)
    if ([string]$receipt.payload.ledgerPayloadSha256 -cne [string]$ledger.integrity.payloadSha256 -or
        [string]$receipt.payload.analyzedInputsSha256 -cne [string]$ledger.integrity.analyzedInputsSha256) {
        throw 'EDGE-SPLIT-LEDGER-FAST-INTEGRITY signed payload/analyzed digests differ from the ledger.'
    }
    if ([string]$receipt.payload.ledgerSha256 -cne (Get-EdgeSha256Bytes $ledgerDocument.bytes)) {
        throw 'EDGE-SPLIT-LEDGER-FAST-LEDGER-BYTES ledger differs from the final signed byte binding.'
    }
    return $receipt
}

function Assert-EdgeReplayEquivalent {
    param(
        [Parameter(Mandatory)][string]$CanonicalLedgerPath,
        [Parameter(Mandatory)][string]$ReplayLedgerPath,
        [Parameter(Mandatory)][string]$LedgerSchemaPath,
        [Parameter(Mandatory)][string]$CanonicalOutputRelativePath,
        [Parameter(Mandatory)][string]$ReplayOutputRelativePath
    )
    $canonical = Read-EdgeGeneratorLedger -Path $CanonicalLedgerPath -SchemaPath $LedgerSchemaPath -Name 'canonical'
    $replay = Read-EdgeGeneratorLedger -Path $ReplayLedgerPath -SchemaPath $LedgerSchemaPath -Name 'replay'
    foreach ($entry in @(
            [pscustomobject]@{ ledger = $canonical; output = $CanonicalOutputRelativePath; name = 'canonical' },
            [pscustomobject]@{ ledger = $replay; output = $ReplayOutputRelativePath; name = 'replay' })) {
        if (@($entry.ledger.sourceState.excludedPaths).Count -ne 1 -or
            [string]$entry.ledger.sourceState.excludedPaths[0] -cne [string]$entry.output) {
            throw "EDGE-SPLIT-AUTHORITY-REPLAY-OUTPUT $($entry.name) ledger does not exclude exactly its own output path."
        }
        $entry.ledger.sourceState.excludedPaths = @('$LEDGER_OUTPUT')
        $entry.ledger.integrity.payloadSha256 = ''
    }
    if ((($canonical | ConvertTo-Json -Depth 100) + "`n") -cne
        (($replay | ConvertTo-Json -Depth 100) + "`n")) {
        throw 'EDGE-SPLIT-AUTHORITY-REPLAY-DIFF generator replay differs outside the only allowed output-path/self-hash normalization.'
    }
}

function Assert-EdgeAuthorityDescriptor {
    param(
        [Parameter(Mandatory)][byte[]]$RawBytes,
        [Parameter(Mandatory)][string]$SchemaPath,
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)][string]$ReceiptFullPath
    )
    $descriptor = Assert-EdgeStrictJson -RawBytes $RawBytes -SchemaPath $SchemaPath `
        -ErrorCode 'EDGE-SPLIT-AUTHORITY-DESCRIPTOR-001' -RequireCanonical
    foreach ($name in @(
            'runId', 'challengeBase64', 'sourceRepositoryRoot', 'authorityRepositoryRoot',
            'authorityHead', 'authorityTree', 'formalFinalHead', 'formalFinalTree',
            'sourceBaseHead', 'sourceBaseTree', 'sourceDirtyManifestSha256',
            'ephemeralSnapshotHead', 'ephemeralSnapshotTree',
            'implementationHead', 'implementationTree', 'receiptPath')) {
        if ([string]$descriptor.$name -cne [string]$Expected.$name) {
            throw "EDGE-SPLIT-AUTHORITY-DESCRIPTOR-$($name.ToUpperInvariant()) descriptor binding differs: $name."
        }
    }
    if ([int]$descriptor.coordinatorPid -ne [int]$Expected.coordinatorPid) {
        throw 'EDGE-SPLIT-AUTHORITY-DESCRIPTOR-PID descriptor PID differs from the direct child process.'
    }
    if ([string]$descriptor.processStartUtc -cne [string]$Expected.processStartUtc) {
        throw 'EDGE-SPLIT-AUTHORITY-DESCRIPTOR-START descriptor process start differs from the direct child process.'
    }
    if (-not (Test-Path -LiteralPath $ReceiptFullPath -PathType Leaf) -or
        [string]$descriptor.receiptSha256 -cne (Get-EdgeSha256File $ReceiptFullPath)) {
        throw 'EDGE-SPLIT-AUTHORITY-DESCRIPTOR-RECEIPT descriptor receipt digest differs from the completed receipt.'
    }
    $expectedReceiptFullPath = Resolve-EdgeRepositoryPath `
        ([string]$Expected.authorityRepositoryRoot) ([string]$Expected.receiptPath)
    if (-not (Test-EdgePathIdentity $expectedReceiptFullPath $ReceiptFullPath)) {
        throw 'EDGE-SPLIT-AUTHORITY-DESCRIPTOR-RECEIPT-PATH descriptor receipt path differs from the expected output path.'
    }
    try { $publicBytes = [Convert]::FromBase64String([string]$descriptor.publicKeySpkiBase64) }
    catch { throw 'EDGE-SPLIT-AUTHORITY-DESCRIPTOR-PUBLIC descriptor public key is not valid base64.' }
    if ([string]$descriptor.publicKeySha256 -cne (Get-EdgeSha256Bytes $publicBytes)) {
        throw 'EDGE-SPLIT-AUTHORITY-DESCRIPTOR-PUBLIC descriptor public-key digest is invalid.'
    }
    $publicKey = [Security.Cryptography.ECDsa]::Create()
    try {
        $bytesRead = 0
        [void]$publicKey.ImportSubjectPublicKeyInfo($publicBytes, [ref]$bytesRead)
        if ($bytesRead -ne $publicBytes.Length -or $publicKey.KeySize -ne 256) {
            throw 'EDGE-SPLIT-AUTHORITY-DESCRIPTOR-PUBLIC descriptor public key is not an exact ECDSA P-256 SPKI value.'
        }
    }
    catch {
        if ($_.Exception.Message.StartsWith('EDGE-SPLIT-AUTHORITY-DESCRIPTOR-PUBLIC', [StringComparison]::Ordinal)) { throw }
        throw 'EDGE-SPLIT-AUTHORITY-DESCRIPTOR-PUBLIC descriptor public key SPKI is invalid.'
    }
    finally { $publicKey.Dispose() }
    return $descriptor
}

Export-ModuleMember -Function @(
    'Assert-EdgeAuthorityGitEnvironment',
    'Assert-EdgeAuthorityCoordinatorParentRequest',
    'Assert-EdgeAuthorityFinalGitExecutablePath',
    'Assert-EdgeAuthorityEmptyGitConfig',
    'Assert-EdgeAuthorityDescriptor',
    'Assert-EdgeAuthorityReceipt',
    'Assert-EdgeCheapLedgerSemantics',
    'Assert-EdgeFactGroupSet',
    'Assert-EdgeReceiptFactGroups',
    'Assert-EdgeReplayEquivalent',
    'Assert-EdgeStrictJson',
    'ConvertFrom-EdgeJsonText',
    'ConvertTo-EdgeCanonicalBytes',
    'ConvertTo-EdgeCanonicalJson',
    'Get-EdgeAuthorityCodeDigest',
    'Get-EdgeAuthorityCodePaths',
    'Get-EdgeAuthorityPinnedPath',
    'Get-EdgeRequiredFactGroupNames',
    'Get-EdgeLedgerFactGroupValues',
    'Get-EdgeSha256Bytes',
    'Get-EdgeSha256File',
    'Get-EdgeSha256Text',
    'New-EdgeAuthorityFactGroups',
    'New-EdgeCandidateFactGroups',
    'New-EdgeSignedAuthorityReceipt',
    'New-EdgeAuthorityGitChildEnvironment',
    'New-EdgeAuthorityCoordinatorParentEnvironment',
    'Initialize-EdgeAuthorityCoordinatorParentEnvironment',
    'Initialize-EdgeAuthorityGitChildEnvironment',
    'Read-EdgeGeneratorLedger',
    'Remove-EdgeDevelopmentCoordinatorRunState',
    'Remove-EdgeDevelopmentOuterRunRoot',
    'Remove-EdgeFormalAuthorityRunState',
    'Resolve-EdgeFixedExecutable',
    'Resolve-EdgeRepositoryPath',
    'Test-EdgePathIdentity',
    'Wait-EdgeBoundedCaptureTasks'
)
