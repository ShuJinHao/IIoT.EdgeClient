param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactRoot,

    [string]$ExpectedChannel = 'stable',

    [string]$ExpectedVersion,

    [string]$ExpectedHostApiVersion = '2.0.0'
)

$ErrorActionPreference = 'Stop'

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

function Assert-SelfContainedRuntimeDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Component
    )

    foreach ($requiredRuntimeFile in @(
        'coreclr.dll',
        'hostfxr.dll',
        'hostpolicy.dll',
        'System.Private.CoreLib.dll'
    )) {
        $runtimeFilePath = Join-Path $Directory $requiredRuntimeFile
        Assert-PathExists -PathValue $runtimeFilePath `
            -Message "$Component must be self-contained; required .NET runtime file was not found: $runtimeFilePath"
    }
}

function Get-TestSha256 {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    return (Get-FileHash -Algorithm SHA256 -Path $PathValue).Hash.ToLowerInvariant()
}

function Get-TestFilePaths {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [string]$Filter = '*'
    )

    if (-not (Test-Path $Directory)) {
        return @()
    }

    $resolvedDirectory = [System.IO.Path]::GetFullPath($Directory)
    return @([System.IO.Directory]::EnumerateFiles(
        $resolvedDirectory,
        $Filter,
        [System.IO.SearchOption]::AllDirectories))
}

function Get-TestDirectorySize {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $size = 0L
    foreach ($filePath in Get-TestFilePaths -Directory $Directory) {
        $size += ([System.IO.FileInfo]$filePath).Length
    }

    return $size
}

function Get-TestRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$BaseDirectory,
        [Parameter(Mandatory = $true)][string]$PathValue
    )

    return [System.IO.Path]::GetRelativePath($BaseDirectory, $PathValue).Replace('\', '/')
}

function Get-TestDirectorySha256 {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $hasher = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $files = @(Get-TestFilePaths -Directory $Directory |
            ForEach-Object {
                [PSCustomObject]@{
                    File = [System.IO.FileInfo]$_
                    RelativePath = Get-TestRelativePath -BaseDirectory $Directory -PathValue $_
                }
            })
        [array]::Sort($files, [System.Comparison[object]]{
            param($left, $right)
            return [System.StringComparer]::Ordinal.Compare($left.RelativePath, $right.RelativePath)
        })

        foreach ($entry in $files) {
            $file = $entry.File
            $relativePath = $entry.RelativePath
            $pathBytes = [System.Text.Encoding]::UTF8.GetBytes($relativePath)
            $hasher.AppendData($pathBytes)
            $hasher.AppendData([byte[]](0))

            $stream = [System.IO.File]::OpenRead($file.FullName)
            try {
                $buffer = New-Object byte[] 1048576
                while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $hasher.AppendData($buffer, 0, $read)
                }
            }
            finally {
                $stream.Dispose()
            }

            $hasher.AppendData([byte[]](10))
        }

        return ([BitConverter]::ToString($hasher.GetHashAndReset()).Replace('-', '').ToLowerInvariant())
    }
    finally {
        $hasher.Dispose()
    }
}

function Assert-ForbiddenFilesMissing {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $forbiddenPatterns = @(
        '(^|/)launcher\.accounts\.json$',
        '(^|/)launcher\.update\.json$',
        '(^|/)iiot-binding\.json$',
        '(^|/)iiot-enabled-plugins\.json$',
        '(^|/)iiot-plugin-binding\.json$',
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

    foreach ($filePath in Get-TestFilePaths -Directory $Directory) {
        $relativePath = Get-TestRelativePath -BaseDirectory $Directory -PathValue $filePath
        foreach ($pattern in $forbiddenPatterns) {
            if ($relativePath -match $pattern) {
                throw "Forbidden site config or runtime data was found in installer artifact directory: $relativePath"
            }
        }
    }
}

function Assert-CloudIdentityTemplatesAreEmpty {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $configFiles = Get-TestFilePaths -Directory $Directory -Filter 'appsettings*.json'
    foreach ($configFile in $configFiles) {
        $relativePath = Get-TestRelativePath -BaseDirectory $Directory -PathValue $configFile
        try {
            $config = Get-Content -Raw -Encoding UTF8 -LiteralPath $configFile | ConvertFrom-Json
        }
        catch {
            throw "Artifact config file could not be parsed: $relativePath"
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
                throw "Installer artifact directory must not contain machine identity CloudApi:$key in $relativePath."
            }
        }
    }
}

function Assert-InstallerVersionInfo {
    param(
        [Parameter(Mandatory = $true)][string]$PathValue,
        [Parameter(Mandatory = $true)][string]$ExpectedProductVersion
    )

    if (-not $IsWindows) {
        Write-Host "Skipping PE VersionInfo check on non-Windows host: $PathValue"
        return
    }

    $versionInfo = (Get-Item -LiteralPath $PathValue).VersionInfo
    if ($versionInfo.CompanyName -ne 'IIoT') {
        throw "Installer CompanyName must be 'IIoT', actual: '$($versionInfo.CompanyName)'."
    }

    if ($versionInfo.ProductName -ne 'IIoT Edge Client') {
        throw "Installer ProductName must be 'IIoT Edge Client', actual: '$($versionInfo.ProductName)'."
    }

    if ($versionInfo.FileDescription -ne 'IIoT Edge Client') {
        throw "Installer FileDescription must be 'IIoT Edge Client', actual: '$($versionInfo.FileDescription)'."
    }

    $expectedFileVersion = "$ExpectedHostApiVersion.0"
    if ($versionInfo.FileVersion -ne $expectedFileVersion) {
        throw "Installer FileVersion must be '$expectedFileVersion', actual: '$($versionInfo.FileVersion)'."
    }

    if ($versionInfo.ProductVersion -ne $ExpectedProductVersion) {
        throw "Installer ProductVersion must be '$ExpectedProductVersion', actual: '$($versionInfo.ProductVersion)'."
    }
}

$resolvedArtifactRoot = Resolve-TestArtifactPath -PathValue $ArtifactRoot
$manifestPath = Join-Path $resolvedArtifactRoot 'installer-artifact.json'
$legacyLayoutZipPath = Join-Path $resolvedArtifactRoot 'layout.zip'
$stubPath = Join-Path $resolvedArtifactRoot 'IIoT.Edge.Setup.exe'

Assert-PathExists -PathValue $manifestPath -Message "Artifact manifest was not found: $manifestPath"
Assert-PathExists -PathValue $stubPath -Message "Installer stub was not found: $stubPath"
Assert-InstallerVersionInfo -PathValue $stubPath -ExpectedProductVersion $ExpectedVersion
if (Test-Path $legacyLayoutZipPath) {
    throw "Legacy layout.zip must not be present in installer artifact: $legacyLayoutZipPath"
}

$manifest = Get-Content -Raw -Encoding UTF8 -Path $manifestPath | ConvertFrom-Json
foreach ($legacyProperty in @('layoutZipFile', 'layoutZipSha256', 'layoutZipSize')) {
    if ($manifest.PSObject.Properties.Name -contains $legacyProperty) {
        throw "Installer artifact manifest still contains legacy property: $legacyProperty"
    }
}

if ([int]$manifest.schemaVersion -ne 2) {
    throw "Installer artifact manifest schemaVersion must be 2, actual: $($manifest.schemaVersion)"
}

if ($manifest.channel -ne $ExpectedChannel) {
    throw "Artifact channel '$($manifest.channel)' does not match expected '$ExpectedChannel'."
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $manifest.version -ne $ExpectedVersion) {
    throw "Artifact version '$($manifest.version)' does not match expected '$ExpectedVersion'."
}

if ([string]$manifest.hostApiVersion -ne $ExpectedHostApiVersion) {
    throw "Artifact hostApiVersion '$($manifest.hostApiVersion)' does not match expected '$ExpectedHostApiVersion'."
}

foreach ($requiredProperty in @('generatedAtUtc', 'sourceCommit', 'previousVersion', 'previousSourceCommit', 'releaseNotes')) {
    if ($manifest.PSObject.Properties.Name -notcontains $requiredProperty) {
        throw "Installer artifact manifest is missing release metadata property: $requiredProperty"
    }
}

if ([string]::IsNullOrWhiteSpace([string]$manifest.sourceCommit)) {
    throw 'Installer artifact manifest sourceCommit must not be empty.'
}

if ([string]::IsNullOrWhiteSpace([string]$manifest.releaseNotes)) {
    throw 'Installer artifact manifest releaseNotes must not be empty.'
}

if ($manifest.installerStubSha256 -ne (Get-TestSha256 -PathValue $stubPath)) {
    throw "Installer stub sha256 does not match installer-artifact.json."
}

if ([long]$manifest.installerStubSize -ne (Get-Item $stubPath).Length) {
    throw "Installer stub size does not match installer-artifact.json."
}

if ($manifest.PSObject.Properties.Name -contains 'velopackSetupFile') {
    $velopackSetupPath = Join-Path $resolvedArtifactRoot ([string]$manifest.velopackSetupFile)
    Assert-PathExists -PathValue $velopackSetupPath -Message "Velopack setup file was not found: $velopackSetupPath"
    if ($manifest.velopackSetupSha256 -ne (Get-TestSha256 -PathValue $velopackSetupPath)) {
        throw "Velopack setup sha256 does not match installer-artifact.json."
    }

    if ([long]$manifest.velopackSetupSize -ne (Get-Item $velopackSetupPath).Length) {
        throw "Velopack setup size does not match installer-artifact.json."
    }
}

$launcherDirectory = [string]$manifest.launcherDirectory
if ([string]::IsNullOrWhiteSpace($launcherDirectory)) {
    throw "Artifact manifest launcherDirectory is empty."
}

$launcherPath = Join-Path $resolvedArtifactRoot $launcherDirectory
Assert-PathExists -PathValue $launcherPath -Message "Launcher directory was not found: $launcherPath"
Assert-PathExists -PathValue (Join-Path $launcherPath 'IIoT.Edge.Launcher.dll') -Message "Launcher runtime file was not found."
Assert-SelfContainedRuntimeDirectory -Directory $launcherPath -Component 'Launcher installer payload'

$hostDirectory = [string]$manifest.hostDirectory
if ([string]::IsNullOrWhiteSpace($hostDirectory)) {
    throw "Artifact manifest hostDirectory is empty."
}

$pluginsRoot = [string]$manifest.pluginsRoot
if ([string]::IsNullOrWhiteSpace($pluginsRoot)) {
    throw "Artifact manifest pluginsRoot is empty."
}

$hostPath = Join-Path $resolvedArtifactRoot $hostDirectory
$pluginsPath = Join-Path $resolvedArtifactRoot $pluginsRoot
Assert-PathExists -PathValue $hostPath -Message "Host directory was not found: $hostPath"
Assert-PathExists -PathValue (Join-Path $hostPath 'IIoT.Edge.Shell.dll') -Message "Host shell file was not found."
Assert-SelfContainedRuntimeDirectory -Directory $hostPath -Component 'Host installer payload'
Assert-PathExists -PathValue $pluginsPath -Message "Plugins root was not found: $pluginsPath"
if (Test-Path (Join-Path $hostPath 'Modules')) {
    throw "Host directory must not contain legacy Modules directory: $hostPath"
}

if ($manifest.hostDirectorySha256 -ne (Get-TestDirectorySha256 -Directory $hostPath)) {
    throw "Host directory sha256 does not match installer-artifact.json."
}

if ([long]$manifest.hostDirectorySize -ne (Get-TestDirectorySize -Directory $hostPath)) {
    throw "Host directory size does not match installer-artifact.json."
}

$modules = @($manifest.modules)
if ($modules.Count -ne 0) {
    throw "Host installer artifact must not contain business module metadata: $($modules[0].moduleId)"
}

$pluginFiles = @(Get-ChildItem -LiteralPath $pluginsPath -Recurse -File -ErrorAction Stop)
if ($pluginFiles.Count -ne 0) {
    throw "Host installer artifact plugins root must be empty; found: $($pluginFiles[0].FullName)"
}

foreach ($directory in @($launcherPath, $hostPath, $pluginsPath)) {
    Assert-ForbiddenFilesMissing -Directory $directory
    Assert-CloudIdentityTemplatesAreEmpty -Directory $directory
}

Write-Host "Edge installer artifact smoke test passed: $resolvedArtifactRoot"
