param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$ReleaseChannel = 'stable',

    [string]$HostApiVersion = '1.0.0',

    [string]$Configuration = 'Release',

    [string]$RuntimeIdentifier = 'win-x64',

    [string]$TargetFramework = 'net10.0',

    [string]$OutputRoot = 'publish\edge-installer-artifacts',

    [string]$VelopackSetupPath,

    [string]$RuntimeLayoutRoot = '',

    [string]$ManifestPath = 'scripts\edge-runtime.publish.json',

    [string]$LauncherProfileCatalogPath = 'src\Edge\IIoT.Edge.Launcher\launcher.profiles.json',

    [bool]$RuntimeSelfContained = $true,

    [switch]$CleanOutput,

    [string]$EdgeUpdatesRoot,

    [string]$VelopackReleasesDir,

    [switch]$RegisterCloudCatalog,

    [string]$CloudApiBaseUrl,

    [string]$CloudToken,

    [string]$PublicBaseUrl,

    [string]$SourceCommit = '',

    [string]$PreviousVersion = '',

    [string]$PreviousSourceCommit = '',

    [string]$ReleaseNotes = '',

    [string]$ReleaseNotesPath = '',

    [string]$Publisher = 'IIoT'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')

function Get-ArtifactSha256 {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    return (Get-FileHash -Algorithm SHA256 -Path $PathValue).Hash.ToLowerInvariant()
}

function Get-ArtifactDirectorySize {
    param([Parameter(Mandatory = $true)][string]$Directory)

    if (-not (Test-Path $Directory)) {
        return 0
    }

    $measure = Get-ChildItem -Path $Directory -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum
    if ($null -eq $measure.Sum) {
        return 0
    }

    return [long]$measure.Sum
}

