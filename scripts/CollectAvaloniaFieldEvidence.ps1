param(
    [string]$RuntimeRoot = 'publish\Release\avalonia-shell',

    [string]$AvaloniaShellDirectory,

    [string]$RuntimeDataRoot,

    [string]$LauncherProfilesPath,

    [string]$AvaloniaLauncherDirectory,

    [string]$OutputRoot = 'publish\field-evidence',

    [string]$PackageName,

    [switch]$Zip,

    [switch]$CreateZip,

    [string]$DiagnosticsSummary,

    [string]$DiagnosticsSummaryPath,

    [string]$ScreenshotDirectory,

    [switch]$PreflightOnly,

    [ValidateRange(1, 200)]
    [int]$MaxLogFiles = 30
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$script:ChecklistPath = Join-Path $script:RepoRoot 'docs\Avalonia12-现场联调检查清单.md'
$script:NuGetExceptionPath = Join-Path $script:RepoRoot 'docs\NuGet预览传递依赖例外记录.md'
$script:SwitchMatrixPath = Join-Path $script:RepoRoot 'docs\Avalonia12-切换前差异矩阵.md'
$script:SwitchBlockerPath = Join-Path $script:RepoRoot 'docs\Avalonia12-切换阻断清单.md'

if (-not [string]::IsNullOrWhiteSpace($AvaloniaShellDirectory) -and -not $PSBoundParameters.ContainsKey('RuntimeRoot')) {
    $RuntimeRoot = $AvaloniaShellDirectory
}

if (-not [string]::IsNullOrWhiteSpace($AvaloniaLauncherDirectory) -and -not $PSBoundParameters.ContainsKey('LauncherProfilesPath')) {
    $LauncherProfilesPath = Join-Path $AvaloniaLauncherDirectory 'launcher.profiles.json'
}

if ($CreateZip) {
    $Zip = $true
}

function Resolve-AbsolutePath {
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

function New-EvidenceDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    New-Item -Path $Path -ItemType Directory -Force | Out-Null
}

function Get-JsonPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [object]$InputObject,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Resolve-EvidenceRuntimeLayout {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InputRuntimeRoot,

        [string]$InputRuntimeDataRoot
    )

    $resolvedRuntimeRoot = Resolve-AbsolutePath -BasePath $script:RepoRoot -PathValue $InputRuntimeRoot
    if (-not (Test-Path -LiteralPath $resolvedRuntimeRoot -PathType Container)) {
        throw "Avalonia 运行目录不存在：$resolvedRuntimeRoot"
    }

    if (-not [string]::IsNullOrWhiteSpace($InputRuntimeDataRoot)) {
        $resolvedRuntimeDataRoot = Resolve-AbsolutePath -BasePath $resolvedRuntimeRoot -PathValue $InputRuntimeDataRoot
    }
    elseif (Test-Path -LiteralPath (Join-Path $resolvedRuntimeRoot 'diagnostics') -PathType Container) {
        $resolvedRuntimeDataRoot = $resolvedRuntimeRoot
    }
    else {
        $resolvedRuntimeDataRoot = Join-Path $resolvedRuntimeRoot 'data\avalonia-migration'
    }

    $diagnosticsDirectory = Join-Path $resolvedRuntimeDataRoot 'diagnostics'
    $logDirectory = Join-Path $diagnosticsDirectory 'logs'

    return [PSCustomObject]@{
        RuntimeRoot = $resolvedRuntimeRoot
        RuntimeDataRoot = $resolvedRuntimeDataRoot
        DiagnosticsDirectory = $diagnosticsDirectory
        LogDirectory = $logDirectory
        RuntimeDataRootExists = Test-Path -LiteralPath $resolvedRuntimeDataRoot -PathType Container
        DiagnosticsDirectoryExists = Test-Path -LiteralPath $diagnosticsDirectory -PathType Container
        LogDirectoryExists = Test-Path -LiteralPath $logDirectory -PathType Container
    }
}

