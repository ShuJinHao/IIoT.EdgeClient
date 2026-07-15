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

$analyzerRoot = Join-Path $RepositoryRoot 'src/Analyzers/IIoT.Edge.Architecture.Analyzers'
$releasePaths = @(
    Join-Path $analyzerRoot 'AnalyzerReleases.Shipped.md'
    Join-Path $analyzerRoot 'AnalyzerReleases.Unshipped.md'
)
$diagnosticsPath = Join-Path $analyzerRoot 'EdgeArchitectureDiagnostics.cs'
foreach ($requiredPath in @($releasePaths + $diagnosticsPath)) {
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

$diagnosticsText = Get-Content $diagnosticsPath -Raw
$sourceIds = @([regex]::Matches(
    $diagnosticsText,
    '(?ms)\bCreate\s*\(\s*"(?<id>[A-Z][A-Z0-9]*\d{3})"\s*,') |
    ForEach-Object { $_.Groups['id'].Value } |
    Sort-Object)
if ($sourceIds.Count -ne 23 -or
    @($sourceIds | Sort-Object -Unique).Count -ne 23 -or
    ($sourceIds -join '|') -cne ($orderedReleaseIds -join '|')) {
    throw "EDGE-ARCH-CATALOG-001 EdgeArchitectureDiagnostics.Create IDs must exactly match shipped+unshipped release IDs. release=[$($orderedReleaseIds -join ', ')] source=[$($sourceIds -join ', ')]."
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
