$ErrorActionPreference = 'Stop'

function Invoke-EdgeNativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [AllowEmptyCollection()]
        [string[]]$Arguments = @()
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Native command failed with exit code $LASTEXITCODE`: $FilePath $($Arguments -join ' ')"
    }
}

function Resolve-EdgeAbsolutePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $PathValue))
}

function Load-EdgeRuntimePublishManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$ManifestPath
    )

    $resolvedManifestPath = Resolve-EdgeAbsolutePath -BasePath $RepoRoot -PathValue $ManifestPath
    if (-not (Test-Path $resolvedManifestPath)) {
        throw "Edge runtime publish manifest was not found: $resolvedManifestPath"
    }

    $manifest = Get-Content -Raw -Encoding UTF8 -Path $resolvedManifestPath | ConvertFrom-Json
    if ($null -eq $manifest) {
        throw "Edge runtime publish manifest '$resolvedManifestPath' could not be parsed."
    }

    if ([string]::IsNullOrWhiteSpace($manifest.launcherDirectory)) {
        throw "Edge runtime publish manifest '$resolvedManifestPath' is missing launcherDirectory."
    }

    if ([string]::IsNullOrWhiteSpace($manifest.hostDirectory)) {
        throw "Edge runtime publish manifest '$resolvedManifestPath' is missing hostDirectory."
    }

    if ([string]::IsNullOrWhiteSpace($manifest.pluginsRoot)) {
        throw "Edge runtime publish manifest '$resolvedManifestPath' is missing pluginsRoot."
    }

    if ($null -eq $manifest.profiles -or $manifest.profiles.Count -eq 0) {
        throw "Edge runtime publish manifest '$resolvedManifestPath' does not contain any profiles."
    }

    $profileIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($profile in $manifest.profiles) {
        foreach ($requiredProperty in @('profileId', 'machineProfile', 'machineConfig')) {
            $value = $profile.$requiredProperty
            if ([string]::IsNullOrWhiteSpace($value)) {
                throw "Profile entry in '$resolvedManifestPath' is missing $requiredProperty."
            }
        }

        if ($null -eq $profile.PSObject.Properties['moduleIds']) {
            throw "Profile entry '$($profile.profileId)' in '$resolvedManifestPath' does not define moduleIds."
        }

        if (@($profile.moduleIds).Count -ne 0) {
            throw "Host runtime profile '$($profile.profileId)' must not declare business plugin moduleIds. Plugins are published only from IIoT.Edge.Plugins.Private."
        }

        if (-not $profileIds.Add($profile.profileId)) {
            throw "Runtime profileId '$($profile.profileId)' is duplicated in '$resolvedManifestPath'."
        }
    }

    return $manifest
}

function Get-EdgeLauncherProfileCatalog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$ProfileCatalogPath
    )

    $resolvedCatalogPath = Resolve-EdgeAbsolutePath -BasePath $RepoRoot -PathValue $ProfileCatalogPath
    if (-not (Test-Path $resolvedCatalogPath)) {
        throw "Launcher profile catalog source was not found: $resolvedCatalogPath"
    }

    $parsedProfiles = Get-Content -Raw -Encoding UTF8 -Path $resolvedCatalogPath | ConvertFrom-Json
    if ($null -eq $parsedProfiles) {
        throw "Launcher profile catalog '$resolvedCatalogPath' could not be parsed."
    }

    $profiles = @($parsedProfiles)
    if ($profiles.Count -eq 0) {
        throw "Launcher profile catalog '$resolvedCatalogPath' is empty."
    }

    $profileIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($profile in $profiles) {
        foreach ($requiredProperty in @('ProfileId', 'DisplayName', 'MachineProfile', 'ExecutablePath')) {
            $value = $profile.$requiredProperty
            if ([string]::IsNullOrWhiteSpace($value)) {
                throw "Launcher profile catalog '$resolvedCatalogPath' contains a profile missing $requiredProperty."
            }
        }

        if (-not $profileIds.Add($profile.ProfileId)) {
            throw "Launcher profile id '$($profile.ProfileId)' is duplicated in '$resolvedCatalogPath'."
        }
    }

    return [PSCustomObject]@{
        Path = $resolvedCatalogPath
        Profiles = $profiles
    }
}

function Copy-EdgeLauncherProfileCatalog {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$LauncherRuntimeRoot
    )

    if (-not (Test-Path $LauncherRuntimeRoot)) {
        New-Item -Path $LauncherRuntimeRoot -ItemType Directory -Force | Out-Null
    }

    $targetPath = Join-Path $LauncherRuntimeRoot 'launcher.profiles.json'
    Copy-Item -Path $SourcePath -Destination $targetPath -Force
    return $targetPath
}

function Test-EdgeIsWindowsPlatform {
    return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)
}

function Get-EdgeExecutableCandidates {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    $resolvedPath = Resolve-EdgeAbsolutePath -BasePath $BasePath -PathValue $PathValue
    $directory = Split-Path -Parent $resolvedPath
    $leaf = Split-Path -Leaf $resolvedPath
    $baseLeaf = $leaf
    if ($leaf.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
        $baseLeaf = $leaf.Substring(0, $leaf.Length - 4)
    }
    elseif ($leaf.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase)) {
        $baseLeaf = $leaf.Substring(0, $leaf.Length - 4)
    }

    $runningOnWindows = Test-EdgeIsWindowsPlatform
    $hasExeExtension = $leaf.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)
    $hasDllExtension = $leaf.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase)
    $candidates = [System.Collections.Generic.List[string]]::new()

    if ($runningOnWindows -and -not $hasExeExtension -and -not $hasDllExtension) {
        Add-EdgeExecutableCandidate -Candidates $candidates -PathValue (Join-Path $directory "$baseLeaf.exe")
    }
    elseif (-not $runningOnWindows -and $hasExeExtension) {
        Add-EdgeExecutableCandidate -Candidates $candidates -PathValue (Join-Path $directory $baseLeaf)
    }

    if (-not $hasDllExtension) {
        Add-EdgeExecutableCandidate -Candidates $candidates -PathValue $resolvedPath
    }

    Add-EdgeExecutableCandidate -Candidates $candidates -PathValue (Join-Path $directory "$baseLeaf.dll")
    return $candidates
}

function Add-EdgeExecutableCandidate {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Candidates,

        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    if (-not $Candidates.Contains($PathValue)) {
        $Candidates.Add($PathValue)
    }
}

function Resolve-EdgeExecutablePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$PathValue
    )

    foreach ($candidate in (Get-EdgeExecutableCandidates -BasePath $BasePath -PathValue $PathValue)) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function Assert-EdgeExecutablePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$PathValue,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $resolvedPath = Resolve-EdgeExecutablePath -BasePath $BasePath -PathValue $PathValue
    if ([string]::IsNullOrWhiteSpace($resolvedPath)) {
        $candidates = Get-EdgeExecutableCandidates -BasePath $BasePath -PathValue $PathValue
        throw "$Message Candidates: $($candidates -join ', ')"
    }

    return $resolvedPath
}

function Test-EdgeLauncherProfilesMatchManifest {
    param(
        [Parameter(Mandatory = $true)]
        $Manifest,

        [Parameter(Mandatory = $true)]
        [System.Collections.IEnumerable]$Profiles,

        [Parameter(Mandatory = $true)]
        [string]$LauncherRuntimeRoot,

        [switch]$CheckExecutablePath
    )

    $profileByProfileId = @{}
    foreach ($entry in $Manifest.profiles) {
        $profileByProfileId[$entry.profileId] = $entry
    }

    $profileIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($profile in $Profiles) {
        [void]$profileIds.Add($profile.ProfileId)
        if (-not $profileByProfileId.ContainsKey($profile.ProfileId)) {
            throw "Launcher profile '$($profile.ProfileId)' does not match any profileId in edge-runtime.publish.json."
        }

        $entry = $profileByProfileId[$profile.ProfileId]
        if ($profile.MachineProfile -ne $entry.machineProfile) {
            throw "Launcher profile '$($profile.ProfileId)' machineProfile '$($profile.MachineProfile)' does not match publish profile machineProfile '$($entry.machineProfile)'."
        }

        if ($CheckExecutablePath) {
            Assert-EdgeExecutablePath `
                -BasePath $LauncherRuntimeRoot `
                -PathValue $profile.ExecutablePath `
                -Message "Launcher profile '$($profile.ProfileId)' points to a missing executable." | Out-Null
        }
    }

    foreach ($entry in $Manifest.profiles) {
        if (-not $profileIds.Contains($entry.profileId)) {
            throw "Publish profile '$($entry.profileId)' is missing from launcher.profiles.json."
        }
    }
}

