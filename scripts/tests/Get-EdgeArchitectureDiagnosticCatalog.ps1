[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$AnalyzerPackageRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

if ([string]::IsNullOrWhiteSpace($AnalyzerPackageRoot)) {
    $assetsPath = Join-Path $RepositoryRoot 'src/Edge/IIoT.Edge.Shell/obj/project.assets.json'
    if (-not (Test-Path $assetsPath -PathType Leaf)) {
        throw "EDGE-ARCH-CATALOG-001 Shell project assets do not exist: $assetsPath"
    }

    $assets = Get-Content $assetsPath -Raw | ConvertFrom-Json -AsHashtable
    $libraryKeys = @(
        $assets['libraries'].Keys |
            Where-Object { $_ -like 'IIoT.Edge.Module.Analyzers/*' }
    )
    if ($libraryKeys.Count -ne 1) {
        throw "EDGE-ARCH-CATALOG-001 expected exactly one resolved IIoT.Edge.Module.Analyzers package; actual=$($libraryKeys.Count)."
    }

    $packageRelativePath = [string]$assets['libraries'][$libraryKeys[0]]['path']
    $resolvedPackageRoots = @(
        foreach ($packageRoot in @($assets['packageFolders'].Keys)) {
            $candidate = [IO.Path]::GetFullPath((Join-Path $packageRoot $packageRelativePath))
            if (Test-Path $candidate -PathType Container) {
                $candidate
            }
        }
    )
    if ($resolvedPackageRoots.Count -ne 1) {
        throw "EDGE-ARCH-CATALOG-001 expected exactly one installed analyzer package root; actual=$($resolvedPackageRoots.Count)."
    }
    $AnalyzerPackageRoot = $resolvedPackageRoots[0]
} else {
    $AnalyzerPackageRoot = [IO.Path]::GetFullPath($AnalyzerPackageRoot)
}

$analyzerRoot = Join-Path $AnalyzerPackageRoot 'analyzers/dotnet/cs'
$releasePaths = @(
    Join-Path $analyzerRoot 'AnalyzerReleases.Shipped.md'
    Join-Path $analyzerRoot 'AnalyzerReleases.Unshipped.md'
)
$analyzerAssemblyPath = Join-Path $analyzerRoot 'IIoT.Edge.Module.Analyzers.dll'
foreach ($requiredPath in @($releasePaths + $analyzerAssemblyPath)) {
    if (-not (Test-Path $requiredPath -PathType Leaf)) {
        throw "EDGE-ARCH-CATALOG-001 required catalog source does not exist: $requiredPath"
    }
}

$releaseIds = [System.Collections.Generic.List[string]]::new()
foreach ($releasePath in $releasePaths) {
    foreach ($line in Get-Content $releasePath) {
        if ($line -notmatch '^\s*(?<id>[A-Z][A-Z0-9]*\d{3})\s*\|\s*(?<category>[^|]+?)\s*\|\s*(?<severity>[^|]+?)\s*\|') {
            continue
        }
        if ($Matches['category'].Trim() -cne 'IIoT.Architecture' -or
            $Matches['severity'].Trim() -cne 'Error') {
            throw "EDGE-ARCH-CATALOG-001 $($Matches['id']) must remain IIoT.Architecture/Error in $(Split-Path $releasePath -Leaf)."
        }
        $releaseIds.Add($Matches['id'])
    }
}

$orderedReleaseIds = @($releaseIds | Sort-Object)
if ($orderedReleaseIds.Count -ne 23 -or
    @($orderedReleaseIds | Sort-Object -Unique).Count -ne 23) {
    throw "EDGE-ARCH-CATALOG-001 release catalog must contain exactly 23 unique compiler diagnostic IDs; actual=$($orderedReleaseIds.Count)."
}

$projectGraphOnlyIds = @('WSARCH001', 'WSARCH005', 'WSARCH006', 'WSARCH007')
$gateIds = @($orderedReleaseIds + $projectGraphOnlyIds | Sort-Object -Unique)
$compilerAlternation = @($orderedReleaseIds | ForEach-Object { [regex]::Escape($_) }) -join '|'
$gateAlternation = @($gateIds | ForEach-Object { [regex]::Escape($_) }) -join '|'

[pscustomobject]@{
    AnalyzerPackageRoot = $AnalyzerPackageRoot
    CompilerIds = [string[]]$orderedReleaseIds
    CompilerIdAlternation = $compilerAlternation
    CompilerIdPattern = "(?i)(?<![A-Z0-9])(?:$compilerAlternation)(?![A-Z0-9])"
    ProjectGraphOnlyIds = [string[]]$projectGraphOnlyIds
    GateIds = [string[]]$gateIds
    GateIdPattern = "(?i)(?<![A-Z0-9])(?:$gateAlternation)(?![A-Z0-9])"
}
