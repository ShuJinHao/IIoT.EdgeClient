param(
    [string]$OutputRoot = 'publish\edge-velopack',

    [string]$Channel = 'homogenization',

    [string]$PackId = 'IIoT.EdgeClient.Homogenization',

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$RuntimeDirectory = 'homogenization',

    [switch]$RequireDelta
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

function Assert-ZipEntryExists {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive,

        [Parameter(Mandatory = $true)]
        [string]$EntryName
    )

    $entry = Get-ZipEntry -Archive $Archive -EntryName $EntryName
    if ($null -eq $entry) {
        throw "Required package entry was not found: $EntryName"
    }

    return $entry
}

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive,

        [Parameter(Mandatory = $true)]
        [string]$EntryName
    )

    $entry = Assert-ZipEntryExists -Archive $Archive -EntryName $EntryName
    $reader = [System.IO.StreamReader]::new($entry.Open(), [System.Text.Encoding]::UTF8)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

function Assert-CloudIdentityTemplateIsEmpty {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Json,

        [Parameter(Mandatory = $true)]
        [string]$EntryName
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
            throw "Packaged config must not contain machine identity CloudApi:$key in ${EntryName}."
        }
    }
}

function Test-ZipEntryMissing {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive,

        [Parameter(Mandatory = $true)]
        [string]$EntryName
    )

    if ($null -ne (Get-ZipEntry -Archive $Archive -EntryName $EntryName)) {
        throw "Forbidden package entry was found: $EntryName"
    }
}

$resolvedOutputRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $OutputRoot
$setupPath = Join-Path $resolvedOutputRoot "$PackId-$Channel-Setup.exe"
$fullPackagePath = Join-Path $resolvedOutputRoot "$PackId-$Version-$Channel-full.nupkg"
$deltaPackagePath = Join-Path $resolvedOutputRoot "$PackId-$Version-$Channel-delta.nupkg"
$portablePath = Join-Path $resolvedOutputRoot "$PackId-$Channel-Portable.zip"
$releasesPath = Join-Path $resolvedOutputRoot "releases.$Channel.json"
$assetsPath = Join-Path $resolvedOutputRoot "assets.$Channel.json"

Assert-PathExists -PathValue $setupPath -Message "Setup output was not found: $setupPath"
Assert-PathExists -PathValue $fullPackagePath -Message "Full package output was not found: $fullPackagePath"
Assert-PathExists -PathValue $portablePath -Message "Portable output was not found: $portablePath"
Assert-PathExists -PathValue $releasesPath -Message "Release metadata was not found: $releasesPath"
Assert-PathExists -PathValue $assetsPath -Message "Asset metadata was not found: $assetsPath"

$releases = Get-Content -Raw -Encoding UTF8 -Path $releasesPath | ConvertFrom-Json
$fullRelease = @($releases.Assets | Where-Object {
    $_.PackageId -eq $PackId -and
    $_.Version -eq $Version -and
    $_.Type -eq 'Full' -and
    $_.FileName -eq (Split-Path -Leaf $fullPackagePath)
})
if ($fullRelease.Count -ne 1) {
    throw "Release metadata does not contain exactly one full asset for $PackId $Version $Channel."
}

$deltaRelease = @($releases.Assets | Where-Object {
    $_.PackageId -eq $PackId -and
    $_.Version -eq $Version -and
    $_.Type -eq 'Delta' -and
    $_.FileName -eq (Split-Path -Leaf $deltaPackagePath)
})
if ($RequireDelta -and $deltaRelease.Count -ne 1) {
    throw "Release metadata does not contain exactly one delta asset for $PackId $Version $Channel."
}

$assets = @(Get-Content -Raw -Encoding UTF8 -Path $assetsPath | ConvertFrom-Json)
foreach ($requiredAsset in @(
    "$PackId-$Channel-Setup.exe",
    "$PackId-$Version-$Channel-full.nupkg",
    "$PackId-$Channel-Portable.zip"
)) {
    if (-not @($assets | Where-Object { $_.RelativeFileName -eq $requiredAsset }).Count) {
        throw "Asset metadata does not include required file: $requiredAsset"
    }
}

