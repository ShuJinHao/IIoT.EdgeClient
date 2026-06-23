param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$Channel = 'homogenization',

    [string]$PackId = 'IIoT.EdgeClient.Homogenization',

    [string]$PackTitle = 'IIoT Edge Client Homogenization',

    [string]$PackAuthors = 'IIoT',

    [string]$ProfileId = 'HomogenizationLine',

    [string]$Configuration = 'Release',

    [string]$RuntimeIdentifier = 'win-x64',

    [string]$RuntimeLayoutRoot = 'publish\edge-runtime-velopack',

    [string]$OutputRoot = 'publish\edge-velopack',

    [string]$ManifestPath = 'scripts\edge-runtime.publish.json',

    [string]$LauncherProfileCatalogPath = 'src\Edge\IIoT.Edge.Launcher\launcher.profiles.json',

    [string]$VpkPath = 'vpk',

    [string]$ReleaseNotes,

    [switch]$SelfContained,

    [switch]$CleanOutput,

    [bool]$SkipVeloAppCheck = $false
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')

function Resolve-EdgeVpkCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    if (Test-Path $PathValue) {
        return [pscustomobject]@{
            FilePath = (Resolve-Path $PathValue).Path
            Arguments = @()
            RunFromRepoRoot = $false
        }
    }

    $command = Get-Command $PathValue -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return [pscustomobject]@{
            FilePath = $command.Source
            Arguments = @()
            RunFromRepoRoot = $false
        }
    }

    $toolManifestPath = Join-Path $repoRoot '.config\dotnet-tools.json'
    if (Test-Path $toolManifestPath) {
        Invoke-EdgeNativeCommand `
            -FilePath 'dotnet' `
            -Arguments @(
                'tool',
                'restore',
                '--tool-manifest',
                $toolManifestPath,
                '--disable-parallel'
            ) | Out-Null

        return [pscustomobject]@{
            FilePath = 'dotnet'
            Arguments = @('tool', 'run', 'vpk', '--')
            RunFromRepoRoot = $true
        }
    }

    throw "Velopack CLI was not found. Install vpk, pass -VpkPath, or restore the local dotnet tool manifest."
}

function Invoke-EdgeVpkCommand {
    param(
        [Parameter(Mandatory = $true)]
        $Command,

        [AllowEmptyCollection()]
        [string[]]$Arguments = @()
    )

    $allArguments = @($Command.Arguments) + $Arguments
    if (-not $Command.RunFromRepoRoot) {
        Invoke-EdgeNativeCommand -FilePath $Command.FilePath -Arguments $allArguments
        return
    }

    Push-Location $repoRoot
    try {
        Invoke-EdgeNativeCommand -FilePath $Command.FilePath -Arguments $allArguments
    }
    finally {
        Pop-Location
    }
}

function Copy-EdgeVelopackDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$TargetDirectory
    )

    if (-not (Test-Path $SourceDirectory)) {
        throw "Velopack staging source directory was not found: $SourceDirectory"
    }

    New-Item -Path $TargetDirectory -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $SourceDirectory '*') -Destination $TargetDirectory -Recurse -Force
}

function Get-EdgeRelativePackPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BaseDirectory,

        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    return [System.IO.Path]::GetRelativePath($BaseDirectory, $PathValue).Replace('\', '/')
}

function Remove-EdgeProtectedPackFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackDirectory
    )

    foreach ($fileName in @('launcher.accounts.json', 'launcher.update.json')) {
        $files = Get-ChildItem -Path $PackDirectory -Recurse -File -Filter $fileName -ErrorAction SilentlyContinue
        foreach ($file in $files) {
            $relativePath = Get-EdgeRelativePackPath -BaseDirectory $PackDirectory -PathValue $file.FullName
            Remove-Item -Path $file.FullName -Force
            Write-Host "Excluded protected site config from package staging: $relativePath"
        }
    }
}

