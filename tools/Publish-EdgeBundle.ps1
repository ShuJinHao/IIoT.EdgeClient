[CmdletBinding()]
param(
    [Parameter()]
    [string]$BundleName = "all-official",

    [Parameter()]
    [string]$Configuration = "Release",

    [Parameter()]
    [string]$RepositoryRoot = (Split-Path -Path $PSScriptRoot -Parent),

    [Parameter()]
    [string]$OutputRoot = "",

    [Parameter()]
    [string]$ShellProject = "src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    $normalizedRoot = Resolve-NormalizedPath $RootPath
    $normalizedTarget = Resolve-NormalizedPath $TargetPath
    if (-not $normalizedTarget.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify '$normalizedTarget' because it is outside '$normalizedRoot'."
    }
}

function Read-BundleConfig {
    param([Parameter(Mandatory = $true)][string]$BundlePath)

    $bundle = Get-Content -Path $BundlePath -Raw | ConvertFrom-Json
    if (-not $bundle.includeModules -or $bundle.includeModules.Count -eq 0) {
        throw "Bundle file '$BundlePath' must contain a non-empty includeModules array."
    }

    return $bundle
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$TargetDirectory
    )

    if (-not (Test-Path -LiteralPath $SourceDirectory)) {
        throw "Source directory '$SourceDirectory' was not found."
    }

    New-Item -ItemType Directory -Path $TargetDirectory -Force | Out-Null

    foreach ($directory in Get-ChildItem -Path $SourceDirectory -Directory -Recurse) {
        $targetPath = $directory.FullName.Replace($SourceDirectory, $TargetDirectory)
        New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
    }

    foreach ($file in Get-ChildItem -Path $SourceDirectory -File -Recurse) {
        $targetPath = $file.FullName.Replace($SourceDirectory, $TargetDirectory)
        $targetParent = Split-Path -Path $targetPath -Parent
        New-Item -ItemType Directory -Path $targetParent -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $targetPath -Force
    }
}

$repoRoot = Resolve-NormalizedPath $RepositoryRoot
$bundleDirectory = Join-Path $repoRoot "tools/PluginBundles"
$bundlePath = Join-Path $bundleDirectory "$BundleName.json"
if (-not (Test-Path -LiteralPath $bundlePath)) {
    throw "Bundle configuration '$bundlePath' was not found."
}

$shellProjectPath = Join-Path $repoRoot $ShellProject
if (-not (Test-Path -LiteralPath $shellProjectPath)) {
    throw "Shell project '$shellProjectPath' was not found."
}

$bundle = Read-BundleConfig -BundlePath $bundlePath
$includeModules = @($bundle.includeModules | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$machineProfiles = @($bundle.machineProfiles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "publish/bundles/$BundleName"
}

$publishRoot = Resolve-NormalizedPath $OutputRoot
if (Test-Path -LiteralPath $publishRoot) {
    Assert-ChildPath -RootPath $publishRoot -TargetPath $publishRoot
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot | Out-Null

Write-Host "Publishing Edge shell for bundle '$BundleName'..."
& dotnet publish $shellProjectPath -c $Configuration -o $publishRoot -p:BuildInParallel=false --disable-build-servers
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for bundle '$BundleName'."
}

$modulesRoot = Join-Path $publishRoot "Modules"
if (-not (Test-Path -LiteralPath $modulesRoot)) {
    $shellProjectDirectory = Split-Path -Path $shellProjectPath -Parent
    $buildModulesRoot = Resolve-NormalizedPath (Join-Path $shellProjectDirectory "..\..\..\..\publish\$Configuration\net10.0-windows\Modules")
    if (Test-Path -LiteralPath $buildModulesRoot) {
        Copy-DirectoryContents -SourceDirectory $buildModulesRoot -TargetDirectory $modulesRoot
    }
}

if (Test-Path -LiteralPath $modulesRoot) {
    Get-ChildItem -Path $modulesRoot -Directory | ForEach-Object {
        if ($includeModules -notcontains $_.Name) {
            Assert-ChildPath -RootPath $modulesRoot -TargetPath $_.FullName
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
        }
    }
}

if ($machineProfiles.Count -gt 0) {
    Get-ChildItem -Path $publishRoot -Filter "appsettings.machine.*.json" | ForEach-Object {
        $profileName = $_.BaseName.Substring("appsettings.machine.".Length)
        if ($machineProfiles -notcontains $profileName) {
            Assert-ChildPath -RootPath $publishRoot -TargetPath $_.FullName
            Remove-Item -LiteralPath $_.FullName -Force
        }
    }
}

Copy-Item -LiteralPath $bundlePath -Destination (Join-Path $publishRoot "bundle.json") -Force

Write-Host "Bundle publish complete."
Write-Host "  Bundle: $BundleName"
Write-Host "  Output: $publishRoot"
Write-Host "  Modules: $($includeModules -join ', ')"
if ($machineProfiles.Count -gt 0) {
    Write-Host "  MachineProfiles: $($machineProfiles -join ', ')"
}
