param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version = '',

    [ValidateSet('stable')]
    [string]$Channel = 'stable',

    [string]$Configuration = 'Release',

    [string]$RuntimeIdentifier = 'win-x64',

    [string]$PackId = 'IIoT.EdgeClient.Homogenization',

    [string]$DeployHost = '10.98.90.154',

    [string]$DeployUser = 'root',

    [int]$DeployPort = 22,

    [string]$EdgeUpdatesDir = '/srv/iiot/edge-updates',

    [ValidateSet('auto', 'rsync', 'scp', 'http')]
    [string]$Transport = 'auto',

    [string]$CloudApiBaseUrl = '',

    [string]$CloudToken = '',

    [string]$ReleaseNotes = '',

    [string]$ReleaseNotesPath = '',

    [ValidateRange(1, 1000)]
    [int]$UploadRateLimitMbps = 100,

    [bool]$SkipVeloAppCheck = $true,

    [switch]$SkipVelopackValidation,

    [switch]$SkipInstallerValidation
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$remote = "$DeployUser@$DeployHost"

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

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [AllowEmptyCollection()]
        [string[]]$Arguments = @()
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function ConvertTo-ShellSingleQuoted {
    param([Parameter(Mandatory = $true)][string]$Value)
    $quote = "'"
    $escapedQuote = "'\''"
    return $quote + $Value.Replace($quote, $escapedQuote) + $quote
}

function Resolve-Transport {
    if ($Channel -eq 'stable' -and $Transport -ne 'http' -and $Transport -ne 'auto') {
        throw 'Stable Edge host releases must use -Transport http so Cloud can enforce release notes, DB registration, audit and retention.'
    }

    if ($Transport -ne 'auto') {
        return $Transport
    }

    if ($Channel -eq 'stable') {
        return 'http'
    }

    throw 'Only stable Edge releases are supported by this script.'
}

function Assert-HttpPublishConfiguration {
    if ([string]::IsNullOrWhiteSpace($CloudApiBaseUrl)) {
        throw 'CloudApiBaseUrl is required when -Transport http is used.'
    }

    if ([string]::IsNullOrWhiteSpace($CloudToken)) {
        throw 'CloudToken is required when -Transport http is used.'
    }
}

function Invoke-CloudJsonGet {
    param([Parameter(Mandatory = $true)][string]$Path)

    Assert-HttpPublishConfiguration
    $apiRoot = $CloudApiBaseUrl.TrimEnd('/')
    $uri = "$apiRoot/$($Path.TrimStart('/'))"
    $headers = @{
        Authorization = "Bearer $CloudToken"
    }
    return Invoke-RestMethod -Method Get -Uri $uri -Headers $headers
}

function Try-GetLatestCloudStableRelease {
    try {
        $catalog = Invoke-CloudJsonGet -Path "/human/client-releases/catalog?channel=$Channel&targetRuntime=$RuntimeIdentifier&onlyPublished=true"
        $versions = @($catalog.host.versions | Where-Object {
            $_.version -match '^\d+\.\d+\.\d+$' -and
            ($_.status -eq 'Published' -or $_.status -eq 'Deprecated')
        })

        if ($versions.Count -eq 0) {
            return $null
        }

        $latest = $versions |
            Sort-Object @{ Expression = { [version]$_.version } } |
            Select-Object -Last 1

        $sourceCommit = ''
        if (-not [string]::IsNullOrWhiteSpace([string]$latest.downloadUrl)) {
            try {
                $downloadBase = Resolve-DownloadBaseUrl
                $manifestUrl = [string]$latest.downloadUrl
                if (-not $manifestUrl.StartsWith('http', [System.StringComparison]::OrdinalIgnoreCase)) {
                    $manifestUrl = "$downloadBase/$($manifestUrl.TrimStart('/'))"
                }

                $manifest = Invoke-RestMethod -Method Get -Uri $manifestUrl
                $sourceCommit = [string]$manifest.sourceCommit
            }
            catch {
                Write-Warning "Could not read previous Edge release manifest sourceCommit. $($_.Exception.Message)"
            }
        }

        return [PSCustomObject]@{
            Version = [string]$latest.version
            SourceCommit = $sourceCommit
        }
    }
    catch {
        Write-Warning "Could not read latest Cloud Edge release. Falling back to 0.0.1. $($_.Exception.Message)"
        return $null
    }
}

