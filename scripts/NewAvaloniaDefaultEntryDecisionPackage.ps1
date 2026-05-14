[CmdletBinding()]
param(
    [string]$CandidateSummaryPath = 'publish\avalonia-migration\Release\candidate-validation-summary.json',

    [Parameter(Mandatory = $true)]
    [string]$TrialReviewSummaryPath,

    [string]$IssueRecoveryPath = 'docs\Avalonia12-试运行问题回收清单.md',

    [string]$AcceptanceRecordPath,

    [string]$OutputRoot = '.artifacts\avalonia-default-entry-decision',

    [string]$PackageName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-DecisionFullPath {
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

function Get-DecisionJsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "必要输入文件不存在：$Path"
    }

    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-OptionalTextFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8
}

function Get-ObjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [object]$InputObject,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [object]$DefaultValue = $null
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $DefaultValue
    }

    return $property.Value
}

function Write-DecisionMarkdown {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$Package
    )

    $recommendation = $Package.recommendation
    $lines = @(
        '# Avalonia 切默认入口决策材料',
        '',
        "生成时间：$($Package.generatedAt)",
        '',
        '## 评审状态',
        '',
        "- 试运行审查状态：$($Package.trialReview.overallStatus)",
        "- 可进入切默认入口评审：$($Package.readyForDefaultEntryReview)",
        "- 当前建议：$recommendation",
        '',
        '> 最终决策留空，等待人工签字；本脚本不会自动写“允许切默认入口”。',
        '',
        '| 决策项 | 人工填写 |',
        '| --- | --- |',
        '| 是否允许把 Avalonia Launcher 设为默认入口 |  |',
        '| 决策人 |  |',
        '| 决策时间 |  |',
        '| 回退负责人 |  |',
        '| 允许切换结论 |  |',
        '',
        '## 候选包信息',
        '',
        "- 候选验收摘要：$($Package.inputs.candidateSummaryPath)",
        "- 试运行审查摘要：$($Package.inputs.trialReviewSummaryPath)",
        "- 问题回收清单：$($Package.inputs.issueRecoveryPath)",
        "- 验收记录：$($Package.inputs.acceptanceRecordPath)",
        "- FullGate：$($Package.candidate.fullGate)",
        "- WPF 回退验证：$($Package.candidate.wpfFallbackVerified)",
        '',
        '## P0/P1 状态',
        '',
        "- P0 数量：$($Package.trialReview.p0BlockerCount)",
        "- P1 数量：$($Package.trialReview.p1PendingCount)",
        "- SkiaSharp preview 例外：$($Package.candidate.previewException)",
        '',
        '## 只读边界',
        '',
        '- 决策包生成脚本只读取候选摘要、试运行审查摘要、问题清单和验收记录。',
        '- 不读取业务数据库，不修改发布包或证据包。',
        '- 不调用 Cloud/MES 清理、重试、删除，不调用 PLC 写入命令。',
        '',
        '## 评审口径',
        '',
        '只有 trial-review-summary.overallStatus 为 ReadyForDefaultEntryReview 时，本材料才写“可进入切默认入口评审”；否则写“不允许进入切默认入口评审”。'
    )

    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$resolvedCandidateSummaryPath = Resolve-DecisionFullPath -BasePath $repoRoot -PathValue $CandidateSummaryPath
$resolvedTrialReviewSummaryPath = Resolve-DecisionFullPath -BasePath $repoRoot -PathValue $TrialReviewSummaryPath
$resolvedIssueRecoveryPath = Resolve-DecisionFullPath -BasePath $repoRoot -PathValue $IssueRecoveryPath

$candidateSummary = Get-DecisionJsonFile -Path $resolvedCandidateSummaryPath
$trialReviewSummary = Get-DecisionJsonFile -Path $resolvedTrialReviewSummaryPath
$issueRecoveryText = Get-OptionalTextFile -Path $resolvedIssueRecoveryPath

if ([string]::IsNullOrWhiteSpace($AcceptanceRecordPath)) {
    $evidenceRoot = Get-ObjectPropertyValue -InputObject $trialReviewSummary -Name 'evidenceRoot'
    if ([string]::IsNullOrWhiteSpace($evidenceRoot)) {
        $resolvedAcceptanceRecordPath = Join-Path $repoRoot 'docs\Avalonia12-现场试运行验收记录.md'
    }
    else {
        $resolvedAcceptanceRecordPath = Join-Path $evidenceRoot 'docs\Avalonia12-现场试运行验收记录.md'
    }
}
else {
    $resolvedAcceptanceRecordPath = Resolve-DecisionFullPath -BasePath $repoRoot -PathValue $AcceptanceRecordPath
}

