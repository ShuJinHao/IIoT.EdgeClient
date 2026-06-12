param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [string]$ExpectedModuleId = 'Homogenization',

    [string]$ExpectedHostDirectory = 'host',

    [string]$ExpectedPluginsRoot = 'plugins',

    [string]$InstallRoot,

    [int]$WaitSeconds = 45,

    [switch]$SkipLauncherProcessCheck,

    [switch]$CleanInstallRoot,

    [switch]$ConfirmCleanInstallRoot
)

$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'This acceptance script must be run on Windows because it executes the Edge installer .exe.'
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Join-Path $env:LOCALAPPDATA 'IIoTEdge'
}

function Resolve-AcceptancePath {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    return [System.IO.Path]::GetFullPath($PathValue)
}

function Wait-Until {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Condition,
        [Parameter(Mandatory = $true)][string]$TimeoutMessage,
        [int]$TimeoutSeconds = 45
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (& $Condition) {
            return
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    throw $TimeoutMessage
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$PathValue)
    return Get-Content -Raw -Encoding UTF8 -Path $PathValue | ConvertFrom-Json
}

function Assert-InstalledLayoutShape {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$HostDirectory,
        [Parameter(Mandatory = $true)][string]$PluginsRoot,
        [Parameter(Mandatory = $true)][string]$ModuleId
    )

    $launcherExe = Join-Path $Root 'launcher/IIoT.Edge.Launcher.exe'
    $hostRoot = Join-Path $Root $HostDirectory
    $hostExe = Join-Path $hostRoot 'IIoT.Edge.Shell.exe'
    $pluginJson = Join-Path $Root "$PluginsRoot/$ModuleId/plugin.json"

    foreach ($path in @($launcherExe, $hostRoot, $hostExe, $pluginJson)) {
        if (-not (Test-Path $path)) {
            throw "Expected installed path was not found: $path"
        }
    }

    $legacyModulesRoot = Join-Path $hostRoot 'Modules'
    if (Test-Path $legacyModulesRoot) {
        throw "Host directory must stay clean and must not contain legacy Modules: $legacyModulesRoot"
    }

    $allowedTopLevel = @(
        'launcher',
        'data',
        $HostDirectory,
        $PluginsRoot
    )
    $unexpected = Get-ChildItem -LiteralPath $Root -Directory -Force |
        Where-Object { $allowedTopLevel -notcontains $_.Name } |
        Select-Object -ExpandProperty Name

    if ($unexpected.Count -gt 0) {
        throw "Unexpected top-level directories were installed: $($unexpected -join ', ')"
    }
}

