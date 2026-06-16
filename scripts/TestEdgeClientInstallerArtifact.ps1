param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactRoot,

    [string]$ExpectedChannel = 'stable',

    [string]$ExpectedVersion,

    [string]$ExpectedModuleId = 'Homogenization'
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

function Get-TestSha256 {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    return (Get-FileHash -Algorithm SHA256 -Path $PathValue).Hash.ToLowerInvariant()
}

function Get-TestDirectorySize {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $measure = Get-ChildItem -Path $Directory -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum
    if ($null -eq $measure.Sum) {
        return 0
    }

    return [long]$measure.Sum
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
        $files = Get-ChildItem -Path $Directory -Recurse -File -ErrorAction SilentlyContinue |
            Sort-Object @{ Expression = { Get-TestRelativePath -BaseDirectory $Directory -PathValue $_.FullName } }

        foreach ($file in $files) {
            $relativePath = Get-TestRelativePath -BaseDirectory $Directory -PathValue $file.FullName
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

    foreach ($file in Get-ChildItem -Path $Directory -Recurse -File -ErrorAction SilentlyContinue) {
        $relativePath = Get-TestRelativePath -BaseDirectory $Directory -PathValue $file.FullName
        foreach ($pattern in $forbiddenPatterns) {
            if ($relativePath -match $pattern) {
                throw "Forbidden site config or runtime data was found in installer artifact directory: $relativePath"
            }
        }
    }
}

function Assert-CloudIdentityTemplatesAreEmpty {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $configFiles = Get-ChildItem -Path $Directory -Recurse -File -Filter 'appsettings*.json' -ErrorAction SilentlyContinue
    foreach ($configFile in $configFiles) {
        $relativePath = Get-TestRelativePath -BaseDirectory $Directory -PathValue $configFile.FullName
        try {
            $config = Get-Content -Raw -Encoding UTF8 -Path $configFile.FullName | ConvertFrom-Json
        }
        catch {
            throw "Artifact config file could not be parsed: $relativePath"
        }

        if ($null -eq $config.CloudApi) {
            continue
        }

        foreach ($key in @('ClientCode', 'BootstrapSecret')) {
            $property = $config.CloudApi.PSObject.Properties[$key]
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

$resolvedArtifactRoot = Resolve-TestArtifactPath -PathValue $ArtifactRoot
$manifestPath = Join-Path $resolvedArtifactRoot 'installer-artifact.json'
$legacyLayoutZipPath = Join-Path $resolvedArtifactRoot 'layout.zip'
$stubPath = Join-Path $resolvedArtifactRoot 'IIoT.Edge.Setup.exe'

Assert-PathExists -PathValue $manifestPath -Message "Artifact manifest was not found: $manifestPath"
Assert-PathExists -PathValue $stubPath -Message "Installer stub was not found: $stubPath"
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

$module = @($manifest.modules | Where-Object { $_.moduleId -eq $ExpectedModuleId }) | Select-Object -First 1
if ($null -eq $module) {
    throw "Artifact manifest does not contain module '$ExpectedModuleId'."
}

if ([string]::IsNullOrWhiteSpace($module.pluginDirectory)) {
    throw "Module '$ExpectedModuleId' pluginDirectory is empty."
}

$pluginPath = Join-Path $pluginsPath ([string]$module.pluginDirectory)
Assert-PathExists -PathValue $pluginPath -Message "Plugin directory was not found: $pluginPath"
Assert-PathExists -PathValue (Join-Path $pluginPath 'plugin.json') -Message "Module plugin manifest was not found."

if ($module.pluginSha256 -ne (Get-TestDirectorySha256 -Directory $pluginPath)) {
    throw "Plugin directory sha256 does not match installer-artifact.json."
}

if ([long]$module.pluginSize -ne (Get-TestDirectorySize -Directory $pluginPath)) {
    throw "Plugin directory size does not match installer-artifact.json."
}

foreach ($directory in @($launcherPath, $hostPath, $pluginsPath)) {
    Assert-ForbiddenFilesMissing -Directory $directory
    Assert-CloudIdentityTemplatesAreEmpty -Directory $directory
}

Write-Host "Edge installer artifact smoke test passed: $resolvedArtifactRoot"