function Resolve-LauncherProfilesFile {
    param(
        [Parameter(Mandatory = $true)]
        [object]$RuntimeLayout,

        [string]$InputLauncherProfilesPath
    )

    if (-not [string]::IsNullOrWhiteSpace($InputLauncherProfilesPath)) {
        $resolved = Resolve-AbsolutePath -BasePath $script:RepoRoot -PathValue $InputLauncherProfilesPath
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "Launcher profile 文件不存在：$resolved"
        }

        return $resolved
    }

    $runtimeRootParent = Split-Path -Parent $RuntimeLayout.RuntimeRoot
    $candidatePaths = @(
        (Join-Path $RuntimeLayout.RuntimeRoot 'launcher.profiles.json'),
        (Join-Path $runtimeRootParent 'avalonia-launcher\launcher.profiles.json'),
        (Join-Path $script:RepoRoot 'src\Edge\IIoT.Edge.Launcher.Avalonia\launcher.profiles.json')
    )

    foreach ($candidatePath in $candidatePaths) {
        if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
            return [System.IO.Path]::GetFullPath($candidatePath)
        }
    }

    return $null
}

function Get-LauncherProfileSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProfilePath
    )

    $parsedProfiles = Get-Content -Raw -Encoding UTF8 -Path $ProfilePath | ConvertFrom-Json
    $profiles = @()
    foreach ($profile in $parsedProfiles) {
        $profiles += $profile
    }

    $rows = @()
    foreach ($profile in $profiles) {
        $argumentsValue = Get-JsonPropertyValue -InputObject $profile -Name 'Arguments'
        $arguments = @()
        if ($null -ne $argumentsValue) {
            $arguments = @($argumentsValue) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }
        }

        $rows += [PSCustomObject]@{
            ProfileId = [string](Get-JsonPropertyValue -InputObject $profile -Name 'ProfileId')
            DisplayName = [string](Get-JsonPropertyValue -InputObject $profile -Name 'DisplayName')
            MachineProfile = [string](Get-JsonPropertyValue -InputObject $profile -Name 'MachineProfile')
            ExecutablePath = [string](Get-JsonPropertyValue -InputObject $profile -Name 'ExecutablePath')
            Arguments = ($arguments -join ' ')
            StartsRuntime = $arguments -contains '--start-runtime'
        }
    }

    $runtimeProfiles = @($rows | Where-Object { $_.StartsRuntime })
    $uiOnlyProfiles = @($rows | Where-Object { -not $_.StartsRuntime })

    return [PSCustomObject]@{
        Path = $ProfilePath
        Profiles = $rows
        RuntimeProfileCount = $runtimeProfiles.Count
        UiOnlyProfileCount = $uiOnlyProfiles.Count
    }
}

function Copy-SelectedFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Patterns,

        [Parameter(Mandatory = $true)]
        [string]$TargetDirectory,

        [int]$MaxFiles = 100
    )

    if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
        return @()
    }

    New-EvidenceDirectory -Path $TargetDirectory

    $byPath = @{}
    foreach ($pattern in $Patterns) {
        $files = Get-ChildItem -Path $SourceDirectory -Filter $pattern -File -ErrorAction SilentlyContinue
        foreach ($file in $files) {
            $byPath[$file.FullName] = $file
        }
    }

    $copied = @()
    foreach ($file in @($byPath.Values | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First $MaxFiles)) {
        $targetPath = Join-Path $TargetDirectory $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination $targetPath -Force
        $copied += [PSCustomObject]@{
            SourcePath = $file.FullName
            CopiedPath = $targetPath
            Length = $file.Length
            LastWriteTimeUtc = $file.LastWriteTimeUtc.ToString('O')
        }
    }

    return $copied
}

function Write-TextFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    Set-Content -LiteralPath $Path -Value $Lines -Encoding UTF8
}