function Invoke-RemoteBash {
    param([Parameter(Mandatory = $true)][string]$Script)

    $sshArgs = @('-p', [string]$DeployPort, $remote, 'bash -s')
    $Script | & ssh @sshArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Remote bash command failed with exit code $LASTEXITCODE."
    }
}

function Invoke-RemoteBashCapture {
    param([Parameter(Mandatory = $true)][string]$Script)

    $sshArgs = @('-p', [string]$DeployPort, $remote, 'bash -s')
    $output = $Script | & ssh @sshArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Remote bash command failed with exit code $LASTEXITCODE."
    }

    return @($output)
}

function Try-GetLatestRemoteStableRelease {
    $script = @"
set -euo pipefail
root=$(ConvertTo-ShellSingleQuoted "$EdgeUpdatesDir/installers/stable")
if [ ! -d "`$root" ]; then
  exit 0
fi
latest_version="`$(find "`$root" -mindepth 1 -maxdepth 1 -type d -printf '%f\n' 2>/dev/null | grep -E '^[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -n 1 || true)"
if [ -z "`$latest_version" ]; then
  exit 0
fi
source_commit=""
manifest="`$root/`$latest_version/installer-artifact.json"
if [ -f "`$manifest" ] && command -v python3 >/dev/null 2>&1; then
  source_commit="`$(python3 - "`$manifest" <<'PY' 2>/dev/null || true
import json
import sys
with open(sys.argv[1], encoding='utf-8-sig') as f:
    data = json.load(f)
print(data.get('sourceCommit') or '')
PY
)"
fi
printf '%s\t%s\n' "`$latest_version" "`$source_commit"
"@

    try {
        $lines = Invoke-RemoteBashCapture $script
        $line = @($lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
        if ($line.Count -eq 0) {
            return $null
        }

        $parts = ([string]$line[0]).Split("`t", 2)
        if ($parts.Count -eq 0 -or [string]::IsNullOrWhiteSpace($parts[0])) {
            return $null
        }

        return [PSCustomObject]@{
            Version = $parts[0]
            SourceCommit = if ($parts.Count -gt 1) { $parts[1] } else { '' }
        }
    }
    catch {
        Write-Warning "Could not read latest remote Edge release. Falling back to 0.0.1. $($_.Exception.Message)"
        return $null
    }
}

function Get-NextPatchVersion {
    param([string]$CurrentVersion)

    if ([string]::IsNullOrWhiteSpace($CurrentVersion)) {
        return '0.0.1'
    }

    $parts = $CurrentVersion.Split('.')
    if ($parts.Count -ne 3) {
        return '0.0.1'
    }

    return '{0}.{1}.{2}' -f ([int]$parts[0]), ([int]$parts[1]), (([int]$parts[2]) + 1)
}

function Invoke-GitText {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & git @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return ''
    }

    return [string]($output -join "`n")
}

function Test-GitCommitExists {
    param([string]$Commit)

    if ([string]::IsNullOrWhiteSpace($Commit)) {
        return $false
    }

    & git cat-file -e "$Commit^{commit}" 2>$null
    return $LASTEXITCODE -eq 0
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

function Publish-DirectoryWithRsync {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$RemoteDirectory
    )

    $source = (Resolve-Path $SourceDirectory).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $remoteSpec = "${remote}:$RemoteDirectory/"
    Invoke-NativeCommand 'rsync' @(
        '-az',
        '--delete',
        '-e',
        "ssh -p $DeployPort",
        $source,
        $remoteSpec
    )
}

