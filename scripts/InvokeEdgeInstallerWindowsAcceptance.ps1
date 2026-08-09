param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath,

    [Parameter(Mandatory = $true)]
    [string[]]$ExpectedClientCode,

    [hashtable]$ExpectedModuleIds = @{},

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

function Get-NormalizedExpectedClientCodes {
    param([Parameter(Mandatory = $true)][string[]]$ClientCodes)

    $values = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($clientCode in $ClientCodes) {
        $normalized = ([string]$clientCode).Trim().ToUpperInvariant()
        if ([string]::IsNullOrWhiteSpace($normalized) -or -not $values.Add($normalized)) {
            throw 'ExpectedClientCode must contain unique, non-empty device plugin identities.'
        }
    }
    if ($values.Count -eq 0) {
        throw 'ExpectedClientCode must contain at least one device plugin identity.'
    }

    return ,@($values | Sort-Object)
}

function Get-ExpectedModuleId {
    param(
        [Parameter(Mandatory = $true)][string]$ClientCode,
        [Parameter(Mandatory = $true)][hashtable]$ExpectedModules,
        [Parameter(Mandatory = $true)][int]$ExpectedCount
    )

    if ($ExpectedModules.Count -eq 0) {
        return $null
    }
    if ($ExpectedModules.Count -ne $ExpectedCount) {
        throw 'ExpectedModuleIds must contain exactly one entry for every ExpectedClientCode.'
    }
    $keys = @($ExpectedModules.Keys | Where-Object {
            [string]::Equals([string]$_, $ClientCode, [StringComparison]::OrdinalIgnoreCase)
        })
    if ($keys.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$ExpectedModules[$keys[0]])) {
        throw "ExpectedModuleIds does not contain exactly one module for ClientCode '$ClientCode'."
    }
    return ([string]$ExpectedModules[$keys[0]]).Trim()
}