function Write-CsvFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Rows,

        [Parameter(Mandatory = $true)]
        [string]$Header
    )

    if ($Rows.Count -eq 0) {
        Set-Content -LiteralPath $Path -Value $Header -Encoding UTF8
        return
    }

    $Rows | ConvertTo-Csv -NoTypeInformation | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Write-LauncherSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [object]$Summary
    )

    if ($null -eq $Summary) {
        Write-TextFile -Path $Path -Lines @(
            '# Launcher Profile 摘要',
            '',
            '- 未找到 `launcher.profiles.json`，请现场人工补充发布包中对应文件。',
            '- 证据包未推断或修改任何 Launcher profile。'
        )
        return
    }

    $lines = @(
        '# Launcher Profile 摘要',
        '',
        "- 来源：$($Summary.Path)",
        "- profile 数：$($Summary.Profiles.Count)",
        "- UI-only profile 数：$($Summary.UiOnlyProfileCount)",
        "- 运行联调 profile 数：$($Summary.RuntimeProfileCount)",
        '',
        '| ProfileId | DisplayName | MachineProfile | ExecutablePath | Arguments |',
        '| --- | --- | --- | --- | --- |'
    )

    foreach ($profile in $Summary.Profiles) {
        $lines += "| $($profile.ProfileId) | $($profile.DisplayName) | $($profile.MachineProfile) | $($profile.ExecutablePath) | $($profile.Arguments) |"
    }

    Write-TextFile -Path $Path -Lines $lines
}

function Write-DiagnosticsSummary {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$RuntimeLayout,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$CopiedLogFiles,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$CopiedDiagnosticsFiles,

        [string]$SummaryText,

        [string]$SummaryPath
    )

    $lines = @(
        '# Diagnostics 摘要',
        '',
        "- 采集时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",
        "- Avalonia 运行目录：$($RuntimeLayout.RuntimeRoot)",
        "- RuntimeDataRoot：$($RuntimeLayout.RuntimeDataRoot)",
        "- Diagnostics 目录：$($RuntimeLayout.DiagnosticsDirectory)",
        "- 日志目录：$($RuntimeLayout.LogDirectory)",
        "- 日志目录存在：$($RuntimeLayout.LogDirectoryExists)",
        "- 已复制日志文件数：$($CopiedLogFiles.Count)",
        "- 已复制诊断文本文件数：$($CopiedDiagnosticsFiles.Count)",
        '',
        '## UI 摘要',
        ''
    )

    if (-not [string]::IsNullOrWhiteSpace($SummaryText)) {
        $lines += $SummaryText
    }
    elseif (-not [string]::IsNullOrWhiteSpace($SummaryPath)) {
        $resolvedSummaryPath = Resolve-AbsolutePath -BasePath $script:RepoRoot -PathValue $SummaryPath
        if (Test-Path -LiteralPath $resolvedSummaryPath -PathType Leaf) {
            $lines += Get-Content -LiteralPath $resolvedSummaryPath -Encoding UTF8
        }
        else {
            $lines += "- 指定的 Diagnostics 摘要文件不存在：$resolvedSummaryPath"
        }
    }
    else {
        $lines += '- 未通过参数提供 UI 中显示的 Diagnostics 摘要。'
        $lines += '- 请现场人工在 Diagnostics 页截图或复制“模块数、PLC 设备数、阻断问题数、运行目录”摘要。'
    }

    Write-TextFile -Path $Path -Lines $lines
}

function Write-ScreenshotPlaceholder {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Write-TextFile -Path $Path -Lines @(
        '# PLC 写入轨迹截图占位说明',
        '',
        '现场证据包至少应补齐以下截图：',
        '',
        '1. Diagnostics 页的“PLC 写入轨迹”页签，能看到尝试、成功或失败记录。',
        '2. Diagnostics 页的“I/O 写入闸门”页签，能看到写入申请被接受或拒绝的原因。',
        '3. I/O 交互页目标信号行，能看到“已进入运行时缓冲，等待扫描任务按块写入”或后续轨迹结果。',
        '4. Equipment 面板的“最近 PLC 块写入”状态。',
        '5. 现场 PLC 侧状态截图或运维确认记录。',
        '',
        '建议命名：',
        '',
        '- `01-diagnostics-plc-write-trace.png`',
        '- `02-diagnostics-io-write-gate.png`',
        '- `03-io-row-buffer-result.png`',
        '- `04-equipment-last-block-write.png`',
        '- `05-plc-side-status.png`',
        '',
        '脚本不会自动触发 PLC 写入、不会清理现场运行数据，也不会判断 PLC 物理侧最终状态。'
    )
}

