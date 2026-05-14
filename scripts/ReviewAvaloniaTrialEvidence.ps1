[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,

    [string]$OutputRoot = '.artifacts\avalonia-trial-review',

    [string]$ReviewName,

    [switch]$RequireCompletedAcceptance,

    [switch]$RequireScreenshots
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

function Get-AcceptanceRecordAssessment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TemplatePath,

        [Parameter(Mandatory = $true)]
        [string]$RecordPath
    )

    $recordExists = Test-Path -LiteralPath $RecordPath -PathType Leaf
    $templateExists = Test-Path -LiteralPath $TemplatePath -PathType Leaf
    if (-not $recordExists) {
        return [PSCustomObject]@{
            exists = $false
            completed = $false
            path = $RecordPath
            reason = '未发现已填写的现场试运行验收记录。'
        }
    }

    $recordText = Get-Content -LiteralPath $RecordPath -Raw -Encoding UTF8
    $templateText = if ($templateExists) { Get-Content -LiteralPath $TemplatePath -Raw -Encoding UTF8 } else { $null }
    $sameAsTemplate = $templateExists -and $recordText.Trim() -eq $templateText.Trim()
    $hasCompletionMarker = $recordText -match '验收记录状态\s*[:：]\s*(已填写|已完成)' -or
        $recordText -match '现场验收状态\s*[:：]\s*(已填写|已完成)' -or
        $recordText -match '是否允许进入切默认入口评审\s*\|\s*是\s*\|'
    $hasTemplateOnlyMarker = $recordText -match '通过 / 不通过|是 / 否|P0 / P1 / P2'
    $blankCellCount = [regex]::Matches($recordText, '\|\s*\|').Count
    $completed = -not $sameAsTemplate -and ($hasCompletionMarker -or (-not $hasTemplateOnlyMarker -and $blankCellCount -le 3))

    return [PSCustomObject]@{
        exists = $true
        completed = [bool]$completed
        path = $RecordPath
        reason = if ($completed) {
            '已识别为现场填写记录。'
        }
        elseif ($sameAsTemplate) {
            '验收记录内容与模板一致。'
        }
        else {
            '验收记录仍包含模板选项或空白项，尚不能用于关闭 P1。'
        }
    }
}