function Assert-EdgePackCloudIdentityTemplatesAreEmpty {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackDirectory
    )

    $configFiles = Get-ChildItem -Path $PackDirectory -Recurse -File -Filter 'appsettings*.json' -ErrorAction SilentlyContinue
    foreach ($configFile in $configFiles) {
        $relativePath = Get-EdgeRelativePackPath -BaseDirectory $PackDirectory -PathValue $configFile.FullName
        try {
            $config = Get-Content -Raw -Encoding UTF8 -Path $configFile.FullName | ConvertFrom-Json
        }
        catch {
            throw "Packaged config file could not be parsed: $relativePath"
        }

        $cloudApiProperty = $config.PSObject.Properties['CloudApi']
        if ($null -eq $cloudApiProperty -or $null -eq $cloudApiProperty.Value) {
            continue
        }

        foreach ($key in @('ClientCode', 'BootstrapSecret')) {
            $property = $cloudApiProperty.Value.PSObject.Properties[$key]
            if ($null -eq $property) {
                continue
            }

            $value = [string]$property.Value
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                throw "Packaged config must not contain machine identity CloudApi:$key in $relativePath."
            }
        }
    }
}

function Assert-EdgeForbiddenPackContentMissing {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackDirectory
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

    $files = Get-ChildItem -Path $PackDirectory -Recurse -File -ErrorAction SilentlyContinue
    foreach ($file in $files) {
        $relativePath = Get-EdgeRelativePackPath -BaseDirectory $PackDirectory -PathValue $file.FullName
        foreach ($pattern in $forbiddenPatterns) {
            if ($relativePath -match $pattern) {
                throw "Forbidden protected runtime data or site config was found in package staging: $relativePath"
            }
        }
    }
}

function Assert-EdgeVelopackStagingRedlines {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackDirectory
    )

    Remove-EdgeProtectedPackFiles -PackDirectory $PackDirectory
    Assert-EdgePackCloudIdentityTemplatesAreEmpty -PackDirectory $PackDirectory
    Assert-EdgeForbiddenPackContentMissing -PackDirectory $PackDirectory
}

function Write-EdgeVelopackProfileCatalog {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IEnumerable]$Profiles,

        [Parameter(Mandatory = $true)]
        $ProfileDefinition,

        [Parameter(Mandatory = $true)]
        $Manifest,

        [Parameter(Mandatory = $true)]
        [string]$PackDirectory
    )

    $selectedProfile = @($Profiles | Where-Object {
        $_.ProfileId -eq $ProfileDefinition.profileId
    })

    if ($selectedProfile.Count -ne 1) {
        throw "Could not find exactly one launcher profile for profile '$($ProfileDefinition.profileId)'."
    }

    $profile = $selectedProfile[0]
    $profile.ExecutablePath = "$($Manifest.hostDirectory)/IIoT.Edge.Shell"
    ConvertTo-Json -InputObject @($profile) -Depth 20 | Set-Content `
        -Encoding UTF8 `
        -Path (Join-Path $PackDirectory 'launcher.profiles.json')
}