function Write-PackageReadme {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$RuntimeLayout,

        [string]$ZipPath
    )

    $zipLine = '- 未生成 zip，请直接提交本目录。'
    if (-not [string]::IsNullOrWhiteSpace($ZipPath)) {
        $zipLine = "- 已生成 zip：$ZipPath"
    }

    Write-TextFile -Path $Path -Lines @(
        '# Avalonia 现场证据包',
        '',
        "- 生成时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",
        "- 运行目录：$($RuntimeLayout.RuntimeRoot)",
        "- RuntimeDataRoot：$($RuntimeLayout.RuntimeDataRoot)",
        $zipLine,
        '',
        '## 内容',
        '',
        '- `field-evidence-summary.json`：采集输入、输出和只读边界。',
        '- `diagnostics-summary.md`：采集时生成的 Diagnostics 摘要和运行目录概况。',
        '- `runtime-logs/`：运行目录 `diagnostics/logs` 下的日志副本。',
        '- `diagnostics/`：运行目录 `diagnostics` 顶层文本诊断文件副本。',
        '- `launcher/launcher.profiles.json`：现场使用的 Launcher profile。',
        '- `launcher/launcher-profile-summary.md`：UI-only 与 `--start-runtime` profile 摘要。',
        '- `screenshots/截图占位说明.md`：PLC 写入轨迹截图占位说明。',
        '- `docs/Avalonia12-现场联调检查清单.md`：现场联调清单副本。',
        '- `docs/Avalonia12-切换前差异矩阵.md`：WPF 与 Avalonia 切换差异副本。',
        '- `docs/Avalonia12-切换阻断清单.md`：P0/P1/P2 阻断项副本。',
        '- `manifest.json`：本次采集输入、输出和只读边界记录。',
        '',
        '## 只读边界',
        '',
        '- 只复制日志、诊断文本、Launcher profile、用户提供的截图。',
        '- 不读取业务数据库文件，不复制 `db`、`context`、`recipe`、`excel` 运行数据目录。',
        '- 不调用 Cloud/MES 清理、重试、补传或数据删除接口。',
        '- 不修改运行目录中的任何文件。'
    )
}

function Invoke-InputPreflight {
    param(
        [Parameter(Mandatory = $true)]
        [object]$RuntimeLayout,

        [object]$LauncherSummary
    )

    Write-Host "Avalonia field evidence preflight"
    Write-Host "RuntimeRoot: $($RuntimeLayout.RuntimeRoot)"
    Write-Host "RuntimeDataRoot: $($RuntimeLayout.RuntimeDataRoot)"
    Write-Host "Diagnostics directory exists: $($RuntimeLayout.DiagnosticsDirectoryExists)"
    Write-Host "Log directory exists: $($RuntimeLayout.LogDirectoryExists)"

    if ($null -eq $LauncherSummary) {
        Write-Warning "未找到 launcher.profiles.json，证据包可生成，但现场需要人工补齐 Launcher profile。"
    }
    else {
        Write-Host "Launcher profiles: $($LauncherSummary.Profiles.Count)"
        Write-Host "Runtime profiles with --start-runtime: $($LauncherSummary.RuntimeProfileCount)"
        Write-Host "UI-only profiles: $($LauncherSummary.UiOnlyProfileCount)"

        if ($LauncherSummary.RuntimeProfileCount -lt 1) {
            throw "launcher.profiles.json 中未找到带 --start-runtime 的运行联调 profile。"
        }

        if ($LauncherSummary.UiOnlyProfileCount -lt 1) {
            throw "launcher.profiles.json 中未找到 UI-only profile。"
        }
    }

    Write-Host "Preflight passed."
}

