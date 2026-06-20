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

    & $scriptPath @Arguments
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
    if ($Transport -ne 'auto') {
        return $Transport
    }

    if (-not [string]::IsNullOrWhiteSpace($CloudApiBaseUrl) -and -not [string]::IsNullOrWhiteSpace($CloudToken)) {
        return 'http'
    }

    if (Get-Command rsync -ErrorAction SilentlyContinue) {
        return 'rsync'
    }

    if (Get-Command scp -ErrorAction SilentlyContinue) {
        return 'scp'
    }

    throw 'Neither rsync nor scp was found. Install one of them, or pass -Transport explicitly.'
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

function New-ReleaseNotes {
    param(
        [string]$PreviousSourceCommit,
        [string]$CurrentSourceCommit
    )

    if ((Test-GitCommitExists $PreviousSourceCommit) -and (Test-GitCommitExists $CurrentSourceCommit)) {
        $range = "$PreviousSourceCommit..$CurrentSourceCommit"
        $notes = Invoke-GitText -Arguments @('log', '--oneline', '--no-decorate', $range)
        if (-not [string]::IsNullOrWhiteSpace($notes)) {
            return $notes.Trim()
        }
    }

    $recent = Invoke-GitText -Arguments @('log', '--oneline', '--no-decorate', '-n', '20')
    if (-not [string]::IsNullOrWhiteSpace($recent)) {
        return $recent.Trim()
    }

    return "Edge local release $Version"
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

    $releaseNotes = New-ReleaseNotes -PreviousSourceCommit $previousSourceCommit -CurrentSourceCommit $sourceCommit

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

    Invoke-EdgeScript 'PublishEdgeRuntime.ps1' @(
        '-Configuration', $Configuration,
        '-RuntimeIdentifier', $RuntimeIdentifier,
        '-Version', $Version,
        '-OutputRoot', $runtimeRoot,
        '-CleanOutput'
    )

    Invoke-EdgeScript 'PackEdgeClientVelopack.ps1' @(
        '-Version', $Version,
        '-Channel', $Channel,
        '-Configuration', $Configuration,
        '-RuntimeIdentifier', $RuntimeIdentifier,
        '-OutputRoot', $velopackRoot,
        '-CleanOutput',
        '-SkipVeloAppCheck', $SkipVeloAppCheck
    )

    if (-not $SkipVelopackValidation) {
        Invoke-EdgeScript 'TestEdgeVelopackPackage.ps1' @(
            '-OutputRoot', $velopackRoot,
            '-Channel', $Channel,
            '-Version', $Version
        )
    }

    Invoke-EdgeScript 'PublishEdgeClientInstallerArtifact.ps1' @(
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
        Invoke-EdgeScript 'TestEdgeClientInstallerArtifact.ps1' @(
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
