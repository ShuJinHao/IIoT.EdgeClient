param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '',

    [ValidateSet('stable')]
    [string]$Channel = 'stable',

    [string]$Configuration = 'Release',

    [string]$RuntimeIdentifier = 'win-x64',

    [string]$PackId = 'IIoT.EdgeClient',

    [ValidateSet('http')]
    [string]$Transport = 'http',

    [string]$CloudApiBaseUrl = '',

    [string]$CloudToken = '',

    [string]$ReleaseNotes = '',

    [string]$ReleaseNotesPath = '',

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$ExpectedSha,

    [ValidateRange(1, 1000)]
    [int]$UploadRateLimitMbps = 1000,

    [string]$ResumeReleaseRoot = $env:IIOT_EDGE_RESUME_RELEASE_ROOT,

    [ValidateRange(1, 300)]
    [int]$ConnectTimeoutSeconds = 10,

    [ValidateRange(1, 86400)]
    [int]$UploadTimeoutSeconds = 1800,

    [ValidateRange(1, 3600)]
    [int]$LowSpeedTimeSeconds = 60,

    [ValidateRange(1, 104857600)]
    [int]$LowSpeedLimitBytesPerSecond = 1024,

    [bool]$SkipVeloAppCheck = $true,

    [switch]$SkipVelopackValidation,

    [switch]$SkipInstallerValidation,

    [switch]$PrepareOnly,

    [switch]$PreparedSourceSnapshot,

    [string]$PreparedResultPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeReleaseCredential.Common.ps1')
. (Join-Path $PSScriptRoot 'EdgeDeployment.Common.ps1')

if ($Channel -ne 'stable') {
    throw 'Production Edge releases must use stable channel.'
}

if ($Channel -match '[\\/]') {
    throw 'Channel must not contain path separators.'
}

function Invoke-EdgeScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptName,

        [AllowEmptyCollection()]
        [object[]]$Arguments = @()
    )

    $scriptPath = Join-Path $PSScriptRoot $ScriptName
    if (-not (Test-Path $scriptPath)) {
        throw "Script was not found: $scriptPath"
    }

    $parameterSplat = @{}
    for ($i = 0; $i -lt $Arguments.Count; $i++) {
        $name = [string]$Arguments[$i]
        if (-not $name.StartsWith('-', [System.StringComparison]::Ordinal)) {
            throw "Script argument name must start with '-': $name"
        }

        $key = $name.TrimStart('-')
        $hasValue = $i + 1 -lt $Arguments.Count -and
            -not ([string]$Arguments[$i + 1]).StartsWith('-', [System.StringComparison]::Ordinal)
        if ($hasValue) {
            $i++
            $parameterSplat[$key] = $Arguments[$i]
        }
        else {
            $parameterSplat[$key] = $true
        }
    }

    & $scriptPath @parameterSplat
    if ($LASTEXITCODE -ne 0) {
        throw "$ScriptName failed with exit code $LASTEXITCODE."
    }
}

function Resolve-Transport {
    if ($Transport -ne 'http') {
        throw 'Stable Edge host releases must use -Transport http so Cloud can enforce release notes, DB registration, audit and retention.'
    }
    return 'http'
}

function Assert-HttpPublishConfiguration {
    if ([string]::IsNullOrWhiteSpace($CloudApiBaseUrl)) {
        throw 'CloudApiBaseUrl is required when -Transport http is used.'
    }

    if ([string]::IsNullOrWhiteSpace($script:CloudToken)) {
        $script:CloudToken = Resolve-EdgeReleaseCloudToken -CloudApiBaseUrl $CloudApiBaseUrl -CloudToken $script:CloudToken
    }

    if ([string]::IsNullOrWhiteSpace($script:CloudToken)) {
        throw 'CloudToken is required when -Transport http is used. Use -CloudToken only for controlled recovery, or store the Edge Release API key in macOS Keychain with scripts/SaveEdgeReleaseApiKey.ps1.'
    }
}

function Invoke-CloudJsonGet {
    param([Parameter(Mandatory = $true)][string]$Path)

    Assert-HttpPublishConfiguration
    $apiRoot = $CloudApiBaseUrl.TrimEnd('/')
    $uri = "$apiRoot/$($Path.TrimStart('/'))"
    return Invoke-EdgeCurlJsonGet `
        -Uri $uri `
        -Token $script:CloudToken `
        -ConnectTimeoutSeconds $ConnectTimeoutSeconds `
        -RequestTimeoutSeconds 60 `
        -LowSpeedTimeSeconds 30 `
        -LowSpeedLimitBytesPerSecond 128
}