$runtimeLayout = Resolve-EvidenceRuntimeLayout -InputRuntimeRoot $RuntimeRoot -InputRuntimeDataRoot $RuntimeDataRoot
$launcherProfilesFile = Resolve-LauncherProfilesFile -RuntimeLayout $runtimeLayout -InputLauncherProfilesPath $LauncherProfilesPath
$launcherSummary = $null
if (-not [string]::IsNullOrWhiteSpace($launcherProfilesFile)) {
    $launcherSummary = Get-LauncherProfileSummary -ProfilePath $launcherProfilesFile
}

Invoke-InputPreflight -RuntimeLayout $runtimeLayout -LauncherSummary $launcherSummary
if ($PreflightOnly) {
    return
}

if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = "AvaloniaFieldEvidence-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
}

$resolvedOutputRoot = Resolve-AbsolutePath -BasePath $script:RepoRoot -PathValue $OutputRoot
$packageRoot = Join-Path $resolvedOutputRoot $PackageName
if (Test-Path -LiteralPath $packageRoot) {
    throw "证据包目录已存在，为避免覆盖现场证据请更换 PackageName：$packageRoot"
}

New-EvidenceDirectory -Path $packageRoot
$logsRoot = Join-Path $packageRoot 'runtime-logs'
$diagnosticsFilesRoot = Join-Path $packageRoot 'diagnostics'
$launcherRoot = Join-Path $packageRoot 'launcher'
$screenshotsRoot = Join-Path $packageRoot 'screenshots'
$docsRoot = Join-Path $packageRoot 'docs'

$copiedLogs = @(Copy-SelectedFiles `
    -SourceDirectory $runtimeLayout.LogDirectory `
    -Patterns @('*.log', '*.txt', '*.jsonl') `
    -TargetDirectory $logsRoot `
    -MaxFiles $MaxLogFiles)

$copiedDiagnosticsFiles = @(Copy-SelectedFiles `
    -SourceDirectory $runtimeLayout.DiagnosticsDirectory `
    -Patterns @('*.log', '*.txt', '*.json', '*.md') `
    -TargetDirectory $diagnosticsFilesRoot `
    -MaxFiles 50)

$copiedScreenshots = @()
if (-not [string]::IsNullOrWhiteSpace($ScreenshotDirectory)) {
    $resolvedScreenshotDirectory = Resolve-AbsolutePath -BasePath $script:RepoRoot -PathValue $ScreenshotDirectory
    $copiedScreenshots = @(Copy-SelectedFiles `
        -SourceDirectory $resolvedScreenshotDirectory `
        -Patterns @('*.png', '*.jpg', '*.jpeg', '*.bmp') `
        -TargetDirectory $screenshotsRoot `
        -MaxFiles 50)
}
else {
    New-EvidenceDirectory -Path $screenshotsRoot
}

Write-ScreenshotPlaceholder -Path (Join-Path $screenshotsRoot '截图占位说明.md')

New-EvidenceDirectory -Path $launcherRoot
if (-not [string]::IsNullOrWhiteSpace($launcherProfilesFile)) {
    Copy-Item -LiteralPath $launcherProfilesFile -Destination (Join-Path $launcherRoot 'launcher.profiles.json') -Force
}

Write-LauncherSummary -Path (Join-Path $launcherRoot 'launcher-profile-summary.md') -Summary $launcherSummary

New-EvidenceDirectory -Path $docsRoot
if (Test-Path -LiteralPath $script:ChecklistPath -PathType Leaf) {
    Copy-Item -LiteralPath $script:ChecklistPath -Destination (Join-Path $docsRoot 'Avalonia12-现场联调检查清单.md') -Force
}
else {
    Write-TextFile -Path (Join-Path $docsRoot 'Avalonia12-现场联调检查清单.md') -Lines @(
        '# Avalonia 12 现场联调检查清单',
        '',
        '- 当前仓库未找到清单源文件，请现场按 UI-only、`--start-runtime`、I/O 手动读取、运行时缓冲写入申请、PLC 写入轨迹截图、日志留存顺序补齐证据。'
    )
}

