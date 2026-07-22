param(
    [string]$Configuration = 'Release',

    [string]$OutputRoot = 'publish\edge-runtime-smoke',

    [string]$ManifestPath = 'scripts\edge-runtime.publish.json',

    [string]$Version,

    [string]$RuntimeIdentifier,

    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')

$manifest = Load-EdgeRuntimePublishManifest -RepoRoot $repoRoot -ManifestPath $ManifestPath
$resolvedOutputRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $OutputRoot

& (Join-Path $PSScriptRoot 'PublishEdgeRuntime.ps1') `
    -Configuration $Configuration `
    -OutputRoot $resolvedOutputRoot `
    -ManifestPath $ManifestPath `
    -Version $Version `
    -RuntimeIdentifier $RuntimeIdentifier `
    -SelfContained:$SelfContained `
    -CleanOutput

$launcherRoot = Join-Path $resolvedOutputRoot $manifest.launcherDirectory
Assert-EdgeExecutablePath `
    -BasePath $launcherRoot `
    -PathValue 'IIoT.Edge.Launcher' `
    -Message "Required launcher executable was not found in '$launcherRoot'." | Out-Null

$launcherRequiredFiles = @(
    'launcher.profiles.json',
    'launcher.accounts.sample.json',
    'launcher.update.sample.json'
)

foreach ($relativePath in $launcherRequiredFiles) {
    $fullPath = Join-Path $launcherRoot $relativePath
    if (-not (Test-Path $fullPath)) {
        throw "Required launcher artifact was not found: $fullPath"
    }
}

$launcherAccountsPath = Join-Path $launcherRoot 'launcher.accounts.json'
if (Test-Path $launcherAccountsPath) {
    throw "Runtime package must not contain real launcher accounts: $launcherAccountsPath"
}

$launcherUpdateConfigPath = Join-Path $launcherRoot 'launcher.update.json'
if (Test-Path $launcherUpdateConfigPath) {
    throw "Runtime package must not contain real launcher update config: $launcherUpdateConfigPath"
}

$launcherProfilesPath = Join-Path $launcherRoot 'launcher.profiles.json'
$launcherProfiles = Get-Content -Raw -Encoding UTF8 -Path $launcherProfilesPath | ConvertFrom-Json
if ($launcherProfiles.Count -ne $manifest.profiles.Count) {
    throw "Generated launcher.profiles.json does not match publish profile count."
}

Test-EdgeLauncherProfilesMatchManifest -Manifest $manifest -Profiles @($launcherProfiles) -LauncherRuntimeRoot $launcherRoot -CheckExecutablePath

foreach ($profile in $launcherProfiles) {
    Assert-EdgeExecutablePath `
        -BasePath $launcherRoot `
        -PathValue $profile.ExecutablePath `
        -Message "Launcher profile '$($profile.ProfileId)' points to a missing executable." | Out-Null
}

$hostRoot = Join-Path $resolvedOutputRoot $manifest.hostDirectory
Assert-EdgeExecutablePath `
    -BasePath $hostRoot `
    -PathValue 'IIoT.Edge.Shell' `
    -Message "Required Shell executable was not found in host '$hostRoot'." | Out-Null

$requiredFiles = @(
    'appsettings.json',
    'appsettings.Production.json'
)

foreach ($relativePath in $requiredFiles) {
    $fullPath = Join-Path $hostRoot $relativePath
    if (-not (Test-Path $fullPath)) {
        throw "Required host artifact was not found: $fullPath"
    }
}

foreach ($profile in $manifest.profiles) {
    $machineConfigPath = Join-Path $hostRoot (Split-Path -Leaf $profile.machineConfig)
    if (-not (Test-Path $machineConfigPath)) {
        throw "Required machine profile config was not found in host: $machineConfigPath"
    }
}

if (Test-Path (Join-Path $hostRoot 'Modules')) {
    throw "Host directory must not contain legacy Modules directory: $hostRoot"
}

$pluginsRoot = Join-Path $resolvedOutputRoot $manifest.pluginsRoot
$moduleDirectories = Get-ChildItem -Path $pluginsRoot -Directory | Select-Object -ExpandProperty Name
$expectedModules = @($manifest.profiles | ForEach-Object { $_.moduleIds } | Sort-Object -Unique)
$actualModuleKey = (@($moduleDirectories | Sort-Object) -join '|')
$expectedModuleKey = (@($expectedModules | Sort-Object) -join '|')

if ($actualModuleKey -ne $expectedModuleKey) {
    throw "Plugins root modules do not match manifest. Expected: $($expectedModules -join ', ') / Actual: $($moduleDirectories -join ', ')"
}

if ($expectedModules.Count -ne 0) {
    throw "Host runtime publish manifest must not declare business modules: $($expectedModules -join ', ')"
}

$packagedPluginFiles = @(Get-ChildItem -LiteralPath $pluginsRoot -Recurse -File -ErrorAction Stop)
if ($packagedPluginFiles.Count -ne 0) {
    throw "Host runtime plugins root must be empty; found: $($packagedPluginFiles[0].FullName)"
}

$dataRoot = Join-Path $resolvedOutputRoot 'data'
if (-not (Test-Path $dataRoot)) {
    throw "Runtime layout data directory was not found: $dataRoot"
}

Write-Host "Edge runtime publish smoke test passed: $resolvedOutputRoot"
