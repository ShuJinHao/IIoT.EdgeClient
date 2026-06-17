param(
    [string]$InstallRoot,

    [string]$AppDataRoot,

    [switch]$IncludeAppDataRoot,

    [switch]$ConfirmApply
)

$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'This script is only supported on Windows.'
}

if (-not $ConfirmApply) {
    throw 'Defender exclusions are optional operations. Re-run with -ConfirmApply after operations approval.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell session.'
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Join-Path $env:LOCALAPPDATA 'IIoTEdge'
}

$paths = [System.Collections.Generic.List[string]]::new()
$paths.Add([System.IO.Path]::GetFullPath($InstallRoot))

if ($IncludeAppDataRoot) {
    if ([string]::IsNullOrWhiteSpace($AppDataRoot)) {
        $AppDataRoot = $env:IIOT_EDGE_PROGRAM_DATA_ROOT
    }

    if (-not [string]::IsNullOrWhiteSpace($AppDataRoot)) {
        $paths.Add([System.IO.Path]::GetFullPath($AppDataRoot))
    }
}

$uniquePaths = @($paths | Sort-Object -Unique)
if ($uniquePaths.Count -eq 0) {
    throw 'No Defender exclusion paths were resolved.'
}

Add-MpPreference -ExclusionPath $uniquePaths

Write-Host 'Added Microsoft Defender exclusion paths:'
foreach ($path in $uniquePaths) {
    Write-Host " - $path"
}