function Get-ArtifactDirectorySha256 {
    param([Parameter(Mandatory = $true)][string]$Directory)

    if (-not (Test-Path $Directory)) {
        throw "Artifact directory was not found: $Directory"
    }

    $hasher = [System.Security.Cryptography.IncrementalHash]::CreateHash([System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try {
        $files = @(Get-ChildItem -Path $Directory -Recurse -File -ErrorAction SilentlyContinue |
            ForEach-Object {
                [PSCustomObject]@{
                    File = $_
                    RelativePath = Get-ArtifactRelativePath -BaseDirectory $Directory -PathValue $_.FullName
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

function Get-ArtifactRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$BaseDirectory,
        [Parameter(Mandatory = $true)][string]$PathValue
    )

    return [System.IO.Path]::GetRelativePath($BaseDirectory, $PathValue).Replace('\', '/')
}

function Invoke-ArtifactGitText {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & git @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return ''
    }

    return [string]($output -join "`n")
}

function Test-ArtifactGitCommitExists {
    param([string]$Commit)

    if ([string]::IsNullOrWhiteSpace($Commit)) {
        return $false
    }

    & git cat-file -e "$Commit^{commit}" 2>$null
    return $LASTEXITCODE -eq 0
}

function Assert-ArtifactForbiddenContentMissing {
    param([Parameter(Mandatory = $true)][string]$Directory)

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

    foreach ($file in Get-ChildItem -Path $Directory -Recurse -File -ErrorAction SilentlyContinue) {
        $relativePath = Get-ArtifactRelativePath -BaseDirectory $Directory -PathValue $file.FullName
        foreach ($pattern in $forbiddenPatterns) {
            if ($relativePath -match $pattern) {
                throw "Forbidden site config or runtime data was found in installer artifact layout: $relativePath"
            }
        }
    }
}

function Assert-ArtifactCloudIdentityTemplatesAreEmpty {
    param([Parameter(Mandatory = $true)][string]$Directory)

    $configFiles = Get-ChildItem -Path $Directory -Recurse -File -Filter 'appsettings*.json' -ErrorAction SilentlyContinue
    foreach ($configFile in $configFiles) {
        $relativePath = Get-ArtifactRelativePath -BaseDirectory $Directory -PathValue $configFile.FullName
        try {
            $config = Get-Content -Raw -Encoding UTF8 -Path $configFile.FullName | ConvertFrom-Json
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
                throw "Installer artifact layout must not contain machine identity CloudApi:$key in $relativePath."
            }
        }
    }
}

function Copy-ArtifactDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$TargetDirectory
    )

    if (-not (Test-Path $SourceDirectory)) {
        throw "Artifact source directory was not found: $SourceDirectory"
    }

    if (Test-Path $TargetDirectory) {
        Remove-Item -Path $TargetDirectory -Recurse -Force
    }

    New-Item -Path $TargetDirectory -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $SourceDirectory '*') -Destination $TargetDirectory -Recurse -Force
}

function Read-ArtifactPluginManifest {
    param(
        [Parameter(Mandatory = $true)][string]$PluginDirectory,
        [Parameter(Mandatory = $true)][string]$ModuleId
    )

    $pluginPath = Join-Path $PluginDirectory 'plugin.json'
    if (-not (Test-Path $pluginPath)) {
        throw "Plugin directory '$PluginDirectory' is missing module plugin manifest: $ModuleId"
    }

    return Get-Content -Raw -Encoding UTF8 -Path $pluginPath | ConvertFrom-Json
}

function Publish-InstallerStub {
    param(
        [Parameter(Mandatory = $true)][string]$OutputDirectory
    )

    $projectPath = Join-Path $repoRoot 'src\Edge\IIoT.Edge.Installer\IIoT.Edge.Installer.csproj'
    $publishOutput = Invoke-EdgeNativeCommand -FilePath 'dotnet' -Arguments @(
        'publish',
        $projectPath,
        '--configuration',
        $Configuration,
        '--runtime',
        $RuntimeIdentifier,
        '--self-contained',
        'true',
        '--output',
        $OutputDirectory,
        '--nologo',
        '--verbosity',
        'minimal',
        '--disable-build-servers',
        '-p:BuildInParallel=false',
        '-p:RestoreDisableParallel=true',
        '-p:UseSharedCompilation=false',
        '-p:PublishSingleFile=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        "-p:Version=$Version",
        "-p:InformationalVersion=$Version"
    )
    $publishOutput | ForEach-Object { Write-Host $_ }

    $stubPath = Join-Path $OutputDirectory 'IIoT.Edge.Setup.exe'
    if (-not (Test-Path $stubPath)) {
        throw "Installer stub publish did not produce $stubPath"
    }

    return $stubPath
}

function Copy-ArtifactToEdgeUpdatesRoot {
    param(
        [Parameter(Mandatory = $true)][string]$ArtifactRoot,
        [Parameter(Mandatory = $true)][string]$TargetEdgeUpdatesRoot
    )

    $targetRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $TargetEdgeUpdatesRoot
    $targetDirectory = Join-Path (Join-Path (Join-Path $targetRoot 'installers') $ReleaseChannel) $Version
    if (Test-Path $targetDirectory) {
        Remove-Item -Path $targetDirectory -Recurse -Force
    }

    New-Item -Path $targetDirectory -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $ArtifactRoot '*') -Destination $targetDirectory -Recurse -Force
    Write-Host "Copied installer artifact to: $targetDirectory"
}

function Copy-VelopackReleasesToEdgeUpdatesRoot {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$TargetEdgeUpdatesRoot
    )

    $targetRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $TargetEdgeUpdatesRoot
    $targetDirectory = Join-Path (Join-Path $targetRoot 'velopack') $ReleaseChannel
    if (-not (Test-Path $targetDirectory)) {
        New-Item -Path $targetDirectory -ItemType Directory -Force | Out-Null
    }

    Copy-Item -Path (Join-Path $SourceDirectory '*') -Destination $targetDirectory -Recurse -Force
    Write-Host "Copied Velopack releases to: $targetDirectory"
}

