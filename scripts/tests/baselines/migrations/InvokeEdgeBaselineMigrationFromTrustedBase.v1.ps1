[CmdletBinding(PositionalBinding = $false)]
param(
    [string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$TrustedBaseRevision,
    [Parameter(Mandatory)][string]$CandidateRevision,
    [ValidateSet('BaseAncestorOfHead', 'HeadAncestorOfBase')]
    [string]$AnchorRelationship = 'BaseAncestorOfHead',
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$validatorRepositoryPath = 'scripts/tests/baselines/migrations/ValidateEdgeBaselineMigration.v1.ps1'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
}
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
if ($RemainingArguments.Count -ne 0) {
    throw "EDGE-BASELINE-MIG-001-PARAMETER unexpected positional arguments: $($RemainingArguments -join ', ')."
}
foreach ($revision in @($TrustedBaseRevision, $CandidateRevision)) {
    if ($revision -notmatch '^[0-9A-Fa-f]{40}$' -or $revision -match '^0{40}$') {
        throw 'EDGE-BASELINE-MIG-001-REVISION trusted base and candidate must be explicit non-zero full SHAs.'
    }
}

function Invoke-GitText {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & git -C $RepositoryRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "EDGE-BASELINE-MIG-001-GIT git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }
    return ($output | Out-String).Trim()
}

function Resolve-Commit {
    param(
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$Name
    )

    try {
        $resolved = Invoke-GitText -Arguments @('rev-parse', '--verify', "$Revision^{commit}")
    }
    catch {
        throw "EDGE-BASELINE-MIG-001-REVISION $Name is not an available commit: $Revision."
    }
    if ($resolved -cnotmatch '^[0-9a-f]{40}$') {
        throw "EDGE-BASELINE-MIG-001-REVISION $Name did not resolve to one full commit SHA."
    }
    return $resolved
}

function Export-GitBlob {
    param(
        [Parameter(Mandatory)][string]$ObjectId,
        [Parameter(Mandatory)][string]$Destination
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    foreach ($argument in @('-C', $RepositoryRoot, 'cat-file', 'blob', $ObjectId)) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw 'EDGE-BASELINE-MIG-001-TRUST could not start git blob extraction.'
    }
    $errorTask = $process.StandardError.ReadToEndAsync()
    try {
        $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $process.StandardOutput.BaseStream.CopyTo($stream) }
        finally { $stream.Dispose() }
        $process.WaitForExit()
        $errorText = $errorTask.GetAwaiter().GetResult().Trim()
        if ($process.ExitCode -ne 0) {
            throw "EDGE-BASELINE-MIG-001-TRUST git blob extraction failed: $errorText"
        }
    }
    finally {
        $process.Dispose()
    }
}

$trustedBase = Resolve-Commit -Revision $TrustedBaseRevision -Name 'TrustedBaseRevision'
$candidate = Resolve-Commit -Revision $CandidateRevision -Name 'CandidateRevision'
$entryText = Invoke-GitText -Arguments @('ls-tree', $trustedBase, '--', $validatorRepositoryPath)
$entryMatch = [regex]::Match($entryText, '^100644 blob ([0-9a-f]+)\t(.+)$')
if (-not $entryMatch.Success -or $entryMatch.Groups[2].Value -cne $validatorRepositoryPath) {
    throw "EDGE-BASELINE-MIG-001-TRUST trusted base does not contain the reviewed mode-100644 validator."
}
$validatorObjectId = $entryMatch.Groups[1].Value
$temporaryValidator = Join-Path ([IO.Path]::GetTempPath()) "$([Guid]::NewGuid().ToString('N')).edge-migration.ps1"
try {
    Export-GitBlob -ObjectId $validatorObjectId -Destination $temporaryValidator
    $extractedObjectId = Invoke-GitText -Arguments @('hash-object', '--no-filters', '--', $temporaryValidator)
    if ($extractedObjectId -cne $validatorObjectId) {
        throw 'EDGE-BASELINE-MIG-001-TRUST extracted validator differs from the trusted Git blob.'
    }

    & pwsh -NoLogo -NoProfile -NonInteractive -File $temporaryValidator `
        -Mode Validate `
        -RepositoryRoot $RepositoryRoot `
        -TrustedBaseRevision $trustedBase `
        -CandidateRevision $candidate `
        -AnchorRelationship $AnchorRelationship
    if ($LASTEXITCODE -ne 0) {
        throw "EDGE-BASELINE-MIG-001-TRUST trusted validator failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item $temporaryValidator -Force -ErrorAction SilentlyContinue
}

$global:LASTEXITCODE = 0
exit 0