function Remove-EdgeLauncherShellArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LauncherRuntimeRoot
    )

    if (-not (Test-Path $LauncherRuntimeRoot)) {
        return
    }

    Get-ChildItem -Path $LauncherRuntimeRoot -Filter 'appsettings*.json' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    foreach ($fileName in @(
        'IIoT.Edge.Shell',
        'IIoT.Edge.Shell.exe',
        'IIoT.Edge.Shell.dll',
        'IIoT.Edge.Shell.deps.json',
        'IIoT.Edge.Shell.runtimeconfig.json',
        'IIoT.Edge.Shell.pdb',
        'log4net.config'
    )) {
        $filePath = Join-Path $LauncherRuntimeRoot $fileName
        if (Test-Path $filePath) {
            Remove-Item -LiteralPath $filePath -Force
        }
    }

    foreach ($directoryName in @('Modules', 'Logs', 'data')) {
        $directoryPath = Join-Path $LauncherRuntimeRoot $directoryName
        if (Test-Path $directoryPath) {
            Remove-Item -LiteralPath $directoryPath -Recurse -Force
        }
    }
}

function Copy-EdgeDirectoryContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$TargetDirectory
    )

    if (-not (Test-Path $SourceDirectory)) {
        throw "Source directory was not found: $SourceDirectory"
    }

    New-Item -Path $TargetDirectory -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $SourceDirectory '*') -Destination $TargetDirectory -Recurse -Force
}

