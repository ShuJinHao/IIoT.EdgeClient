[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidencePath,

    [string]$OutputRoot = '.artifacts\avalonia-field-evidence-inbox',

    [string]$ImportName,

    [string]$ReleaseRoot = 'publish\avalonia-migration\Release',

    [string]$SignedDecisionPackagePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-ImportFullPath {
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

function New-ImportDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    New-Item -Path $Path -ItemType Directory -Force | Out-Null
}

function Get-OptionalJsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-JsonPropertyValue {
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

function Copy-ImportDirectoryContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$TargetDirectory
    )

    New-ImportDirectory -Path $TargetDirectory
    Get-ChildItem -LiteralPath $SourceDirectory -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $TargetDirectory -Recurse -Force
    }
}

function Resolve-ReviewEvidenceRoot {
    param([Parameter(Mandatory = $true)][string]$ImportedRoot)

    if (Test-Path -LiteralPath (Join-Path $ImportedRoot 'release-manifest.json') -PathType Leaf) {
        return $ImportedRoot
    }

    $childDirectories = @(Get-ChildItem -LiteralPath $ImportedRoot -Directory -Force)
    if ($childDirectories.Count -eq 1 -and
        (Test-Path -LiteralPath (Join-Path $childDirectories[0].FullName 'release-manifest.json') -PathType Leaf)) {
        return $childDirectories[0].FullName
    }

    return $ImportedRoot
}

function Get-ImportInventory {
    param([Parameter(Mandatory = $true)][string]$Root)

    $files = @(Get-ChildItem -LiteralPath $Root -File -Recurse -Force)
    foreach ($file in $files | Sort-Object FullName) {
        $rootPrefix = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
        $filePath = [System.IO.Path]::GetFullPath($file.FullName)
        $relativePath = if ($filePath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $filePath.Substring($rootPrefix.Length)
        }
        else {
            $file.Name
        }
        $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
        [PSCustomObject]@{
            relativePath = $relativePath
            length = $file.Length
            lastWriteTimeUtc = $file.LastWriteTimeUtc.ToString('O')
            sha256 = $hash.Hash
        }
    }
}

function Test-RequiredEvidenceFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    Test-Path -LiteralPath (Join-Path $Root $RelativePath) -PathType Leaf
}

function Get-ImportMissingItems {
    param([Parameter(Mandatory = $true)][string]$Root)

    $requiredFiles = @(
        'release-manifest.json',
        'candidate-validation-summary.json',
        'field-evidence-summary.json',
        'diagnostics-summary.md',
        'launcher\launcher.profiles.json',
        'docs\Avalonia12-现场试运行验收记录模板.md',
        'docs\Avalonia12-现场试运行验收记录.md',
        'screenshots\截图占位说明.md'
    )

    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-RequiredEvidenceFile -Root $Root -RelativePath $relativePath)) {
            $missing.Add($relativePath) | Out-Null
        }
    }

    $screenshotRequirements = @(
        [PSCustomObject]@{ key = 'diagnostics-summary'; label = 'Diagnostics 摘要截图' },
        [PSCustomObject]@{ key = 'io-write-gate'; label = 'I/O 写入闸门截图' },
        [PSCustomObject]@{ key = 'plc-write-trace'; label = 'PLC 写入轨迹截图' },
        [PSCustomObject]@{ key = 'wpf-fallback'; label = 'WPF 回退截图' }
    )

    $screenshotsRoot = Join-Path $Root 'screenshots'
    $screenshots = @()
    if (Test-Path -LiteralPath $screenshotsRoot -PathType Container) {
        $screenshots = @(Get-ChildItem -LiteralPath $screenshotsRoot -File -Recurse -Force |
            Where-Object { @('.png', '.jpg', '.jpeg', '.bmp') -contains $_.Extension.ToLowerInvariant() })
    }

    foreach ($requirement in $screenshotRequirements) {
        $match = @($screenshots | Where-Object {
            $_.BaseName.IndexOf($requirement.key, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
        }) | Select-Object -First 1

        if ($null -eq $match) {
            $missing.Add($requirement.label) | Out-Null
        }
    }

    return @($missing)
}

function ConvertTo-ImportCommandLineArgument {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value) {
        return '""'
    }

    return '"' + $Value.Replace('"', '\"') + '"'
}

function Invoke-ImportCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'powershell'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $allArguments = @('-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass') + $Arguments
    $startInfo.Arguments = ($allArguments | ForEach-Object { ConvertTo-ImportCommandLineArgument $_ }) -join ' '

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $standardOutput = $process.StandardOutput.ReadToEnd()
    $standardError = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    $output = @($standardOutput, $standardError) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    [PSCustomObject]@{
        name = $Name
        exitCode = $process.ExitCode
        output = ($output -join [Environment]::NewLine)
        command = "powershell $($allArguments -join ' ')"
    }
}

