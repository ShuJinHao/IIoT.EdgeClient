param(
    [string]$OutputRoot = 'publish\edge-velopack',

    [string]$Channel = 'stable',

    [string]$PackId = 'IIoT.EdgeClient',

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$ExpectedHostDirectory = 'host',

    [string]$ExpectedPluginsRoot = 'plugins',

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

function Assert-SelfContainedRuntimeEntries {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive,

        [Parameter(Mandatory = $true)]
        [string]$LauncherRoot,

        [Parameter(Mandatory = $true)]
        [string]$HostRoot
    )

    foreach ($requiredRuntimeFile in @(
        'coreclr.dll',
        'hostfxr.dll',
        'hostpolicy.dll',
        'System.Private.CoreLib.dll'
    )) {
        Assert-ZipEntryExists -Archive $Archive -EntryName "$LauncherRoot/$requiredRuntimeFile" | Out-Null
        Assert-ZipEntryExists -Archive $Archive -EntryName "$HostRoot/$requiredRuntimeFile" | Out-Null
    }
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
    $cloudApiProperty = $config.PSObject.Properties['CloudApi']
    if ($null -eq $cloudApiProperty -or $null -eq $cloudApiProperty.Value) {
        return
    }

    foreach ($key in @('ClientCode', 'BootstrapSecret')) {
        $property = $cloudApiProperty.Value.PSObject.Properties[$key]
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
    Assert-ZipEntryExists -Archive $archive -EntryName "lib/app/$ExpectedHostDirectory/IIoT.Edge.Shell.exe" | Out-Null
    Assert-SelfContainedRuntimeEntries `
        -Archive $archive `
        -LauncherRoot 'lib/app' `
        -HostRoot "lib/app/$ExpectedHostDirectory"
    $packagedPluginEntries = @($archive.Entries | Where-Object {
        $_.FullName.StartsWith("lib/app/$ExpectedPluginsRoot/", [System.StringComparison]::OrdinalIgnoreCase)
    })
    if ($packagedPluginEntries.Count -ne 0) {
        throw "Host Velopack package must not contain business plugin files: $($packagedPluginEntries[0].FullName)"
    }
    Assert-ZipEntryExists -Archive $archive -EntryName 'lib/app/launcher.accounts.sample.json' | Out-Null
    Assert-ZipEntryExists -Archive $archive -EntryName 'lib/app/launcher.update.sample.json' | Out-Null

    $appSettingsEntryName = "lib/app/$ExpectedHostDirectory/appsettings.json"
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
    $expectedProfileIds = @('Default')
    if ($profiles.Count -lt $expectedProfileIds.Count) {
        throw "Velopack package launcher.profiles.json contains $($profiles.Count) profile(s), expected at least $($expectedProfileIds.Count)."
    }

    foreach ($expectedProfileId in $expectedProfileIds) {
        if (-not @($profiles | Where-Object { $_.ProfileId -eq $expectedProfileId }).Count) {
            throw "Velopack package launcher.profiles.json is missing profile '$expectedProfileId'."
        }
    }

    $machineProfiles = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($profile in $profiles) {
        if ($profile.ExecutablePath -ne "$ExpectedHostDirectory/IIoT.Edge.Shell") {
            throw "Launcher profile executable path should point to '$ExpectedHostDirectory/IIoT.Edge.Shell', actual: $($profile.ExecutablePath)"
        }

        $executableCandidates = @(
            "lib/app/$($profile.ExecutablePath).exe",
            "lib/app/$($profile.ExecutablePath)",
            "lib/app/$($profile.ExecutablePath).dll"
        )
        $hasExecutableCandidate = $false
        foreach ($candidate in $executableCandidates) {
            if ($null -ne (Get-ZipEntry -Archive $archive -EntryName $candidate)) {
                $hasExecutableCandidate = $true
                break
            }
        }
        if (-not $hasExecutableCandidate) {
            throw "Launcher profile '$($profile.ProfileId)' executable path '$($profile.ExecutablePath)' does not resolve to an executable file in package."
        }

        $machineProfile = [string]$profile.MachineProfile
        if ([string]::IsNullOrWhiteSpace($machineProfile)) {
            throw "Launcher profile is missing MachineProfile."
        }
        if (-not $machineProfiles.Add($machineProfile)) {
            throw "Launcher profile MachineProfile is duplicated: $machineProfile"
        }

        $machineConfigEntryName = "lib/app/$ExpectedHostDirectory/appsettings.machine.$machineProfile.json"
        $machineConfigJson = Read-ZipEntryText -Archive $archive -EntryName $machineConfigEntryName
        Assert-CloudIdentityTemplateIsEmpty -Json $machineConfigJson -EntryName $machineConfigEntryName

        if ($machineConfigJson.Contains('../data', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Machine profile still points to a hand-counted relative data directory: $machineConfigEntryName"
        }

        if ($machineConfigJson.Contains('%ProgramData%', [System.StringComparison]::OrdinalIgnoreCase) -or
            $machineConfigJson.Contains('CommonApplicationData', [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Machine profile must not use system data directories: $machineConfigEntryName"
        }
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
    $assemblyVersionPrefix = ($Version -split '[-+]')[0]
    $assemblyVersionPartCount = $assemblyVersionPrefix.Split('.').Length
    $expectedAssemblyVersion = if ($assemblyVersionPartCount -eq 3) { "$assemblyVersionPrefix.0" } else { $assemblyVersionPrefix }
    $expectedVersion = [System.Version]::Parse($expectedAssemblyVersion)
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

$portableArchive = [System.IO.Compression.ZipFile]::OpenRead($portablePath)
try {
    Assert-SelfContainedRuntimeEntries `
        -Archive $portableArchive `
        -LauncherRoot 'current' `
        -HostRoot "current/$ExpectedHostDirectory"
}
finally {
    $portableArchive.Dispose()
}

& (Join-Path $PSScriptRoot 'TestEdgeDependencyClosure.ps1') `
    -SourcePath $fullPackagePath `
    -LayoutRoot 'lib/app' `
    -RequireReferenceComparison
& (Join-Path $PSScriptRoot 'TestEdgeDependencyClosure.ps1') `
    -SourcePath $portablePath `
    -LayoutRoot 'current' `
    -RequireReferenceComparison

Write-Host "Velopack package smoke test passed: $resolvedOutputRoot"
