param(
    [string]$Configuration = 'Release',

    [string]$OutputRoot = 'publish\edge-runtime-smoke',

    [string]$ManifestPath = 'scripts\edge-runtime.publish.json'
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
    -CleanOutput

$launcherRoot = Join-Path $resolvedOutputRoot $manifest.launcherDirectory
Assert-EdgeExecutablePath `
    -BasePath $launcherRoot `
    -PathValue 'IIoT.Edge.Launcher' `
    -Message "Required launcher executable was not found in '$launcherRoot'." | Out-Null

$launcherRequiredFiles = @(
    'launcher.profiles.json',
    'launcher.accounts.sample.json',
    'Assets\Profiles\homogenization.png'
)

foreach ($relativePath in $launcherRequiredFiles) {
    $fullPath = Join-Path $launcherRoot $relativePath
    if (-not (Test-Path $fullPath)) {
        throw "Required launcher artifact was not found: $fullPath"
    }
}

$launcherProfilesPath = Join-Path $launcherRoot 'launcher.profiles.json'
$launcherProfiles = Get-Content -Raw -Encoding UTF8 -Path $launcherProfilesPath | ConvertFrom-Json
if ($launcherProfiles.Count -ne $manifest.runtimes.Count) {
    throw "Generated launcher.profiles.json does not match runtime manifest count."
}

Test-EdgeLauncherProfilesMatchManifest -Manifest $manifest -Profiles @($launcherProfiles) -LauncherRuntimeRoot $launcherRoot -CheckExecutablePath

foreach ($profile in $launcherProfiles) {
    Assert-EdgeExecutablePath `
        -BasePath $launcherRoot `
        -PathValue $profile.ExecutablePath `
        -Message "Launcher profile '$($profile.ProfileId)' points to a missing executable." | Out-Null
}

foreach ($runtime in $manifest.runtimes) {
    $runtimeRoot = Join-Path $resolvedOutputRoot $runtime.outputDirectory
    Assert-EdgeExecutablePath `
        -BasePath $runtimeRoot `
        -PathValue 'IIoT.Edge.Shell' `
        -Message "Required Shell executable was not found in runtime '$($runtime.runtimeId)'." | Out-Null

    $requiredFiles = @(
        'appsettings.json',
        'appsettings.Production.json',
        (Split-Path -Leaf $runtime.machineConfig)
    )

    foreach ($relativePath in $requiredFiles) {
        $fullPath = Join-Path $runtimeRoot $relativePath
        if (-not (Test-Path $fullPath)) {
            throw "Required runtime artifact was not found: $fullPath"
        }
    }

    $allMachineConfigs = Get-ChildItem -Path $runtimeRoot -Filter 'appsettings.machine.*.json' -File
    if ($allMachineConfigs.Count -ne 1) {
        throw "Runtime '$($runtime.runtimeId)' should contain exactly one machine profile config, found $($allMachineConfigs.Count)."
    }

    $modulesRoot = Join-Path $runtimeRoot 'Modules'
    $moduleDirectories = Get-ChildItem -Path $modulesRoot -Directory | Select-Object -ExpandProperty Name
    $expectedModules = @($runtime.moduleIds)
    $actualModuleKey = (@($moduleDirectories | Sort-Object) -join '|')
    $expectedModuleKey = (@($expectedModules | Sort-Object) -join '|')

    if ($actualModuleKey -ne $expectedModuleKey) {
        throw "Runtime '$($runtime.runtimeId)' modules do not match manifest. Expected: $($expectedModules -join ', ') / Actual: $($moduleDirectories -join ', ')"
    }

    foreach ($moduleId in $expectedModules) {
        Test-EdgePluginManifestFile -ManifestPath (Join-Path (Join-Path $modulesRoot $moduleId) 'plugin.json')
    }
}

Write-Host "Edge runtime publish smoke test passed: $resolvedOutputRoot"
