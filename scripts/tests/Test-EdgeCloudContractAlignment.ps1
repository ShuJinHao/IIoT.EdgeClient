[CmdletBinding()]
param(
    [string]$EdgeRepositoryRoot = (Join-Path $PSScriptRoot '../..'),
    [Parameter(Mandatory)]
    [string]$CloudRepositoryRoot,
    [string]$OutputPath = 'artifacts/cross-project/edge-cloud-contract.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepositoryRoot {
    param(
        [Parameter(Mandatory)][string]$Candidate,
        [Parameter(Mandatory)][string]$Solution,
        [Parameter(Mandatory)][string]$Label
    )

    $resolved = (Resolve-Path $Candidate).Path
    if (-not (Test-Path (Join-Path $resolved $Solution) -PathType Leaf)) {
        throw "$Label repository root does not contain ${Solution}: $resolved"
    }
    return $resolved
}

function Get-Head {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Label
    )

    $head = ((& git -C $Root rev-parse HEAD 2>&1) -join "`n").Trim().ToLowerInvariant()
    if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-f]{40}$') {
        throw "$Label HEAD is not a full Git commit: $head"
    }
    return $head
}

$edgeRoot = Resolve-RepositoryRoot $EdgeRepositoryRoot 'IIoT.EdgeClient.slnx' 'Edge'
$cloudRoot = Resolve-RepositoryRoot $CloudRepositoryRoot 'IIoT.CloudPlatform.slnx' 'Cloud'
$edgeContractPath = Join-Path $edgeRoot 'src/Tests/IIoT.Edge.Cloud.ContractFilesystemTests/ContractSnapshots/pass-station-batch-v1.json'
$cloudContractPath = Join-Path $cloudRoot 'scripts/tests/baselines/cloud-pass-station-contract.json'
foreach ($path in @($edgeContractPath, $cloudContractPath)) {
    if (-not (Test-Path $path -PathType Leaf)) {
        throw "Cross-project contract snapshot is missing: $path"
    }
}

$edgeBytes = [IO.File]::ReadAllBytes($edgeContractPath)
$cloudBytes = [IO.File]::ReadAllBytes($cloudContractPath)
$edgeSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($edgeBytes)).ToLowerInvariant()
$cloudSha = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($cloudBytes)).ToLowerInvariant()
if ($edgeSha -cne $cloudSha -or
    -not [Linq.Enumerable]::SequenceEqual([byte[]]$edgeBytes, [byte[]]$cloudBytes)) {
    throw "Edge/Cloud pass-station contract snapshots differ: edge=$edgeSha cloud=$cloudSha"
}

$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $edgeRoot $OutputPath))
}
[void](New-Item (Split-Path $resolvedOutput -Parent) -ItemType Directory -Force)
[ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    contract = 'edge-pass-station-batch-v1'
    sha256 = $edgeSha
    edgeHead = Get-Head $edgeRoot 'Edge'
    cloudHead = Get-Head $cloudRoot 'Cloud'
} | ConvertTo-Json -Depth 4 | Set-Content $resolvedOutput -Encoding utf8

Write-Host "EDGE_CLOUD_CONTRACT_ALIGNMENT_OK sha256=$edgeSha output=$resolvedOutput"
