[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReadinessSummaryPath,

    [string]$ReleaseRoot = 'publish\avalonia-migration\Release',

    [string]$OutputRoot = '.artifacts\avalonia-default-entry-switch',

    [string]$ReportName,

    [switch]$Preview
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-SwitchFullPath {
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

function Get-SwitchJsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "必要输入文件不存在：$Path"
    }

    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function ConvertTo-SwitchArray {
    param([Parameter(Mandatory = $true)][object]$Value)

    if ($Value -is [System.Array]) {
        return @($Value)
    }

    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        return @($Value | ForEach-Object { $_ })
    }

    return @($Value)
}

function Write-SwitchPreviewMarkdown {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$Report
    )

    $lines = @(
        '# Avalonia 默认入口切换 Preview 报告',
        '',
        "- 生成时间：$($Report.generatedAt)",
        "- Preview：$($Report.previewOnly)",
        "- 将修改文件：$($Report.wouldModifyFiles)",
        '',
        '## 当前默认入口',
        '',
        "- Launcher：$($Report.currentDefaultEntry.launcher)",
        "- Shell：$($Report.currentDefaultEntry.shell)",
        '',
        '## 目标默认入口',
        '',
        "- Launcher：$($Report.targetDefaultEntry.launcher)",
        "- Shell：$($Report.targetDefaultEntry.shell)",
        "- UI-only profile：$($Report.targetDefaultEntry.uiOnlyProfileId)",
        "- 运行联调 profile：$($Report.targetDefaultEntry.runtimeProfileId)",
        '',
        '## 回退入口',
        '',
        "- Launcher：$($Report.rollbackEntry.launcher)",
        "- Shell：$($Report.rollbackEntry.shell)",
        "- 回退负责人：$($Report.approval.rollbackOwner)",
        '',
        '## 结论',
        '',
        '本批只生成 preview 报告，不修改 Launcher profile、不改发布链路、不改 WPF 默认入口。'
    )

    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$resolvedReadinessSummaryPath = Resolve-SwitchFullPath -BasePath $repoRoot -PathValue $ReadinessSummaryPath
$resolvedReleaseRoot = Resolve-SwitchFullPath -BasePath $repoRoot -PathValue $ReleaseRoot
$readinessSummary = Get-SwitchJsonFile -Path $resolvedReadinessSummaryPath

if ($readinessSummary.overallStatus -ne 'ApprovedForDefaultEntrySwitch' -or -not [bool]$readinessSummary.approvedForDefaultEntrySwitch) {
    throw '默认入口切换 preview 被拒绝：readiness summary 未达到 ApprovedForDefaultEntrySwitch。'
}

$launcherProfilesPath = Join-Path $resolvedReleaseRoot 'avalonia-launcher\launcher.profiles.json'
if (-not (Test-Path -LiteralPath $launcherProfilesPath -PathType Leaf)) {
    throw "Avalonia launcher profile 文件不存在：$launcherProfilesPath"
}

$profiles = ConvertTo-SwitchArray -Value (Get-Content -LiteralPath $launcherProfilesPath -Raw -Encoding UTF8 | ConvertFrom-Json)
$uiOnlyProfile = @($profiles | Where-Object { $_.ProfileId -eq 'HomogenizationLineAvalonia' }) | Select-Object -First 1
$runtimeProfile = @($profiles | Where-Object { $_.ProfileId -eq 'HomogenizationLineAvaloniaRuntime' }) | Select-Object -First 1

if ($null -eq $uiOnlyProfile) {
    throw 'Avalonia launcher profile 缺少 HomogenizationLineAvalonia。'
}

if ($null -eq $runtimeProfile) {
    throw 'Avalonia launcher profile 缺少 HomogenizationLineAvaloniaRuntime。'
}

if ([string]::IsNullOrWhiteSpace($ReportName)) {
    $ReportName = "DefaultEntrySwitchPreview-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
}

$resolvedOutputRoot = Resolve-SwitchFullPath -BasePath $repoRoot -PathValue $OutputRoot
$reportRoot = Join-Path $resolvedOutputRoot $ReportName
if (Test-Path -LiteralPath $reportRoot) {
    throw "默认入口切换 preview 输出目录已存在，为避免覆盖请更换 ReportName：$reportRoot"
}

New-Item -Path $reportRoot -ItemType Directory -Force | Out-Null

$report = [PSCustomObject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('O')
    outputRoot = $reportRoot
    previewOnly = $true
    requestedPreviewSwitch = [bool]$Preview
    wouldModifyFiles = $false
    readinessSummaryPath = $resolvedReadinessSummaryPath
    releaseRoot = $resolvedReleaseRoot
    currentDefaultEntry = [PSCustomObject]@{
        launcher = 'IIoT.Edge.Launcher.exe'
        shell = 'IIoT.Edge.Shell.exe'
        note = '第十七批不修改 WPF 生产默认入口。'
    }
    targetDefaultEntry = [PSCustomObject]@{
        launcher = 'IIoT.Edge.Launcher.Avalonia.exe'
        shell = 'IIoT.Edge.AvaloniaShell.exe'
        uiOnlyProfileId = $uiOnlyProfile.ProfileId
        uiOnlyExecutablePath = $uiOnlyProfile.ExecutablePath
        runtimeProfileId = $runtimeProfile.ProfileId
        runtimeExecutablePath = $runtimeProfile.ExecutablePath
        runtimeArguments = @($runtimeProfile.Arguments)
    }
    rollbackEntry = [PSCustomObject]@{
        launcher = 'IIoT.Edge.Launcher.exe'
        shell = 'IIoT.Edge.Shell.exe'
        note = 'WPF Launcher/WPF Shell 继续作为回退线。'
    }
    approval = [PSCustomObject]@{
        approver = $readinessSummary.gates.approver
        decidedAt = $readinessSummary.gates.decidedAt
        rollbackOwner = $readinessSummary.gates.rollbackOwner
        approvedForDefaultEntrySwitch = $readinessSummary.approvedForDefaultEntrySwitch
    }
    readonlyBoundary = @(
        '只读取 readiness summary 和 Avalonia launcher profile。',
        '只写出 default-entry-switch-preview.md/json。',
        '不修改 Launcher profile、发布链路或 WPF 默认入口。',
        '不调用 Cloud/MES 清理、重试、删除或 PLC 读写命令。'
    )
}

$jsonPath = Join-Path $reportRoot 'default-entry-switch-preview.json'
$markdownPath = Join-Path $reportRoot 'default-entry-switch-preview.md'
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
Write-SwitchPreviewMarkdown -Path $markdownPath -Report $report

Write-Host 'Avalonia default entry switch preview generated.'
Write-Host "  Preview: True"
Write-Host "  Json: $jsonPath"
Write-Host "  Markdown: $markdownPath"
