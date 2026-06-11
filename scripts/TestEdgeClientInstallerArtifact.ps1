param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactRoot,

    [string]$ExpectedChannel = 'stable',

    [string]$ExpectedVersion,

    [string]$ExpectedRuntimeDirectory = 'homogenization',

    [string]$ExpectedModuleId = 'Homogenization'
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Resolve-TestArtifactPath {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    return [System.IO.Path]::GetFullPath($PathValue)
}

function Assert-PathExists {
    param(
        [Parameter(Mandatory = $true)][string]$PathValue,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not (Test-Path $PathValue)) {
        throw $Message
    }
}

function Get-TestSha256 {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    return (Get-FileHash -Algorithm SHA256 -Path $PathValue).Hash.ToLowerInvariant()
}

function Get-ZipEntry {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive,

        [Parameter(Mandatory = $true)]
        [string]$EntryName
    )

    $normalized = $EntryName.Replace('\', '/')
    return $Archive.Entries |
        Where-Object { $_.FullName -eq $normalized } |
        Select-Object -First 1
}

function Assert-ZipEntryExists {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive,

        [Parameter(Mandatory = $true)]
        [string]$EntryName
    )

    if ($null -eq (Get-ZipEntry -Archive $Archive -EntryName $EntryName)) {
        throw "Required artifact zip entry was not found: $EntryName"
    }
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
        throw "Required artifact zip entry was not found: $EntryName"
    }

    $reader = [System.IO.StreamReader]::new($entry.Open(), [System.Text.Encoding]::UTF8)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

function Assert-ZipForbiddenEntriesMissing {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive
    )

    $forbiddenPatterns = @(
        '(^|/)launcher\.accounts\.json$',
        '(^|/)launcher\.update\.json$',
        '(^|/)iiot-binding\.json$',
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

    foreach ($entry in $Archive.Entries) {
        foreach ($pattern in $forbiddenPatterns) {
            if ($entry.FullName -match $pattern) {
                throw "Forbidden entry was found in artifact zip: $($entry.FullName)"
            }
        }
    }
}

function Assert-CloudIdentityTemplateIsEmpty {
    param(
        [Parameter(Mandatory = $true)][string]$Json,
        [Parameter(Mandatory = $true)][string]$EntryName
    )

    $config = $Json | ConvertFrom-Json
    if ($null -eq $config.CloudApi) {
        return
    }

    foreach ($key in @('ClientCode', 'BootstrapSecret')) {
        $property = $config.CloudApi.PSObject.Properties[$key]
        if ($null -eq $property) {
            continue
        }

        $value = [string]$property.Value
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            throw "Artifact zip must not contain machine identity CloudApi:$key in $EntryName."
        }
    }
}

$resolvedArtifactRoot = Resolve-TestArtifactPath -PathValue $ArtifactRoot
$manifestPath = Join-Path $resolvedArtifactRoot 'installer-artifact.json'
$layoutZipPath = Join-Path $resolvedArtifactRoot 'layout.zip'
$stubPath = Join-Path $resolvedArtifactRoot 'IIoT.Edge.Setup.exe'

Assert-PathExists -PathValue $manifestPath -Message "Artifact manifest was not found: $manifestPath"
Assert-PathExists -PathValue $layoutZipPath -Message "Artifact layout zip was not found: $layoutZipPath"
Assert-PathExists -PathValue $stubPath -Message "Installer stub was not found: $stubPath"

$manifest = Get-Content -Raw -Encoding UTF8 -Path $manifestPath | ConvertFrom-Json
if ($manifest.channel -ne $ExpectedChannel) {
    throw "Artifact channel '$($manifest.channel)' does not match expected '$ExpectedChannel'."
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $manifest.version -ne $ExpectedVersion) {
    throw "Artifact version '$($manifest.version)' does not match expected '$ExpectedVersion'."
}

if ($manifest.installerStubSha256 -ne (Get-TestSha256 -PathValue $stubPath)) {
    throw "Installer stub sha256 does not match installer-artifact.json."
}

if ($manifest.layoutZipSha256 -ne (Get-TestSha256 -PathValue $layoutZipPath)) {
    throw "Layout zip sha256 does not match installer-artifact.json."
}

$module = @($manifest.modules | Where-Object { $_.moduleId -eq $ExpectedModuleId }) | Select-Object -First 1
if ($null -eq $module) {
    throw "Artifact manifest does not contain module '$ExpectedModuleId'."
}

if ($module.runtimeDirectory -ne $ExpectedRuntimeDirectory) {
    throw "Module '$ExpectedModuleId' runtime directory '$($module.runtimeDirectory)' does not match '$ExpectedRuntimeDirectory'."
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($layoutZipPath)
try {
    Assert-ZipEntryExists -Archive $archive -EntryName 'launcher/IIoT.Edge.Launcher.dll'
    Assert-ZipEntryExists -Archive $archive -EntryName "$ExpectedRuntimeDirectory/IIoT.Edge.Shell.dll"
    Assert-ZipEntryExists -Archive $archive -EntryName "$ExpectedRuntimeDirectory/Modules/$ExpectedModuleId/plugin.json"
    Assert-ZipForbiddenEntriesMissing -Archive $archive

    foreach ($entry in @($archive.Entries | Where-Object {
        $_.FullName -like '*/appsettings*.json' -or $_.FullName -like 'appsettings*.json'
    })) {
        Assert-CloudIdentityTemplateIsEmpty `
            -Json (Read-ZipEntryText -Archive $archive -EntryName $entry.FullName) `
            -EntryName $entry.FullName
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Edge installer artifact smoke test passed: $resolvedArtifactRoot"