function Get-CloudStableCatalog {
    return Invoke-CloudJsonGet -Path "/human/client-releases/catalog?channel=$Channel&targetRuntime=$RuntimeIdentifier&onlyPublished=true"
}

function Get-CloudHostVersions {
    param([Parameter(Mandatory = $true)]$Catalog)

    if ($null -eq $Catalog.host -or $null -eq $Catalog.host.versions) {
        throw 'Cloud Edge release catalog did not contain host.versions.'
    }

    return @($Catalog.host.versions | Where-Object {
        $_.version -match '^\d+\.\d+\.\d+$' -and
        ($_.status -eq 'Published' -or $_.status -eq 'Deprecated')
    })
}

function Get-CloudReleaseManifestSourceCommit {
    param([Parameter(Mandatory = $true)]$Release)

    if ([string]::IsNullOrWhiteSpace([string]$Release.downloadUrl)) {
        return ''
    }

    $downloadBase = Resolve-DownloadBaseUrl
    $manifestUrl = [string]$Release.downloadUrl
    if (-not $manifestUrl.StartsWith('http', [System.StringComparison]::OrdinalIgnoreCase)) {
        $manifestUrl = "$downloadBase/$($manifestUrl.TrimStart('/'))"
    }

    $manifest = Invoke-EdgeCurlJsonGet -Uri $manifestUrl `
        -ConnectTimeoutSeconds $ConnectTimeoutSeconds -RequestTimeoutSeconds 60 `
        -LowSpeedTimeSeconds 30 -LowSpeedLimitBytesPerSecond 128
    return [string]$manifest.sourceCommit
}

function Get-LatestCloudStableRelease {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Versions)

    if ($Versions.Count -eq 0) {
        return $null
    }

    $latest = $Versions |
        Sort-Object @{ Expression = { [version]$_.version } } |
        Select-Object -Last 1
    return [PSCustomObject]@{
        Version = [string]$latest.version
        SourceCommit = Get-CloudReleaseManifestSourceCommit -Release $latest
        CatalogEntry = $latest
    }
}

function Get-NextPatchVersion {
    param([string]$CurrentVersion)

    if ([string]::IsNullOrWhiteSpace($CurrentVersion)) {
        return '0.0.1'
    }

    $parts = $CurrentVersion.Split('.')
    if ($parts.Count -ne 3) {
        throw "Verified Cloud catalog returned an invalid stable version: $CurrentVersion"
    }

    return '{0}.{1}.{2}' -f ([int]$parts[0]), ([int]$parts[1]), (([int]$parts[2]) + 1)
}

function Resolve-ExplicitReleaseNotes {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotes) -and -not [string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
        throw 'Use either -ReleaseNotes or -ReleaseNotesPath, not both.'
    }

    $resolvedNotes = ''
    if (-not [string]::IsNullOrWhiteSpace($ReleaseNotesPath)) {
        $path = if ([System.IO.Path]::IsPathRooted($ReleaseNotesPath)) {
            $ReleaseNotesPath
        }
        else {
            Join-Path $repoRoot $ReleaseNotesPath
        }

        if (-not (Test-Path -LiteralPath $path)) {
            throw "Release notes file was not found: $path"
        }

        $resolvedNotes = Get-Content -Raw -Encoding UTF8 -LiteralPath $path
    }
    else {
        $resolvedNotes = $ReleaseNotes
    }

    $resolvedNotes = $resolvedNotes.Trim()
    if ([string]::IsNullOrWhiteSpace($resolvedNotes)) {
        throw 'Production Edge release notes are required. Pass -ReleaseNotes or -ReleaseNotesPath with the real update content.'
    }

    return $resolvedNotes
}

