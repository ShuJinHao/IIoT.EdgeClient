param(
    [Parameter(Mandatory = $true)]
    [string]$ModuleId,

    [Parameter(Mandatory = $true)]
    [string]$PluginRepositoryRoot,

    [ValidateSet('stable')]
    [string]$Channel = 'stable',

    [string]$Configuration = 'Release',

    [string]$RuntimeIdentifier = 'win-x64',

    [string]$OutputRoot = 'publish\edge-plugin-releases',

    [Parameter(Mandatory = $true)]
    [string]$CloudApiBaseUrl,

    [string]$CloudToken = '',

    [string]$ReleaseNotes = '',

    [string]$ReleaseNotesPath = '',

    [ValidateRange(1, 1000)]
    [int]$UploadRateLimitMbps = 1000,

    [string]$ResumeReleaseRoot = $env:IIOT_EDGE_RESUME_RELEASE_ROOT,

    [ValidateRange(1, 300)]
    [int]$ConnectTimeoutSeconds = 10,

    [ValidateRange(1, 86400)]
    [int]$UploadTimeoutSeconds = 900,

    [ValidateRange(1, 3600)]
    [int]$LowSpeedTimeSeconds = 60,

    [ValidateRange(1, 104857600)]
    [int]$LowSpeedLimitBytesPerSecond = 1024,

    [switch]$SkipPackageValidation
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$pluginRepoRoot = [System.IO.Path]::GetFullPath($PluginRepositoryRoot)
if (-not (Test-Path -LiteralPath $pluginRepoRoot -PathType Container)) {
    throw "Plugin repository was not found: $pluginRepoRoot"
}
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')
. (Join-Path $PSScriptRoot 'EdgeReleaseCredential.Common.ps1')
. (Join-Path $PSScriptRoot 'EdgeDeployment.Common.ps1')

if ($Channel -ne 'stable') {
    throw 'Production Edge plugin releases must use stable channel.'
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
        throw 'Production Edge plugin release notes are required. Pass -ReleaseNotes or -ReleaseNotesPath with the real update content.'
    }

    return $resolvedNotes
}

function Invoke-PluginPack {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Parameters
    )

    $scriptPath = Join-Path $pluginRepoRoot 'eng/PackEdgePlugin.ps1'
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "Plugin repository pack script was not found: $scriptPath"
    }

    & $scriptPath @Parameters
    if ($LASTEXITCODE -ne 0) {
        throw "eng/PackEdgePlugin.ps1 failed with exit code $LASTEXITCODE."
    }
}

function Get-PreservedPluginMetadataPath {
    param([Parameter(Mandatory = $true)][string]$PackageRoot)

    foreach ($candidate in @(Get-ChildItem -LiteralPath $PackageRoot -Filter '*.zip.json' -File -ErrorAction SilentlyContinue | Sort-Object FullName)) {
        try {
            $candidateMetadata = Get-Content -Raw -Encoding UTF8 -LiteralPath $candidate.FullName | ConvertFrom-Json
            $candidatePackageFileName = [string]$candidateMetadata.packageFileName
            if (-not [string]::IsNullOrWhiteSpace($candidatePackageFileName) -and
                (Test-Path -LiteralPath (Join-Path $PackageRoot $candidatePackageFileName) -PathType Leaf)) {
                return $candidate
            }
        }
        catch {
            # A failed pack may leave partial metadata. Resume rebuilds instead of trusting it.
        }
    }

    return $null
}