function Write-ImportMarkdown {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$Summary
    )

    $lines = @(
        '# Avalonia 现场证据导入摘要',
        '',
        "- 导入时间：$($Summary.importedAt)",
        "- 来源：$($Summary.sourcePath)",
        "- 导入目录：$($Summary.importedEvidenceRoot)",
        "- 复审证据目录：$($Summary.reviewEvidenceRoot)",
        "- 文件数量：$($Summary.fileCount)",
        "- 缺失项数量：$($Summary.missingItems.Count)",
        "- 试运行复审状态：$($Summary.reviewStatus)",
        "- 默认入口预审状态：$($Summary.readinessStatus)",
        "- 预演报告：$($Summary.switchPreviewStatus)",
        '',
        '## 缺失项',
        ''
    )

    if ($Summary.missingItems.Count -eq 0) {
        $lines += '- 无。'
    }
    else {
        foreach ($item in $Summary.missingItems) {
            $lines += "- $item"
        }
    }

    $lines += @(
        '',
        '## 输出材料',
        '',
        "- 导入摘要 JSON：$($Summary.summaryJsonPath)",
        "- 试运行复审摘要：$($Summary.trialReviewSummaryPath)",
        "- 决策包草案：$($Summary.decisionPackagePath)",
        "- 默认入口预审摘要：$($Summary.readinessSummaryPath)",
        "- 默认入口预演报告：$($Summary.switchPreviewPath)",
        '',
        '## 只读边界',
        '',
        '- 本脚本只读取现场证据包原件，并写出导入副本、摘要和预审材料。',
        '- 不修改现场证据原件。',
        '- 不读取业务数据库。',
        '- 不调用 Cloud/MES 清理、重试、删除或 PLC 读写命令。',
        '- 不修改 Launcher profile、发布链路或 WPF 默认入口。'
    )

    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$resolvedEvidencePath = Resolve-ImportFullPath -BasePath $repoRoot -PathValue $EvidencePath
if (-not (Test-Path -LiteralPath $resolvedEvidencePath)) {
    throw "现场证据包路径不存在：$resolvedEvidencePath"
}

if ([string]::IsNullOrWhiteSpace($ImportName)) {
    $ImportName = "FieldEvidenceImport-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
}

$resolvedOutputRoot = Resolve-ImportFullPath -BasePath $repoRoot -PathValue $OutputRoot
$importRoot = Join-Path $resolvedOutputRoot $ImportName
if (Test-Path -LiteralPath $importRoot) {
    throw "现场证据导入目录已存在，为避免覆盖请更换 ImportName：$importRoot"
}

New-ImportDirectory -Path $importRoot
$importedEvidenceRoot = Join-Path $importRoot 'imported-evidence'
$bundleRoot = Join-Path $importRoot 'field-evidence-review-bundle'
New-ImportDirectory -Path $importedEvidenceRoot
New-ImportDirectory -Path $bundleRoot

if ((Test-Path -LiteralPath $resolvedEvidencePath -PathType Leaf) -and
    [System.IO.Path]::GetExtension($resolvedEvidencePath).Equals('.zip', [System.StringComparison]::OrdinalIgnoreCase)) {
    Expand-Archive -LiteralPath $resolvedEvidencePath -DestinationPath $importedEvidenceRoot
}
elseif (Test-Path -LiteralPath $resolvedEvidencePath -PathType Container) {
    Copy-ImportDirectoryContent -SourceDirectory $resolvedEvidencePath -TargetDirectory $importedEvidenceRoot
}
else {
    throw "现场证据包必须是目录或 zip 文件：$resolvedEvidencePath"
}

$reviewEvidenceRoot = Resolve-ReviewEvidenceRoot -ImportedRoot $importedEvidenceRoot
$inventory = @(Get-ImportInventory -Root $reviewEvidenceRoot)
$missingItems = @(Get-ImportMissingItems -Root $reviewEvidenceRoot)
$inventoryPath = Join-Path $importRoot 'evidence-file-inventory.json'
$inventory | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $inventoryPath -Encoding UTF8

$reviewOutputRoot = Join-Path $bundleRoot 'review'
$decisionOutputRoot = Join-Path $bundleRoot 'decision'
$readinessOutputRoot = Join-Path $bundleRoot 'readiness'
$switchOutputRoot = Join-Path $bundleRoot 'switch-preview'