function Assert-BindingApplied {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$ModuleId
    )

    $rawBindingPath = Join-Path $Root 'launcher/iiot-binding.json'
    $appliedRoot = Join-Path $Root 'data/IIoT/EdgeClient/launcher'
    $profilesRoot = Join-Path $Root 'data/IIoT/EdgeClient/profiles'

    Wait-Until `
        -TimeoutSeconds $WaitSeconds `
        -TimeoutMessage "Timed out waiting for binding import summary under: $appliedRoot" `
        -Condition {
            Test-Path (Join-Path $appliedRoot 'iiot-binding.applied.*.json')
        }

    if (Test-Path $rawBindingPath) {
        throw "Raw iiot-binding.json still exists after launcher start: $rawBindingPath"
    }

    $latestSummary = Get-ChildItem -LiteralPath $appliedRoot -Filter 'iiot-binding.applied.*.json' |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $latestSummary) {
        throw "Binding summary was not found under: $appliedRoot"
    }

    $summary = Read-JsonFile -PathValue $latestSummary.FullName
    $bindings = @($summary.bindings)
    $matchingBinding = @($bindings | Where-Object { $_.moduleId -eq $ModuleId }) | Select-Object -First 1
    if ($null -eq $matchingBinding) {
        throw "Binding summary does not contain module '$ModuleId': $($latestSummary.FullName)"
    }
    if ([string]::IsNullOrWhiteSpace([string]$matchingBinding.clientCode)) {
        throw "Binding summary has empty clientCode: $($latestSummary.FullName)"
    }
    if ($null -ne $matchingBinding.PSObject.Properties['bootstrapSecret']) {
        throw "Binding summary must not persist bootstrapSecret: $($latestSummary.FullName)"
    }

    $machineConfigs = @(Get-ChildItem -LiteralPath $profilesRoot -Recurse -Filter 'appsettings.machine.*.json' -ErrorAction SilentlyContinue)
    if ($machineConfigs.Count -eq 0) {
        throw "No external machine profile config was written under: $profilesRoot"
    }

    $identityConfig = $null
    foreach ($configFile in $machineConfigs) {
        try {
            $config = Read-JsonFile -PathValue $configFile.FullName
        }
        catch {
            continue
        }

        $clientCode = [string]$config.CloudApi.ClientCode
        $bootstrapSecret = [string]$config.CloudApi.BootstrapSecret
        $enabledModules = @($config.Modules.Enabled)
        if (
            (-not [string]::IsNullOrWhiteSpace($clientCode)) -and
            (-not [string]::IsNullOrWhiteSpace($bootstrapSecret)) -and
            ($enabledModules -contains $ModuleId)
        ) {
            $identityConfig = $configFile
            break
        }
    }

    if ($null -eq $identityConfig) {
        throw "No machine profile config contains CloudApi ClientCode/BootstrapSecret and enabled module '$ModuleId'."
    }

    Write-Host "Binding import summary: $($latestSummary.FullName)"
    Write-Host "Machine identity config: $($identityConfig.FullName)"
}

function Assert-LauncherProcessStarted {
    Wait-Until `
        -TimeoutSeconds $WaitSeconds `
        -TimeoutMessage 'Timed out waiting for IIoT.Edge.Launcher process to start.' `
        -Condition {
            $null -ne (Get-Process -Name 'IIoT.Edge.Launcher' -ErrorAction SilentlyContinue | Select-Object -First 1)
        }
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$resolvedInstallerPath = Resolve-AcceptancePath -PathValue $InstallerPath
$resolvedInstallRoot = Resolve-AcceptancePath -PathValue $InstallRoot

if ($CleanInstallRoot) {
    if (-not $ConfirmCleanInstallRoot) {
        throw 'CleanInstallRoot deletes the existing install directory. Re-run with -ConfirmCleanInstallRoot after confirming this is safe.'
    }
    if (Test-Path $resolvedInstallRoot) {
        Remove-Item -LiteralPath $resolvedInstallRoot -Recurse -Force
    }
}

& (Join-Path $scriptRoot 'TestEdgeDownloadedInstallerPackage.ps1') `
    -InstallerPath $resolvedInstallerPath `
    -ExpectedModuleId $ExpectedModuleId `
    -ExpectedHostDirectory $ExpectedHostDirectory `
    -ExpectedPluginsRoot $ExpectedPluginsRoot

$process = Start-Process `
    -FilePath $resolvedInstallerPath `
    -WorkingDirectory (Split-Path -Parent $resolvedInstallerPath) `
    -PassThru `
    -Wait

if ($process.ExitCode -ne 0) {
    throw "Installer exited with code $($process.ExitCode)."
}

Wait-Until `
    -TimeoutSeconds $WaitSeconds `
    -TimeoutMessage "Timed out waiting for install root: $resolvedInstallRoot" `
    -Condition { Test-Path $resolvedInstallRoot }

Assert-InstalledLayoutShape `
    -Root $resolvedInstallRoot `
    -HostDirectory $ExpectedHostDirectory `
    -PluginsRoot $ExpectedPluginsRoot `
    -ModuleId $ExpectedModuleId

if (-not $SkipLauncherProcessCheck) {
    Assert-LauncherProcessStarted
}

Assert-BindingApplied `
    -Root $resolvedInstallRoot `
    -ModuleId $ExpectedModuleId

Write-Host "Edge installer Windows acceptance passed: $resolvedInstallerPath"
Write-Host "InstallRoot=$resolvedInstallRoot"
