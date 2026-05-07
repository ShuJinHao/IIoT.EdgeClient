param(
    [string]$Configuration = 'Debug',

    [string]$ManifestPath = 'scripts\edge-runtime.publish.json',

    [string]$LauncherProfileCatalogPath = 'src\Edge\IIoT.Edge.Launcher\launcher.profiles.json',

    [string]$LayoutRoot = "..\publish\$Configuration",

    [string]$LauncherRuntimeRoot = "..\publish\$Configuration\launcher",

    [string]$ShellRuntimeRoot = "..\publish\$Configuration\shell"
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')

$manifest = Load-EdgeRuntimePublishManifest -RepoRoot $repoRoot -ManifestPath $ManifestPath
$launcherProfileCatalog = Get-EdgeLauncherProfileCatalog -RepoRoot $repoRoot -ProfileCatalogPath $LauncherProfileCatalogPath
$resolvedLayoutRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $LayoutRoot
$resolvedLauncherRuntimeRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $LauncherRuntimeRoot
$resolvedShellRuntimeRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $ShellRuntimeRoot
Test-EdgeLauncherProfilesMatchManifest -Manifest $manifest -Profiles $launcherProfileCatalog.Profiles -LauncherRuntimeRoot $resolvedLauncherRuntimeRoot
$legacyRuntimeRoot = Join-Path $resolvedLayoutRoot 'net10.0-windows'

if (-not (Test-Path $resolvedLauncherRuntimeRoot)) {
    throw "Launcher build output was not found: $resolvedLauncherRuntimeRoot"
}

if (-not (Test-Path $resolvedShellRuntimeRoot)) {
    throw "Shell build output was not found: $resolvedShellRuntimeRoot"
}

if (Test-Path $legacyRuntimeRoot) {
    Remove-Item -LiteralPath $legacyRuntimeRoot -Recurse -Force
}

Copy-EdgeLauncherProfileCatalog -SourcePath $launcherProfileCatalog.Path -LauncherRuntimeRoot $resolvedLauncherRuntimeRoot | Out-Null
Remove-EdgeLauncherShellArtifacts -LauncherRuntimeRoot $resolvedLauncherRuntimeRoot

foreach ($runtime in $manifest.runtimes) {
    Sync-EdgeProcessRuntime `
        -RepoRoot $repoRoot `
        -Configuration $Configuration `
        -ShellRuntimeSource $resolvedShellRuntimeRoot `
        -RuntimeDefinition $runtime `
        -LayoutRoot $resolvedLayoutRoot
}

Test-EdgeLauncherProfilesMatchManifest -Manifest $manifest -Profiles $launcherProfileCatalog.Profiles -LauncherRuntimeRoot $resolvedLauncherRuntimeRoot -CheckExecutablePath

Write-Host "Synchronized local runtime layout: $resolvedLayoutRoot"