if ($RequireDelta) {
    Assert-PathExists -PathValue $deltaPackagePath -Message "Delta package output was not found: $deltaPackagePath"
    if (-not @($assets | Where-Object {
            $_.RelativeFileName -eq (Split-Path -Leaf $deltaPackagePath) -and
            $_.Type -eq 'Delta'
        }).Count) {
        throw "Asset metadata does not include required delta file: $(Split-Path -Leaf $deltaPackagePath)"
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [System.IO.Compression.ZipFile]::OpenRead($fullPackagePath)
$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "iiot-edge-velopack-package-test-$([System.Guid]::NewGuid().ToString('N'))"
try {
    Assert-ZipEntryExists -Archive $archive -EntryName 'lib/app/IIoT.Edge.Launcher.exe' | Out-Null
    $launcherAssemblyEntry = Assert-ZipEntryExists -Archive $archive -EntryName 'lib/app/IIoT.Edge.Launcher.dll'
    Assert-ZipEntryExists -Archive $archive -EntryName "lib/app/$RuntimeDirectory/IIoT.Edge.Shell.exe" | Out-Null
    Assert-ZipEntryExists -Archive $archive -EntryName 'lib/app/launcher.accounts.sample.json' | Out-Null
    Assert-ZipEntryExists -Archive $archive -EntryName 'lib/app/launcher.update.sample.json' | Out-Null

    $appSettingsEntryName = "lib/app/$RuntimeDirectory/appsettings.json"
    $appSettingsJson = Read-ZipEntryText -Archive $archive -EntryName $appSettingsEntryName
    Assert-CloudIdentityTemplateIsEmpty -Json $appSettingsJson -EntryName $appSettingsEntryName

    Test-ZipEntryMissing -Archive $archive -EntryName 'lib/app/launcher.accounts.json'
    Test-ZipEntryMissing -Archive $archive -EntryName 'lib/app/launcher.update.json'

    foreach ($forbiddenPattern in @(
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
            throw "Forbidden runtime data entry was found in package: $($forbidden[0].FullName)"
        }
    }

    $profilesJson = Read-ZipEntryText -Archive $archive -EntryName 'lib/app/launcher.profiles.json'
    $profiles = @($profilesJson | ConvertFrom-Json)
    if ($profiles.Count -ne 1) {
        throw "Velopack package should contain exactly one launcher profile for channel '$Channel'."
    }

    $profile = $profiles[0]
    if ($profile.ExecutablePath -ne "$RuntimeDirectory/IIoT.Edge.Shell") {
        throw "Launcher profile executable path should point to '$RuntimeDirectory/IIoT.Edge.Shell', actual: $($profile.ExecutablePath)"
    }

    $machineProfile = [string]$profile.MachineProfile
    if ([string]::IsNullOrWhiteSpace($machineProfile)) {
        throw "Launcher profile is missing MachineProfile."
    }

    $machineConfigEntryName = "lib/app/$RuntimeDirectory/appsettings.machine.$machineProfile.json"
    $machineConfigJson = Read-ZipEntryText -Archive $archive -EntryName $machineConfigEntryName
    Assert-CloudIdentityTemplateIsEmpty -Json $machineConfigJson -EntryName $machineConfigEntryName

    if ($machineConfigJson.Contains('../data', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Machine profile still points to the old relative data directory: $machineConfigEntryName"
    }

    if (-not $machineConfigJson.Contains('%ProgramData%', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Machine profile does not use ProgramData data root: $machineConfigEntryName"
    }

    New-Item -Path $tempDirectory -ItemType Directory -Force | Out-Null
    $launcherAssemblyPath = Join-Path $tempDirectory 'IIoT.Edge.Launcher.dll'
    $launcherAssemblyStream = $launcherAssemblyEntry.Open()
    $launcherAssemblyFile = [System.IO.File]::OpenWrite($launcherAssemblyPath)
    try {
        $launcherAssemblyStream.CopyTo($launcherAssemblyFile)
    }
    finally {
        $launcherAssemblyFile.Dispose()
        $launcherAssemblyStream.Dispose()
    }

    $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($launcherAssemblyPath).Version
    $expectedVersion = [System.Version]::Parse("$Version.0")
    if ($assemblyVersion -ne $expectedVersion) {
        throw "Launcher assembly version '$assemblyVersion' does not match package version '$expectedVersion'."
    }
}
finally {
    $archive.Dispose()
    if (Test-Path $tempDirectory) {
        Remove-Item -Path $tempDirectory -Recurse -Force
    }
}

Write-Host "Velopack package smoke test passed: $resolvedOutputRoot"