function New-EdgeHttpReleaseBundle {
    param(
        [Parameter(Mandatory = $true)][string]$InstallerArtifactRoot,
        [Parameter(Mandatory = $true)][string]$VelopackRoot,
        [Parameter(Mandatory = $true)][string]$OutputZip
    )

    $bundleRoot = Join-Path (Split-Path -Parent $OutputZip) 'edge-http-bundle'
    if (Test-Path $bundleRoot) {
        Remove-Item -Path $bundleRoot -Recurse -Force
    }

    New-Item -Path (Join-Path $bundleRoot 'installer') -ItemType Directory -Force | Out-Null
    New-Item -Path (Join-Path $bundleRoot 'velopack') -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $InstallerArtifactRoot '*') -Destination (Join-Path $bundleRoot 'installer') -Recurse -Force
    Copy-Item -Path (Join-Path $VelopackRoot '*') -Destination (Join-Path $bundleRoot 'velopack') -Recurse -Force

    if (Test-Path $OutputZip) {
        Remove-Item -Path $OutputZip -Force
    }

    Compress-Archive -Path (Join-Path $bundleRoot '*') -DestinationPath $OutputZip -CompressionLevel Fastest -Force
    Remove-Item -Path $bundleRoot -Recurse -Force
    return $OutputZip
}

function Invoke-EdgeHttpReleaseUpload {
    param([Parameter(Mandatory = $true)][string]$BundleZip)

    Assert-HttpPublishConfiguration
    $apiRoot = $CloudApiBaseUrl.TrimEnd('/')
    $uri = "$apiRoot/human/client-releases/edge-release-bundles"
    $responsePath = Join-Path (Split-Path -Parent $BundleZip) 'edge-http-upload-response.json'
    $rateBytesPerSecond = [int64]([math]::Floor($UploadRateLimitMbps * 1024 * 1024 / 8))

    Invoke-EdgeCurlRequest `
        -Method POST `
        -Uri $uri `
        -Token $script:CloudToken `
        -UploadFile $BundleZip `
        -ResponsePath $responsePath `
        -RateLimitBytesPerSecond $rateBytesPerSecond `
        -ConnectTimeoutSeconds $ConnectTimeoutSeconds `
        -RequestTimeoutSeconds $UploadTimeoutSeconds `
        -LowSpeedTimeSeconds $LowSpeedTimeSeconds `
        -LowSpeedLimitBytesPerSecond $LowSpeedLimitBytesPerSecond | Out-Null

    return Get-Content -Raw -Encoding UTF8 -Path $responsePath | ConvertFrom-Json
}

function Resolve-DownloadBaseUrl {
    Assert-HttpPublishConfiguration
    $uri = [Uri]$CloudApiBaseUrl
    $builder = [System.UriBuilder]::new($uri)
    $path = $builder.Path.TrimEnd('/')
    if ($path.EndsWith('/api/v1', [System.StringComparison]::OrdinalIgnoreCase)) {
        $builder.Path = $path.Substring(0, $path.Length - '/api/v1'.Length)
    }
    elseif ($path.EndsWith('/api', [System.StringComparison]::OrdinalIgnoreCase)) {
        $builder.Path = $path.Substring(0, $path.Length - '/api'.Length)
    }
    else {
        $builder.Path = $path
    }

    return $builder.Uri.AbsoluteUri.TrimEnd('/')
}

function Test-EdgeHttpReleaseUrls {
    param([Parameter(Mandatory = $true)]$PublishResult)

    $downloadBase = Resolve-DownloadBaseUrl
    foreach ($relativeUrl in @($PublishResult.verificationUrls)) {
        $uri = "$downloadBase/$(([string]$relativeUrl).TrimStart('/'))"
        Test-EdgeCurlUrl -Uri $uri -ConnectTimeoutSeconds $ConnectTimeoutSeconds -RequestTimeoutSeconds 60
    }
}

