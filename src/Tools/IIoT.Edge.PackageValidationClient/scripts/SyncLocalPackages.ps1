$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$edgeRepoRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot '..\..\..'))
$source = Join-Path $edgeRepoRoot '.artifacts\nuget'
$target = Join-Path $repoRoot ".artifacts\\nuget"

if (-not (Test-Path $source)) {
    throw "Source package feed not found: $source"
}

New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source "*.nupkg") -Destination $target -Force -ErrorAction SilentlyContinue
Copy-Item -Path (Join-Path $source "*.snupkg") -Destination $target -Force -ErrorAction SilentlyContinue

Write-Host "Synced local packages from $source to $target"