function Sync-EdgeHostLayout {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$ShellRuntimeSource,

        [Parameter(Mandatory = $true)]
        $Manifest,

        [Parameter(Mandatory = $true)]
        [string]$LayoutRoot
    )

    $hostRoot = Join-Path $LayoutRoot $Manifest.hostDirectory
    if (Test-Path $hostRoot) {
        Get-ChildItem -Path $hostRoot -Force -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction Stop
    }
    else {
        New-Item -Path $hostRoot -ItemType Directory -Force | Out-Null
    }

    Copy-EdgeDirectoryContent -SourceDirectory $ShellRuntimeSource -TargetDirectory $hostRoot

    Get-ChildItem -Path $hostRoot -Filter 'appsettings.machine.*.json' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    foreach ($profile in $Manifest.profiles) {
        $machineConfigSource = Resolve-EdgeAbsolutePath -BasePath $RepoRoot -PathValue $profile.machineConfig
        if (-not (Test-Path $machineConfigSource)) {
            throw "Machine profile config was not found for profile '$($profile.profileId)': $machineConfigSource"
        }

        Copy-Item -Path $machineConfigSource -Destination (Join-Path $hostRoot (Split-Path -Leaf $machineConfigSource)) -Force
    }

    $staleModulesRoot = Join-Path $hostRoot 'Modules'
    if (Test-Path $staleModulesRoot) {
        Remove-Item -Path $staleModulesRoot -Recurse -Force
    }

    Assert-EdgeExecutablePath `
        -BasePath $hostRoot `
        -PathValue 'IIoT.Edge.Shell' `
        -Message "Shell executable was not found in host directory '$hostRoot'." | Out-Null
}

function Sync-EdgePluginsLayout {
    param(
        [Parameter(Mandatory = $true)]
        $Manifest,

        [Parameter(Mandatory = $true)]
        [string]$LayoutRoot
    )

    $moduleIds = @($Manifest.profiles | ForEach-Object { @($_.moduleIds) } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($moduleIds.Count -ne 0) {
        throw 'Host runtime publication cannot stage business plugins. Publish plugins only from IIoT.Edge.Plugins.Private.'
    }

    $pluginsRoot = Join-Path $LayoutRoot $Manifest.pluginsRoot
    if (Test-Path -LiteralPath $pluginsRoot) {
        Remove-Item -LiteralPath $pluginsRoot -Recurse -Force
    }
    New-Item -Path $pluginsRoot -ItemType Directory -Force | Out-Null
}
