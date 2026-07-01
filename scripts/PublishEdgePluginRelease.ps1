param(
    [Parameter(Mandatory = $true)]
    [string]$ModuleId,

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
    [int]$UploadRateLimitMbps = 100,

    [switch]$SkipPackageValidation
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')
. (Join-Path $PSScriptRoot 'EdgeReleaseCredential.Common.ps1')

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

    & $scriptPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$ScriptName failed with exit code $LASTEXITCODE."
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
    return Invoke-RestMethod -Method Get -Uri $uri -Headers @{
        Authorization = "Bearer $script:CloudToken"
    }
}

function Assert-PluginVersionDoesNotExist {
    param(
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$TargetRuntime
    )

    $catalog = Invoke-CloudJsonGet -Path "/human/client-releases/catalog?channel=$Channel&targetRuntime=$TargetRuntime&includeArchived=true"
    foreach ($plugin in @($catalog.plugins)) {
        if (-not [string]::Equals([string]$plugin.moduleId, $ModuleId, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        foreach ($entry in @($plugin.versions)) {
            if ([string]::Equals([string]$entry.version, $Version, [System.StringComparison]::OrdinalIgnoreCase) -and
                [string]::Equals([string]$entry.targetRuntime, $TargetRuntime, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Plugin version already exists in Cloud catalog: $ModuleId/$Channel/$Version/$TargetRuntime. Bump plugin.json version before publishing."
            }
        }
    }
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
    $curl = Get-Command curl -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $curl) {
        throw 'curl is required for HTTP Edge plugin release verification.'
    }

    foreach ($relativeUrl in @($PublishResult.verificationUrls)) {
        $uri = "$downloadBase/$(([string]$relativeUrl).TrimStart('/'))"
        & $curl.Source --fail --silent --show-error --head "$uri" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "HTTP verification failed: $uri"
        }
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
        packageSchemaVersion = [int]$Metadata.packageSchemaVersion
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

    $curl = Get-Command curl -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $curl) {
        throw 'curl is required for HTTP Edge plugin release upload with client-side rate limiting.'
    }

    $apiRoot = $CloudApiBaseUrl.TrimEnd('/')
    $uri = "$apiRoot/human/client-releases/plugin-packages"
    $responsePath = Join-Path (Split-Path -Parent $WrapperZip) 'edge-plugin-upload-response.json'
    $rateBytesPerSecond = [int64]([math]::Floor($UploadRateLimitMbps * 1024 * 1024 / 8))

    if (Test-Path $responsePath) {
        Remove-Item -Path $responsePath -Force
    }

    & $curl.Source `
        --fail `
        --show-error `
        --silent `
        --request POST `
        --header "Authorization: Bearer $script:CloudToken" `
        --header "Content-Type: application/zip" `
        --limit-rate "$rateBytesPerSecond" `
        --data-binary "@$WrapperZip" `
        --output "$responsePath" `
        "$uri"

    if ($LASTEXITCODE -ne 0) {
        throw "HTTP Edge plugin release upload failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path $responsePath)) {
        throw 'HTTP Edge plugin release upload did not return a response body.'
    }

    return Get-Content -Raw -Encoding UTF8 -Path $responsePath | ConvertFrom-Json
}

$releaseNotesText = Resolve-ExplicitReleaseNotes
$resolvedOutputRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $OutputRoot
$releaseRoot = Join-Path $resolvedOutputRoot "$Channel/$ModuleId/$([Guid]::NewGuid().ToString('N'))"
$packageOutputRoot = Join-Path $releaseRoot 'package'
New-Item -Path $packageOutputRoot -ItemType Directory -Force | Out-Null

Push-Location $repoRoot
try {
    Invoke-EdgeScript 'PackEdgePlugin.ps1' @(
        '-ModuleId', $ModuleId,
        '-Configuration', $Configuration,
        '-TargetRuntime', $RuntimeIdentifier,
        '-OutputRoot', $packageOutputRoot,
        '-CleanOutput'
    )

    $metadataPath = Get-ChildItem -Path $packageOutputRoot -Filter '*.zip.json' -File |
        Select-Object -First 1
    if ($null -eq $metadataPath) {
        throw "Plugin package metadata was not generated under $packageOutputRoot."
    }

    $metadata = Get-Content -Raw -Encoding UTF8 -Path $metadataPath.FullName | ConvertFrom-Json
    $packagePath = Join-Path $packageOutputRoot ([string]$metadata.packageFileName)
    if (-not (Test-Path $packagePath)) {
        throw "Plugin package was not generated: $packagePath"
    }

    if (-not $SkipPackageValidation) {
        Invoke-EdgeScript 'TestEdgePluginPackage.ps1' @(
            '-OutputRoot', $packageOutputRoot,
            '-ModuleId', $ModuleId,
            '-Version', ([string]$metadata.version),
            '-TargetRuntime', $RuntimeIdentifier
        )
    }

    Assert-PluginVersionDoesNotExist -Version ([string]$metadata.version) -TargetRuntime ([string]$metadata.targetRuntime)

    $wrapperZip = Join-Path $releaseRoot "edge-plugin-release-$ModuleId-$($metadata.version)-$RuntimeIdentifier.zip"
    New-PluginReleaseWrapper `
        -Metadata $metadata `
        -PackagePath $packagePath `
        -ReleaseNotesText $releaseNotesText `
        -OutputZip $wrapperZip | Out-Null

    Write-Host "Publishing Edge plugin release over HTTP: module=$ModuleId version=$($metadata.version) runtime=$RuntimeIdentifier"
    Write-Host "Compatibility: hostApi=$($metadata.hostApiVersion), hostVersion=$($metadata.minHostVersion)..$($metadata.maxHostVersion)"
    $publishResult = Invoke-PluginPackageUpload -WrapperZip $wrapperZip
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
    Write-Host "  httpVerification: ok"
    Write-Host '  releaseNotes:'
    foreach ($line in $releaseNotesText.Split("`n", [System.StringSplitOptions]::RemoveEmptyEntries)) {
        Write-Host "    $($line.Trim())"
    }
}
finally {
    Pop-Location
}