function Assert-InstalledLayoutShape {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$HostDirectory,
        [Parameter(Mandatory = $true)][string]$PluginsRoot,
        [Parameter(Mandatory = $true)][string[]]$ClientCodes
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

    foreach ($path in @($launcherExe, $hostRoot, $hostExe)) {
        if (-not (Test-Path $path)) {
            throw "Expected installed path was not found: $path"
        }
    }
    foreach ($clientCode in $ClientCodes) {
        foreach ($relativePath in @(
            "$PluginsRoot/$clientCode/app/plugin.json",
            "$PluginsRoot/$clientCode/config",
            "$PluginsRoot/$clientCode/db",
            "$PluginsRoot/$clientCode/logs",
            "$PluginsRoot/$clientCode/cache",
            "$PluginsRoot/$clientCode/context",
            "$PluginsRoot/$clientCode/buffers",
            "$PluginsRoot/$clientCode/data"
        )) {
            $path = Join-Path $pluginRoot $relativePath
            if (-not (Test-Path $path)) {
                throw "Expected ClientCode-isolated installed path was not found: $path"
            }
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
        [Parameter(Mandatory = $true)][string[]]$ClientCodes,
        [Parameter(Mandatory = $true)][hashtable]$ExpectedModules
    )

    $appliedRoot = Join-Path $Root 'data/IIoT/EdgeClient/launcher'
    $runtimeBindingPath = Join-Path $appliedRoot 'iiot-binding.runtime.json'
    $hostDatabasePath = Join-Path $Root 'data/IIoT/EdgeClient/host/host.db'

    Wait-Until `
        -TimeoutSeconds $WaitSeconds `
        -TimeoutMessage "Timed out waiting for runtime Binding and host.db under: $Root" `
        -Condition {
            (Test-Path $runtimeBindingPath) -and (Test-Path $hostDatabasePath)
        }

    foreach ($rawBindingPath in @(
        (Join-Path $appliedRoot 'iiot-binding.json'),
        (Join-Path $Root 'launcher/iiot-binding.json'),
        (Join-Path $AppContentRoot 'iiot-binding.json'),
        (Join-Path $AppContentRoot 'launcher/iiot-binding.json')
    )) {
        if (Test-Path $rawBindingPath) {
            throw "Raw iiot-binding.json still exists after install: $rawBindingPath"
        }
    }

    $runtimeJson = Get-Content -Raw -Encoding UTF8 -LiteralPath $runtimeBindingPath
    if ($runtimeJson -match '(?i)"(pendingCredentialSecret|bootstrapSecret|refreshToken|accessToken)"\s*:\s*"(?!\s*")') {
        throw "Runtime Binding contains a plaintext credential: $runtimeBindingPath"
    }
    $runtime = $runtimeJson | ConvertFrom-Json
    if ([int]$runtime.schemaVersion -ne 3) {
        throw "Runtime Binding must use schemaVersion 3: $runtimeBindingPath"
    }
    $expectedRuntimePaths = [ordered]@{
        deviceInstance = '/api/v1/edge/bootstrap/device-instance'
        bootstrapRefresh = '/api/v1/edge/bootstrap/edge-refresh'
        activateDevice = '/api/v1/edge/bootstrap/device-activate'
        activateDeviceConfirm = '/api/v1/edge/bootstrap/device-activation-confirm'
        identityDeviceLogin = '/api/v1/human/identity/edge-login'
        humanIdentityRefresh = '/api/v1/human/identity/refresh'
        humanSessionValidation = '/api/v1/human/identity/session'
        deviceLog = '/api/v1/edge/device-logs'
        passStationBatchTemplate = '/api/v1/edge/pass-stations/{typeKey}/batch'
        capacityHourly = '/api/v1/edge/capacity/hourly'
        capacitySummary = '/api/v1/edge/capacity/summary'
        capacitySummaryRange = '/api/v1/edge/capacity/summary/range'
        recipeByDeviceTemplate = '/api/v1/edge/recipes/device/{deviceId}'
        clientReleaseCatalogTemplate = '/api/v1/edge/client-releases/device/{deviceId}/catalog'
        clientVersionReport = '/api/v1/edge/client-releases/version-reports'
        runtimeHeartbeat = '/api/v1/edge/runtime-heartbeats'
        edgeHostPlcRuntimeStates = '/api/v1/edge/edge-hosts/plc-runtime-states'
    }
    if (@($runtime.paths.PSObject.Properties).Count -ne $expectedRuntimePaths.Count) {
        throw 'Runtime Binding must contain exactly 17 routes.'
    }
    foreach ($routeName in $expectedRuntimePaths.Keys) {
        if ([string]$runtime.paths.$routeName -cne [string]$expectedRuntimePaths[$routeName]) {
            throw "Runtime Binding route '$routeName' is invalid."
        }
    }
    $credentialOwnerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $bindings = @($runtime.bindings)
    if ($bindings.Count -ne $ClientCodes.Count) {
        throw "Runtime Binding ClientCode count '$($bindings.Count)' does not match expected '$($ClientCodes.Count)'."
    }
    foreach ($clientCode in $ClientCodes) {
        $matchingBindings = @($bindings | Where-Object {
                [string]::Equals([string]$_.clientCode, $clientCode, [StringComparison]::OrdinalIgnoreCase)
            })
        if ($matchingBindings.Count -ne 1) {
            throw "Runtime Binding must contain exactly one entry for ClientCode '$clientCode'."
        }
        $binding = $matchingBindings[0]
        $expectedModuleId = Get-ExpectedModuleId `
            -ClientCode $clientCode `
            -ExpectedModules $ExpectedModules `
            -ExpectedCount $ClientCodes.Count
        if ($null -ne $expectedModuleId -and [string]$binding.moduleId -cne $expectedModuleId) {
            throw "Runtime Binding moduleId for '$clientCode' does not match ExpectedModuleIds."
        }
        if ([string]::IsNullOrWhiteSpace([string]$binding.moduleId) -or
            [string]::IsNullOrWhiteSpace([string]$binding.pluginVersion) -or
            [string]$binding.packageSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
            -not ([string]$binding.pendingCredentialReference).StartsWith('IIoT.Edge/Pending/', [StringComparison]::Ordinal) -or
            [string]$binding.credentialOwnerSid -cne $credentialOwnerSid) {
            throw "Runtime Binding facts are incomplete for ClientCode '$clientCode'."
        }
        if ([string]$binding.activationStatus -notin @('Pending', 'Activating', 'Activated', 'Expired', 'Failed')) {
            throw "Runtime Binding activationStatus is invalid for ClientCode '$clientCode'."
        }

        $machineConfigPath = Join-Path $Root "plugins/$clientCode/config/appsettings.machine.$clientCode.json"
        if (-not (Test-Path $machineConfigPath)) {
            throw "Device plugin machine configuration was not found: $machineConfigPath"
        }
        $machineConfigJson = Get-Content -Raw -Encoding UTF8 -LiteralPath $machineConfigPath
        if ($machineConfigJson -match '(?i)"(bootstrapSecret|pendingCredentialSecret|refreshToken|accessToken)"\s*:\s*"(?!\s*")') {
            throw "Machine configuration contains a plaintext credential: $machineConfigPath"
        }
        $config = $machineConfigJson | ConvertFrom-Json
        if ([string]$config.CloudApi.ClientCode -cne $clientCode -or
            [string]$config.CloudApi.BootstrapCredentialReference -cne [string]$binding.pendingCredentialReference) {
            throw "Machine configuration identity does not match Runtime Binding for '$clientCode'."
        }
        $enabledModules = @($config.Modules.Enabled)
        $moduleId = if ($null -ne $expectedModuleId) { $expectedModuleId } else { [string]$binding.moduleId }
        $paths = $config.CloudApi.Paths
        $hasRequiredPaths = $null -ne $paths -and @($paths.PSObject.Properties).Count -eq 17
        foreach ($routeName in $expectedRuntimePaths.Keys) {
            $machineKey = $routeName.Substring(0, 1).ToUpperInvariant() + $routeName.Substring(1)
            if ([string]$paths.$machineKey -cne [string]$expectedRuntimePaths[$routeName]) {
                $hasRequiredPaths = $false
            }
        }
        if (-not $hasRequiredPaths -or $enabledModules.Count -ne 1 -or $enabledModules[0] -cne $moduleId) {
            throw "Machine configuration routes or enabled module are invalid for '$clientCode'."
        }
        if ([string]$config.DevicePluginBinding.ClientCode -cne $clientCode -or
            [string]$config.DevicePluginBinding.ModuleId -cne [string]$binding.moduleId -or
            [string]$config.DevicePluginBinding.PluginVersion -cne [string]$binding.pluginVersion -or
            [string]$config.DevicePluginBinding.PackageSha256 -cne [string]$binding.packageSha256) {
            throw "Machine configuration binding facts do not match Runtime Binding for '$clientCode'."
        }
        Write-Host "Machine identity config: $machineConfigPath"
    }

    Write-Host "Runtime Binding: $runtimeBindingPath"
    Write-Host "Host database: $hostDatabasePath"
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
$expectedClientCodes = Get-NormalizedExpectedClientCodes -ClientCodes $ExpectedClientCode

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
    -ExpectedClientCode $expectedClientCodes `
    -ExpectedModuleIds $ExpectedModuleIds `
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
    -ClientCodes $expectedClientCodes

$installedLauncherPath = Resolve-InstalledLauncherPath -AppContentRoot $appContentRoot
if (-not $SkipLauncherProcessCheck) {
    Assert-LauncherProcessStarted
}

Assert-StartMenuShortcut -ExpectedTargetPath $installedLauncherPath
Assert-NoNewDesktopShortcut -ExistingBeforeInstall $desktopShortcutExistedBeforeInstall

Assert-BindingApplied `
    -Root $resolvedInstallRoot `
    -AppContentRoot $appContentRoot `
    -ClientCodes $expectedClientCodes `
    -ExpectedModules $ExpectedModuleIds

Assert-InstalledUpdateConfig `
    -Root $resolvedInstallRoot `
    -AppContentRoot $appContentRoot `
    -ExpectedSource $ExpectedUpdateSource `
    -ExpectedChannel $ExpectedChannel `
    -ExpectedTargetRuntime $ExpectedTargetRuntime

Write-Host "Edge installer Windows acceptance passed: $resolvedInstallerPath"
Write-Host "InstallRoot=$resolvedInstallRoot"