$reviewCommand = Invoke-ImportCommand `
    -Name 'review imported field evidence' `
    -Arguments @(
        '-File',
        (Join-Path $repoRoot 'scripts\ReviewAvaloniaTrialEvidence.ps1'),
        '-EvidencePath',
        $reviewEvidenceRoot,
        '-OutputRoot',
        $reviewOutputRoot,
        '-ReviewName',
        'TrialReview',
        '-RequireCompletedAcceptance',
        '-RequireScreenshots'
    )

$trialReviewSummaryPath = Join-Path $reviewOutputRoot 'TrialReview\trial-review-summary.json'
$trialReviewSummary = Get-OptionalJsonFile -Path $trialReviewSummaryPath
$reviewStatus = if ($null -eq $trialReviewSummary) { 'NotGenerated' } else { [string](Get-JsonPropertyValue -InputObject $trialReviewSummary -Name 'overallStatus' -DefaultValue 'Unknown') }

$candidateSummaryPath = Join-Path $reviewEvidenceRoot 'candidate-validation-summary.json'
$issueRecoveryPath = Join-Path $reviewEvidenceRoot 'docs\Avalonia12-试运行问题回收清单.md'
$acceptanceRecordPath = Join-Path $reviewEvidenceRoot 'docs\Avalonia12-现场试运行验收记录.md'
$decisionCommand = $null
$generatedDecisionPackagePath = $null
if ((Test-Path -LiteralPath $candidateSummaryPath -PathType Leaf) -and
    (Test-Path -LiteralPath $trialReviewSummaryPath -PathType Leaf)) {
    $decisionCommand = Invoke-ImportCommand `
        -Name 'generate default entry decision package draft' `
        -Arguments @(
            '-File',
            (Join-Path $repoRoot 'scripts\NewAvaloniaDefaultEntryDecisionPackage.ps1'),
            '-CandidateSummaryPath',
            $candidateSummaryPath,
            '-TrialReviewSummaryPath',
            $trialReviewSummaryPath,
            '-IssueRecoveryPath',
            $issueRecoveryPath,
            '-AcceptanceRecordPath',
            $acceptanceRecordPath,
            '-OutputRoot',
            $decisionOutputRoot,
            '-PackageName',
            'DefaultEntryDecision'
        )

    $generatedDecisionPackagePath = Join-Path $decisionOutputRoot 'DefaultEntryDecision\default-entry-decision-package.json'
}

$importedSignedDecisionPath = Join-Path $reviewEvidenceRoot 'default-entry-decision-package.json'
$decisionPackageForReadiness = $null
if (-not [string]::IsNullOrWhiteSpace($SignedDecisionPackagePath)) {
    $resolvedSignedDecisionPackagePath = Resolve-ImportFullPath -BasePath $repoRoot -PathValue $SignedDecisionPackagePath
    if (-not (Test-Path -LiteralPath $resolvedSignedDecisionPackagePath -PathType Leaf)) {
        throw "签字决策包不存在：$resolvedSignedDecisionPackagePath"
    }

    $decisionPackageForReadiness = $resolvedSignedDecisionPackagePath
}
elseif (Test-Path -LiteralPath $importedSignedDecisionPath -PathType Leaf) {
    $decisionPackageForReadiness = $importedSignedDecisionPath
}
elseif (-not [string]::IsNullOrWhiteSpace($generatedDecisionPackagePath) -and
    (Test-Path -LiteralPath $generatedDecisionPackagePath -PathType Leaf)) {
    $decisionPackageForReadiness = $generatedDecisionPackagePath
}

$readinessCommand = $null
$readinessSummaryPath = $null
$readinessStatus = 'NotGenerated'
if (-not [string]::IsNullOrWhiteSpace($decisionPackageForReadiness) -and
    (Test-Path -LiteralPath $candidateSummaryPath -PathType Leaf) -and
    (Test-Path -LiteralPath $trialReviewSummaryPath -PathType Leaf)) {
    $readinessCommand = Invoke-ImportCommand `
        -Name 'precheck default entry readiness' `
        -Arguments @(
            '-File',
            (Join-Path $repoRoot 'scripts\TestAvaloniaDefaultEntryReadiness.ps1'),
            '-DecisionPackagePath',
            $decisionPackageForReadiness,
            '-CandidateSummaryPath',
            $candidateSummaryPath,
            '-TrialReviewSummaryPath',
            $trialReviewSummaryPath,
            '-AcceptanceRecordPath',
            $acceptanceRecordPath,
            '-OutputRoot',
            $readinessOutputRoot,
            '-ReviewName',
            'DefaultEntryReadiness'
        )

    $readinessSummaryPath = Join-Path $readinessOutputRoot 'DefaultEntryReadiness\default-entry-readiness-summary.json'
    $readinessSummary = Get-OptionalJsonFile -Path $readinessSummaryPath
    if ($null -ne $readinessSummary) {
        $readinessStatus = [string](Get-JsonPropertyValue -InputObject $readinessSummary -Name 'overallStatus' -DefaultValue 'Unknown')
    }
}