function Publish-DirectoryWithScp {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDirectory,
        [Parameter(Mandatory = $true)][string]$RemoteDirectory
    )

    Invoke-RemoteBash "mkdir -p $(ConvertTo-ShellSingleQuoted $RemoteDirectory)"
    $source = Join-Path (Resolve-Path $SourceDirectory).Path '.'
    Invoke-NativeCommand 'scp' @(
        '-P',
        [string]$DeployPort,
        '-r',
        $source,
        "${remote}:$RemoteDirectory/"
    )
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
    $curl = Get-Command curl -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $curl) {
        throw 'curl is required for HTTP Edge release upload with client-side rate limiting.'
    }

    $apiRoot = $CloudApiBaseUrl.TrimEnd('/')
    $uri = "$apiRoot/human/client-releases/edge-release-bundles"
    $responsePath = Join-Path (Split-Path -Parent $BundleZip) 'edge-http-upload-response.json'
    $rateBytesPerSecond = [int64]([math]::Floor($UploadRateLimitMbps * 1024 * 1024 / 8))

    if (Test-Path $responsePath) {
        Remove-Item -Path $responsePath -Force
    }

    & $curl.Source `
        --fail `
        --show-error `
        --silent `
        --request POST `
        --header "Authorization: Bearer $CloudToken" `
        --header "Content-Type: application/zip" `
        --limit-rate "$rateBytesPerSecond" `
        --data-binary "@$BundleZip" `
        --output "$responsePath" `
        "$uri"

    if ($LASTEXITCODE -ne 0) {
        throw "HTTP Edge release upload failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path $responsePath)) {
        throw 'HTTP Edge release upload did not return a response body.'
    }

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
    $curl = Get-Command curl -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $curl) {
        throw 'curl is required for HTTP Edge release verification.'
    }

    foreach ($relativeUrl in @($PublishResult.verificationUrls)) {
        $uri = "$downloadBase/$(([string]$relativeUrl).TrimStart('/'))"
        & $curl.Source --fail --silent --show-error --head "$uri" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "HTTP verification failed: $uri"
        }
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

