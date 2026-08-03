param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [string]$ExpectedModuleId = 'CP',

    [string]$ExpectedUpdateSource,

    [string]$ExpectedChannel,

    [string]$ExpectedTargetRuntime,

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

    $currentRoot = Join-Path $Root 'current'
    $isVelopackInstall = Test-Path (Join-Path $currentRoot 'IIoT.Edge.Launcher.exe')
    $appContentRoot = if ($isVelopackInstall) { $currentRoot } else { $Root }
    $launcherExe = if ($isVelopackInstall) {
        Join-Path $appContentRoot 'IIoT.Edge.Launcher.exe'
    } else {
        Join-Path $appContentRoot 'launcher/IIoT.Edge.Launcher.exe'
    }
    $hostRoot = Join-Path $appContentRoot $HostDirectory
    $hostExe = Join-Path $hostRoot 'IIoT.Edge.Shell.exe'
    $pluginRoot = if ($isVelopackInstall) { $Root } else { $appContentRoot }
    $pluginJson = Join-Path $pluginRoot "$PluginsRoot/$ModuleId/plugin.json"

    foreach ($path in @($launcherExe, $hostRoot, $hostExe, $pluginJson)) {
        if (-not (Test-Path $path)) {
            throw "Expected installed path was not found: $path"
        }
    }

    $legacyModulesRoot = Join-Path $hostRoot 'Modules'
    if (Test-Path $legacyModulesRoot) {
        throw "Host directory must stay clean and must not contain legacy Modules: $legacyModulesRoot"
    }

    if ($isVelopackInstall) {
        $currentData = Join-Path $currentRoot 'data'
        if (Test-Path $currentData) {
            throw "Velopack-managed current directory must not contain mutable data: $currentData"
        }

        $currentPlugins = Join-Path $currentRoot $PluginsRoot
        if (Test-Path $currentPlugins) {
            throw "Velopack-managed current directory must not contain mutable plugins: $currentPlugins"
        }

        foreach ($forbiddenCurrentFile in @('iiot-binding.json', 'iiot-enabled-plugins.json', 'launcher.update.json')) {
            $forbiddenPath = Join-Path $currentRoot $forbiddenCurrentFile
            if (Test-Path $forbiddenPath) {
                throw "Velopack-managed current directory must not contain bootstrap binding files: $forbiddenPath"
            }
        }
    }
    else {
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

    return $appContentRoot
}

function Assert-BindingApplied {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$AppContentRoot,
        [Parameter(Mandatory = $true)][string]$ModuleId
    )

    $appliedRoot = Join-Path $Root 'data/IIoT/EdgeClient/launcher'
    $rawBindingPath = if ((Split-Path -Leaf $AppContentRoot) -eq 'current') {
        Join-Path $appliedRoot 'iiot-binding.json'
    } else {
        Join-Path $Root 'launcher/iiot-binding.json'
    }
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
        $paths = $config.CloudApi.Paths
        $hasRequiredPaths = $null -ne $paths -and
            [string]$paths.DeviceInstance -ceq '/api/v1/bootstrap/device-instance' -and
            [string]$paths.ClientReleaseCatalogTemplate -ceq '/api/v1/edge/client-releases/device/{deviceId}/catalog' -and
            [string]$paths.ClientVersionReport -ceq '/api/v1/edge/client-releases/version-reports' -and
            [string]$paths.RuntimeHeartbeat -ceq '/api/v1/edge/runtime-heartbeats'
        if (
            (-not [string]::IsNullOrWhiteSpace($clientCode)) -and
            (-not [string]::IsNullOrWhiteSpace($bootstrapSecret)) -and
            $hasRequiredPaths -and
            ($enabledModules -contains $ModuleId)
        ) {
            $identityConfig = $configFile
            break
        }
    }

    if ($null -eq $identityConfig) {
        throw "No machine profile config contains CloudApi ClientCode/BootstrapSecret, all four Cloud paths, and enabled module '$ModuleId'."
    }

    Write-Host "Binding import summary: $($latestSummary.FullName)"
    Write-Host "Machine identity config: $($identityConfig.FullName)"
}

function Assert-InstalledUpdateConfig {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$AppContentRoot,
        [string]$ExpectedSource,
        [string]$ExpectedChannel,
        [string]$ExpectedTargetRuntime
    )

    $launcherRoot = Join-Path $Root 'data/IIoT/EdgeClient/launcher'
    $updateConfigPath = Join-Path $launcherRoot 'launcher.update.json'

    if (-not (Test-Path $updateConfigPath)) {
        throw "Installed launcher.update.json was not found in the standard launcher data directory: $updateConfigPath"
    }

    if ((Split-Path -Leaf $AppContentRoot) -eq 'current') {
        foreach ($forbiddenPath in @(
            (Join-Path $AppContentRoot 'launcher.update.json'),
            (Join-Path $AppContentRoot 'launcher/launcher.update.json'),
            (Join-Path $Root 'launcher/launcher.update.json')
        )) {
            if (Test-Path $forbiddenPath) {
                throw "launcher.update.json must not remain under a program directory after Velopack install: $forbiddenPath"
            }
        }
    }

    $updateConfig = Read-JsonFile -PathValue $updateConfigPath
    $propertyNames = @($updateConfig.PSObject.Properties.Name)
    foreach ($propertyName in @('source', 'channel', 'targetRuntime')) {
        if (-not ($propertyNames -ccontains $propertyName)) {
            throw "Installed launcher.update.json must contain camelCase property '$propertyName': $updateConfigPath"
        }

        $value = [string]$updateConfig.$propertyName
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "Installed launcher.update.json property '$propertyName' must not be empty: $updateConfigPath"
        }
    }

    foreach ($legacyName in @('Source', 'Channel', 'TargetRuntime')) {
        if ($propertyNames -ccontains $legacyName) {
            throw "Installed launcher.update.json must use camelCase, but found legacy property '$legacyName': $updateConfigPath"
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedSource) -and $updateConfig.source -ne $ExpectedSource) {
        throw "Installed launcher.update.json source '$($updateConfig.source)' does not match expected '$ExpectedSource'."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedChannel) -and $updateConfig.channel -ne $ExpectedChannel) {
        throw "Installed launcher.update.json channel '$($updateConfig.channel)' does not match expected '$ExpectedChannel'."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedTargetRuntime) -and $updateConfig.targetRuntime -ne $ExpectedTargetRuntime) {
        throw "Installed launcher.update.json targetRuntime '$($updateConfig.targetRuntime)' does not match expected '$ExpectedTargetRuntime'."
    }

    Write-Host "Launcher update config: $updateConfigPath"
}