function Write-EdgePublishSummary {
    param([Parameter(Mandatory = $true)]$PublishResult)

    Write-Host ''
    Write-Host 'Edge HTTP release deployment summary'
    Write-Host "  channel: $($PublishResult.channel)"
    Write-Host "  version: $($PublishResult.version)"
    Write-Host "  sourceCommit: $($PublishResult.sourceCommit)"
    Write-Host "  previousSourceCommit: $($PublishResult.previousSourceCommit)"
    Write-Host "  bundleSize: $($PublishResult.bundleSize)"
    Write-Host "  uploadSeconds: $([math]::Round([double]$PublishResult.uploadSeconds, 2))"
    Write-Host "  uploadRateLimitMbps: $($PublishResult.uploadRateLimitMbps)"
    Write-Host "  installerPath: $($PublishResult.installerPath)"
    Write-Host "  velopackPath: $($PublishResult.velopackPath)"
    Write-Host "  components: $(@($PublishResult.components) -join ', ')"
    Write-Host "  archivedVersions: $(@($PublishResult.archivedVersions) -join ', ')"
    Write-Host "  deletedInstallerVersions: $(@($PublishResult.deletedInstallerVersions) -join ', ')"
    Write-Host "  deletedVelopackFiles: $(@($PublishResult.deletedVelopackFiles) -join ', ')"
    Write-Host "  cleanupSucceeded: $($PublishResult.cleanupSucceeded)"
    if (-not [string]::IsNullOrWhiteSpace([string]$PublishResult.cleanupWarning)) {
        Write-Host "  cleanupWarning: $($PublishResult.cleanupWarning)"
    }
    Write-Host '  httpVerification: ok'
    Write-Host "  verificationUrls: $(@($PublishResult.verificationUrls) -join ', ')"
    Write-Host '  releaseNotes:'
    foreach ($line in @($PublishResult.changedCommits)) {
        Write-Host "    $line"
    }
}

