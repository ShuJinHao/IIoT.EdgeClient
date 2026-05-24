$ErrorActionPreference = 'Stop'

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

function Get-EdgeProjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$ProjectXml,

        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    foreach ($propertyGroup in $ProjectXml.Project.PropertyGroup) {
        $value = $propertyGroup.$PropertyName
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    return $null
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

    if ($null -eq $manifest.runtimes -or $manifest.runtimes.Count -eq 0) {
        throw "Edge runtime publish manifest '$resolvedManifestPath' does not contain any runtimes."
    }

    $runtimeIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $outputDirectories = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $profileIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($runtime in $manifest.runtimes) {
        foreach ($requiredProperty in @('runtimeId', 'profileId', 'machineProfile', 'outputDirectory', 'machineConfig')) {
            $value = $runtime.$requiredProperty
            if ([string]::IsNullOrWhiteSpace($value)) {
                throw "Runtime entry in '$resolvedManifestPath' is missing $requiredProperty."
            }
        }

        if ($null -eq $runtime.moduleIds -or $runtime.moduleIds.Count -eq 0) {
            throw "Runtime entry '$($runtime.runtimeId)' in '$resolvedManifestPath' does not define moduleIds."
        }

        if (-not $runtimeIds.Add($runtime.runtimeId)) {
            throw "Runtime id '$($runtime.runtimeId)' is duplicated in '$resolvedManifestPath'."
        }

        if (-not $outputDirectories.Add($runtime.outputDirectory)) {
            throw "Runtime outputDirectory '$($runtime.outputDirectory)' is duplicated in '$resolvedManifestPath'."
        }

        if (-not $profileIds.Add($runtime.profileId)) {
            throw "Runtime profileId '$($runtime.profileId)' is duplicated in '$resolvedManifestPath'."
        }
    }

    return $manifest
}

function Get-EdgeModuleProjectMap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $map = @{}
    $projectFiles = Get-ChildItem -Path (Join-Path $RepoRoot 'src\Modules') -Recurse -Filter *.csproj -File
    foreach ($projectFile in $projectFiles) {
        [xml]$projectXml = Get-Content -Path $projectFile.FullName
        $moduleId = Get-EdgeProjectPropertyValue -ProjectXml $projectXml -PropertyName 'PluginModuleId'
        if ([string]::IsNullOrWhiteSpace($moduleId)) {
            continue
        }

        $targetFramework = Get-EdgeProjectPropertyValue -ProjectXml $projectXml -PropertyName 'TargetFramework'
        if ([string]::IsNullOrWhiteSpace($targetFramework)) {
            throw "Module project '$($projectFile.FullName)' is missing TargetFramework."
        }

        $map[$moduleId] = [PSCustomObject]@{
            ModuleId = $moduleId
            ProjectPath = $projectFile.FullName
            ProjectDirectory = $projectFile.Directory.FullName
            TargetFramework = $targetFramework
        }
    }

    return $map
}

