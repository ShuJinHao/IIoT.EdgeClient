[CmdletBinding()]
param(
    [string]$SourceRoot,

    [string]$RepositoryUrl = 'git@github.com:ShuJinHao/IIoT.EdgeClient.git',

    [string]$BaseBranch = 'main',

    [string]$LongLivedBranch = 'codex/avalonia-default-entry-review',

    [string]$ReviewBranch = 'codex/avalonia-default-entry-review-pr',

    [string]$WorkRoot,

    [string]$CommitMessage = 'edge: update avalonia migration review snapshot',

    [switch]$Commit,

    [switch]$Push
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-SyncSourceRoot {
    param([string]$InputRoot)

    if (-not [string]::IsNullOrWhiteSpace($InputRoot)) {
        return [System.IO.Path]::GetFullPath($InputRoot)
    }

    return [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
}

function Resolve-SyncWorkRoot {
    param([string]$InputRoot)

    if (-not [string]::IsNullOrWhiteSpace($InputRoot)) {
        return [System.IO.Path]::GetFullPath($InputRoot)
    }

    $name = "iiot-edgeclient-avalonia-review-sync-$([DateTimeOffset]::Now.ToString('yyyyMMddHHmmss'))"
    return [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) $name))
}

function Invoke-SyncCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    Write-Host "==> $Name"
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& $Executable @Arguments 2>&1)
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    foreach ($line in $output) {
        Write-Host $line
    }

    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Invoke-SyncRobocopy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Target
    )

    Write-Host "==> mirror local migration tree"
    $arguments = @(
        $Source,
        $Target,
        '/MIR',
        '/XD',
        '.git',
        'bin',
        'obj',
        'publish',
        '.vs',
        'TestResults',
        '.artifacts',
        'node_modules',
        'dist',
        '/R:2',
        '/W:2',
        '/NFL',
        '/NDL',
        '/NJH',
        '/NJS',
        '/NP'
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& robocopy @arguments 2>&1)
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    foreach ($line in $output) {
        Write-Host $line
    }

    if ($LASTEXITCODE -gt 7) {
        throw "robocopy failed with exit code $LASTEXITCODE."
    }
}

function Test-SyncGeneratedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $normalized = $Path.Replace('\', '/')
    return $normalized -match '(^|/)(bin|obj|\.artifacts|\.vs|publish|TestResults|node_modules|dist)(/|$)'
}

function Assert-SyncRepositoryRoot {
    param([Parameter(Mandatory = $true)][string]$Root)

    if (-not (Test-Path -LiteralPath (Join-Path $Root 'IIoT.EdgeClient.slnx') -PathType Leaf)) {
        throw "IIoT.EdgeClient.slnx was not found under source root: $Root"
    }

    if (-not (Test-Path -LiteralPath (Join-Path $Root '.git') -PathType Container)) {
        throw "Source root is not a git worktree: $Root"
    }
}

function Assert-SyncSafeWorkRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Target
    )

    if (Test-Path -LiteralPath $Target) {
        throw "Work root already exists. Use a new path so the script never deletes an existing checkout: $Target"
    }

    $sourceWithSeparator = $Source.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $targetWithSeparator = $Target.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if ($targetWithSeparator.StartsWith($sourceWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Work root must not be inside the long-lived migration source root."
    }
}

function Get-SyncGitLines {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& git -C $Repository @Arguments 2>&1)
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($LASTEXITCODE -ne 0) {
        $text = $output -join "`n"
        throw "git $($Arguments -join ' ') failed in $Repository.`n$text"
    }

    return @($output | ForEach-Object { $_.ToString() })
}

$source = Resolve-SyncSourceRoot -InputRoot $SourceRoot
$work = Resolve-SyncWorkRoot -InputRoot $WorkRoot

Assert-SyncRepositoryRoot -Root $source
Assert-SyncSafeWorkRoot -Source $source -Target $work

$sourceBranch = (Get-SyncGitLines -Repository $source -Arguments @('branch', '--show-current') | Select-Object -First 1)
if ($sourceBranch -ne $LongLivedBranch) {
    throw "Source worktree must be on $LongLivedBranch before syncing review snapshot. Current branch: $sourceBranch"
}

if ($Push -and -not $Commit) {
    throw 'Use -Commit together with -Push. The script will not push an uncommitted review snapshot.'
}

Invoke-SyncCommand -Name 'clone review repository' -Executable 'git' -Arguments @('clone', $RepositoryUrl, $work)
Invoke-SyncCommand -Name 'fetch base branch' -Executable 'git' -Arguments @('-C', $work, 'fetch', 'origin', $BaseBranch)
Invoke-SyncCommand -Name 'create review branch from base' -Executable 'git' -Arguments @('-C', $work, 'switch', '-C', $ReviewBranch, "origin/$BaseBranch")
Invoke-SyncRobocopy -Source $source -Target $work
Invoke-SyncCommand -Name 'validate worktree whitespace' -Executable 'git' -Arguments @('-C', $work, 'diff', '--check')

if ($Commit) {
    Invoke-SyncCommand -Name 'stage review snapshot' -Executable 'git' -Arguments @('-C', $work, 'add', '-A')
    Invoke-SyncCommand -Name 'validate staged whitespace' -Executable 'git' -Arguments @('-C', $work, 'diff', '--cached', '--check')

    $stagedFiles = Get-SyncGitLines -Repository $work -Arguments @('diff', '--cached', '--name-only')
    $generatedFiles = @($stagedFiles | Where-Object { Test-SyncGeneratedPath -Path $_ })
    if ($generatedFiles.Count -gt 0) {
        throw "Generated output paths were staged for review snapshot:`n$($generatedFiles -join "`n")"
    }

    if ($stagedFiles.Count -eq 0) {
        Write-Host 'No review snapshot changes were found.'
    }
    else {
        Invoke-SyncCommand -Name 'commit review snapshot' -Executable 'git' -Arguments @('-C', $work, 'commit', '-m', $CommitMessage)

        if ($Push) {
            Invoke-SyncCommand -Name 'push review snapshot' -Executable 'git' -Arguments @('-C', $work, 'push', '--force-with-lease', '-u', 'origin', $ReviewBranch)
        }
    }
}
else {
    Write-Host 'Dry run finished. Re-run with -Commit to create a local review snapshot commit, or -Commit -Push to update the PR branch.'
}

Write-Host "Source branch: $sourceBranch"
Write-Host "Review branch: $ReviewBranch"
Write-Host "Work root: $work"
