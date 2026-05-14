[CmdletBinding()]
param(
    [string]$ReleaseRoot = 'publish\avalonia-migration\Release',

    [ValidateSet('Launcher', 'Shell')]
    [string]$Target = 'Shell',

    [switch]$StartRuntime
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-TrialRunFullPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $PathValue))
}

function ConvertTo-TrialRunCommandLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $escapedArguments = $Arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + ($_.Replace('"', '\"')) + '"'
        }
        else {
            $_
        }
    }

    return "$Executable $($escapedArguments -join ' ')".TrimEnd()
}

$resolvedReleaseRoot = Resolve-TrialRunFullPath -PathValue $ReleaseRoot
if (-not (Test-Path -LiteralPath $resolvedReleaseRoot -PathType Container)) {
    throw "Avalonia 发布目录不存在：$resolvedReleaseRoot"
}

$launcherExe = Join-Path $resolvedReleaseRoot 'avalonia-launcher\IIoT.Edge.Launcher.Avalonia.exe'
$shellExe = Join-Path $resolvedReleaseRoot 'avalonia-shell\IIoT.Edge.AvaloniaShell.exe'
$executable = if ($Target -eq 'Launcher') { $launcherExe } else { $shellExe }
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "启动目标不存在：$executable"
}

$arguments = @()
if ($StartRuntime) {
    if ($Target -ne 'Shell') {
        throw "运行联调必须直接启动 AvaloniaShell。请使用 -Target Shell -StartRuntime。"
    }

    $arguments += '--start-runtime'
}

$diagnosticsLogDirectory = Join-Path $resolvedReleaseRoot 'avalonia-shell\data\avalonia-migration\diagnostics\logs'
$trialLogDirectory = Join-Path $resolvedReleaseRoot 'trial-run-logs'
New-Item -Path $trialLogDirectory -ItemType Directory -Force | Out-Null

$commandLine = ConvertTo-TrialRunCommandLine -Executable $executable -Arguments $arguments
$process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $executable) -PassThru

$record = [PSCustomObject]@{
    startedAt = [DateTimeOffset]::Now.ToString('O')
    target = $Target
    startRuntime = [bool]$StartRuntime
    executable = $executable
    arguments = $arguments
    commandLine = $commandLine
    processId = $process.Id
    diagnosticsLogDirectory = $diagnosticsLogDirectory
}

$recordPath = Join-Path $trialLogDirectory ("trial-run-{0:yyyyMMdd-HHmmss}.json" -f [DateTime]::Now)
$record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $recordPath -Encoding UTF8

Write-Host "Avalonia 试运行进程已启动。"
Write-Host "  Target: $Target"
Write-Host "  ProcessId: $($process.Id)"
Write-Host "  Command: $commandLine"
Write-Host "  Diagnostics logs: $diagnosticsLogDirectory"
Write-Host "  Trial record: $recordPath"
