param(
    [string]$Configuration = 'Release',

    [string]$OutputRoot = 'publish\edge-runtime',

    [string]$ManifestPath = 'scripts\edge-runtime.publish.json',

    [string]$LauncherProfileCatalogPath = 'src\Edge\IIoT.Edge.Launcher\launcher.profiles.json',

    [string]$Version,

    [string]$RuntimeIdentifier,

    [switch]$SelfContained,

    [switch]$CleanOutput
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')

$manifest = Load-EdgeRuntimePublishManifest -RepoRoot $repoRoot -ManifestPath $ManifestPath
$launcherProfileCatalog = Get-EdgeLauncherProfileCatalog -RepoRoot $repoRoot -ProfileCatalogPath $LauncherProfileCatalogPath
$resolvedOutputRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $OutputRoot
$stagingRoot = Join-Path $resolvedOutputRoot ('.staging\' + [Guid]::NewGuid().ToString('N'))
$launcherPublishRoot = Join-Path $stagingRoot 'launcher'
$shellPublishRoot = Join-Path $stagingRoot 'shell'
$launcherRuntimeRoot = Join-Path $resolvedOutputRoot $manifest.launcherDirectory
Test-EdgeLauncherProfilesMatchManifest -Manifest $manifest -Profiles $launcherProfileCatalog.Profiles -LauncherRuntimeRoot $launcherRuntimeRoot

function Publish-Project {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [string]$PublishRoot
    )

    $publishArgs = @(
        'publish',
        $ProjectPath,
        '--configuration',
        $Configuration,
        '--output',
        $PublishRoot,
        '--nologo',
        '--verbosity',
        'minimal',
        '--disable-build-servers',
        '-p:BuildInParallel=false',
        '-p:RestoreDisableParallel=true',
        '-p:SkipEdgeRuntimeLayoutSync=true'
    )

    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $publishArgs += @('--runtime', $RuntimeIdentifier)
    }

    if ($SelfContained) {
        $publishArgs += '--self-contained'
        $publishArgs += 'true'
    }

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $publishArgs += @(
            "-p:Version=$Version",
            "-p:InformationalVersion=$Version"
        )
    }

    Invoke-EdgeNativeCommand -FilePath 'dotnet' -Arguments $publishArgs
}

try {
    if ($CleanOutput -and (Test-Path $resolvedOutputRoot)) {
        Remove-Item -Path $resolvedOutputRoot -Recurse -Force
    }

    New-Item -Path $resolvedOutputRoot -ItemType Directory -Force | Out-Null
    New-Item -Path $stagingRoot -ItemType Directory -Force | Out-Null

    Publish-Project `
        -ProjectPath (Join-Path $repoRoot 'src\Edge\IIoT.Edge.Launcher\IIoT.Edge.Launcher.csproj') `
        -PublishRoot $launcherPublishRoot

    Publish-Project `
        -ProjectPath (Join-Path $repoRoot 'src\Edge\IIoT.Edge.Shell\IIoT.Edge.Shell.csproj') `
        -PublishRoot $shellPublishRoot

    if (Test-Path $launcherRuntimeRoot) {
        Remove-Item -Path $launcherRuntimeRoot -Recurse -Force
    }

    Copy-EdgeDirectoryContent -SourceDirectory $launcherPublishRoot -TargetDirectory $launcherRuntimeRoot
    Copy-EdgeLauncherProfileCatalog -SourcePath $launcherProfileCatalog.Path -LauncherRuntimeRoot $launcherRuntimeRoot | Out-Null
    Remove-EdgeLauncherShellArtifacts -LauncherRuntimeRoot $launcherRuntimeRoot

    foreach ($runtime in $manifest.runtimes) {
        Sync-EdgeProcessRuntime `
            -RepoRoot $repoRoot `
            -Configuration $Configuration `
            -ShellRuntimeSource $shellPublishRoot `
            -RuntimeDefinition $runtime `
            -LayoutRoot $resolvedOutputRoot
    }

    Test-EdgeLauncherProfilesMatchManifest -Manifest $manifest -Profiles $launcherProfileCatalog.Profiles -LauncherRuntimeRoot $launcherRuntimeRoot -CheckExecutablePath

    Write-Host "Published runtime layout root: $resolvedOutputRoot"
}
finally {
    if (Test-Path $stagingRoot) {
        Remove-Item -Path $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
