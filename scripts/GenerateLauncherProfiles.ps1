param(
    [string]$ManifestPath = 'scripts\edge-runtime.publish.json',

    [string]$OutputPath = 'src\Edge\IIoT.Edge.Launcher\launcher.profiles.json'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')

$manifest = Load-EdgeRuntimePublishManifest -RepoRoot $repoRoot -ManifestPath $ManifestPath
$resolvedOutputPath = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $OutputPath

Write-EdgeLauncherProfiles -Manifest $manifest -OutputPath $resolvedOutputPath

Write-Host "Generated launcher profile catalog: $resolvedOutputPath"
