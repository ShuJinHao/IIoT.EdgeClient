[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,

    [string]$OutputRoot = '.artifacts\avalonia-trial-review',

    [string]$ReviewName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-ReviewFullPath {
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

function New-ReviewDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    New-Item -Path $Path -ItemType Directory -Force | Out-Null
}

function Join-ReviewUnicodeName {
    param([Parameter(Mandatory = $true)][int[]]$CodePoints)

    return [string]::Concat([char[]]$CodePoints)
}

function Get-ReviewJsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Test-ReviewFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Missing
    )

    $path = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $Missing.Add($RelativePath) | Out-Null
        return $false
    }

    return $true
}

function Get-LauncherProfileAssessment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProfilePath
    )

    if (-not (Test-Path -LiteralPath $ProfilePath -PathType Leaf)) {
        return [PSCustomObject]@{
            exists = $false
            profileCount = 0
            uiOnlyProfileCount = 0
            runtimeProfileCount = 0
        }
    }

    $parsedProfiles = Get-Content -LiteralPath $ProfilePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $profiles = @($parsedProfiles | ForEach-Object { $_ })
    $runtimeProfiles = @($profiles | Where-Object {
        $argumentsProperty = $_.PSObject.Properties['Arguments']
        $arguments = if ($null -eq $argumentsProperty) { @() } else { @($argumentsProperty.Value) }
        $arguments -contains '--start-runtime'
    })
    $uiOnlyProfiles = @($profiles | Where-Object {
        $argumentsProperty = $_.PSObject.Properties['Arguments']
        $null -eq $argumentsProperty -or @($argumentsProperty.Value).Count -eq 0
    })

    return [PSCustomObject]@{
        exists = $true
        profileCount = $profiles.Count
        uiOnlyProfileCount = $uiOnlyProfiles.Count
        runtimeProfileCount = $runtimeProfiles.Count
    }
}

function Write-ReviewReport {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$Summary
    )

    $lines = @(
        '# Avalonia 试运行证据审查报告',
        '',
        "- 生成时间：$($Summary.generatedAt)",
        "- 证据目录：$($Summary.evidenceRoot)",
        "- 总体状态：$($Summary.overallStatus)",
        '',
        '## P0/P1 判定',
        '',
        "- P0 阻断数量：$($Summary.p0Blockers.Count)",
        "- P1 待现场确认数量：$($Summary.p1Pending.Count)",
        "- WPF 回退验证：$($Summary.wpfFallbackVerified)",
        "- UI-only profile 数量：$($Summary.launcherProfiles.uiOnlyProfileCount)",
        "- `--start-runtime` profile 数量：$($Summary.launcherProfiles.runtimeProfileCount)",
        '',
        '## 缺失项',
        ''
    )

    if ($Summary.missingRequiredFiles.Count -eq 0) {
        $lines += '- 无。'
    }
    else {
        foreach ($item in $Summary.missingRequiredFiles) {
            $lines += "- $item"
        }
    }

    $lines += @(
        '',
        '## 只读边界',
        '',
        '- 本审查脚本只读取证据包和写出审查报告。',
        '- 不读取业务数据库，不修改运行目录，不调用 Cloud/MES 清理、重试、删除或 PLC 写入命令。',
        '',
        '## 后续结论',
        '',
        '只有 P0 为零、P1 有明确关闭或接受记录时，才允许进入切默认入口评审。'
    )

    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$resolvedEvidencePath = Resolve-ReviewFullPath -BasePath $repoRoot -PathValue $EvidencePath
if (-not (Test-Path -LiteralPath $resolvedEvidencePath)) {
    throw "证据包路径不存在：$resolvedEvidencePath"
}

if ([string]::IsNullOrWhiteSpace($ReviewName)) {
    $ReviewName = "TrialReview-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
}

$resolvedOutputRoot = Resolve-ReviewFullPath -BasePath $repoRoot -PathValue $OutputRoot
$reviewRoot = Join-Path $resolvedOutputRoot $ReviewName
if (Test-Path -LiteralPath $reviewRoot) {
    throw "审查输出目录已存在，为避免覆盖请更换 ReviewName：$reviewRoot"
}

New-ReviewDirectory -Path $reviewRoot