if (Test-Path -LiteralPath $script:NuGetExceptionPath -PathType Leaf) {
    Copy-Item -LiteralPath $script:NuGetExceptionPath -Destination (Join-Path $docsRoot 'NuGet预览传递依赖例外记录.md') -Force
}

if (Test-Path -LiteralPath $script:SwitchMatrixPath -PathType Leaf) {
    Copy-Item -LiteralPath $script:SwitchMatrixPath -Destination (Join-Path $docsRoot 'Avalonia12-切换前差异矩阵.md') -Force
}

if (Test-Path -LiteralPath $script:SwitchBlockerPath -PathType Leaf) {
    Copy-Item -LiteralPath $script:SwitchBlockerPath -Destination (Join-Path $docsRoot 'Avalonia12-切换阻断清单.md') -Force
}

Write-DiagnosticsSummary `
    -Path (Join-Path $packageRoot 'diagnostics-summary.md') `
    -RuntimeLayout $runtimeLayout `
    -CopiedLogFiles $copiedLogs `
    -CopiedDiagnosticsFiles $copiedDiagnosticsFiles `
    -SummaryText $DiagnosticsSummary `
    -SummaryPath $DiagnosticsSummaryPath

Write-CsvFile -Path (Join-Path $packageRoot 'logs-inventory.csv') -Rows $copiedLogs -Header '"SourcePath","CopiedPath","Length","LastWriteTimeUtc"'
Write-CsvFile -Path (Join-Path $packageRoot 'diagnostics-files-inventory.csv') -Rows $copiedDiagnosticsFiles -Header '"SourcePath","CopiedPath","Length","LastWriteTimeUtc"'
Write-CsvFile -Path (Join-Path $packageRoot 'screenshots-inventory.csv') -Rows $copiedScreenshots -Header '"SourcePath","CopiedPath","Length","LastWriteTimeUtc"'

$manifest = [ordered]@{
    generatedAt = (Get-Date).ToString('O')
    script = 'scripts/CollectAvaloniaFieldEvidence.ps1'
    runtimeRoot = $runtimeLayout.RuntimeRoot
    runtimeDataRoot = $runtimeLayout.RuntimeDataRoot
    diagnosticsDirectory = $runtimeLayout.DiagnosticsDirectory
    logDirectory = $runtimeLayout.LogDirectory
    launcherProfilesPath = $launcherProfilesFile
    packageRoot = $packageRoot
    maxLogFiles = $MaxLogFiles
    copiedLogCount = $copiedLogs.Count
    copiedDiagnosticsFileCount = $copiedDiagnosticsFiles.Count
    copiedScreenshotCount = $copiedScreenshots.Count
    excludedRuntimeDataDirectories = @('db', 'context', 'recipe', 'excel')
    readonlyBoundary = @(
        '只复制诊断日志、诊断文本、Launcher profile 和用户提供的截图。',
        '不读取业务数据库文件。',
        '不复制 db/context/recipe/excel 运行数据目录。',
        '不调用 Cloud/MES 清理、重试、补传或数据删除接口。',
        '不修改运行目录文件。'
    )
}

$manifestJson = $manifest | ConvertTo-Json -Depth 8
$manifestJson | Set-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Encoding UTF8
$manifestJson | Set-Content -LiteralPath (Join-Path $packageRoot 'field-evidence-summary.json') -Encoding UTF8

$zipPath = $null
if ($Zip) {
    $zipPath = "$packageRoot.zip"
    if (Test-Path -LiteralPath $zipPath) {
        throw "证据包 zip 已存在，为避免覆盖现场证据请更换 PackageName：$zipPath"
    }

    Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath
}

Write-PackageReadme -Path (Join-Path $packageRoot 'README.md') -RuntimeLayout $runtimeLayout -ZipPath $zipPath

Write-Host "Avalonia field evidence package created: $packageRoot"
if (-not [string]::IsNullOrWhiteSpace($zipPath)) {
    Write-Host "Avalonia field evidence zip created: $zipPath"
}
