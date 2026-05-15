[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RollbackSnapshotPath,

    [string]$ReleaseRoot = 'publish\avalonia-migration\Release',

    [string]$OutputRoot = '.artifacts\avalonia-default-entry-restore',

    [string]$ReportName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-RestoreFullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $PathValue))
}

function Write-RestoreMarkdown {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$Summary
    )

    $lines = @(
        '# Avalonia 默认入口回退报告',
        '',
        "- 生成时间：$($Summary.generatedAt)",
        "- rollback snapshot：$($Summary.rollbackSnapshotRoot)",
        "- 发布包：$($Summary.releaseRoot)",
        "- 恢复文件：$($Summary.restoredFile)",
        '',
        '## 结论',
        '',
        '本次只从 rollback snapshot 恢复发布包内 Avalonia Launcher profile，不删除发布包、不清理日志、不改业务数据、不触碰 Cloud/MES/PLC。'
    )

    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$resolvedSnapshotRoot = Resolve-RestoreFullPath -BasePath $repoRoot -PathValue $RollbackSnapshotPath
$resolvedReleaseRoot = Resolve-RestoreFullPath -BasePath $repoRoot -PathValue $ReleaseRoot

if (-not (Test-Path -LiteralPath $resolvedSnapshotRoot -PathType Container)) {
    throw "默认入口回退 snapshot 不存在：$resolvedSnapshotRoot"
}

$snapshotProfilePath = Join-Path $resolvedSnapshotRoot 'launcher.profiles.json'
if (-not (Test-Path -LiteralPath $snapshotProfilePath -PathType Leaf)) {
    throw "默认入口回退 snapshot 缺少 launcher.profiles.json：$snapshotProfilePath"
}

$targetProfilePath = Join-Path $resolvedReleaseRoot 'avalonia-launcher\launcher.profiles.json'
$targetProfileDirectory = Split-Path -Parent $targetProfilePath
if (-not (Test-Path -LiteralPath $targetProfileDirectory -PathType Container)) {
    throw "默认入口回退目标目录不存在：$targetProfileDirectory"
}

if ([string]::IsNullOrWhiteSpace($ReportName)) {
    $ReportName = "DefaultEntryRestore-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
}

$resolvedOutputRoot = Resolve-RestoreFullPath -BasePath $repoRoot -PathValue $OutputRoot
$reportRoot = Join-Path $resolvedOutputRoot $ReportName
if (Test-Path -LiteralPath $reportRoot) {
    throw "默认入口回退输出目录已存在，为避免覆盖请更换 ReportName：$reportRoot"
}

New-Item -Path $reportRoot -ItemType Directory -Force | Out-Null
Copy-Item -LiteralPath $snapshotProfilePath -Destination $targetProfilePath -Force

$summary = [PSCustomObject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('O')
    script = 'scripts\RestoreAvaloniaDefaultEntry.ps1'
    rollbackSnapshotRoot = $resolvedSnapshotRoot
    releaseRoot = $resolvedReleaseRoot
    restoredFile = $targetProfilePath
    sourceFile = $snapshotProfilePath
    outputRoot = $reportRoot
    boundary = @(
        '只读取 rollback snapshot 中的 launcher.profiles.json。',
        '只恢复发布包内 avalonia-launcher\launcher.profiles.json。',
        '不删除发布包，不清理日志，不修改业务数据库。',
        '不调用 Cloud/MES 清理、重试、删除或 PLC 读写命令。'
    )
}

$jsonPath = Join-Path $reportRoot 'default-entry-restore-summary.json'
$markdownPath = Join-Path $reportRoot 'default-entry-restore-summary.md'
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
Write-RestoreMarkdown -Path $markdownPath -Summary $summary

Write-Host 'Avalonia default entry restored from rollback snapshot.'
Write-Host "  Json: $jsonPath"
Write-Host "  Markdown: $markdownPath"