$evidenceRoot = $resolvedEvidencePath
if ((Test-Path -LiteralPath $resolvedEvidencePath -PathType Leaf) -and
    [System.IO.Path]::GetExtension($resolvedEvidencePath).Equals('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
    $expandedRoot = Join-Path $reviewRoot 'expanded-evidence'
    New-ReviewDirectory -Path $expandedRoot
    Expand-Archive -LiteralPath $resolvedEvidencePath -DestinationPath $expandedRoot
    $evidenceRoot = $expandedRoot
}
elseif (-not (Test-Path -LiteralPath $resolvedEvidencePath -PathType Container)) {
    throw "证据包路径必须是目录或 zip 文件：$resolvedEvidencePath"
}

$acceptanceTemplateName = 'Avalonia12-' + (Join-ReviewUnicodeName -CodePoints @(0x73B0, 0x573A, 0x8BD5, 0x8FD0, 0x884C, 0x9A8C, 0x6536, 0x8BB0, 0x5F55, 0x6A21, 0x677F)) + '.md'
$screenshotPlaceholderName = (Join-ReviewUnicodeName -CodePoints @(0x622A, 0x56FE, 0x5360, 0x4F4D, 0x8BF4, 0x660E)) + '.md'
$missing = [System.Collections.Generic.List[string]]::new()

[void](Test-ReviewFile -Root $evidenceRoot -RelativePath 'release-manifest.json' -Missing $missing)
[void](Test-ReviewFile -Root $evidenceRoot -RelativePath 'candidate-validation-summary.json' -Missing $missing)
[void](Test-ReviewFile -Root $evidenceRoot -RelativePath 'field-evidence-summary.json' -Missing $missing)
[void](Test-ReviewFile -Root $evidenceRoot -RelativePath 'diagnostics-summary.md' -Missing $missing)
[void](Test-ReviewFile -Root $evidenceRoot -RelativePath 'launcher\launcher.profiles.json' -Missing $missing)
[void](Test-ReviewFile -Root $evidenceRoot -RelativePath (Join-Path 'docs' $acceptanceTemplateName) -Missing $missing)
[void](Test-ReviewFile -Root $evidenceRoot -RelativePath (Join-Path 'screenshots' $screenshotPlaceholderName) -Missing $missing)

$releaseManifest = Get-ReviewJsonFile -Path (Join-Path $evidenceRoot 'release-manifest.json')
$candidateSummary = Get-ReviewJsonFile -Path (Join-Path $evidenceRoot 'candidate-validation-summary.json')
$launcherProfiles = Get-LauncherProfileAssessment -ProfilePath (Join-Path $evidenceRoot 'launcher\launcher.profiles.json')
$screenshotFiles = @()
$screenshotsRoot = Join-Path $evidenceRoot 'screenshots'
if (Test-Path -LiteralPath $screenshotsRoot -PathType Container) {
    $screenshotFiles = @(Get-ChildItem -LiteralPath $screenshotsRoot -File -Include '*.png', '*.jpg', '*.jpeg', '*.bmp' -Recurse -ErrorAction SilentlyContinue)
}

$p0Blockers = [System.Collections.Generic.List[string]]::new()
foreach ($item in $missing) {
    $p0Blockers.Add("缺失必要证据：$item") | Out-Null
}

if ($releaseManifest -and $releaseManifest.releaseKind -ne 'AvaloniaMigration') {
    $p0Blockers.Add("release-manifest.json releaseKind 不是 AvaloniaMigration。") | Out-Null
}

$wpfFallbackVerified = $false
if ($candidateSummary -and $candidateSummary.PSObject.Properties.Name -contains 'wpfFallback') {
    $wpfFallbackVerified = [bool]$candidateSummary.wpfFallback.verified
}

if (-not $wpfFallbackVerified) {
    $p0Blockers.Add('candidate-validation-summary.json 未记录 WPF 回退构建通过。') | Out-Null
}

if ($launcherProfiles.uiOnlyProfileCount -lt 1) {
    $p0Blockers.Add('launcher.profiles.json 缺少 UI-only profile。') | Out-Null
}

if ($launcherProfiles.runtimeProfileCount -lt 1) {
    $p0Blockers.Add('launcher.profiles.json 缺少 --start-runtime profile。') | Out-Null
}

$p1Pending = [System.Collections.Generic.List[string]]::new()
if ($screenshotFiles.Count -eq 0) {
    $p1Pending.Add('证据包尚未包含现场截图文件，仅包含截图占位说明。') | Out-Null
}

$completedAcceptanceRecord = Test-Path -LiteralPath (Join-Path $evidenceRoot 'docs\Avalonia12-现场试运行验收记录.md') -PathType Leaf
if (-not $completedAcceptanceRecord) {
    $p1Pending.Add('证据包尚未包含已填写的现场试运行验收记录，仅包含模板。') | Out-Null
}

$overallStatus = if ($p0Blockers.Count -gt 0) {
    'P0Blocked'
}
elseif ($p1Pending.Count -gt 0) {
    'P1Pending'
}
else {
    'ReadyForDefaultEntryReview'
}

$summary = [PSCustomObject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('O')
    evidencePath = $resolvedEvidencePath
    evidenceRoot = $evidenceRoot
    outputRoot = $reviewRoot
    overallStatus = $overallStatus
    missingRequiredFiles = @($missing)
    p0Blockers = @($p0Blockers)
    p1Pending = @($p1Pending)
    releaseKind = if ($releaseManifest) { $releaseManifest.releaseKind } else { $null }
    wpfFallbackVerified = $wpfFallbackVerified
    launcherProfiles = $launcherProfiles
    screenshotFileCount = $screenshotFiles.Count
    completedAcceptanceRecord = $completedAcceptanceRecord
    readonlyBoundary = @(
        '只读取证据包、Launcher profile、manifest、summary、日志摘要和截图目录。',
        '不读取业务数据库。',
        '不修改运行目录或证据包。',
        '不调用 Cloud/MES 清理、重试、删除或 PLC 写入命令。'
    )
}

$summaryPath = Join-Path $reviewRoot 'trial-review-summary.json'
$reportPath = Join-Path $reviewRoot 'trial-review-report.md'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-ReviewReport -Path $reportPath -Summary $summary

if ($p0Blockers.Count -gt 0) {
    throw "试运行证据审查发现 P0 阻断：$($p0Blockers -join '；')"
}

Write-Host 'Avalonia trial evidence review completed.'
Write-Host "  Status: $overallStatus"
Write-Host "  Summary: $summaryPath"
Write-Host "  Report: $reportPath"
