param(
    [Parameter(Mandatory = $true)]
    [string]$ModuleId,

    [string]$Configuration = 'Release',

    [string]$TargetRuntime = 'win-x64',

    [string]$OutputRoot = 'publish\edge-plugins',

    [switch]$CleanOutput
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')

function Get-EdgePluginRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseDirectory,

        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    return [System.IO.Path]::GetRelativePath($BaseDirectory, $PathValue).Replace('\', '/')
}

function Assert-EdgePluginPackageStaging {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StagingDirectory
    )

    $forbiddenPatterns = @(
        '(^|/)launcher\.accounts\.json$',
        '(^|/)launcher\.update\.json$',
        '(^|/)edge\.db$',
        '(^|/)pipeline_cloud\.db$',
        '(^|/)pipeline_mes\.db$',
        '\.db-wal$',
        '\.db-shm$',
        '(^|/)diagnostics/logs/',
        '(^|/)crash\.log$',
        '(^|/)recipe/',
        '(^|/)excel/'
    )

    $files = Get-ChildItem -Path $StagingDirectory -Recurse -File -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        $relativePath = Get-EdgePluginRelativePath -BaseDirectory $StagingDirectory -PathValue $file.FullName
        foreach ($pattern in $forbiddenPatterns) {
            if ($relativePath -match $pattern) {
                throw "Forbidden runtime data or site config was found in plugin package staging: $relativePath"
            }
        }
    }
}

$resolvedOutputRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $OutputRoot
if ($CleanOutput -and (Test-Path $resolvedOutputRoot)) {
    Remove-Item -Path $resolvedOutputRoot -Recurse -Force
}

New-Item -Path $resolvedOutputRoot -ItemType Directory -Force | Out-Null

$moduleProjectMap = Get-EdgeModuleProjectMap -RepoRoot $repoRoot
if (-not $moduleProjectMap.ContainsKey($ModuleId)) {
    throw "Module '$ModuleId' was not found under src\Modules."
}

Build-EdgeModuleProjects `
    -ModuleIds @($ModuleId) `
    -ModuleProjectMap $moduleProjectMap `
    -Configuration $Configuration

$project = $moduleProjectMap[$ModuleId]
$moduleBuildRoot = Join-Path $project.ProjectDirectory "bin\$Configuration\$($project.TargetFramework)"
if (-not (Test-Path $moduleBuildRoot)) {
    throw "Module build output was not found: $moduleBuildRoot"
}

$manifestPath = Join-Path $moduleBuildRoot 'plugin.json'
Test-EdgePluginManifestFile -ManifestPath $manifestPath
$manifest = Get-Content -Raw -Encoding UTF8 -Path $manifestPath | ConvertFrom-Json
[object[]]$dependencies = @()
if ($null -ne $manifest.dependencies) {
    $dependencies = @($manifest.dependencies | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
}
$entryAssemblyPath = Join-Path $moduleBuildRoot $manifest.entryAssembly
if (-not (Test-Path $entryAssemblyPath)) {
    throw "Plugin entry assembly was not found: $entryAssemblyPath"
}

$packageId = "IIoT.EdgePlugin.$($manifest.moduleId)"
$packageVersion = [string]$manifest.version
$packageFileName = "$packageId-$packageVersion-$TargetRuntime.zip"
$packagePath = Join-Path $resolvedOutputRoot $packageFileName
$metadataPath = Join-Path $resolvedOutputRoot "$packageFileName.json"
$sha256Path = Join-Path $resolvedOutputRoot "$packageFileName.sha256"
$stagingDirectory = Join-Path $resolvedOutputRoot ".staging\$($manifest.moduleId)"

if (Test-Path $stagingDirectory) {
    Remove-Item -Path $stagingDirectory -Recurse -Force
}

New-Item -Path $stagingDirectory -ItemType Directory -Force | Out-Null
Copy-Item -Path (Join-Path $moduleBuildRoot '*') -Destination $stagingDirectory -Recurse -Force
Assert-EdgePluginPackageStaging -StagingDirectory $stagingDirectory

if (Test-Path $packagePath) {
    Remove-Item -Path $packagePath -Force
}

Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $packagePath -Force
$hash = Get-FileHash -Algorithm SHA256 -Path $packagePath
$packageFile = Get-Item -Path $packagePath

$metadata = [ordered]@{
    packageSchemaVersion = 1
    moduleId = [string]$manifest.moduleId
    processType = [string]$manifest.supportedProcessType
    displayName = [string]$manifest.displayName
    version = $packageVersion
    hostApiVersion = [string]$manifest.hostApiVersion
    minHostVersion = [string]$manifest.minHostVersion
    maxHostVersion = [string]$manifest.maxHostVersion
    dependencies = $dependencies
    targetRuntime = $TargetRuntime
    targetFramework = $project.TargetFramework
    packageFileName = $packageFileName
    packageSize = $packageFile.Length
    sha256 = $hash.Hash.ToUpperInvariant()
    signature = ''
    publisher = 'IIoT'
    packageLayout = 'plugin-root-v1'
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}

$metadata | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -Path $metadataPath
"$($hash.Hash.ToUpperInvariant())  $packageFileName" | Set-Content -Encoding ASCII -Path $sha256Path

Write-Host "Edge plugin package completed."
Write-Host "Module: $($manifest.moduleId)"
Write-Host "Version: $packageVersion"
Write-Host "TargetRuntime: $TargetRuntime"
Write-Host "Output: $resolvedOutputRoot"
Write-Host "Package: $packagePath"
Write-Host "Metadata: $metadataPath"