function Get-ReviewScreenshotEvidence {
    param([Parameter(Mandatory = $true)][string]$ScreenshotsRoot)

    $requirements = @(
        [PSCustomObject]@{
            key = 'diagnostics-summary'
            label = 'Diagnostics 摘要截图'
            tokens = @('diagnostics-summary', 'diagnostics', '现场摘要')
        },
        [PSCustomObject]@{
            key = 'io-write-gate'
            label = 'I/O 写入闸门截图'
            tokens = @('io-write-gate', 'write-gate', '写入闸门')
        },
        [PSCustomObject]@{
            key = 'plc-write-trace'
            label = 'PLC 写入轨迹截图'
            tokens = @('plc-write-trace', 'write-trace', '写入轨迹')
        },
        [PSCustomObject]@{
            key = 'wpf-fallback'
            label = 'WPF 回退截图'
            tokens = @('wpf-fallback', 'fallback', '回退')
        }
    )

    $files = @()
    if (Test-Path -LiteralPath $ScreenshotsRoot -PathType Container) {
        $files = @(Get-ChildItem -LiteralPath $ScreenshotsRoot -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { @('.png', '.jpg', '.jpeg', '.bmp') -contains $_.Extension.ToLowerInvariant() })
    }

    $items = foreach ($requirement in $requirements) {
        $matched = $files | Where-Object {
            $name = $_.BaseName
            $isMatch = $false
            foreach ($token in $requirement.tokens) {
                if ($name.IndexOf($token, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    $isMatch = $true
                    break
                }
            }

            $isMatch
        } | Select-Object -First 1

        [PSCustomObject]@{
            key = $requirement.key
            label = $requirement.label
            exists = $null -ne $matched
            path = if ($null -eq $matched) { $null } else { $matched.FullName }
        }
    }

    return [PSCustomObject]@{
        screenshotRoot = $ScreenshotsRoot
        screenshotFileCount = $files.Count
        requiredScreenshots = @($items)
        allRequiredScreenshotsPresent = -not (@($items | Where-Object { -not $_.exists }).Count -gt 0)
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
        "- 可进入切默认入口评审：$($Summary.readyForDefaultEntryReview)",
        '',
        '## P0/P1 判定',
        '',
        "- P0 阻断数量：$($Summary.p0Blockers.Count)",
        "- P1 待现场确认数量：$($Summary.p1Pending.Count)",
        "- WPF 回退验证：$($Summary.wpfFallbackVerified)",
        "- UI-only profile 数量：$($Summary.launcherProfiles.uiOnlyProfileCount)",
        "- `--start-runtime` profile 数量：$($Summary.launcherProfiles.runtimeProfileCount)",
        '',
        '## P1 证据',
        '',
        "| 证据项 | 状态 | 路径/说明 |",
        "| --- | --- | --- |",
        "| 已填写验收记录 | $($Summary.p1Evidence.completedAcceptanceRecord.completed) | $($Summary.p1Evidence.completedAcceptanceRecord.reason) |"
    )

    foreach ($item in $Summary.p1Evidence.requiredScreenshots) {
        $pathText = if ([string]::IsNullOrWhiteSpace($item.path)) { '缺失' } else { $item.path }
        $lines += "| $($item.label) | $($item.exists) | $pathText |"
    }

    $lines += @(
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
        '只有 P0 为零、验收记录已填写、关键截图齐全时，才输出 ReadyForDefaultEntryReview；最终是否切默认入口仍等待人工签字。'
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

$acceptanceTemplateName = 'Avalonia12-现场试运行验收记录模板.md'
$acceptanceRecordName = 'Avalonia12-现场试运行验收记录.md'
$screenshotPlaceholderName = '截图占位说明.md'
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
$acceptanceRecord = Get-AcceptanceRecordAssessment `
    -TemplatePath (Join-Path $evidenceRoot (Join-Path 'docs' $acceptanceTemplateName)) `
    -RecordPath (Join-Path $evidenceRoot (Join-Path 'docs' $acceptanceRecordName))
$screenshotEvidence = Get-ReviewScreenshotEvidence -ScreenshotsRoot (Join-Path $evidenceRoot 'screenshots')

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
if (-not $acceptanceRecord.completed) {
    $p1Pending.Add("现场试运行验收记录未完成：$($acceptanceRecord.reason)") | Out-Null
}

foreach ($item in $screenshotEvidence.requiredScreenshots) {
    if (-not $item.exists) {
        $p1Pending.Add("缺少关键截图：$($item.label)（建议文件名包含 $($item.key)）。") | Out-Null
    }
}

$readyForDefaultEntryReview = $p0Blockers.Count -eq 0 -and $acceptanceRecord.completed -and $screenshotEvidence.allRequiredScreenshotsPresent
$overallStatus = if ($p0Blockers.Count -gt 0) {
    'P0Blocked'
}
elseif ($readyForDefaultEntryReview) {
    'ReadyForDefaultEntryReview'
}
else {
    'P1Pending'
}

$p1Evidence = [PSCustomObject]@{
    completedAcceptanceRecord = $acceptanceRecord
    requiredScreenshots = @($screenshotEvidence.requiredScreenshots)
    allRequiredScreenshotsPresent = $screenshotEvidence.allRequiredScreenshotsPresent
}

$summary = [PSCustomObject]@{
    generatedAt = [DateTimeOffset]::Now.ToString('O')
    evidencePath = $resolvedEvidencePath
    evidenceRoot = $evidenceRoot
    outputRoot = $reviewRoot
    overallStatus = $overallStatus
    readyForDefaultEntryReview = $readyForDefaultEntryReview
    missingRequiredFiles = @($missing)
    p0Blockers = @($p0Blockers)
    p1Pending = @($p1Pending)
    p1Evidence = $p1Evidence
    releaseKind = if ($releaseManifest) { $releaseManifest.releaseKind } else { $null }
    wpfFallbackVerified = $wpfFallbackVerified
    launcherProfiles = $launcherProfiles
    screenshotFileCount = $screenshotEvidence.screenshotFileCount
    completedAcceptanceRecord = $acceptanceRecord.completed
    readonlyBoundary = @(
        '只读取证据包、Launcher profile、manifest、summary、日志摘要和截图目录。',
        '不读取业务数据库。',
        '不修改运行目录或证据包。',
        '不调用 Cloud/MES 清理、重试、删除或 PLC 写入命令。'
    )
}

$summaryPath = Join-Path $reviewRoot 'trial-review-summary.json'
$reportPath = Join-Path $reviewRoot 'trial-review-report.md'
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-ReviewReport -Path $reportPath -Summary $summary

if ($p0Blockers.Count -gt 0) {
    throw "试运行证据审查发现 P0 阻断：$($p0Blockers -join '；')"
}

if ($RequireCompletedAcceptance -and -not $acceptanceRecord.completed) {
    throw "试运行证据审查未通过：缺少已填写的现场试运行验收记录。"
}

if ($RequireScreenshots -and -not $screenshotEvidence.allRequiredScreenshotsPresent) {
    $missingScreenshots = @($screenshotEvidence.requiredScreenshots | Where-Object { -not $_.exists } | ForEach-Object { $_.label })
    throw "试运行证据审查未通过：缺少关键截图：$($missingScreenshots -join '；')"
}

Write-Host 'Avalonia trial evidence review completed.'
Write-Host "  Status: $overallStatus"
Write-Host "  ReadyForDefaultEntryReview: $readyForDefaultEntryReview"
Write-Host "  Summary: $summaryPath"
Write-Host "  Report: $reportPath"
