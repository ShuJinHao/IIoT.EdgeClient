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
        foreach ($requiredProperty in @('runtimeId', 'profileId', 'machineProfile', 'outputDirectory', 'machineConfig', 'displayName', 'description', 'iconKind', 'accentColor')) {
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

function New-EdgeLauncherProfiles {
    param(
        [Parameter(Mandatory = $true)]
        $Manifest
    )

    return @(
        foreach ($runtime in $Manifest.runtimes) {
            [ordered]@{
                ProfileId = $runtime.profileId
                DisplayName = $runtime.displayName
                Description = $runtime.description
                ImagePath = if ([string]::IsNullOrWhiteSpace($runtime.imagePath)) { $null } else { $runtime.imagePath }
                IconKind = $runtime.iconKind
                AccentColor = $runtime.accentColor
                MachineProfile = $runtime.machineProfile
                ExecutablePath = "..\$($runtime.outputDirectory)\IIoT.Edge.Shell.exe"
            }
        }
    )
}

function Convert-EdgeJsonStringLiteral {
    param(
        [AllowNull()]
        [string]$Value
    )

    if ($null -eq $Value) {
        return 'null'
    }

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')

    foreach ($character in $Value.ToCharArray()) {
        $codePoint = [int][char]$character

        if ($character -eq '"') {
            [void]$builder.Append('\"')
            continue
        }

        if ($character -eq '\') {
            [void]$builder.Append('\\')
            continue
        }

        if ($character -eq "`r") {
            [void]$builder.Append('\r')
            continue
        }

        if ($character -eq "`n") {
            [void]$builder.Append('\n')
            continue
        }

        if ($character -eq "`t") {
            [void]$builder.Append('\t')
            continue
        }

        if ($codePoint -lt 32 -or $codePoint -gt 126) {
            [void]$builder.AppendFormat('\u{0:x4}', $codePoint)
            continue
        }

        [void]$builder.Append($character)
    }

    [void]$builder.Append('"')
    return $builder.ToString()
}

function Write-EdgeLauncherProfiles {
    param(
        [Parameter(Mandatory = $true)]
        $Manifest,

        [Parameter(Mandatory = $true)]
        [string]$OutputPath
    )

    $profiles = New-EdgeLauncherProfiles -Manifest $Manifest
    $jsonLines = New-Object System.Collections.Generic.List[string]
    [void]$jsonLines.Add('[')

    for ($index = 0; $index -lt $profiles.Count; $index++) {
        $profile = $profiles[$index]
        [void]$jsonLines.Add('  {')

        $properties = @(
            @{ Name = 'ProfileId'; Value = $profile.ProfileId },
            @{ Name = 'DisplayName'; Value = $profile.DisplayName },
            @{ Name = 'Description'; Value = $profile.Description },
            @{ Name = 'ImagePath'; Value = $profile.ImagePath },
            @{ Name = 'IconKind'; Value = $profile.IconKind },
            @{ Name = 'AccentColor'; Value = $profile.AccentColor },
            @{ Name = 'MachineProfile'; Value = $profile.MachineProfile },
            @{ Name = 'ExecutablePath'; Value = $profile.ExecutablePath }
        )

        for ($propertyIndex = 0; $propertyIndex -lt $properties.Count; $propertyIndex++) {
            $property = $properties[$propertyIndex]
            $suffix = if ($propertyIndex -lt ($properties.Count - 1)) { ',' } else { '' }
            [void]$jsonLines.Add(("    ""{0}"": {1}{2}" -f $property.Name, (Convert-EdgeJsonStringLiteral -Value $property.Value), $suffix))
        }

        $objectSuffix = if ($index -lt ($profiles.Count - 1)) { '  },' } else { '  }' }
        [void]$jsonLines.Add($objectSuffix)
    }

    [void]$jsonLines.Add(']')
    $json = [string]::Join([Environment]::NewLine, $jsonLines)
    $parentDirectory = Split-Path -Parent $OutputPath
    if (-not (Test-Path $parentDirectory)) {
        New-Item -Path $parentDirectory -ItemType Directory -Force | Out-Null
    }

    [System.IO.File]::WriteAllText(
        $OutputPath,
        $json,
        [System.Text.UTF8Encoding]::new($false))
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

    $shellExecutable = Join-Path $runtimeRoot 'IIoT.Edge.Shell.exe'
    if (-not (Test-Path $shellExecutable)) {
        throw "Shell executable was not found in runtime directory: $shellExecutable"
    }
}