function Invoke-CloudJsonPost {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Body
    )

    if ([string]::IsNullOrWhiteSpace($CloudApiBaseUrl) -or [string]::IsNullOrWhiteSpace($CloudToken)) {
        throw "CloudApiBaseUrl and CloudToken are required when RegisterCloudCatalog is set."
    }

    $apiRoot = $CloudApiBaseUrl.TrimEnd('/')
    $uri = "$apiRoot/$($Path.TrimStart('/'))"
    $headers = @{
        Authorization = "Bearer $CloudToken"
    }
    Invoke-RestMethod `
        -Method Post `
        -Uri $uri `
        -Headers $headers `
        -ContentType 'application/json' `
        -Body ($Body | ConvertTo-Json -Depth 20) | Out-Null
}

function Register-CloudCatalog {
    param(
        [Parameter(Mandatory = $true)]$Artifact,
        [Parameter(Mandatory = $true)][string]$ArtifactPublicUrl
    )

    Invoke-CloudJsonPost -Path '/human/client-releases/host-releases' -Body @{
        channel = $Artifact.channel
        version = $Artifact.version
        hostApiVersion = $Artifact.hostApiVersion
        targetRuntime = $Artifact.targetRuntime
        targetFramework = $Artifact.targetFramework
        downloadUrl = $ArtifactPublicUrl
        sha256 = $Artifact.installerStubSha256
        packageSize = $Artifact.installerStubSize
        releaseNotes = $Artifact.releaseNotes
        status = 'Published'
        signature = $null
        publisher = $Publisher
    }

    foreach ($module in $Artifact.modules) {
        Invoke-CloudJsonPost -Path '/human/client-releases/plugin-releases' -Body @{
            moduleId = $module.moduleId
            displayName = $module.displayName
            description = if ($module.PSObject.Properties['description']) { $module.description } else { $null }
            iconKind = $null
            accentColor = $null
            channel = $Artifact.channel
            version = $module.version
            hostApiVersion = $module.hostApiVersion
            minHostVersion = $module.minHostVersion
            maxHostVersion = $module.maxHostVersion
            targetRuntime = $Artifact.targetRuntime
            targetFramework = $Artifact.targetFramework
            downloadUrl = "$ArtifactPublicUrl#moduleId=$($module.moduleId)"
            sha256 = $module.pluginSha256
            packageSize = $module.pluginSize
            releaseNotes = $Artifact.releaseNotes
            dependenciesJson = '[]'
            status = 'Published'
            signature = $null
            publisher = $Publisher
        }
    }
}

function Resolve-ArtifactReleaseNotes {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotes) -and -not [string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
        throw 'Use either -ReleaseNotes or -ReleaseNotesPath, not both.'
    }

    $resolvedNotes = ''
    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
        $resolvedPath = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $ReleaseNotesPath
        if (-not (Test-Path -LiteralPath $resolvedPath)) {
            throw "Release notes file was not found: $resolvedPath"
        }

        $resolvedNotes = Get-Content -Raw -Encoding UTF8 -LiteralPath $resolvedPath
    }
    else {
        $resolvedNotes = $ReleaseNotes
    }

    $resolvedNotes = $resolvedNotes.Trim()
    if ([string]::IsNullOrWhiteSpace($resolvedNotes)) {
        throw 'ReleaseNotes is required for Edge installer artifacts. Production releases must include real update content.'
    }

    return $resolvedNotes
}

$manifest = Load-EdgeRuntimePublishManifest -RepoRoot $repoRoot -ManifestPath $ManifestPath
$resolvedOutputRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $OutputRoot
$artifactRoot = Join-Path (Join-Path $resolvedOutputRoot $ReleaseChannel) $Version
$runtimeLayoutRootValue = if ([string]::IsNullOrWhiteSpace($RuntimeLayoutRoot)) {
    Join-Path $artifactRoot '.runtime-layout'
} else {
    $RuntimeLayoutRoot
}
$resolvedRuntimeLayoutRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $runtimeLayoutRootValue
$stubPublishRoot = Join-Path $artifactRoot '.installer-stub'
$legacyLayoutZipPath = Join-Path $artifactRoot 'layout.zip'
$installerStubTargetPath = Join-Path $artifactRoot 'IIoT.Edge.Setup.exe'
$artifactManifestPath = Join-Path $artifactRoot 'installer-artifact.json'
$velopackSetupRelativePath = $null
$velopackSetupTargetPath = $null

if ([string]::IsNullOrWhiteSpace($SourceCommit)) {
    $SourceCommit = Invoke-ArtifactGitText -Arguments @('rev-parse', 'HEAD')
}

if ([string]::IsNullOrWhiteSpace($SourceCommit)) {
    $SourceCommit = 'unknown'
}
else {
    $SourceCommit = $SourceCommit.Trim()
}

$ReleaseNotes = Resolve-ArtifactReleaseNotes

$previousVersionManifestValue = if ([string]::IsNullOrWhiteSpace($PreviousVersion)) { $null } else { $PreviousVersion }
$previousSourceCommitManifestValue = if ([string]::IsNullOrWhiteSpace($PreviousSourceCommit)) { $null } else { $PreviousSourceCommit }

if ($CleanOutput -and (Test-Path $artifactRoot)) {
    Remove-Item -Path $artifactRoot -Recurse -Force
}

New-Item -Path $artifactRoot -ItemType Directory -Force | Out-Null
Remove-Item -Path $legacyLayoutZipPath -Force -ErrorAction SilentlyContinue

& (Join-Path $PSScriptRoot 'PublishEdgeRuntime.ps1') `
    -Configuration $Configuration `
    -OutputRoot $resolvedRuntimeLayoutRoot `
    -ManifestPath $ManifestPath `
    -LauncherProfileCatalogPath $LauncherProfileCatalogPath `
    -Version $Version `
    -RuntimeIdentifier $RuntimeIdentifier `
    -SelfContained:$RuntimeSelfContained `
    -CleanOutput

Copy-ArtifactDirectory `
    -SourceDirectory (Join-Path $resolvedRuntimeLayoutRoot $manifest.launcherDirectory) `
    -TargetDirectory (Join-Path $artifactRoot $manifest.launcherDirectory)

Copy-ArtifactDirectory `
    -SourceDirectory (Join-Path $resolvedRuntimeLayoutRoot $manifest.hostDirectory) `
    -TargetDirectory (Join-Path $artifactRoot $manifest.hostDirectory)

Copy-ArtifactDirectory `
    -SourceDirectory (Join-Path $resolvedRuntimeLayoutRoot $manifest.pluginsRoot) `
    -TargetDirectory (Join-Path $artifactRoot $manifest.pluginsRoot)

$artifactDirectoriesToValidate = @(
    (Join-Path $artifactRoot $manifest.launcherDirectory),
    (Join-Path $artifactRoot $manifest.hostDirectory),
    (Join-Path $artifactRoot $manifest.pluginsRoot)
)

$modules = @()
$moduleIds = @($manifest.profiles | ForEach-Object { $_.moduleIds } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
foreach ($moduleId in $moduleIds) {
    $pluginDirectory = Join-Path (Join-Path $resolvedRuntimeLayoutRoot $manifest.pluginsRoot) $moduleId
    $artifactPluginDirectory = Join-Path (Join-Path $artifactRoot $manifest.pluginsRoot) $moduleId
    $plugin = Read-ArtifactPluginManifest -PluginDirectory $pluginDirectory -ModuleId $moduleId
    $modules += [PSCustomObject]@{
        moduleId = [string]$plugin.moduleId
        displayName = [string]$plugin.displayName
        description = if ($plugin.PSObject.Properties['description']) { [string]$plugin.description } else { $null }
        version = [string]$plugin.version
        hostApiVersion = [string]$plugin.hostApiVersion
        minHostVersion = [string]$plugin.minHostVersion
        maxHostVersion = [string]$plugin.maxHostVersion
        pluginDirectory = [string]$moduleId
        pluginSha256 = Get-ArtifactDirectorySha256 -Directory $artifactPluginDirectory
        pluginSize = Get-ArtifactDirectorySize -Directory $artifactPluginDirectory
    }
}

foreach ($directory in $artifactDirectoriesToValidate) {
    Assert-ArtifactForbiddenContentMissing -Directory $directory
    Assert-ArtifactCloudIdentityTemplatesAreEmpty -Directory $directory
}

$publishedStubPath = Publish-InstallerStub -OutputDirectory $stubPublishRoot
Copy-Item -Path $publishedStubPath -Destination $installerStubTargetPath -Force

if (-not [string]::IsNullOrWhiteSpace($VelopackSetupPath)) {
    $resolvedVelopackSetupPath = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $VelopackSetupPath
    if (-not (Test-Path $resolvedVelopackSetupPath)) {
        throw "Velopack setup file was not found: $resolvedVelopackSetupPath"
    }

    $velopackSetupRelativePath = "velopack/$([System.IO.Path]::GetFileName($resolvedVelopackSetupPath))"
    $velopackSetupTargetPath = Join-Path $artifactRoot $velopackSetupRelativePath
    New-Item -Path (Split-Path -Parent $velopackSetupTargetPath) -ItemType Directory -Force | Out-Null
    Copy-Item -Path $resolvedVelopackSetupPath -Destination $velopackSetupTargetPath -Force
}

$artifactProperties = [ordered]@{
    schemaVersion = 2
    channel = $ReleaseChannel
    version = $Version
    hostApiVersion = $HostApiVersion
    targetRuntime = $RuntimeIdentifier
    targetFramework = $TargetFramework
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    sourceCommit = $SourceCommit
    previousVersion = $previousVersionManifestValue
    previousSourceCommit = $previousSourceCommitManifestValue
    releaseNotes = $ReleaseNotes
    installerStubFile = 'IIoT.Edge.Setup.exe'
    installerStubSha256 = Get-ArtifactSha256 -PathValue $installerStubTargetPath
    installerStubSize = (Get-Item $installerStubTargetPath).Length
    launcherDirectory = [string]$manifest.launcherDirectory
    hostDirectory = [string]$manifest.hostDirectory
    hostDirectorySha256 = Get-ArtifactDirectorySha256 -Directory (Join-Path $artifactRoot $manifest.hostDirectory)
    hostDirectorySize = Get-ArtifactDirectorySize -Directory (Join-Path $artifactRoot $manifest.hostDirectory)
    pluginsRoot = [string]$manifest.pluginsRoot
    modules = $modules
}

$artifact = [PSCustomObject]$artifactProperties
if ($null -ne $velopackSetupTargetPath) {
    $artifact | Add-Member -NotePropertyName 'velopackSetupFile' -NotePropertyValue $velopackSetupRelativePath
    $artifact | Add-Member -NotePropertyName 'velopackSetupSha256' -NotePropertyValue (Get-ArtifactSha256 -PathValue $velopackSetupTargetPath)
    $artifact | Add-Member -NotePropertyName 'velopackSetupSize' -NotePropertyValue (Get-Item $velopackSetupTargetPath).Length
}

$artifact | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 -Path $artifactManifestPath

Remove-Item -Path $stubPublishRoot -Recurse -Force -ErrorAction SilentlyContinue
if ([string]::IsNullOrWhiteSpace($RuntimeLayoutRoot)) {
    Remove-Item -Path $resolvedRuntimeLayoutRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not [string]::IsNullOrWhiteSpace($EdgeUpdatesRoot)) {
    Copy-ArtifactToEdgeUpdatesRoot -ArtifactRoot $artifactRoot -TargetEdgeUpdatesRoot $EdgeUpdatesRoot
}

if (-not [string]::IsNullOrWhiteSpace($VelopackReleasesDir)) {
    $resolvedVelopackReleasesDir = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $VelopackReleasesDir
    if (-not (Test-Path $resolvedVelopackReleasesDir)) {
        throw "Velopack releases directory was not found: $resolvedVelopackReleasesDir"
    }

    if (-not [string]::IsNullOrWhiteSpace($EdgeUpdatesRoot)) {
        Copy-VelopackReleasesToEdgeUpdatesRoot -SourceDirectory $resolvedVelopackReleasesDir -TargetEdgeUpdatesRoot $EdgeUpdatesRoot
    }
}

if ($RegisterCloudCatalog) {
    if ([string]::IsNullOrWhiteSpace($PublicBaseUrl)) {
        throw "PublicBaseUrl is required when RegisterCloudCatalog is set."
    }

    $artifactPublicUrl = "$($PublicBaseUrl.TrimEnd('/'))/edge-updates/installers/$ReleaseChannel/$Version/installer-artifact.json"
    Register-CloudCatalog -Artifact $artifact -ArtifactPublicUrl $artifactPublicUrl
    Write-Host "Registered Cloud client release catalog for $ReleaseChannel/$Version."
}

Write-Host "Edge installer artifact completed."
Write-Host "Channel: $ReleaseChannel"
Write-Host "Version: $Version"
Write-Host "Output: $artifactRoot"
Write-Host "Manifest: $artifactManifestPath"
