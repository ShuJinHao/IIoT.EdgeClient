param(
    [string]$Configuration = 'Release',

    [string]$TargetPluginsRoot = '.artifacts\edge-runtime\plugins',

    [string[]]$ModuleIds,

    [string]$ManifestPath = 'scripts\edge-runtime.publish.json',

    [switch]$CleanPluginsDirectory
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')

$manifest = Load-EdgeRuntimePublishManifest -RepoRoot $repoRoot -ManifestPath $ManifestPath

if (-not $ModuleIds -or $ModuleIds.Count -eq 0) {
        $ModuleIds = @(
        $manifest.profiles |
            ForEach-Object { @($_.moduleIds) } |
            Select-Object -Unique
    )
}

$resolvedTargetPluginsRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $TargetPluginsRoot

Publish-EdgeModulesToPluginsRoot `
    -RepoRoot $repoRoot `
    -Configuration $Configuration `
    -ModuleIds $ModuleIds `
    -TargetPluginsRoot $resolvedTargetPluginsRoot `
    -CleanPluginsDirectory:$CleanPluginsDirectory

Write-Host "Published plugin root: $resolvedTargetPluginsRoot"