$dispatchInvocationId = Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgeHost
$gitFacts = Assert-EdgeReleaseGitState `
    -RepoRoot $repoRoot `
    -ExpectedSha $ExpectedSha `
    -AllowDetachedExactSha:$PreparedSourceSnapshot
$deploymentLock = Enter-EdgeDeploymentLock -RepoRoot $repoRoot -InvocationId $dispatchInvocationId -Target EdgeHost
$locationPushed = $false
$attemptReleaseRoot = ''
$attemptStage = 'initializing'

try {
    Push-Location $repoRoot
    $locationPushed = $true

    $selectedTransport = Resolve-Transport
    if ($selectedTransport -ne 'http') {
        throw 'Formal stable Edge host releases only support Cloud Human HTTP publication.'
    }

    Assert-HttpPublishConfiguration
    $catalog = Get-CloudStableCatalog
    $catalogVersions = @(Get-CloudHostVersions -Catalog $catalog)
    $previousRelease = Get-LatestCloudStableRelease -Versions $catalogVersions
    $previousVersion = if ($null -ne $previousRelease) { [string]$previousRelease.Version } else { '' }
    $previousSourceCommit = if ($null -ne $previousRelease) { [string]$previousRelease.SourceCommit } else { '' }
    $sourceCommit = [string]$gitFacts.Head
    $isResume = -not [string]::IsNullOrWhiteSpace($ResumeReleaseRoot)

    if ($isResume) {
        $releaseRoot = [System.IO.Path]::GetFullPath($(if ([System.IO.Path]::IsPathRooted($ResumeReleaseRoot)) { $ResumeReleaseRoot } else { Join-Path $repoRoot $ResumeReleaseRoot }))
        if (-not (Test-Path -LiteralPath $releaseRoot -PathType Container)) {
            throw "Resume release root was not found: $releaseRoot"
        }
        $statePath = Join-Path $releaseRoot 'edge-deployment-attempt.json'
        if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
            throw "Resume release root is missing edge-deployment-attempt.json: $releaseRoot"
        }
        $savedState = Get-Content -Raw -Encoding UTF8 -LiteralPath $statePath | ConvertFrom-Json
        Assert-EdgeResumeAttemptIdentity `
            -State $savedState `
            -ExpectedTarget EdgeHost `
            -ExpectedInvocationId $dispatchInvocationId `
            -ExpectedSha $ExpectedSha `
            -AllowPreparedHandoff:$PreparedSourceSnapshot

        $manifestFile = Get-ChildItem -LiteralPath $releaseRoot -Recurse -File -Filter 'installer-artifact.json' | Select-Object -First 1
        if ($null -eq $manifestFile) {
            throw "Resume release root does not contain installer-artifact.json: $releaseRoot"
        }

        $artifactManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestFile.FullName | ConvertFrom-Json
        $artifactVersion = [string]$artifactManifest.version
        if ([string]::IsNullOrWhiteSpace($artifactVersion)) {
            throw "Resume artifact manifest is missing version: $($manifestFile.FullName)"
        }
        if (-not [string]::IsNullOrWhiteSpace($Version) -and $Version -ne $artifactVersion) {
            throw "Requested version '$Version' does not match resume artifact version '$artifactVersion'."
        }
        if ([string]$artifactManifest.channel -ne $Channel) {
            throw "Resume artifact channel '$($artifactManifest.channel)' does not match '$Channel'."
        }
        if ([string]$artifactManifest.sourceCommit -ne $sourceCommit) {
            throw "Resume artifact sourceCommit '$($artifactManifest.sourceCommit)' does not match pushed HEAD '$sourceCommit'."
        }

        $Version = $artifactVersion
        $releaseNotes = Resolve-ExplicitReleaseNotes
        if ($releaseNotes.Trim() -ne ([string]$artifactManifest.releaseNotes).Trim()) {
            throw 'Resume release notes do not match the preserved installer artifact.'
        }
    }
    else {
        if ([string]::IsNullOrWhiteSpace($Version)) {
            $Version = Get-NextPatchVersion -CurrentVersion $previousVersion
            Write-Host "Auto-generated Edge version from verified Cloud catalog: $Version"
        }
        $releaseNotes = Resolve-ExplicitReleaseNotes
        $releaseRoot = Join-Path $repoRoot "publish/local-edge-release/$Channel/$Version"
    }

    if ($Version -match '[\\/]') {
        throw 'Version must not contain path separators.'
    }

    $existingVersion = @($catalogVersions | Where-Object { [string]$_.version -eq $Version } | Select-Object -First 1)
    if ($existingVersion.Count -gt 0 -and -not $isResume) {
        throw "Edge host version '$Version' already exists in the verified Cloud catalog. No build was started."
    }

    $attemptReleaseRoot = $releaseRoot
    if ($existingVersion.Count -gt 0) {
        $existingSourceCommit = Get-CloudReleaseManifestSourceCommit -Release $existingVersion[0]
        if (-not [string]::Equals(
                $existingSourceCommit,
                $ExpectedSha,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Existing Edge host release does not match the frozen candidate: version='$Version' expectedSha='$ExpectedSha' actualSha='$existingSourceCommit'."
        }
        $existingManifestUrl = [string]$existingVersion[0].downloadUrl
        if (-not [string]::IsNullOrWhiteSpace($existingManifestUrl)) {
            if (-not $existingManifestUrl.StartsWith('http', [System.StringComparison]::OrdinalIgnoreCase)) {
                $existingManifestUrl = "$(Resolve-DownloadBaseUrl)/$($existingManifestUrl.TrimStart('/'))"
            }
            Test-EdgeCurlUrl -Uri $existingManifestUrl -ConnectTimeoutSeconds $ConnectTimeoutSeconds -RequestTimeoutSeconds 60
        }
        Write-EdgeDeploymentAttemptState -ReleaseRoot $releaseRoot -Target EdgeHost -InvocationId $dispatchInvocationId `
            -Stage 'reconciled-existing-release' -Status succeeded -Facts @{ version = $Version; sourceCommit = $sourceCommit; alreadyPublished = $true } | Out-Null
        Write-Host "Edge host resume reconciliation succeeded: version=$Version alreadyPublished=true"
        return
    }

    $runtimeRoot = Join-Path $releaseRoot 'edge-runtime'
    $velopackRoot = Join-Path $releaseRoot 'edge-velopack'
    $installerOutputRoot = Join-Path $releaseRoot 'edge-installer-artifacts'
    $installerArtifactRoot = Join-Path $installerOutputRoot "$Channel/$Version"
    $velopackSetupPath = Join-Path $velopackRoot "$PackId-$Channel-Setup.exe"
    $bundleZip = Join-Path $releaseRoot "edge-release-bundle-$Channel-$Version.zip"

    if (-not $isResume) {
        $gitFacts = Assert-EdgeReleaseGitState `
            -RepoRoot $repoRoot `
            -ExpectedSha $ExpectedSha `
            -AllowDetachedExactSha:$PreparedSourceSnapshot
        $sourceCommit = [string]$gitFacts.Head
        Write-Host "Publishing Edge local release: version=$Version channel=$Channel runtime=$RuntimeIdentifier"
        if (-not [string]::IsNullOrWhiteSpace($previousVersion)) {
            Write-Host "Previous Edge stable release: $previousVersion"
        }
        Write-Host "Source commit: $sourceCommit upstream=$($gitFacts.Upstream)"
        if (Test-Path -LiteralPath $releaseRoot) {
            Remove-Item -LiteralPath $releaseRoot -Recurse -Force
        }
        New-Item -Path $releaseRoot -ItemType Directory -Force | Out-Null
        $attemptStage = 'building-artifacts'
        Write-EdgeDeploymentAttemptState -ReleaseRoot $releaseRoot -Target EdgeHost -InvocationId $dispatchInvocationId `
            -Stage $attemptStage -Status running -Facts @{ version = $Version; sourceCommit = $sourceCommit; resumeReleaseRoot = $releaseRoot } | Out-Null

        $releaseNotesFile = Join-Path $releaseRoot 'release-notes.md'
        Set-Content -Path $releaseNotesFile -Encoding UTF8 -Value $releaseNotes

        Invoke-EdgeScript 'PublishEdgeRuntime.ps1' -Arguments @(
            '-Configuration', $Configuration,
            '-RuntimeIdentifier', $RuntimeIdentifier,
            '-Version', $Version,
            '-OutputRoot', $runtimeRoot,
            '-SelfContained',
            '-CleanOutput'
        )
        Invoke-EdgeScript 'PackEdgeClientVelopack.ps1' -Arguments @(
            '-Version', $Version,
            '-Channel', $Channel,
            '-Configuration', $Configuration,
            '-RuntimeIdentifier', $RuntimeIdentifier,
            '-OutputRoot', $velopackRoot,
            '-ReleaseNotes', $releaseNotesFile,
            '-SelfContained',
            '-CleanOutput',
            '-SkipVeloAppCheck', $SkipVeloAppCheck
        )
        Invoke-EdgeScript 'PublishEdgeClientInstallerArtifact.ps1' -Arguments @(
            '-Version', $Version,
            '-ReleaseChannel', $Channel,
            '-Configuration', $Configuration,
            '-RuntimeIdentifier', $RuntimeIdentifier,
            '-OutputRoot', $installerOutputRoot,
            '-RuntimeLayoutRoot', $runtimeRoot,
            '-VelopackSetupPath', $velopackSetupPath,
            '-SourceCommit', $sourceCommit,
            '-PreviousVersion', $previousVersion,
            '-PreviousSourceCommit', $previousSourceCommit,
            '-ReleaseNotes', $releaseNotes,
            '-CleanOutput'
        )
    }

    $attemptStage = 'validating-preserved-artifacts'
    Write-EdgeDeploymentAttemptState -ReleaseRoot $releaseRoot -Target EdgeHost -InvocationId $dispatchInvocationId `
        -Stage $attemptStage -Status running -Facts @{ version = $Version; sourceCommit = $sourceCommit; resumed = $isResume } | Out-Null
    if (-not $SkipInstallerValidation) {
        Invoke-EdgeScript 'TestEdgeClientInstallerArtifact.ps1' -Arguments @('-ArtifactRoot', $installerArtifactRoot, '-ExpectedChannel', $Channel, '-ExpectedVersion', $Version)
    }
    if (-not $SkipVelopackValidation) {
        Invoke-EdgeScript 'TestEdgeVelopackPackage.ps1' -Arguments @('-OutputRoot', $velopackRoot, '-Channel', $Channel, '-Version', $Version)
    }
    if (-not (Test-Path -LiteralPath (Join-Path $installerArtifactRoot 'installer-artifact.json') -PathType Leaf)) {
        throw "Installer artifact manifest was not generated: $installerArtifactRoot"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $velopackRoot "releases.$Channel.json") -PathType Leaf)) {
        throw "Velopack releases manifest was not generated: $velopackRoot"
    }
    if (-not $isResume -or -not (Test-Path -LiteralPath $bundleZip -PathType Leaf)) {
        New-EdgeHttpReleaseBundle -InstallerArtifactRoot $installerArtifactRoot -VelopackRoot $velopackRoot -OutputZip $bundleZip | Out-Null
    }

    if ($PrepareOnly) {
        if ([string]::IsNullOrWhiteSpace($PreparedResultPath)) {
            throw 'PrepareOnly requires PreparedResultPath.'
        }
        $preparedResult = [System.IO.Path]::GetFullPath($PreparedResultPath)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $preparedResult) | Out-Null
        $manifestPath = Join-Path $installerArtifactRoot 'installer-artifact.json'
        $manifestHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash.ToLowerInvariant()
        $bundle = Get-Item -LiteralPath $bundleZip
        [ordered]@{
            schemaVersion = 1
            kind = 'iiot-edge-host-prepared-result'
            component = 'Host'
            version = $Version
            sourceCommit = $sourceCommit
            releaseRoot = $releaseRoot
            bundlePath = $bundle.FullName
            bundleSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $bundle.FullName).Hash.ToLowerInvariant()
            bundleSize = [long]$bundle.Length
            installerManifestPath = $manifestPath
            installerManifestSha256 = $manifestHash
            targetRuntime = $RuntimeIdentifier
            selfContained = $true
            completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        } | ConvertTo-Json -Depth 8 |
            Set-Content -LiteralPath $preparedResult -Encoding utf8NoBOM
        Write-EdgeDeploymentAttemptState -ReleaseRoot $releaseRoot -Target EdgeHost -InvocationId $dispatchInvocationId `
            -Stage 'prepared' -Status succeeded `
            -Facts @{ version = $Version; sourceCommit = $sourceCommit; bundle = $bundleZip; uploaded = $false } | Out-Null
        Write-Host "Edge host prepared without production upload: version=$Version result=$preparedResult"
        return
    }

    $attemptStage = 'uploading'
    $gitFacts = Assert-EdgeReleaseGitState `
        -RepoRoot $repoRoot `
        -ExpectedSha $ExpectedSha `
        -AllowDetachedExactSha:$PreparedSourceSnapshot
    $sourceCommit = [string]$gitFacts.Head
    Write-EdgeDeploymentAttemptState -ReleaseRoot $releaseRoot -Target EdgeHost -InvocationId $dispatchInvocationId `
        -Stage $attemptStage -Status running -Facts @{ version = $Version; sourceCommit = $sourceCommit; bundle = $bundleZip } | Out-Null
    Write-Host "Publishing Edge release bundle over HTTP: $CloudApiBaseUrl (limit=${UploadRateLimitMbps}Mbps timeout=${UploadTimeoutSeconds}s)"
    $publishResult = Invoke-EdgeHttpReleaseUpload -BundleZip $bundleZip
    $attemptStage = 'verifying-downloads'
    Test-EdgeHttpReleaseUrls -PublishResult $publishResult
    Write-EdgePublishSummary -PublishResult $publishResult
    Write-EdgeDeploymentAttemptState -ReleaseRoot $releaseRoot -Target EdgeHost -InvocationId $dispatchInvocationId `
        -Stage 'completed' -Status succeeded -Facts @{ version = $Version; sourceCommit = $sourceCommit; resumeReleaseRoot = $releaseRoot } | Out-Null
}
catch [System.Management.Automation.PipelineStoppedException] {
    if (-not [string]::IsNullOrWhiteSpace($attemptReleaseRoot)) {
        Write-EdgeDeploymentAttemptState -ReleaseRoot $attemptReleaseRoot -Target EdgeHost -InvocationId $dispatchInvocationId `
            -Stage $attemptStage -Status cancelled `
            -Facts @{ version = $Version; sourceCommit = $sourceCommit; resumeReleaseRoot = $attemptReleaseRoot } | Out-Null
    }
    throw
}
catch {
    $message = $_.Exception.Message
    if (-not [string]::IsNullOrWhiteSpace($attemptReleaseRoot)) {
        try {
            Write-EdgeDeploymentAttemptState -ReleaseRoot $attemptReleaseRoot -Target EdgeHost -InvocationId $dispatchInvocationId `
                -Stage $attemptStage -Status failed `
                -Facts @{ version = $Version; sourceCommit = $sourceCommit; error = $message; resumeReleaseRoot = $attemptReleaseRoot } | Out-Null
        }
        catch {
            Write-Warning "Could not write Edge host failure state. $($_.Exception.Message)"
        }
        throw "Edge host release failed at stage '$attemptStage'. Artifacts were preserved at '$attemptReleaseRoot'. Retry through the workspace entrypoint with ResumeReleaseRoot='$attemptReleaseRoot'. $message"
    }
    throw
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
    Exit-EdgeDeploymentLock -Lock $deploymentLock
}
