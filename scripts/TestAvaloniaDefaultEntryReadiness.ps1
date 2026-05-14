[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$DecisionPackagePath,

    [string]$CandidateSummaryPath,

    [string]$TrialReviewSummaryPath,

    [string]$AcceptanceRecordPath,

    [string]$OutputRoot = '.artifacts\avalonia-default-entry-readiness',

    [string]$ReviewName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-ReadinessFullPath {
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

function Get-ReadinessJsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "必要输入文件不存在：$Path"
    }

    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-ObjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
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

function Add-ReadinessBlocker {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Blockers,

        [Parameter(Mandatory = $true)]
        [string]$Code,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $Blockers.Add([PSCustomObject]@{
        code = $Code
        message = $Message
    }) | Out-Null
}

function Write-ReadinessMarkdown {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$Summary
    )

    $lines = @(
        '# Avalonia 默认入口切换评审门禁',
        '',
        "- 生成时间：$($Summary.generatedAt)",
        "- 总体状态：$($Summary.overallStatus)",
        "- 可切默认入口：$($Summary.approvedForDefaultEntrySwitch)",
        '',
        '## 输入',
        '',
        "- 决策包：$($Summary.inputs.decisionPackagePath)",
        "- 候选摘要：$($Summary.inputs.candidateSummaryPath)",
        "- 试运行审查摘要：$($Summary.inputs.trialReviewSummaryPath)",
        "- 验收记录：$($Summary.inputs.acceptanceRecordPath)",
        '',
        '## 阻断项',
        ''
    )

    if ($Summary.blockers.Count -eq 0) {
        $lines += '- 无。'
    }
    else {
        foreach ($blocker in $Summary.blockers) {
            $lines += "- `$($blocker.code)`：$($blocker.message)"
        }
    }

    $lines += @(
        '',
        '## 只读边界',
        '',
        '- 本脚本只读取决策包、候选摘要、试运行审查摘要和验收记录。',
        '- 本脚本只写出 default-entry-readiness-summary.md/json。',
        '- 不修改 Launcher profile，不改发布链路，不改 WPF 默认入口。',
        '- 不调用 Cloud/MES 清理、重试、删除，不调用 PLC 读写命令。'
    )

    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$resolvedDecisionPackagePath = Resolve-ReadinessFullPath -BasePath $repoRoot -PathValue $DecisionPackagePath
$decisionPackage = Get-ReadinessJsonFile -Path $resolvedDecisionPackagePath

if ([string]::IsNullOrWhiteSpace($CandidateSummaryPath)) {
    $CandidateSummaryPath = [string](Get-ObjectPropertyValue -InputObject $decisionPackage.inputs -Name 'candidateSummaryPath')
}

if ([string]::IsNullOrWhiteSpace($TrialReviewSummaryPath)) {
    $TrialReviewSummaryPath = [string](Get-ObjectPropertyValue -InputObject $decisionPackage.inputs -Name 'trialReviewSummaryPath')
}

if ([string]::IsNullOrWhiteSpace($AcceptanceRecordPath)) {
    $AcceptanceRecordPath = [string](Get-ObjectPropertyValue -InputObject $decisionPackage.inputs -Name 'acceptanceRecordPath')
}

$resolvedCandidateSummaryPath = Resolve-ReadinessFullPath -BasePath $repoRoot -PathValue $CandidateSummaryPath
$resolvedTrialReviewSummaryPath = Resolve-ReadinessFullPath -BasePath $repoRoot -PathValue $TrialReviewSummaryPath
$resolvedAcceptanceRecordPath = Resolve-ReadinessFullPath -BasePath $repoRoot -PathValue $AcceptanceRecordPath

$candidateSummary = Get-ReadinessJsonFile -Path $resolvedCandidateSummaryPath
$trialReviewSummary = Get-ReadinessJsonFile -Path $resolvedTrialReviewSummaryPath
$blockers = [System.Collections.Generic.List[object]]::new()

$trialOverallStatus = [string](Get-ObjectPropertyValue -InputObject $trialReviewSummary -Name 'overallStatus' -DefaultValue 'Unknown')
$trialReady = [bool](Get-ObjectPropertyValue -InputObject $trialReviewSummary -Name 'readyForDefaultEntryReview' -DefaultValue $false)
if ($trialOverallStatus -ne 'ReadyForDefaultEntryReview' -or -not $trialReady) {
    Add-ReadinessBlocker -Blockers $blockers -Code 'TrialReviewNotReady' -Message '试运行证据复审尚未达到 ReadyForDefaultEntryReview。'
}

$fullGate = [bool](Get-ObjectPropertyValue -InputObject $candidateSummary -Name 'fullGate' -DefaultValue $false)
if (-not $fullGate) {
    Add-ReadinessBlocker -Blockers $blockers -Code 'FullGateNotPassed' -Message 'candidate-validation-summary.json 未记录 FullGate 通过。'
}

$wpfFallbackVerified = $false
$wpfFallback = Get-ObjectPropertyValue -InputObject $candidateSummary -Name 'wpfFallback'
if ($null -ne $wpfFallback) {
    $wpfFallbackVerified = [bool](Get-ObjectPropertyValue -InputObject $wpfFallback -Name 'verified' -DefaultValue $false)
}

if (-not $wpfFallbackVerified) {
    Add-ReadinessBlocker -Blockers $blockers -Code 'WpfFallbackNotVerified' -Message 'WPF Launcher/WPF Shell 回退验证未通过。'
}

$p0Blockers = @()
if ($trialReviewSummary.PSObject.Properties.Name -contains 'p0Blockers') {
    $p0Blockers = @($trialReviewSummary.p0Blockers | ForEach-Object { $_ })
}

$p1Pending = @()
if ($trialReviewSummary.PSObject.Properties.Name -contains 'p1Pending') {
    $p1Pending = @($trialReviewSummary.p1Pending | ForEach-Object { $_ })
}

if ($p0Blockers.Count -gt 0) {
    Add-ReadinessBlocker -Blockers $blockers -Code 'P0BlockersRemain' -Message "仍存在 P0 阻断：$($p0Blockers.Count) 项。"
}

if ($p1Pending.Count -gt 0) {
    Add-ReadinessBlocker -Blockers $blockers -Code 'P1PendingRemain' -Message "仍存在 P1 待关闭项：$($p1Pending.Count) 项。"
}

if (-not (Test-Path -LiteralPath $resolvedAcceptanceRecordPath -PathType Leaf)) {
    Add-ReadinessBlocker -Blockers $blockers -Code 'AcceptanceRecordMissing' -Message '未找到已填写的现场试运行验收记录。'
}
else {
    $acceptanceText = Get-Content -LiteralPath $resolvedAcceptanceRecordPath -Raw -Encoding UTF8
    if ($acceptanceText -notmatch '验收记录状态\s*[:：]\s*(已完成|已填写)' -and
        $acceptanceText -notmatch '是否允许进入切默认入口评审\s*\|\s*是\s*\|') {
        Add-ReadinessBlocker -Blockers $blockers -Code 'AcceptanceRecordIncomplete' -Message '现场验收记录未标记为已完成。'
    }
}

$finalDecision = Get-ObjectPropertyValue -InputObject $decisionPackage -Name 'finalDecision'
$allowedToSwitchDefaultEntry = [bool](Get-ObjectPropertyValue -InputObject $finalDecision -Name 'allowedToSwitchDefaultEntry' -DefaultValue $false)
$approver = [string](Get-ObjectPropertyValue -InputObject $finalDecision -Name 'approver' -DefaultValue '')
$decidedAt = [string](Get-ObjectPropertyValue -InputObject $finalDecision -Name 'decidedAt' -DefaultValue '')
$rollbackOwner = [string](Get-ObjectPropertyValue -InputObject $finalDecision -Name 'rollbackOwner' -DefaultValue '')

if (-not $allowedToSwitchDefaultEntry) {
    Add-ReadinessBlocker -Blockers $blockers -Code 'HumanDecisionNotApproved' -Message '人工决策未明确允许把 Avalonia 设为默认入口。'
}

if ([string]::IsNullOrWhiteSpace($approver)) {
    Add-ReadinessBlocker -Blockers $blockers -Code 'ApproverMissing' -Message '人工签字缺少决策人。'
}

if ([string]::IsNullOrWhiteSpace($decidedAt)) {
    Add-ReadinessBlocker -Blockers $blockers -Code 'DecisionTimeMissing' -Message '人工签字缺少决策时间。'
}

if ([string]::IsNullOrWhiteSpace($rollbackOwner)) {
    Add-ReadinessBlocker -Blockers $blockers -Code 'RollbackOwnerMissing' -Message '人工签字缺少回退负责人。'
}

if ([string]::IsNullOrWhiteSpace($ReviewName)) {
    $ReviewName = "DefaultEntryReadiness-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
}

$resolvedOutputRoot = Resolve-ReadinessFullPath -BasePath $repoRoot -PathValue $OutputRoot
$reviewRoot = Join-Path $resolvedOutputRoot $ReviewName
if (Test-Path -LiteralPath $reviewRoot) {
    throw "评审门禁输出目录已存在，为避免覆盖请更换 ReviewName：$reviewRoot"
}

New-Item -Path $reviewRoot -ItemType Directory -Force | Out-Null

$approved = $blockers.Count -eq 0
$overallStatus = if ($approved) { 'ApprovedForDefaultEntrySwitch' } else { 'DefaultEntrySwitchRejected' }
$summary = [PSCustomObject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('O')
    outputRoot = $reviewRoot
    overallStatus = $overallStatus
    approvedForDefaultEntrySwitch = $approved
    inputs = [PSCustomObject]@{
        decisionPackagePath = $resolvedDecisionPackagePath
        candidateSummaryPath = $resolvedCandidateSummaryPath
        trialReviewSummaryPath = $resolvedTrialReviewSummaryPath
        acceptanceRecordPath = $resolvedAcceptanceRecordPath
    }
    gates = [PSCustomObject]@{
        trialReviewStatus = $trialOverallStatus
        readyForDefaultEntryReview = $trialReady
        fullGate = $fullGate
        wpfFallbackVerified = $wpfFallbackVerified
        p0BlockerCount = $p0Blockers.Count
        p1PendingCount = $p1Pending.Count
        allowedToSwitchDefaultEntry = $allowedToSwitchDefaultEntry
        approver = $approver
        decidedAt = $decidedAt
        rollbackOwner = $rollbackOwner
    }
    blockers = @($blockers)
    readonlyBoundary = @(
        '只读取决策包、候选摘要、试运行审查摘要和验收记录。',
        '只写出 default-entry-readiness-summary.md/json。',
        '不修改 Launcher profile、发布链路或 WPF 默认入口。',
        '不调用 Cloud/MES 清理、重试、删除或 PLC 读写命令。'
    )
}

$jsonPath = Join-Path $reviewRoot 'default-entry-readiness-summary.json'
$markdownPath = Join-Path $reviewRoot 'default-entry-readiness-summary.md'
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
Write-ReadinessMarkdown -Path $markdownPath -Summary $summary

Write-Host 'Avalonia default entry readiness checked.'
Write-Host "  Status: $overallStatus"
Write-Host "  Summary: $jsonPath"
Write-Host "  Report: $markdownPath"

if (-not $approved) {
    throw "默认入口切换评审门禁未通过：$($blockers.Count) 个阻断项。"
}