Push-Location $repoRoot
try {
    $selectedTransport = Resolve-Transport
    if ($selectedTransport -eq 'http') {
        Assert-HttpPublishConfiguration
        $previousRelease = Try-GetLatestCloudStableRelease
    }
    else {
        $previousRelease = Try-GetLatestRemoteStableRelease
    }
    $previousVersion = if ($null -ne $previousRelease) { [string]$previousRelease.Version } else { '' }
    $previousSourceCommit = if ($null -ne $previousRelease) { [string]$previousRelease.SourceCommit } else { '' }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = Get-NextPatchVersion -CurrentVersion $previousVersion
        Write-Host "Auto-generated Edge version: $Version"
    }

    if ($Version -match '[\\/]') {
        throw 'Version must not contain path separators.'
    }

    $sourceCommit = Invoke-GitText -Arguments @('rev-parse', 'HEAD')
    if ([string]::IsNullOrWhiteSpace($sourceCommit)) {
        $sourceCommit = 'unknown'
    }
    else {
        $sourceCommit = $sourceCommit.Trim()
    }

    $releaseNotes = Resolve-ExplicitReleaseNotes

    $releaseRoot = Join-Path $repoRoot "publish/local-edge-release/$Channel/$Version"
    $runtimeRoot = Join-Path $releaseRoot 'edge-runtime'
    $velopackRoot = Join-Path $releaseRoot 'edge-velopack'
    $installerOutputRoot = Join-Path $releaseRoot 'edge-installer-artifacts'
    $installerArtifactRoot = Join-Path $installerOutputRoot "$Channel/$Version"
    $velopackSetupPath = Join-Path $velopackRoot "$PackId-$Channel-Setup.exe"
    $remoteTmp = "$EdgeUpdatesDir/.edge-local-publish-$Channel-$Version-$([Guid]::NewGuid().ToString('N'))"
    $installerTarget = "$EdgeUpdatesDir/installers/$Channel/$Version"
    $velopackTarget = "$EdgeUpdatesDir/velopack/$Channel"

    Write-Host "Publishing Edge local release: version=$Version channel=$Channel runtime=$RuntimeIdentifier"
    if (-not [string]::IsNullOrWhiteSpace($previousVersion)) {
        Write-Host "Previous Edge stable release: $previousVersion"
    }
    Write-Host "Source commit: $sourceCommit"
    if (Test-Path $releaseRoot) {
        Remove-Item -Path $releaseRoot -Recurse -Force
    }
    New-Item -Path $releaseRoot -ItemType Directory -Force | Out-Null

    $releaseNotesFile = Join-Path $releaseRoot 'release-notes.md'
    Set-Content -Path $releaseNotesFile -Encoding UTF8 -Value $releaseNotes

    Invoke-EdgeScript 'PublishEdgeRuntime.ps1' -Arguments @(
        '-Configuration', $Configuration,
        '-RuntimeIdentifier', $RuntimeIdentifier,
        '-Version', $Version,
        '-OutputRoot', $runtimeRoot,
        '-CleanOutput'
    )

    Invoke-EdgeScript 'PackEdgeClientVelopack.ps1' -Arguments @(
        '-Version', $Version,
        '-Channel', $Channel,
        '-Configuration', $Configuration,
        '-RuntimeIdentifier', $RuntimeIdentifier,
        '-OutputRoot', $velopackRoot,
        '-ReleaseNotes', $releaseNotesFile,
        '-CleanOutput',
        '-SkipVeloAppCheck', $SkipVeloAppCheck
    )

    if (-not $SkipVelopackValidation) {
        Invoke-EdgeScript 'TestEdgeVelopackPackage.ps1' -Arguments @(
            '-OutputRoot', $velopackRoot,
            '-Channel', $Channel,
            '-Version', $Version
        )
    }

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

    if (-not $SkipInstallerValidation) {
        Invoke-EdgeScript 'TestEdgeClientInstallerArtifact.ps1' -Arguments @(
            '-ArtifactRoot', $installerArtifactRoot,
            '-ExpectedChannel', $Channel,
            '-ExpectedVersion', $Version
        )
    }

    if (-not (Test-Path (Join-Path $installerArtifactRoot 'installer-artifact.json'))) {
        throw "Installer artifact manifest was not generated: $installerArtifactRoot"
    }

    if (-not (Test-Path (Join-Path $velopackRoot "releases.$Channel.json"))) {
        throw "Velopack releases manifest was not generated: $velopackRoot"
    }

    if ($selectedTransport -eq 'http') {
        $bundleZip = Join-Path $releaseRoot "edge-release-bundle-$Channel-$Version.zip"
        New-EdgeHttpReleaseBundle `
            -InstallerArtifactRoot $installerArtifactRoot `
            -VelopackRoot $velopackRoot `
            -OutputZip $bundleZip | Out-Null

        Write-Host "Publishing Edge release bundle over HTTP: $CloudApiBaseUrl (limit=${UploadRateLimitMbps}Mbps)"
        $publishResult = Invoke-EdgeHttpReleaseUpload -BundleZip $bundleZip
        Test-EdgeHttpReleaseUrls -PublishResult $publishResult
        Write-EdgePublishSummary -PublishResult $publishResult
        return
    }

    Write-Host "Publishing to ${remote}:$EdgeUpdatesDir with $selectedTransport"
    $prepareScript = @"
set -euo pipefail
tmp_root=$(ConvertTo-ShellSingleQuoted $remoteTmp)
rm -rf "`$tmp_root"
mkdir -p "`$tmp_root/installer" "`$tmp_root/velopack"
"@
    Invoke-RemoteBash $prepareScript

    if ($selectedTransport -eq 'rsync') {
        Publish-DirectoryWithRsync -SourceDirectory $installerArtifactRoot -RemoteDirectory "$remoteTmp/installer"
        Publish-DirectoryWithRsync -SourceDirectory $velopackRoot -RemoteDirectory "$remoteTmp/velopack"
    }
    else {
        Publish-DirectoryWithScp -SourceDirectory $installerArtifactRoot -RemoteDirectory "$remoteTmp/installer"
        Publish-DirectoryWithScp -SourceDirectory $velopackRoot -RemoteDirectory "$remoteTmp/velopack"
    }

    $finalizeScript = @"
set -euo pipefail
tmp_root=$(ConvertTo-ShellSingleQuoted $remoteTmp)
installer_target=$(ConvertTo-ShellSingleQuoted $installerTarget)
velopack_target=$(ConvertTo-ShellSingleQuoted $velopackTarget)
mkdir -p "`$(dirname "`$installer_target")" "`$velopack_target"
find $(ConvertTo-ShellSingleQuoted "$EdgeUpdatesDir/installers") -mindepth 1 -maxdepth 1 -type d ! -name stable -exec rm -rf {} + 2>/dev/null || true
find $(ConvertTo-ShellSingleQuoted "$EdgeUpdatesDir/velopack") -mindepth 1 -maxdepth 1 -type d ! -name stable -exec rm -rf {} + 2>/dev/null || true
test -f "`$tmp_root/installer/installer-artifact.json"
test -f "`$tmp_root/installer/IIoT.Edge.Setup.exe"
test -d "`$tmp_root/installer/launcher"
test -d "`$tmp_root/installer/host"
test -d "`$tmp_root/installer/plugins"
test -f "`$tmp_root/velopack/releases.$Channel.json"
test -f "`$tmp_root/velopack/assets.$Channel.json"
rm -rf "`$installer_target"
mv "`$tmp_root/installer" "`$installer_target"
cp -a "`$tmp_root/velopack/." "`$velopack_target/"
installer_channel_root="`$(dirname "`$installer_target")"
mapfile -t keep_versions < <(find "`$installer_channel_root" -mindepth 1 -maxdepth 1 -type d -printf '%f\n' 2>/dev/null | grep -E '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$' | sort -V | tail -n 3 || true)
for version_dir in "`$installer_channel_root"/*
do
  [ -d "`$version_dir" ] || continue
  version_name="`$(basename "`$version_dir")"
  keep=false
  for keep_version in "`${keep_versions[@]}"
  do
    if [ "`$version_name" = "`$keep_version" ]; then
      keep=true
      break
    fi
  done
  [ "`$keep" = "true" ] || rm -rf "`$version_dir"
done
for velopack_file in "`$velopack_target"/*
do
  [ -f "`$velopack_file" ] || continue
  file_name="`$(basename "`$velopack_file")"
  case "`$file_name" in
    releases.*.json|assets.*.json|RELEASES)
      continue
      ;;
  esac
  file_version=""
  if [[ "`$file_name" =~ -([0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?)-$Channel- ]]; then
    file_version="`${BASH_REMATCH[1]}"
  else
    file_version="`$(printf '%s\n' "`$file_name" | grep -Eo '[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?' | head -n 1 || true)"
  fi
  [ -n "`$file_version" ] || continue
  keep=false
  for keep_version in "`${keep_versions[@]}"
  do
    if [ "`$file_version" = "`$keep_version" ]; then
      keep=true
      break
    fi
  done
  [ "`$keep" = "true" ] || rm -f "`$velopack_file"
done
rm -rf "`$tmp_root"
test -f "`$installer_target/installer-artifact.json"
test -f "`$installer_target/IIoT.Edge.Setup.exe"
test -f "`$velopack_target/releases.$Channel.json"
test -f "`$velopack_target/assets.$Channel.json"
echo "Published Edge installer artifact to `$installer_target"
echo "Published Edge Velopack releases to `$velopack_target"
"@
    Invoke-RemoteBash $finalizeScript
}
finally {
    Pop-Location
}