$acceptanceRecordText = Get-OptionalTextFile -Path $resolvedAcceptanceRecordPath

if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = "DefaultEntryDecision-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
}

$resolvedOutputRoot = Resolve-DecisionFullPath -BasePath $repoRoot -PathValue $OutputRoot
$packageRoot = Join-Path $resolvedOutputRoot $PackageName
if (Test-Path -LiteralPath $packageRoot) {
    throw "决策材料输出目录已存在，为避免覆盖请更换 PackageName：$packageRoot"
}

New-Item -Path $packageRoot -ItemType Directory -Force | Out-Null

$overallStatus = [string](Get-ObjectPropertyValue -InputObject $trialReviewSummary -Name 'overallStatus' -DefaultValue 'Unknown')
$ready = $overallStatus -eq 'ReadyForDefaultEntryReview' -and [bool](Get-ObjectPropertyValue -InputObject $trialReviewSummary -Name 'readyForDefaultEntryReview' -DefaultValue $false)
$recommendation = if ($ready) { '可进入切默认入口评审' } else { '不允许进入切默认入口评审' }
$wpfFallbackVerified = $false
if ($candidateSummary.PSObject.Properties.Name -contains 'wpfFallback') {
    $wpfFallbackVerified = [bool]$candidateSummary.wpfFallback.verified
}

$previewPackages = @()
if ($candidateSummary.PSObject.Properties.Name -contains 'previewPackages') {
    $previewPackages = @($candidateSummary.previewPackages | ForEach-Object { $_ })
}

$fullGate = [bool](Get-ObjectPropertyValue -InputObject $candidateSummary -Name 'fullGate' -DefaultValue $false)
$p0Blockers = @()
if ($trialReviewSummary.PSObject.Properties.Name -contains 'p0Blockers') {
    $p0Blockers = @($trialReviewSummary.p0Blockers | ForEach-Object { $_ })
}

$p1Pending = @()
if ($trialReviewSummary.PSObject.Properties.Name -contains 'p1Pending') {
    $p1Pending = @($trialReviewSummary.p1Pending | ForEach-Object { $_ })
}

$decisionPackage = [PSCustomObject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('O')
    outputRoot = $packageRoot
    recommendation = $recommendation
    readyForDefaultEntryReview = $ready
    finalDecision = [PSCustomObject]@{
        allowedToSwitchDefaultEntry = $null
        approver = $null
        decidedAt = $null
        rollbackOwner = $null
        notes = '等待人工签字。'
    }
    inputs = [PSCustomObject]@{
        candidateSummaryPath = $resolvedCandidateSummaryPath
        trialReviewSummaryPath = $resolvedTrialReviewSummaryPath
        issueRecoveryPath = $resolvedIssueRecoveryPath
        acceptanceRecordPath = $resolvedAcceptanceRecordPath
        issueRecoveryDocumentPresent = -not [string]::IsNullOrWhiteSpace($issueRecoveryText)
        acceptanceRecordPresent = -not [string]::IsNullOrWhiteSpace($acceptanceRecordText)
    }
    candidate = [PSCustomObject]@{
        fullGate = $fullGate
        wpfFallbackVerified = $wpfFallbackVerified
        previewException = '仅允许已批准的 SkiaSharp preview 传递依赖。'
        previewPackages = $previewPackages
        testResults = Get-ObjectPropertyValue -InputObject $candidateSummary -Name 'testResults' -DefaultValue @()
    }
    trialReview = [PSCustomObject]@{
        overallStatus = $overallStatus
        p0BlockerCount = $p0Blockers.Count
        p1PendingCount = $p1Pending.Count
        p0Blockers = $p0Blockers
        p1Pending = $p1Pending
        p1Evidence = Get-ObjectPropertyValue -InputObject $trialReviewSummary -Name 'p1Evidence'
    }
    readonlyBoundary = @(
        '只读取候选摘要、试运行审查摘要、问题回收清单和验收记录。',
        '只写出 default-entry-decision-package.md/json。',
        '不读取业务数据库。',
        '不修改发布包、证据包、运行目录或 Launcher 默认入口。',
        '不调用 Cloud/MES 清理、重试、删除或 PLC 写入命令。'
    )
}

$jsonPath = Join-Path $packageRoot 'default-entry-decision-package.json'
$markdownPath = Join-Path $packageRoot 'default-entry-decision-package.md'
$decisionPackage | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
Write-DecisionMarkdown -Path $markdownPath -Package $decisionPackage

Write-Host 'Avalonia default entry decision package generated.'
Write-Host "  Recommendation: $recommendation"
Write-Host "  Json: $jsonPath"
Write-Host "  Markdown: $markdownPath"