function Build-EdgeModuleProjects {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IEnumerable]$ModuleIds,

        [Parameter(Mandatory = $true)]
        [hashtable]$ModuleProjectMap,

        [Parameter(Mandatory = $true)]
        [string]$Configuration
    )

    foreach ($moduleId in ($ModuleIds | Select-Object -Unique)) {
        if (-not $ModuleProjectMap.ContainsKey($moduleId)) {
            throw "Module '$moduleId' was not found under src\\Modules."
        }

        $project = $ModuleProjectMap[$moduleId]
        dotnet build $project.ProjectPath `
            --configuration $Configuration `
            --nologo `
            --verbosity minimal `
            --disable-build-servers `
            -p:BuildInParallel=false `
            -p:RestoreDisableParallel=true
    }
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

    $isWindows = Test-EdgeIsWindowsPlatform
    $hasExeExtension = $leaf.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)
    $hasDllExtension = $leaf.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase)
    $candidates = [System.Collections.Generic.List[string]]::new()

    if ($isWindows -and -not $hasExeExtension -and -not $hasDllExtension) {
        Add-EdgeExecutableCandidate -Candidates $candidates -PathValue (Join-Path $directory "$baseLeaf.exe")
    }
    elseif (-not $isWindows -and $hasExeExtension) {
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

    $runtimeByProfileId = @{}
    foreach ($runtime in $Manifest.runtimes) {
        $runtimeByProfileId[$runtime.profileId] = $runtime
    }

    $profileIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($profile in $Profiles) {
        [void]$profileIds.Add($profile.ProfileId)
        if (-not $runtimeByProfileId.ContainsKey($profile.ProfileId)) {
            throw "Launcher profile '$($profile.ProfileId)' does not match any runtime profileId in edge-runtime.publish.json."
        }

        $runtime = $runtimeByProfileId[$profile.ProfileId]
        if ($profile.MachineProfile -ne $runtime.machineProfile) {
            throw "Launcher profile '$($profile.ProfileId)' machineProfile '$($profile.MachineProfile)' does not match runtime machineProfile '$($runtime.machineProfile)'."
        }

        if ($CheckExecutablePath) {
            Assert-EdgeExecutablePath `
                -BasePath $LauncherRuntimeRoot `
                -PathValue $profile.ExecutablePath `
                -Message "Launcher profile '$($profile.ProfileId)' points to a missing executable." | Out-Null
        }
    }

    foreach ($runtime in $Manifest.runtimes) {
        if (-not $profileIds.Contains($runtime.profileId)) {
            throw "Runtime '$($runtime.runtimeId)' profileId '$($runtime.profileId)' is missing from launcher.profiles.json."
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

function Test-EdgePluginManifestFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath
    )

    if (-not (Test-Path $ManifestPath)) {
        throw "Plugin manifest was not found: $ManifestPath"
    }

    $manifest = Get-Content -Raw -Encoding UTF8 -Path $ManifestPath | ConvertFrom-Json
    foreach ($requiredProperty in @('moduleId', 'displayName', 'version', 'hostApiVersion', 'minHostVersion', 'maxHostVersion', 'entryAssembly', 'entryType', 'supportedProcessType')) {
        if ([string]::IsNullOrWhiteSpace($manifest.$requiredProperty)) {
            throw "Plugin manifest '$ManifestPath' is missing $requiredProperty."
        }
    }
}

function Publish-EdgeModulesToRuntimeRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$Configuration,

        [Parameter(Mandatory = $true)]
        [string[]]$ModuleIds,

        [Parameter(Mandatory = $true)]
        [string]$TargetModulesRoot,

        [switch]$CleanModulesDirectory
    )

    $moduleProjectMap = Get-EdgeModuleProjectMap -RepoRoot $RepoRoot
    Build-EdgeModuleProjects -ModuleIds $ModuleIds -ModuleProjectMap $moduleProjectMap -Configuration $Configuration

    if ($CleanModulesDirectory -and (Test-Path $TargetModulesRoot)) {
        Remove-Item -Path $TargetModulesRoot -Recurse -Force
    }

    New-Item -Path $TargetModulesRoot -ItemType Directory -Force | Out-Null

    foreach ($moduleId in ($ModuleIds | Select-Object -Unique)) {
        $project = $moduleProjectMap[$moduleId]
        $moduleBuildRoot = Join-Path $project.ProjectDirectory "bin\$Configuration\$($project.TargetFramework)"
        if (-not (Test-Path $moduleBuildRoot)) {
            throw "Module build output was not found: $moduleBuildRoot"
        }

        $moduleRuntimeDirectory = Join-Path $TargetModulesRoot $moduleId
        if (Test-Path $moduleRuntimeDirectory) {
            Remove-Item -Path $moduleRuntimeDirectory -Recurse -Force
        }

        New-Item -Path $moduleRuntimeDirectory -ItemType Directory -Force | Out-Null
        Copy-Item -Path (Join-Path $moduleBuildRoot '*') -Destination $moduleRuntimeDirectory -Recurse -Force

        Test-EdgePluginManifestFile -ManifestPath (Join-Path $moduleRuntimeDirectory 'plugin.json')
    }
}

function Sync-EdgeProcessRuntime {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$Configuration,

        [Parameter(Mandatory = $true)]
        [string]$ShellRuntimeSource,

        [Parameter(Mandatory = $true)]
        $RuntimeDefinition,

        [Parameter(Mandatory = $true)]
        [string]$LayoutRoot
    )

    $runtimeRoot = Join-Path $LayoutRoot $RuntimeDefinition.outputDirectory
    if (Test-Path $runtimeRoot) {
        Get-ChildItem -Path $runtimeRoot -Force -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction Stop
    }
    else {
        New-Item -Path $runtimeRoot -ItemType Directory -Force | Out-Null
    }

    Copy-EdgeDirectoryContent -SourceDirectory $ShellRuntimeSource -TargetDirectory $runtimeRoot

    Get-ChildItem -Path $runtimeRoot -Filter 'appsettings.machine.*.json' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    $machineConfigSource = Resolve-EdgeAbsolutePath -BasePath $RepoRoot -PathValue $RuntimeDefinition.machineConfig
    if (-not (Test-Path $machineConfigSource)) {
        throw "Machine profile config was not found for runtime '$($RuntimeDefinition.runtimeId)': $machineConfigSource"
    }

    Copy-Item -Path $machineConfigSource -Destination (Join-Path $runtimeRoot (Split-Path -Leaf $machineConfigSource)) -Force

    $modulesRoot = Join-Path $runtimeRoot 'Modules'
    Publish-EdgeModulesToRuntimeRoot `
        -RepoRoot $RepoRoot `
        -Configuration $Configuration `
        -ModuleIds @($RuntimeDefinition.moduleIds) `
        -TargetModulesRoot $modulesRoot `
        -CleanModulesDirectory

    Assert-EdgeExecutablePath `
        -BasePath $runtimeRoot `
        -PathValue 'IIoT.Edge.Shell' `
        -Message "Shell executable was not found in runtime directory '$runtimeRoot'." | Out-Null
}