function Test-PreservedPluginPackage {
    param(
        [Parameter(Mandatory = $true)]$Metadata,
        [Parameter(Mandatory = $true)][string]$PackagePath
    )

    if ([int]$Metadata.packageSchemaVersion -ne 2) {
        throw "Unexpected plugin package metadata schema: $($Metadata.packageSchemaVersion)"
    }
    if ([string]$Metadata.moduleId -ne $ModuleId -or
        [string]$Metadata.version -ne $declaredVersion -or
        [string]$Metadata.targetRuntime -ne $RuntimeIdentifier) {
        throw 'Plugin package metadata does not match the requested module/version/runtime.'
    }
    if ([string]$Metadata.sourceCommit -ne [string]$gitFacts.Head) {
        throw "Plugin package sourceCommit '$($Metadata.sourceCommit)' does not match release HEAD '$($gitFacts.Head)'."
    }
    $package = Get-Item -LiteralPath $PackagePath
    if ($package.Length -ne [int64]$Metadata.packageSize) {
        throw "Plugin package size does not match metadata: expected=$($Metadata.packageSize) actual=$($package.Length)"
    }
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $PackagePath).Hash.ToUpperInvariant()
    if ($hash -ne ([string]$Metadata.sha256).ToUpperInvariant()) {
        throw "Plugin package SHA256 does not match metadata: expected=$($Metadata.sha256) actual=$hash"
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $manifestEntry = $archive.Entries | Where-Object { $_.FullName -eq 'plugin.json' } | Select-Object -First 1
        if ($null -eq $manifestEntry) { throw 'Plugin package root is missing plugin.json.' }
        $reader = [System.IO.StreamReader]::new($manifestEntry.Open(), [System.Text.Encoding]::UTF8)
        try { $packagedManifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ([string]$packagedManifest.moduleId -ne $ModuleId -or
            [string]$packagedManifest.version -ne $declaredVersion -or
            [string]$packagedManifest.hostApiVersion -ne [string]$Metadata.hostApiVersion) {
            throw 'Packaged plugin.json does not match package metadata.'
        }
        if ($archive.Entries.FullName -notcontains [string]$packagedManifest.entryAssembly) {
            throw "Plugin package entry assembly is missing: $($packagedManifest.entryAssembly)"
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Invoke-CloudJsonGet {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($script:CloudToken)) {
        $script:CloudToken = Resolve-EdgeReleaseCloudToken -CloudApiBaseUrl $CloudApiBaseUrl -CloudToken $script:CloudToken
    }

    if ([string]::IsNullOrWhiteSpace($script:CloudToken)) {
        throw 'CloudToken is required. Pass -CloudToken, set $env:IIOT_CLOUD_RELEASE_TOKEN, set $env:IIOT_EDGE_RELEASE_API_KEY, or run scripts/SaveEdgeReleaseApiKey.ps1.'
    }

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

function Find-PluginCatalogVersion {
    param(
        [Parameter(Mandatory = $true)]$Catalog,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$TargetRuntime
    )

    if ($null -eq $Catalog.plugins) {
        throw 'Cloud Edge release catalog did not contain plugins.'
    }

    foreach ($plugin in @($Catalog.plugins)) {
        if (-not [string]::Equals([string]$plugin.moduleId, $ModuleId, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        foreach ($entry in @($plugin.versions)) {
            if ([string]::Equals([string]$entry.version, $Version, [System.StringComparison]::OrdinalIgnoreCase) -and
                [string]::Equals([string]$entry.targetRuntime, $TargetRuntime, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $entry
            }
        }
    }

    return $null
}

function Resolve-DownloadBaseUrl {
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

function Test-VerificationUrls {
    param([Parameter(Mandatory = $true)]$PublishResult)

    $downloadBase = Resolve-DownloadBaseUrl
    foreach ($relativeUrl in @($PublishResult.verificationUrls)) {
        $uri = "$downloadBase/$(([string]$relativeUrl).TrimStart('/'))"
        Test-EdgeCurlUrl -Uri $uri -ConnectTimeoutSeconds $ConnectTimeoutSeconds -RequestTimeoutSeconds 60
    }
}

function New-PluginReleaseWrapper {
    param(
        [Parameter(Mandatory = $true)]$Metadata,
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$ReleaseNotesText,
        [Parameter(Mandatory = $true)][string]$OutputZip
    )

    $wrapperRoot = Join-Path (Split-Path -Parent $OutputZip) 'edge-plugin-wrapper'
    if (Test-Path $wrapperRoot) {
        Remove-Item -Path $wrapperRoot -Recurse -Force
    }

    New-Item -Path (Join-Path $wrapperRoot 'plugin') -ItemType Directory -Force | Out-Null
    Copy-Item -Path $PackagePath -Destination (Join-Path $wrapperRoot 'plugin') -Force

    $releaseManifest = [ordered]@{
        packageSchemaVersion = 1
        channel = $Channel
        moduleId = [string]$Metadata.moduleId
        processType = [string]$Metadata.processType
        displayName = [string]$Metadata.displayName
        description = if ($Metadata.PSObject.Properties['description']) { [string]$Metadata.description } else { $null }
        iconKind = if ($Metadata.PSObject.Properties['iconKind']) { [string]$Metadata.iconKind } else { $null }
        accentColor = if ($Metadata.PSObject.Properties['accentColor']) { [string]$Metadata.accentColor } else { $null }
        version = [string]$Metadata.version
        hostApiVersion = [string]$Metadata.hostApiVersion
        minHostVersion = [string]$Metadata.minHostVersion
        maxHostVersion = [string]$Metadata.maxHostVersion
        dependencies = @($Metadata.dependencies)
        targetRuntime = [string]$Metadata.targetRuntime
        targetFramework = [string]$Metadata.targetFramework
        packageFileName = [string]$Metadata.packageFileName
        packageSize = [int64]$Metadata.packageSize
        sha256 = [string]$Metadata.sha256
        signature = [string]$Metadata.signature
        publisher = [string]$Metadata.publisher
        sourceCommit = [string]$Metadata.sourceCommit
        releaseNotes = $ReleaseNotesText
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }

    $releaseManifest |
        ConvertTo-Json -Depth 20 |
        Set-Content -Encoding UTF8 -Path (Join-Path $wrapperRoot 'plugin-release.json')

    if (Test-Path $OutputZip) {
        Remove-Item -Path $OutputZip -Force
    }

    Compress-Archive -Path (Join-Path $wrapperRoot '*') -DestinationPath $OutputZip -CompressionLevel Fastest -Force
    Remove-Item -Path $wrapperRoot -Recurse -Force
    return $OutputZip
}

function Invoke-PluginPackageUpload {
    param([Parameter(Mandatory = $true)][string]$WrapperZip)

    $apiRoot = $CloudApiBaseUrl.TrimEnd('/')
    $uri = "$apiRoot/human/client-releases/plugin-packages"
    $responsePath = Join-Path (Split-Path -Parent $WrapperZip) 'edge-plugin-upload-response.json'
    $rateBytesPerSecond = [int64]([math]::Floor($UploadRateLimitMbps * 1024 * 1024 / 8))

    Invoke-EdgeCurlRequest `
        -Method POST `
        -Uri $uri `
        -Token $script:CloudToken `
        -UploadFile $WrapperZip `
        -ResponsePath $responsePath `
        -RateLimitBytesPerSecond $rateBytesPerSecond `
        -ConnectTimeoutSeconds $ConnectTimeoutSeconds `
        -RequestTimeoutSeconds $UploadTimeoutSeconds `
        -LowSpeedTimeSeconds $LowSpeedTimeSeconds `
        -LowSpeedLimitBytesPerSecond $LowSpeedLimitBytesPerSecond | Out-Null

    return Get-Content -Raw -Encoding UTF8 -Path $responsePath | ConvertFrom-Json
}

$dispatchInvocationId = Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgePlugin
$gitFacts = Assert-EdgeReleaseGitState -RepoRoot $pluginRepoRoot
$deploymentLock = Enter-EdgeDeploymentLock -RepoRoot $repoRoot -InvocationId $dispatchInvocationId -Target EdgePlugin
$locationPushed = $false
$attemptReleaseRoot = ''
$attemptStage = 'initializing'

try {
    Push-Location $repoRoot
    $locationPushed = $true
    $releaseNotesText = Resolve-ExplicitReleaseNotes
    $sourceManifestPath = Join-Path $pluginRepoRoot "src/IIoT.Edge.Module.$ModuleId/plugin.json"
    if (-not (Test-Path -LiteralPath $sourceManifestPath -PathType Leaf)) {
        throw "Plugin manifest was not found: $sourceManifestPath"
    }
    $sourceManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $sourceManifestPath | ConvertFrom-Json
    if (-not [string]::Equals([string]$sourceManifest.moduleId, $ModuleId, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Plugin manifest moduleId '$($sourceManifest.moduleId)' does not match '$ModuleId'."
    }

    $declaredVersion = [string]$sourceManifest.version
    if ($declaredVersion -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
        throw "Plugin manifest version is invalid: $declaredVersion"
    }

    $catalog = Invoke-CloudJsonGet -Path "/human/client-releases/catalog?channel=$Channel&targetRuntime=$RuntimeIdentifier&includeArchived=true"
    $existingRelease = Find-PluginCatalogVersion -Catalog $catalog -Version $declaredVersion -TargetRuntime $RuntimeIdentifier
    $isResume = -not [string]::IsNullOrWhiteSpace($ResumeReleaseRoot)
    $resolvedOutputRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $OutputRoot

    if ($isResume) {
        $releaseRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $ResumeReleaseRoot
        if (-not (Test-Path -LiteralPath $releaseRoot -PathType Container)) {
            throw "Resume release root was not found: $releaseRoot"
        }
        $statePath = Join-Path $releaseRoot 'edge-deployment-attempt.json'
        if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
            throw "Resume release root is missing edge-deployment-attempt.json: $releaseRoot"
        }
        $savedState = Get-Content -Raw -Encoding UTF8 -LiteralPath $statePath | ConvertFrom-Json
        if ([string]$savedState.facts.sourceCommit -ne [string]$gitFacts.Head) {
            throw "Resume artifact sourceCommit '$($savedState.facts.sourceCommit)' does not match pushed HEAD '$($gitFacts.Head)'."
        }
        if ([string]$savedState.facts.version -ne $declaredVersion) {
            throw "Resume artifact version '$($savedState.facts.version)' does not match current plugin manifest '$declaredVersion'."
        }
    }
    else {
        if ($null -ne $existingRelease) {
            throw "Plugin version already exists in Cloud catalog: $ModuleId/$Channel/$declaredVersion/$RuntimeIdentifier. No package build was started."
        }
        $releaseRoot = Join-Path $resolvedOutputRoot "$Channel/$ModuleId/$([Guid]::NewGuid().ToString('N'))"
    }

    $attemptReleaseRoot = $releaseRoot
    if ($isResume -and $null -ne $existingRelease) {
        $downloadUrl = [string]$existingRelease.downloadUrl
        if (-not [string]::IsNullOrWhiteSpace($downloadUrl)) {
            if (-not $downloadUrl.StartsWith('http', [System.StringComparison]::OrdinalIgnoreCase)) {
                $downloadUrl = "$(Resolve-DownloadBaseUrl)/$($downloadUrl.TrimStart('/'))"
            }
            Test-EdgeCurlUrl -Uri $downloadUrl -ConnectTimeoutSeconds $ConnectTimeoutSeconds -RequestTimeoutSeconds 60
        }
        Write-EdgeDeploymentAttemptState -ReleaseRoot $releaseRoot -Target EdgePlugin -InvocationId $dispatchInvocationId `
            -Stage 'reconciled-existing-release' -Status succeeded `
            -Facts @{ moduleId = $ModuleId; version = $declaredVersion; sourceCommit = $gitFacts.Head; alreadyPublished = $true } | Out-Null
        Write-Host "Edge plugin resume reconciliation succeeded: module=$ModuleId version=$declaredVersion alreadyPublished=true"
        return
    }

    $packageOutputRoot = Join-Path $releaseRoot 'package'
    New-Item -Path $packageOutputRoot -ItemType Directory -Force | Out-Null
    $metadataPath = Get-PreservedPluginMetadataPath -PackageRoot $packageOutputRoot
    if ($null -eq $metadataPath) {
        $pluginPackageScratchRoot = Join-Path $pluginRepoRoot "artifacts/deploy-pack/$dispatchInvocationId"
        $attemptStage = 'building-package'
        Write-EdgeDeploymentAttemptState -ReleaseRoot $releaseRoot -Target EdgePlugin -InvocationId $dispatchInvocationId `
            -Stage $attemptStage -Status running `
            -Facts @{ moduleId = $ModuleId; version = $declaredVersion; sourceCommit = $gitFacts.Head; resumeReleaseRoot = $releaseRoot; resumed = $isResume } | Out-Null
        Invoke-PluginPack -Parameters @{
            ModuleId      = $ModuleId
            Configuration = $Configuration
            TargetRuntime = $RuntimeIdentifier
            OutputRoot    = $pluginPackageScratchRoot
            SourceCommit  = $gitFacts.Head
            CleanOutput   = $true
        }
        Get-ChildItem -LiteralPath $pluginPackageScratchRoot -File |
            Copy-Item -Destination $packageOutputRoot -Force
        $metadataPath = Get-PreservedPluginMetadataPath -PackageRoot $packageOutputRoot
    }

    if ($null -eq $metadataPath) {
        throw "Plugin package metadata was not generated under $packageOutputRoot."
    }
    $metadata = Get-Content -Raw -Encoding UTF8 -Path $metadataPath.FullName | ConvertFrom-Json
    if ([string]$metadata.version -ne $declaredVersion -or [string]$metadata.targetRuntime -ne $RuntimeIdentifier) {
        throw 'Preserved plugin metadata does not match the current manifest version/runtime.'
    }
    $packagePath = Join-Path $packageOutputRoot ([string]$metadata.packageFileName)
    if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
        throw "Plugin package was not generated: $packagePath"
    }

    $attemptStage = 'validating-preserved-package'
    Write-EdgeDeploymentAttemptState -ReleaseRoot $releaseRoot -Target EdgePlugin -InvocationId $dispatchInvocationId `
        -Stage $attemptStage -Status running `
        -Facts @{ moduleId = $ModuleId; version = $declaredVersion; sourceCommit = $gitFacts.Head; resumed = $isResume } | Out-Null
    if (-not $SkipPackageValidation) {
        Test-PreservedPluginPackage -Metadata $metadata -PackagePath $packagePath
    }

    $wrapperZip = Join-Path $releaseRoot "edge-plugin-release-$ModuleId-$($metadata.version)-$RuntimeIdentifier.zip"
    if (-not $isResume -or -not (Test-Path -LiteralPath $wrapperZip -PathType Leaf)) {
        New-PluginReleaseWrapper -Metadata $metadata -PackagePath $packagePath -ReleaseNotesText $releaseNotesText -OutputZip $wrapperZip | Out-Null
    }

    $attemptStage = 'uploading'
    Write-EdgeDeploymentAttemptState -ReleaseRoot $releaseRoot -Target EdgePlugin -InvocationId $dispatchInvocationId `
        -Stage $attemptStage -Status running `
        -Facts @{ moduleId = $ModuleId; version = $declaredVersion; sourceCommit = $gitFacts.Head; wrapper = $wrapperZip } | Out-Null
    Write-Host "Publishing Edge plugin release over HTTP: module=$ModuleId version=$($metadata.version) runtime=$RuntimeIdentifier timeout=${UploadTimeoutSeconds}s"
    Write-Host "Compatibility: hostApi=$($metadata.hostApiVersion), hostVersion=$($metadata.minHostVersion)..$($metadata.maxHostVersion)"
    $publishResult = Invoke-PluginPackageUpload -WrapperZip $wrapperZip
    $attemptStage = 'verifying-downloads'
    Test-VerificationUrls -PublishResult $publishResult

    Write-Host ''
    Write-Host 'Edge plugin HTTP release deployment summary'
    Write-Host "  moduleId: $($publishResult.moduleId)"
    Write-Host "  displayName: $($publishResult.displayName)"
    Write-Host "  channel: $($publishResult.channel)"
    Write-Host "  version: $($publishResult.version)"
    Write-Host "  targetRuntime: $($publishResult.targetRuntime)"
    Write-Host "  downloadUrl: $($publishResult.downloadUrl)"
    Write-Host "  sha256: $($publishResult.sha256)"
    Write-Host "  packageSize: $($publishResult.packageSize)"
    Write-Host "  uploadSeconds: $([math]::Round([double]$publishResult.uploadSeconds, 2))"
    Write-Host '  httpVerification: ok'
    Write-Host '  releaseNotes:'
    foreach ($line in $releaseNotesText.Split("`n", [System.StringSplitOptions]::RemoveEmptyEntries)) {
        Write-Host "    $($line.Trim())"
    }
    Write-EdgeDeploymentAttemptState -ReleaseRoot $releaseRoot -Target EdgePlugin -InvocationId $dispatchInvocationId `
        -Stage 'completed' -Status succeeded `
        -Facts @{ moduleId = $ModuleId; version = $declaredVersion; sourceCommit = $gitFacts.Head; resumeReleaseRoot = $releaseRoot } | Out-Null
}
catch [System.Management.Automation.PipelineStoppedException] {
    if (-not [string]::IsNullOrWhiteSpace($attemptReleaseRoot)) {
        Write-EdgeDeploymentAttemptState -ReleaseRoot $attemptReleaseRoot -Target EdgePlugin -InvocationId $dispatchInvocationId `
            -Stage $attemptStage -Status cancelled -Facts @{ resumeReleaseRoot = $attemptReleaseRoot } | Out-Null
    }
    throw
}
catch {
    $message = $_.Exception.Message
    if (-not [string]::IsNullOrWhiteSpace($attemptReleaseRoot)) {
        try {
            Write-EdgeDeploymentAttemptState -ReleaseRoot $attemptReleaseRoot -Target EdgePlugin -InvocationId $dispatchInvocationId `
                -Stage $attemptStage -Status failed -Facts @{ error = $message; resumeReleaseRoot = $attemptReleaseRoot } | Out-Null
        }
        catch {
            Write-Warning "Could not write Edge plugin failure state. $($_.Exception.Message)"
        }
        throw "Edge plugin release failed at stage '$attemptStage'. Artifacts were preserved at '$attemptReleaseRoot'. Retry through the workspace entrypoint with ResumeReleaseRoot='$attemptReleaseRoot'. $message"
    }
    throw
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
    Exit-EdgeDeploymentLock -Lock $deploymentLock
}
