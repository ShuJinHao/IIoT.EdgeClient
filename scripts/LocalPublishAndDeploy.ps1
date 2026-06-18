param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [ValidateSet('stable')]
    [string]$Channel = 'stable',

    [string]$Configuration = 'Release',

    [string]$RuntimeIdentifier = 'win-x64',

    [string]$PackId = 'IIoT.EdgeClient.Homogenization',

    [string]$DeployHost = '10.98.90.154',

    [string]$DeployUser = 'root',

    [int]$DeployPort = 22,

    [string]$EdgeUpdatesDir = '/srv/iiot/edge-updates',

    [ValidateSet('auto', 'rsync', 'scp')]
    [string]$Transport = 'auto',

    [bool]$SkipVeloAppCheck = $true,

    [switch]$SkipVelopackValidation,

    [switch]$SkipInstallerValidation
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $repoRoot "publish/local-edge-release/$Channel/$Version"
$runtimeRoot = Join-Path $releaseRoot 'edge-runtime'
$velopackRoot = Join-Path $releaseRoot 'edge-velopack'
$installerOutputRoot = Join-Path $releaseRoot 'edge-installer-artifacts'
$installerArtifactRoot = Join-Path $installerOutputRoot "$Channel/$Version"
$velopackSetupPath = Join-Path $velopackRoot "$PackId-$Channel-Setup.exe"
$remote = "$DeployUser@$DeployHost"
$remoteTmp = "$EdgeUpdatesDir/.edge-local-publish-$Channel-$Version-$([Guid]::NewGuid().ToString('N'))"
$installerTarget = "$EdgeUpdatesDir/installers/$Channel/$Version"
$velopackTarget = "$EdgeUpdatesDir/velopack/$Channel"

if ($Version -match '[\\/]' -or $Channel -match '[\\/]') {
    throw 'Version and Channel must not contain path separators.'
}

if ($Channel -ne 'stable') {
    throw 'Production Edge releases must use stable channel.'
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

    if (Get-Command rsync -ErrorAction SilentlyContinue) {
        return 'rsync'
    }

    if (Get-Command scp -ErrorAction SilentlyContinue) {
        return 'scp'
    }

    throw 'Neither rsync nor scp was found. Install one of them, or pass -Transport explicitly.'
}

function Invoke-RemoteBash {
    param([Parameter(Mandatory = $true)][string]$Script)

    $sshArgs = @('-p', [string]$DeployPort, $remote, 'bash -s')
    $Script | & ssh @sshArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Remote bash command failed with exit code $LASTEXITCODE."
    }
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

Push-Location $repoRoot
try {
    Write-Host "Publishing Edge local release: version=$Version channel=$Channel runtime=$RuntimeIdentifier"
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

    $selectedTransport = Resolve-Transport
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
