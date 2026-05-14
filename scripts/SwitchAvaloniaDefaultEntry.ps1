[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ReadinessSummaryPath,

    [string]$ReleaseRoot = 'publish\avalonia-migration\Release',

    [string]$OutputRoot = '.artifacts\avalonia-default-entry-switch',

    [string]$ReportName,

    [switch]$Preview,

    [switch]$Apply
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

function Get-SwitchPropertyValue {
    param(
        [object]$InputObject,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [object]$DefaultValue = $null
    )

    if ($null -eq $InputObject) {
        return $DefaultValue
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $DefaultValue
    }

    return $property.Value
}

function Set-SwitchPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [object]$InputObject,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [object]$Value
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        $InputObject | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
    else {
        $property.Value = $Value
    }
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

function Assert-SwitchReadinessApproved {
    param([Parameter(Mandatory = $true)][object]$Summary)

    if ($Summary.overallStatus -ne 'ApprovedForDefaultEntrySwitch' -or -not [bool]$Summary.approvedForDefaultEntrySwitch) {
        throw '默认入口切换被拒绝：readiness summary 未达到 ApprovedForDefaultEntrySwitch。'
    }

    $gates = Get-SwitchPropertyValue -InputObject $Summary -Name 'gates'
    $approver = [string](Get-SwitchPropertyValue -InputObject $gates -Name 'approver' -DefaultValue '')
    $decidedAt = [string](Get-SwitchPropertyValue -InputObject $gates -Name 'decidedAt' -DefaultValue '')
    $rollbackOwner = [string](Get-SwitchPropertyValue -InputObject $gates -Name 'rollbackOwner' -DefaultValue '')

    if ([string]::IsNullOrWhiteSpace($approver)) {
        throw '默认入口切换被拒绝：readiness summary 缺少人工决策人。'
    }

    if ([string]::IsNullOrWhiteSpace($decidedAt)) {
        throw '默认入口切换被拒绝：readiness summary 缺少人工决策时间。'
    }

    if ([string]::IsNullOrWhiteSpace($rollbackOwner)) {
        throw '默认入口切换被拒绝：readiness summary 缺少回退负责人。'
    }
}

function Write-SwitchMarkdown {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$Report
    )

    $title = if ($Report.applyMode) {
        '# Avalonia 默认入口切换 Apply 报告'
    }
    else {
        '# Avalonia 默认入口切换 Preview 报告'
    }

    $conclusion = if ($Report.applyMode) {
        '本次只修改发布包内 Avalonia Launcher profile 默认入口元数据，并已生成 rollback snapshot；不改源码、不改 WPF 项目、不改业务链路。'
    }
    else {
        '本次只生成 preview 报告，不修改 Launcher profile、不改发布链路、不改 WPF 默认入口。'
    }

    $lines = @(
        $title,
        '',
        "- 生成时间：$($Report.generatedAt)",
        "- Preview：$($Report.previewOnly)",
        "- Apply：$($Report.applyMode)",
        "- 将修改文件：$($Report.wouldModifyFiles)",
        "- 输出目录：$($Report.outputRoot)",
        '',
        '## 当前默认入口',
        '',
        "- Launcher：$($Report.currentDefaultEntry.launcher)",
        "- Shell：$($Report.currentDefaultEntry.shell)",
        "- Profile：$($Report.currentDefaultEntry.profileId)",
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
        "- rollback snapshot：$($Report.rollbackSnapshotRoot)",
        '',
        '## 结论',
        '',
        $conclusion
    )

    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

if ($Preview -and $Apply) {
    throw '默认入口切换参数无效：-Preview 和 -Apply 不能同时使用。'
}

$applyMode = [bool]$Apply
$previewMode = -not $applyMode
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$resolvedReadinessSummaryPath = Resolve-SwitchFullPath -BasePath $repoRoot -PathValue $ReadinessSummaryPath
$resolvedReleaseRoot = Resolve-SwitchFullPath -BasePath $repoRoot -PathValue $ReleaseRoot
$readinessSummary = Get-SwitchJsonFile -Path $resolvedReadinessSummaryPath
Assert-SwitchReadinessApproved -Summary $readinessSummary

$launcherProfilesPath = Join-Path $resolvedReleaseRoot 'avalonia-launcher\launcher.profiles.json'
$manifestPath = Join-Path $resolvedReleaseRoot 'release-manifest.json'
$candidateSummaryPath = Join-Path $resolvedReleaseRoot 'candidate-validation-summary.json'
foreach ($requiredPath in @($launcherProfilesPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "默认入口切换缺少必要发布包文件：$requiredPath"
    }
}

if ($Apply) {
    foreach ($requiredPath in @($manifestPath, $candidateSummaryPath)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "默认入口 Apply 缺少必要发布包文件：$requiredPath"
        }
    }
}

