param(
    [string]$OutputRoot = 'publish\edge-plugins',

    [string]$ModuleId = 'Homogenization',

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$TargetRuntime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')

function Assert-PathExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathValue,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not (Test-Path $PathValue)) {
        throw $Message
    }
}

function Get-ZipEntry {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive,

        [Parameter(Mandatory = $true)]
        [string]$EntryName
    )

    $normalizedEntryName = $EntryName.Replace('\', '/')
    return $Archive.Entries |
        Where-Object { $_.FullName -eq $normalizedEntryName } |
        Select-Object -First 1
}

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive,

        [Parameter(Mandatory = $true)]
        [string]$EntryName
    )

    $entry = Get-ZipEntry -Archive $Archive -EntryName $EntryName
    if ($null -eq $entry) {
        throw "Required plugin package entry was not found: $EntryName"
    }

    $reader = [System.IO.StreamReader]::new($entry.Open(), [System.Text.Encoding]::UTF8)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

$resolvedOutputRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $OutputRoot
$packageId = "IIoT.EdgePlugin.$ModuleId"
$packageFileName = "$packageId-$Version-$TargetRuntime.zip"
$packagePath = Join-Path $resolvedOutputRoot $packageFileName
$metadataPath = Join-Path $resolvedOutputRoot "$packageFileName.json"
$sha256Path = Join-Path $resolvedOutputRoot "$packageFileName.sha256"

Assert-PathExists -PathValue $packagePath -Message "Plugin package output was not found: $packagePath"
Assert-PathExists -PathValue $metadataPath -Message "Plugin package metadata was not found: $metadataPath"
Assert-PathExists -PathValue $sha256Path -Message "Plugin package sha256 file was not found: $sha256Path"

$metadata = Get-Content -Raw -Encoding UTF8 -Path $metadataPath | ConvertFrom-Json
if ($metadata.packageSchemaVersion -ne 1) {
    throw "Unexpected plugin package schema version: $($metadata.packageSchemaVersion)"
}

foreach ($required in @('moduleId', 'processType', 'version', 'hostApiVersion', 'minHostVersion', 'maxHostVersion', 'targetRuntime', 'targetFramework', 'packageFileName', 'sha256')) {
    $property = $metadata.PSObject.Properties[$required]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "Plugin package metadata is missing required field: $required"
    }
}

if ($metadata.moduleId -ne $ModuleId) {
    throw "Plugin package metadata moduleId '$($metadata.moduleId)' does not match expected '$ModuleId'."
}

if ($metadata.version -ne $Version) {
    throw "Plugin package metadata version '$($metadata.version)' does not match expected '$Version'."
}

if ($metadata.targetRuntime -ne $TargetRuntime) {
    throw "Plugin package metadata targetRuntime '$($metadata.targetRuntime)' does not match expected '$TargetRuntime'."
}

if ($metadata.packageFileName -ne $packageFileName) {
    throw "Plugin package metadata file name '$($metadata.packageFileName)' does not match expected '$packageFileName'."
}

$hash = Get-FileHash -Algorithm SHA256 -Path $packagePath
if ($hash.Hash.ToUpperInvariant() -ne [string]$metadata.sha256) {
    throw "Plugin package metadata sha256 does not match package bytes."
}

$sha256Text = Get-Content -Raw -Encoding ASCII -Path $sha256Path
if (-not $sha256Text.Contains($hash.Hash.ToUpperInvariant(), [System.StringComparison]::Ordinal)) {
    throw "Plugin package sha256 file does not contain the computed hash."
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $pluginJson = Read-ZipEntryText -Archive $archive -EntryName 'plugin.json'
    $manifest = $pluginJson | ConvertFrom-Json

    if ($manifest.moduleId -ne $ModuleId) {
        throw "Packaged plugin.json moduleId '$($manifest.moduleId)' does not match expected '$ModuleId'."
    }

    if ($manifest.version -ne $Version) {
        throw "Packaged plugin.json version '$($manifest.version)' does not match expected '$Version'."
    }

    if ($manifest.hostApiVersion -ne $metadata.hostApiVersion) {
        throw "Packaged plugin.json hostApiVersion does not match metadata."
    }

    if ($null -eq (Get-ZipEntry -Archive $archive -EntryName $manifest.entryAssembly)) {
        throw "Packaged plugin entry assembly was not found: $($manifest.entryAssembly)"
    }

    foreach ($forbiddenPattern in @(
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
    )) {
        $forbidden = @($archive.Entries | Where-Object {
            $_.FullName -match $forbiddenPattern
        })
        if ($forbidden.Count -gt 0) {
            throw "Forbidden runtime data entry was found in plugin package: $($forbidden[0].FullName)"
        }
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Edge plugin package smoke test passed: $resolvedOutputRoot"