$switchCommand = $null
$switchPreviewPath = $null
$switchPreviewStatus = 'Skipped'
if ($readinessStatus -eq 'ApprovedForDefaultEntrySwitch' -and
    -not [string]::IsNullOrWhiteSpace($readinessSummaryPath)) {
    $resolvedReleaseRoot = Resolve-ImportFullPath -BasePath $repoRoot -PathValue $ReleaseRoot
    $switchCommand = Invoke-ImportCommand `
        -Name 'generate default entry switch preview' `
        -Arguments @(
            '-File',
            (Join-Path $repoRoot 'scripts\SwitchAvaloniaDefaultEntry.ps1'),
            '-ReadinessSummaryPath',
            $readinessSummaryPath,
            '-ReleaseRoot',
            $resolvedReleaseRoot,
            '-OutputRoot',
            $switchOutputRoot,
            '-ReportName',
            'DefaultEntrySwitchPreview',
            '-Preview'
        )

    $switchPreviewPath = Join-Path $switchOutputRoot 'DefaultEntrySwitchPreview\default-entry-switch-preview.json'
    $switchPreviewStatus = if ($switchCommand.exitCode -eq 0 -and (Test-Path -LiteralPath $switchPreviewPath -PathType Leaf)) {
        'Generated'
    }
    else {
        'Failed'
    }
}

$commands = @($reviewCommand)
if ($null -ne $decisionCommand) {
    $commands += $decisionCommand
}

if ($null -ne $readinessCommand) {
    $commands += $readinessCommand
}

if ($null -ne $switchCommand) {
    $commands += $switchCommand
}

$summaryJsonPath = Join-Path $importRoot 'evidence-import-summary.json'
$summaryMarkdownPath = Join-Path $importRoot 'evidence-import-summary.md'
$summary = [PSCustomObject]@{
    importedAt = [DateTimeOffset]::Now.ToString('O')
    script = 'scripts\ImportAvaloniaFieldEvidence.ps1'
    sourcePath = $resolvedEvidencePath
    outputRoot = $importRoot
    importedEvidenceRoot = $importedEvidenceRoot
    reviewEvidenceRoot = $reviewEvidenceRoot
    bundleRoot = $bundleRoot
    fileCount = $inventory.Count
    inventoryPath = $inventoryPath
    missingItems = @($missingItems)
    reviewStatus = $reviewStatus
    trialReviewSummaryPath = if (Test-Path -LiteralPath $trialReviewSummaryPath -PathType Leaf) { $trialReviewSummaryPath } else { $null }
    decisionPackagePath = if (-not [string]::IsNullOrWhiteSpace($generatedDecisionPackagePath) -and (Test-Path -LiteralPath $generatedDecisionPackagePath -PathType Leaf)) { $generatedDecisionPackagePath } else { $null }
    decisionPackageUsedForReadiness = $decisionPackageForReadiness
    readinessStatus = $readinessStatus
    readinessSummaryPath = if (-not [string]::IsNullOrWhiteSpace($readinessSummaryPath) -and (Test-Path -LiteralPath $readinessSummaryPath -PathType Leaf)) { $readinessSummaryPath } else { $null }
    switchPreviewStatus = $switchPreviewStatus
    switchPreviewPath = if (-not [string]::IsNullOrWhiteSpace($switchPreviewPath) -and (Test-Path -LiteralPath $switchPreviewPath -PathType Leaf)) { $switchPreviewPath } else { $null }
    commands = @($commands)
    summaryJsonPath = $summaryJsonPath
    readonlyBoundary = @(
        '只读取现场证据包原件。',
        '只写出导入副本、hash 清单、复审报告、决策包草案、readiness 结果和可选 preview 报告。',
        '不修改现场证据原件。',
        '不读取业务数据库。',
        '不调用 Cloud/MES 清理、重试、删除或 PLC 读写命令。',
        '不修改 Launcher profile、发布链路或 WPF 默认入口。'
    )
}

$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summaryJsonPath -Encoding UTF8
Write-ImportMarkdown -Path $summaryMarkdownPath -Summary $summary

Write-Host 'Avalonia field evidence import completed.'
Write-Host "  Review status: $reviewStatus"
Write-Host "  Readiness status: $readinessStatus"
Write-Host "  Switch preview: $switchPreviewStatus"
Write-Host "  Summary: $summaryJsonPath"
