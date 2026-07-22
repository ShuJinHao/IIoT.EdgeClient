[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
} else {
    $RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
}

$packagesPropsPath = Join-Path $RepositoryRoot 'Directory.Packages.props'
if (-not (Test-Path $packagesPropsPath -PathType Leaf)) {
    throw "EDGE-ARCH-CATALOG-001 package version registry does not exist: $packagesPropsPath"
}

[xml]$packagesProps = Get-Content $packagesPropsPath -Raw
$analyzerVersionNodes = @($packagesProps.Project.ItemGroup.PackageVersion | Where-Object {
    [string]$_.Include -ceq 'IIoT.Edge.Module.Analyzers'
})
if ($analyzerVersionNodes.Count -ne 1 -or
    [string]::IsNullOrWhiteSpace([string]$analyzerVersionNodes[0].Version)) {
    throw 'EDGE-ARCH-CATALOG-001 IIoT.Edge.Module.Analyzers must have exactly one centrally managed package version.'
}

$analyzerPackagePath = Join-Path $RepositoryRoot (
    "eng/local-package-feed/IIoT.Edge.Module.Analyzers.$([string]$analyzerVersionNodes[0].Version).nupkg")
if (-not (Test-Path $analyzerPackagePath -PathType Leaf)) {
    throw "EDGE-ARCH-CATALOG-001 analyzer package does not exist: $analyzerPackagePath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($analyzerPackagePath)
try {
    $releaseEntryNames = @(
        'analyzers/dotnet/cs/AnalyzerReleases.Shipped.md',
        'analyzers/dotnet/cs/AnalyzerReleases.Unshipped.md'
    )
    $releaseDocuments = [Collections.Generic.List[object]]::new()
    foreach ($entryName in $releaseEntryNames) {
        $entry = $archive.GetEntry($entryName)
        if ($null -eq $entry) {
            throw "EDGE-ARCH-CATALOG-001 analyzer package is missing catalog entry: $entryName"
        }

        $stream = $entry.Open()
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true)
        try {
            $releaseDocuments.Add([pscustomobject]@{
                Name = $entryName
                Text = $reader.ReadToEnd()
            })
        } finally {
            $reader.Dispose()
            $stream.Dispose()
        }
    }

    $analyzerAssemblyEntry = $archive.GetEntry('analyzers/dotnet/cs/IIoT.Edge.Module.Analyzers.dll')
    if ($null -eq $analyzerAssemblyEntry -or $analyzerAssemblyEntry.Length -le 0) {
        throw 'EDGE-ARCH-CATALOG-001 analyzer package does not contain its compiler assembly.'
    }
} finally {
    $archive.Dispose()
}

$releaseIds = [System.Collections.Generic.List[string]]::new()
foreach ($releaseDocument in $releaseDocuments) {
    foreach ($line in ([string]$releaseDocument.Text -split "`r?`n")) {
        if ($line -notmatch '^\s*(?<id>[A-Z][A-Z0-9]*\d{3})\s*\|\s*(?<category>[^|]+?)\s*\|\s*(?<severity>[^|]+?)\s*\|') {
            continue
        }
        if ($Matches['category'].Trim() -cne 'IIoT.Architecture' -or
            $Matches['severity'].Trim() -cne 'Error') {
            throw "EDGE-ARCH-CATALOG-001 $($Matches['id']) must remain IIoT.Architecture/Error in $($releaseDocument.Name)."
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
    CompilerIds = [string[]]$orderedReleaseIds
    CompilerIdAlternation = $compilerAlternation
    CompilerIdPattern = "(?i)(?<![A-Z0-9])(?:$compilerAlternation)(?![A-Z0-9])"
    ProjectGraphOnlyIds = [string[]]$projectGraphOnlyIds
    GateIds = [string[]]$gateIds
    GateIdPattern = "(?i)(?<![A-Z0-9])(?:$gateAlternation)(?![A-Z0-9])"
}
