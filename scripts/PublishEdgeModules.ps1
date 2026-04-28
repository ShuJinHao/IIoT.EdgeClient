param(
    [string]$Configuration = 'Release',

    [string]$TargetModulesRoot = '.artifacts\edge-runtime\edge-runtime\Modules',

    [string[]]$ModuleIds,

    [string]$ManifestPath = 'scripts\edge-runtime.publish.json',

    [switch]$CleanModulesDirectory
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')

$manifest = Load-EdgeRuntimePublishManifest -RepoRoot $repoRoot -ManifestPath $ManifestPath

if (-not $ModuleIds -or $ModuleIds.Count -eq 0) {
    $ModuleIds = @(
        $manifest.runtimes |
            ForEach-Object { @($_.moduleIds) } |
            Select-Object -Unique
    )
}

$resolvedTargetModulesRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $TargetModulesRoot

Publish-EdgeModulesToRuntimeRoot `
    -RepoRoot $repoRoot `
    -Configuration $Configuration `
    -ModuleIds $ModuleIds `
    -TargetModulesRoot $resolvedTargetModulesRoot `
    -CleanModulesDirectory:$CleanModulesDirectory

Write-Host "Published module runtime root: $resolvedTargetModulesRoot"