$profiles = ConvertTo-SwitchArray -Value (Get-Content -LiteralPath $launcherProfilesPath -Raw -Encoding UTF8 | ConvertFrom-Json)
$uiOnlyProfile = @($profiles | Where-Object { $_.ProfileId -eq 'HomogenizationLineAvalonia' }) | Select-Object -First 1
$runtimeProfile = @($profiles | Where-Object { $_.ProfileId -eq 'HomogenizationLineAvaloniaRuntime' }) | Select-Object -First 1
$existingDefaultProfile = @($profiles | Where-Object { [bool](Get-SwitchPropertyValue -InputObject $_ -Name 'IsDefault' -DefaultValue $false) }) | Select-Object -First 1

if ($null -eq $uiOnlyProfile) {
    throw 'Avalonia launcher profile 缺少 HomogenizationLineAvalonia。'
}

if ($null -eq $runtimeProfile) {
    throw 'Avalonia launcher profile 缺少 HomogenizationLineAvaloniaRuntime。'
}

if ($applyMode -and $null -ne $existingDefaultProfile) {
    throw "默认入口切换被拒绝：发布包已存在默认 profile '$($existingDefaultProfile.ProfileId)'，请先使用 rollback snapshot 回退或重新发布候选包。"
}

if ([string]::IsNullOrWhiteSpace($ReportName)) {
    $ReportName = if ($applyMode) {
        "DefaultEntrySwitchApply-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    }
    else {
        "DefaultEntrySwitchPreview-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    }
}

$resolvedOutputRoot = Resolve-SwitchFullPath -BasePath $repoRoot -PathValue $OutputRoot
$reportRoot = Join-Path $resolvedOutputRoot $ReportName
if (Test-Path -LiteralPath $reportRoot) {
    throw "默认入口切换输出目录已存在，为避免覆盖请更换 ReportName：$reportRoot"
}

New-Item -Path $reportRoot -ItemType Directory -Force | Out-Null
$rollbackSnapshotRoot = if ($applyMode) { Join-Path $reportRoot 'rollback-snapshot' } else { $null }
$gates = Get-SwitchPropertyValue -InputObject $readinessSummary -Name 'gates'
$approver = [string](Get-SwitchPropertyValue -InputObject $gates -Name 'approver' -DefaultValue '')
$decidedAt = [string](Get-SwitchPropertyValue -InputObject $gates -Name 'decidedAt' -DefaultValue '')
$rollbackOwner = [string](Get-SwitchPropertyValue -InputObject $gates -Name 'rollbackOwner' -DefaultValue '')

$report = [PSCustomObject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('O')
    outputRoot = $reportRoot
    previewOnly = $previewMode
    applyMode = $applyMode
    requestedPreviewSwitch = [bool]$Preview
    requestedApplySwitch = $applyMode
    wouldModifyFiles = $applyMode
    modifiedFiles = if ($applyMode) { @($launcherProfilesPath) } else { @() }
    readinessSummaryPath = $resolvedReadinessSummaryPath
    releaseRoot = $resolvedReleaseRoot
    rollbackSnapshotRoot = $rollbackSnapshotRoot
    currentDefaultEntry = [PSCustomObject]@{
        launcher = 'IIoT.Edge.Launcher.exe'
        shell = 'IIoT.Edge.Shell.exe'
        profileId = if ($null -eq $existingDefaultProfile) { '未设置' } else { $existingDefaultProfile.ProfileId }
        note = 'Apply 前 WPF Launcher/WPF Shell 仍是生产默认入口。'
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
        snapshotProfile = if ($applyMode) { Join-Path $rollbackSnapshotRoot 'launcher.profiles.json' } else { $null }
        note = 'WPF Launcher/WPF Shell 继续作为回退线。'
    }
    approval = [PSCustomObject]@{
        approver = $approver
        decidedAt = $decidedAt
        rollbackOwner = $rollbackOwner
        approvedForDefaultEntrySwitch = $readinessSummary.approvedForDefaultEntrySwitch
    }
    boundary = @(
        '只读取 readiness summary、release manifest、candidate summary 和 Avalonia launcher profile。',
        'Apply 只修改发布包内 avalonia-launcher\launcher.profiles.json。',
        '不改源码、不改 WPF 项目、不改原仓生产入口。',
        '不调用 Cloud/MES 清理、重试、删除或 PLC 读写命令。'
    )
}

if ($applyMode) {
    New-Item -Path $rollbackSnapshotRoot -ItemType Directory -Force | Out-Null
    Copy-Item -LiteralPath $launcherProfilesPath -Destination (Join-Path $rollbackSnapshotRoot 'launcher.profiles.json') -Force
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $rollbackSnapshotRoot 'release-manifest.json') -Force
    Copy-Item -LiteralPath $candidateSummaryPath -Destination (Join-Path $rollbackSnapshotRoot 'candidate-validation-summary.json') -Force
    Copy-Item -LiteralPath $resolvedReadinessSummaryPath -Destination (Join-Path $rollbackSnapshotRoot 'default-entry-readiness-summary.json') -Force

    foreach ($profile in $profiles) {
        Set-SwitchPropertyValue -InputObject $profile -Name 'IsDefault' -Value $false
        Set-SwitchPropertyValue -InputObject $profile -Name 'DefaultEntryRole' -Value 'TrialOrFallback'
    }

    Set-SwitchPropertyValue -InputObject $uiOnlyProfile -Name 'IsDefault' -Value $true
    Set-SwitchPropertyValue -InputObject $uiOnlyProfile -Name 'DefaultEntryRole' -Value 'ProductionDefault'
    Set-SwitchPropertyValue -InputObject $uiOnlyProfile -Name 'DefaultEntryAppliedAt' -Value $report.generatedAt
    Set-SwitchPropertyValue -InputObject $uiOnlyProfile -Name 'DefaultEntryApprovedBy' -Value $approver
    Set-SwitchPropertyValue -InputObject $uiOnlyProfile -Name 'DefaultEntryRollbackOwner' -Value $rollbackOwner

    @($profiles) | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $launcherProfilesPath -Encoding UTF8
}

$jsonFileName = if ($applyMode) { 'default-entry-switch-apply-summary.json' } else { 'default-entry-switch-preview.json' }
$markdownFileName = if ($applyMode) { 'default-entry-switch-apply-summary.md' } else { 'default-entry-switch-preview.md' }
$jsonPath = Join-Path $reportRoot $jsonFileName
$markdownPath = Join-Path $reportRoot $markdownFileName
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
Write-SwitchMarkdown -Path $markdownPath -Report $report

if ($applyMode) {
    Copy-Item -LiteralPath $jsonPath -Destination (Join-Path $rollbackSnapshotRoot 'default-entry-switch-apply-summary.json') -Force
}

Write-Host 'Avalonia default entry switch report generated.'
Write-Host "  Preview: $previewMode"
Write-Host "  Apply: $applyMode"
Write-Host "  Json: $jsonPath"
Write-Host "  Markdown: $markdownPath"