function Assert-LauncherProcessStarted {
    Wait-Until `
        -TimeoutSeconds $WaitSeconds `
        -TimeoutMessage 'Timed out waiting for IIoT.Edge.Launcher process to start.' `
        -Condition {
            $null -ne (Get-Process -Name 'IIoT.Edge.Launcher' -ErrorAction SilentlyContinue | Select-Object -First 1)
        }
}

function Assert-StartMenuShortcut {
    param([Parameter(Mandatory = $true)][string]$ExpectedTargetPath)

    $shortcutPath = Get-StartMenuShortcutPath
    if (-not (Test-Path $shortcutPath)) {
        throw "Start Menu shortcut was not created: $shortcutPath"
    }

    $targetPath = Get-WindowsShortcutTargetPath -ShortcutPath $shortcutPath
    if ($targetPath -ne $ExpectedTargetPath) {
        throw "Start Menu shortcut target '$targetPath' does not match installed launcher '$ExpectedTargetPath'."
    }
}

function Get-StartMenuShortcutPath {
    $programsPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    if ([string]::IsNullOrWhiteSpace($programsPath)) {
        throw 'Could not resolve current user Start Menu Programs folder.'
    }

    return (Join-Path $programsPath 'IIoT Edge/IIoT Edge Client.lnk')
}

function Get-DesktopShortcutPath {
    $desktopPath = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    if ([string]::IsNullOrWhiteSpace($desktopPath)) {
        throw 'Could not resolve current user Desktop folder.'
    }

    return (Join-Path $desktopPath 'IIoT Edge Client.lnk')
}

function Test-DesktopShortcut {
    $shortcutPath = Get-DesktopShortcutPath
    return (Test-Path $shortcutPath)
}

function Assert-NoNewDesktopShortcut {
    param([bool]$ExistingBeforeInstall)

    if ($ExistingBeforeInstall) {
        Write-Host 'Desktop shortcut already existed before install; skipping no-new-desktop-shortcut assertion.'
        return
    }

    $shortcutPath = Get-DesktopShortcutPath
    if (Test-Path $shortcutPath) {
        throw "Silent installer must not create a desktop shortcut by default: $shortcutPath"
    }
}

function Get-WindowsShortcutTargetPath {
    param([Parameter(Mandatory = $true)][string]$ShortcutPath)

    $shell = $null
    $shortcut = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($ShortcutPath)
        return [string]$shortcut.TargetPath
    }
    finally {
        if ($null -ne $shortcut) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        }

        if ($null -ne $shell) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
    }
}

function Resolve-InstalledLauncherPath {
    param([Parameter(Mandatory = $true)][string]$AppContentRoot)

    if ((Split-Path -Leaf $AppContentRoot) -eq 'current') {
        return (Join-Path $AppContentRoot 'IIoT.Edge.Launcher.exe')
    }

    return (Join-Path $AppContentRoot 'launcher/IIoT.Edge.Launcher.exe')
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
    -ExpectedUpdateSource $ExpectedUpdateSource `
    -ExpectedChannel $ExpectedChannel `
    -ExpectedTargetRuntime $ExpectedTargetRuntime `
    -ExpectedHostDirectory $ExpectedHostDirectory `
    -ExpectedPluginsRoot $ExpectedPluginsRoot

$desktopShortcutExistedBeforeInstall = Test-DesktopShortcut
$installerArguments = "--silent --installto `"$resolvedInstallRoot`""

$process = Start-Process `
    -FilePath $resolvedInstallerPath `
    -ArgumentList $installerArguments `
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

$appContentRoot = Assert-InstalledLayoutShape `
    -Root $resolvedInstallRoot `
    -HostDirectory $ExpectedHostDirectory `
    -PluginsRoot $ExpectedPluginsRoot `
    -ModuleId $ExpectedModuleId

$installedLauncherPath = Resolve-InstalledLauncherPath -AppContentRoot $appContentRoot
if (-not $SkipLauncherProcessCheck) {
    Assert-LauncherProcessStarted
}

Assert-StartMenuShortcut -ExpectedTargetPath $installedLauncherPath
Assert-NoNewDesktopShortcut -ExistingBeforeInstall $desktopShortcutExistedBeforeInstall

Assert-BindingApplied `
    -Root $resolvedInstallRoot `
    -AppContentRoot $appContentRoot `
    -ModuleId $ExpectedModuleId

Assert-InstalledUpdateConfig `
    -Root $resolvedInstallRoot `
    -AppContentRoot $appContentRoot `
    -ExpectedSource $ExpectedUpdateSource `
    -ExpectedChannel $ExpectedChannel `
    -ExpectedTargetRuntime $ExpectedTargetRuntime

Write-Host "Edge installer Windows acceptance passed: $resolvedInstallerPath"
Write-Host "InstallRoot=$resolvedInstallRoot"