function Get-EdgeVelopackReleaseNotesPath {
    param(
        [string]$ReleaseNotesPath,

        [Parameter(Mandatory = $true)]
        [string]$VersionValue,

        [Parameter(Mandatory = $true)]
        [string]$ChannelValue
    )

    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
        $resolvedPath = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $ReleaseNotesPath
        if (-not (Test-Path $resolvedPath)) {
            throw "Release notes file was not found: $resolvedPath"
        }

        return $resolvedPath
    }

    $notesPath = Join-Path ([System.IO.Path]::GetTempPath()) "iiot-edge-velopack-notes-$ChannelValue-$VersionValue.md"
    Set-Content `
        -Encoding UTF8 `
        -Path $notesPath `
        -Value "IIoT Edge Client $ChannelValue $VersionValue"
    return $notesPath
}

$manifest = Load-EdgeRuntimePublishManifest -RepoRoot $repoRoot -ManifestPath $ManifestPath
$launcherProfileCatalog = Get-EdgeLauncherProfileCatalog -RepoRoot $repoRoot -ProfileCatalogPath $LauncherProfileCatalogPath
$profileDefinition = @($manifest.profiles | Where-Object {
    $_.profileId -eq $ProfileId
})[0]

if ($null -eq $profileDefinition) {
    throw "ProfileId '$ProfileId' does not match any profile in '$ManifestPath'."
}

$resolvedRuntimeLayoutRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $RuntimeLayoutRoot
$resolvedOutputRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $OutputRoot
$packDirectory = Join-Path $resolvedOutputRoot ".staging\$Channel"
$vpkCommand = Resolve-EdgeVpkCommand -PathValue $VpkPath

if ($CleanOutput -and (Test-Path $resolvedOutputRoot)) {
    Remove-Item -Path $resolvedOutputRoot -Recurse -Force
}

& (Join-Path $PSScriptRoot 'PublishEdgeRuntime.ps1') `
    -Configuration $Configuration `
    -OutputRoot $resolvedRuntimeLayoutRoot `
    -ManifestPath $ManifestPath `
    -LauncherProfileCatalogPath $LauncherProfileCatalogPath `
    -Version $Version `
    -RuntimeIdentifier $RuntimeIdentifier `
    -SelfContained:$SelfContained `
    -CleanOutput

if (Test-Path $packDirectory) {
    Remove-Item -Path $packDirectory -Recurse -Force
}

New-Item -Path $packDirectory -ItemType Directory -Force | Out-Null
Copy-EdgeVelopackDirectory `
    -SourceDirectory (Join-Path $resolvedRuntimeLayoutRoot $manifest.launcherDirectory) `
    -TargetDirectory $packDirectory
Copy-EdgeVelopackDirectory `
    -SourceDirectory (Join-Path $resolvedRuntimeLayoutRoot $manifest.hostDirectory) `
    -TargetDirectory (Join-Path $packDirectory $manifest.hostDirectory)
Write-EdgeVelopackProfileCatalog `
    -Profiles $launcherProfileCatalog.Profiles `
    -ProfileDefinition $profileDefinition `
    -Manifest $manifest `
    -PackDirectory $packDirectory
Assert-EdgeVelopackStagingRedlines -PackDirectory $packDirectory

Assert-EdgeExecutablePath `
    -BasePath $packDirectory `
    -PathValue 'IIoT.Edge.Launcher.exe' `
    -Message "Velopack main exe must be at pack root." | Out-Null
Assert-EdgeExecutablePath `
    -BasePath (Join-Path $packDirectory $manifest.hostDirectory) `
    -PathValue 'IIoT.Edge.Shell.exe' `
    -Message "Velopack host shell exe was not found." | Out-Null

$releaseNotesPath = Get-EdgeVelopackReleaseNotesPath `
    -ReleaseNotesPath $ReleaseNotes `
    -VersionValue $Version `
    -ChannelValue $Channel
$vpkArgs = @()
if ($RuntimeIdentifier.StartsWith('win', [System.StringComparison]::OrdinalIgnoreCase) -and -not (Test-EdgeIsWindowsPlatform)) {
    $vpkArgs += '[win]'
}

$iconPath = Join-Path $repoRoot 'src\Shared\IIoT.Edge.UI.Shared\Assets\images\icon.ico'
$vpkArgs += @(
    'pack',
    '--packId', $PackId,
    '--packVersion', $Version,
    '--packDir', $packDirectory,
    '--mainExe', 'IIoT.Edge.Launcher.exe',
    '--channel', $Channel,
    '--runtime', $RuntimeIdentifier,
    '--packTitle', $PackTitle,
    '--packAuthors', $PackAuthors,
    '--releaseNotes', $releaseNotesPath,
    '--icon', $iconPath,
    '--outputDir', $resolvedOutputRoot
)

if ($SkipVeloAppCheck) {
    $vpkArgs += @('--skipVeloAppCheck', 'true')
}

Invoke-EdgeVpkCommand -Command $vpkCommand -Arguments $vpkArgs

$setupPath = Get-ChildItem -Path $resolvedOutputRoot -Filter "*-$Channel-Setup.exe" -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if ($null -eq $setupPath) {
    throw "Velopack setup output was not found in '$resolvedOutputRoot'."
}

foreach ($metadataFileName in @("releases.$Channel.json", "assets.$Channel.json")) {
    $metadataPath = Join-Path $resolvedOutputRoot $metadataFileName
    if (-not (Test-Path $metadataPath)) {
        throw "Velopack metadata output was not found: $metadataPath"
    }
}

$deltaPackagePath = Join-Path $resolvedOutputRoot "$PackId-$Version-$Channel-delta.nupkg"

Write-Host "Velopack package completed."
Write-Host "Channel: $Channel"
Write-Host "Version: $Version"
Write-Host "Output: $resolvedOutputRoot"
Write-Host "Setup: $($setupPath.FullName)"
if (Test-Path $deltaPackagePath) {
    Write-Host "Delta: $deltaPackagePath"
}
