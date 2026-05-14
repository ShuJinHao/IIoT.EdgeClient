[CmdletBinding()]
param(
    [string]$Configuration = 'Release',

    [string]$RepositoryRoot,

    [string]$OutputRoot = 'publish\avalonia-migration',

    [string]$LauncherAccountsSource,

    [switch]$CleanOutput,

    [switch]$SkipNuGetPreviewValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Release docs copied into the package:
# Avalonia12-现场联调检查清单.md
# NuGet预览传递依赖例外记录.md
# Avalonia12-切换前差异矩阵.md
# Avalonia12-切换阻断清单.md

function Join-UnicodeName {
    param(
        [Parameter(Mandatory = $true)]
        [int[]]$CodePoints
    )

    return [string]::Concat([char[]]$CodePoints)
}

function Resolve-AvaloniaFullPath {
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

function Assert-AvaloniaChildPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath,

        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $normalizedRoot = [System.IO.Path]::GetFullPath($RootPath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $normalizedTarget = [System.IO.Path]::GetFullPath($TargetPath)
    $rootPrefix = $normalizedRoot + [System.IO.Path]::DirectorySeparatorChar

    if (-not $normalizedTarget.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and
        -not $normalizedTarget.Equals($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify '$normalizedTarget' because it is outside '$normalizedRoot'."
    }
}

function Remove-AvaloniaPathIfExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$GuardRoot,

        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    if (-not (Test-Path -LiteralPath $TargetPath)) {
        return
    }

    Assert-AvaloniaChildPath -RootPath $GuardRoot -TargetPath $TargetPath
    Remove-Item -LiteralPath $TargetPath -Recurse -Force
}

function Remove-AvaloniaLauncherShellArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LauncherRoot
    )

    foreach ($fileName in @(
        'IIoT.Edge.AvaloniaShell.exe',
        'IIoT.Edge.AvaloniaShell.dll',
        'IIoT.Edge.AvaloniaShell.deps.json',
        'IIoT.Edge.AvaloniaShell.runtimeconfig.json',
        'IIoT.Edge.AvaloniaShell.pdb'
    )) {
        Remove-AvaloniaPathIfExists -GuardRoot $LauncherRoot -TargetPath (Join-Path $LauncherRoot $fileName)
    }

    foreach ($directoryName in @('Modules', 'data')) {
        Remove-AvaloniaPathIfExists -GuardRoot $LauncherRoot -TargetPath (Join-Path $LauncherRoot $directoryName)
    }
}

function Copy-AvaloniaDirectoryContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,

        [Parameter(Mandatory = $true)]
        [string]$TargetDirectory
    )

    if (-not (Test-Path -LiteralPath $SourceDirectory)) {
        throw "Source directory was not found: $SourceDirectory"
    }

    New-Item -Path $TargetDirectory -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $SourceDirectory '*') -Destination $TargetDirectory -Recurse -Force
}

function ConvertTo-AvaloniaCommandLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $escapedArguments = $Arguments | ForEach-Object {
        if ($_ -match '[\s"]') {
            '"' + ($_.Replace('"', '\"')) + '"'
        }
        else {
            $_
        }
    }

    return "$Executable $($escapedArguments -join ' ')"
}

function ConvertTo-AvaloniaJsonArray {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    if ($Value -is [System.Array]) {
        return @($Value)
    }

    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        return @($Value | ForEach-Object { $_ })
    }

    return @($Value)
}

function Invoke-AvaloniaDotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [System.Collections.Generic.List[object]]$ValidationCommands,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $ValidationCommands.Add([PSCustomObject]@{
        name = $Name
        command = ConvertTo-AvaloniaCommandLine -Executable 'dotnet' -Arguments $Arguments
    }) | Out-Null

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed: $(ConvertTo-AvaloniaCommandLine -Executable 'dotnet' -Arguments $Arguments)"
    }
}

function Get-AvaloniaProjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)]
        [xml]$ProjectXml,

        [Parameter(Mandatory = $true)]
        [string]$PropertyName
    )

    foreach ($propertyGroup in $ProjectXml.Project.PropertyGroup) {
        foreach ($child in $propertyGroup.ChildNodes) {
            if ($child.Name -ne $PropertyName) {
                continue
            }

            if (-not [string]::IsNullOrWhiteSpace($child.InnerText)) {
                return $child.InnerText.Trim()
            }
        }
    }

    return $null
}

function Get-AvaloniaRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $normalizedBasePath = [System.IO.Path]::GetFullPath($BasePath).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $baseUri = [System.Uri]$normalizedBasePath
    $targetUri = [System.Uri]([System.IO.Path]::GetFullPath($TargetPath))
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', '\')
}

function Get-AvaloniaProjectMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,

        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [string]$PublishedAssemblyPath
    )

    [xml]$projectXml = Get-Content -Path $ProjectPath -Encoding UTF8
    $targetFramework = Get-AvaloniaProjectPropertyValue -ProjectXml $projectXml -PropertyName 'TargetFramework'
    $projectVersion = Get-AvaloniaProjectPropertyValue -ProjectXml $projectXml -PropertyName 'Version'

    if ([string]::IsNullOrWhiteSpace($projectVersion) -and (Test-Path -LiteralPath $PublishedAssemblyPath)) {
        $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($PublishedAssemblyPath)
        $projectVersion = $versionInfo.ProductVersion
        if ([string]::IsNullOrWhiteSpace($projectVersion)) {
            $projectVersion = $versionInfo.FileVersion
        }
    }

    if ([string]::IsNullOrWhiteSpace($projectVersion)) {
        $projectVersion = '1.0.0'
    }

    return [PSCustomObject]@{
        name = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
        path = Get-AvaloniaRelativePath -BasePath $RepoRoot -TargetPath $ProjectPath
        targetFramework = $targetFramework
        version = $projectVersion
    }
}

function Get-AvaloniaGitCommitInfo {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    try {
        $sha = (& git -C $RepoRoot rev-parse HEAD 2>$null).Trim()
        $shortSha = (& git -C $RepoRoot rev-parse --short HEAD 2>$null).Trim()
        $status = @(& git -C $RepoRoot status --short 2>$null)

        return [PSCustomObject]@{
            sha = $sha
            shortSha = $shortSha
            isDirty = ($status.Count -gt 0)
        }
    }
    catch {
        return [PSCustomObject]@{
            sha = 'unknown'
            shortSha = 'unknown'
            isDirty = $null
        }
    }
}

function Get-AvaloniaPrereleasePackages {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ProjectPaths
    )

    $packages = @{}
    foreach ($projectPath in $ProjectPaths) {
        $assetsPath = Join-Path (Split-Path -Path $projectPath -Parent) 'obj\project.assets.json'
        if (-not (Test-Path -LiteralPath $assetsPath)) {
            throw "NuGet assets file was not found after publish: $assetsPath"
        }

        $assets = Get-Content -Path $assetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($library in $assets.libraries.PSObject.Properties) {
            $nameParts = $library.Name -split '/', 2
            if ($nameParts.Count -ne 2) {
                continue
            }

            $packageName = $nameParts[0]
            $packageVersion = $nameParts[1]
            if ($packageVersion -notmatch '-') {
                continue
            }

            $packageKey = "$packageName/$packageVersion"
            if (-not $packages.ContainsKey($packageKey)) {
                $packages[$packageKey] = [PSCustomObject]@{
                    name = $packageName
                    version = $packageVersion
                }
            }
        }
    }

    return @($packages.Values | Sort-Object -Property name, version)
}

function Test-AvaloniaNuGetPreviewException {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ProjectPaths,

        [Parameter(Mandatory = $true)]
        [string]$ExceptionDocument
    )

    $allowedPackageNames = @(
        'SkiaSharp',
        'SkiaSharp.NativeAssets.Linux',
        'SkiaSharp.NativeAssets.macOS',
        'SkiaSharp.NativeAssets.WebAssembly',
        'SkiaSharp.NativeAssets.Win32'
    )

    $detectedPackages = Get-AvaloniaPrereleasePackages -ProjectPaths $ProjectPaths
    $unexpectedPackages = @($detectedPackages | Where-Object { $allowedPackageNames -notcontains $_.name })
    if ($unexpectedPackages.Count -gt 0) {
        $unexpectedText = $unexpectedPackages | ForEach-Object { "$($_.name)/$($_.version)" }
        throw "Unexpected preview/prerelease NuGet packages were detected: $($unexpectedText -join ', ')"
    }

    return [PSCustomObject]@{
        approved = $true
        decisionDate = '2026-05-13'
        document = $ExceptionDocument
        allowedPackages = $allowedPackageNames
        detectedPackages = $detectedPackages
    }
}

function Assert-AvaloniaRequiredFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $fullPath = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Required file was not found: $fullPath"
    }
}

function Test-AvaloniaReleaseLayout {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReleaseRoot,

        [Parameter(Mandatory = $true)]
        [string]$LauncherRoot,

        [Parameter(Mandatory = $true)]
        [string]$ShellRoot,

        [Parameter(Mandatory = $true)]
        [string]$FieldChecklistName,

        [Parameter(Mandatory = $true)]
        [string]$NuGetExceptionName,

        [Parameter(Mandatory = $true)]
        [string]$SwitchMatrixName,

        [Parameter(Mandatory = $true)]
        [string]$SwitchBlockerName
    )

    Assert-AvaloniaRequiredFile -Root $LauncherRoot -RelativePath 'IIoT.Edge.Launcher.Avalonia.exe'
    Assert-AvaloniaRequiredFile -Root $LauncherRoot -RelativePath 'launcher.profiles.json'
    Assert-AvaloniaRequiredFile -Root $LauncherRoot -RelativePath 'launcher.accounts.sample.json'
    Assert-AvaloniaRequiredFile -Root $LauncherRoot -RelativePath 'Assets\Profiles\homogenization.png'
    if (Test-Path -LiteralPath (Join-Path $LauncherRoot 'IIoT.Edge.Shell.exe')) {
        throw 'Avalonia launcher output contains WPF shell executable.'
    }

    Assert-AvaloniaRequiredFile -Root $ShellRoot -RelativePath 'IIoT.Edge.AvaloniaShell.exe'
    Assert-AvaloniaRequiredFile -Root $ShellRoot -RelativePath 'IIoT.Edge.AvaloniaShell.dll'
    if (Test-Path -LiteralPath (Join-Path $ShellRoot 'IIoT.Edge.Launcher.Avalonia.exe')) {
        throw 'Avalonia shell output contains launcher executable.'
    }

    $moduleRoot = Join-Path $ShellRoot 'Modules\Homogenization'
    Assert-AvaloniaRequiredFile -Root $moduleRoot -RelativePath 'IIoT.Edge.Module.Homogenization.Avalonia.dll'
    Assert-AvaloniaRequiredFile -Root $moduleRoot -RelativePath 'plugin.json'
    Assert-AvaloniaRequiredFile -Root $moduleRoot -RelativePath 'Config\homogenization.module.json'

    $pluginManifest = Get-Content -Path (Join-Path $moduleRoot 'plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($pluginManifest.entryAssembly -ne 'IIoT.Edge.Module.Homogenization.Avalonia.dll') {
        throw "Homogenization Avalonia plugin entryAssembly is '$($pluginManifest.entryAssembly)'."
    }

    $profilesPath = Join-Path $LauncherRoot 'launcher.profiles.json'
    $parsedProfiles = Get-Content -Path $profilesPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $profiles = ConvertTo-AvaloniaJsonArray -Value $parsedProfiles

    if ($profiles.Count -ne 2) {
        throw "Avalonia launcher profile catalog must contain exactly 2 profiles. Actual: $($profiles.Count)"
    }

    foreach ($profile in $profiles) {
        if ([string]::IsNullOrWhiteSpace($profile.ExecutablePath)) {
            throw "Launcher profile '$($profile.ProfileId)' does not define ExecutablePath."
        }

        $resolvedExecutablePath = Resolve-AvaloniaFullPath -BasePath $LauncherRoot -PathValue $profile.ExecutablePath
        if (-not (Test-Path -LiteralPath $resolvedExecutablePath -PathType Leaf)) {
            throw "Launcher profile '$($profile.ProfileId)' points to missing executable: $resolvedExecutablePath"
        }
    }

    $uiOnlyProfile = @($profiles | Where-Object { $_.ProfileId -eq 'HomogenizationLineAvalonia' })
    if ($uiOnlyProfile.Count -ne 1 -or $uiOnlyProfile[0].PSObject.Properties.Name -contains 'Arguments') {
        throw 'Avalonia UI-only launcher profile must exist and must not define Arguments.'
    }

    $runtimeProfile = @($profiles | Where-Object { $_.ProfileId -eq 'HomogenizationLineAvaloniaRuntime' })
    if ($runtimeProfile.Count -ne 1 -or @($runtimeProfile[0].Arguments) -notcontains '--start-runtime') {
        throw 'Avalonia runtime launcher profile must exist and must pass --start-runtime.'
    }

    Assert-AvaloniaRequiredFile -Root (Join-Path $ReleaseRoot 'docs') -RelativePath $FieldChecklistName
    Assert-AvaloniaRequiredFile -Root (Join-Path $ReleaseRoot 'docs') -RelativePath $NuGetExceptionName
    Assert-AvaloniaRequiredFile -Root (Join-Path $ReleaseRoot 'docs') -RelativePath $SwitchMatrixName
    Assert-AvaloniaRequiredFile -Root (Join-Path $ReleaseRoot 'docs') -RelativePath $SwitchBlockerName
}

$fieldChecklistName = 'Avalonia12-' + (Join-UnicodeName -CodePoints @(0x73B0, 0x573A, 0x8054, 0x8C03, 0x68C0, 0x67E5, 0x6E05, 0x5355)) + '.md'
$nugetExceptionName = 'NuGet' + (Join-UnicodeName -CodePoints @(0x9884, 0x89C8, 0x4F20, 0x9012, 0x4F9D, 0x8D56, 0x4F8B, 0x5916, 0x8BB0, 0x5F55)) + '.md'
$switchMatrixName = 'Avalonia12-' + (Join-UnicodeName -CodePoints @(0x5207, 0x6362, 0x524D, 0x5DEE, 0x5F02, 0x77E9, 0x9635)) + '.md'
$switchBlockerName = 'Avalonia12-' + (Join-UnicodeName -CodePoints @(0x5207, 0x6362, 0x963B, 0x65AD, 0x6E05, 0x5355)) + '.md'
$fieldChecklistRelativePath = Join-Path 'docs' $fieldChecklistName
$nugetExceptionRelativePath = Join-Path 'docs' $nugetExceptionName
$switchMatrixRelativePath = Join-Path 'docs' $switchMatrixName
$switchBlockerRelativePath = Join-Path 'docs' $switchBlockerName

$repoRoot = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
}
else {
    [System.IO.Path]::GetFullPath($RepositoryRoot)
}
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'IIoT.EdgeClient.slnx'))) {
    throw "Repository root is invalid: $repoRoot"
}

$outputBaseRoot = Resolve-AvaloniaFullPath -BasePath $repoRoot -PathValue $OutputRoot
$releaseRoot = Join-Path $outputBaseRoot $Configuration
Assert-AvaloniaChildPath -RootPath $repoRoot -TargetPath $releaseRoot

$launcherProject = Join-Path $repoRoot 'src\Edge\IIoT.Edge.Launcher.Avalonia\IIoT.Edge.Launcher.Avalonia.csproj'
$shellProject = Join-Path $repoRoot 'src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj'
$pluginProject = Join-Path $repoRoot 'src\Modules\IIoT.Edge.Module.Homogenization.Avalonia\IIoT.Edge.Module.Homogenization.Avalonia.csproj'

foreach ($projectPath in @($launcherProject, $shellProject, $pluginProject)) {
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Required Avalonia project was not found: $projectPath"
    }
}

$stagingRoot = Join-Path $repoRoot ('.artifacts\avalonia-migration-publish\' + [Guid]::NewGuid().ToString('N'))
$launcherStagingRoot = Join-Path $stagingRoot 'publish\avalonia-launcher'
$shellStagingRoot = Join-Path $stagingRoot 'publish\avalonia-shell'
$pluginStagingRoot = Join-Path $stagingRoot 'publish\module-homogenization'
$launcherBuildRoot = Join-Path $stagingRoot 'build\avalonia-launcher'
$shellBuildRoot = Join-Path $stagingRoot 'build\avalonia-shell'
$pluginBuildRoot = Join-Path $stagingRoot 'build\module-homogenization'
$launcherRoot = Join-Path $releaseRoot 'avalonia-launcher'
$shellRoot = Join-Path $releaseRoot 'avalonia-shell'
$docsRoot = Join-Path $releaseRoot 'docs'
$manifestPath = Join-Path $releaseRoot 'release-manifest.json'
$validationCommands = [System.Collections.Generic.List[object]]::new()

try {
    if ($CleanOutput) {
        Remove-AvaloniaPathIfExists -GuardRoot $repoRoot -TargetPath $releaseRoot
    }
    else {
        foreach ($ownedPath in @($launcherRoot, $shellRoot, $docsRoot, $manifestPath)) {
            Remove-AvaloniaPathIfExists -GuardRoot $repoRoot -TargetPath $ownedPath
        }
    }

    New-Item -Path $releaseRoot -ItemType Directory -Force | Out-Null
    New-Item -Path $stagingRoot -ItemType Directory -Force | Out-Null

    Invoke-AvaloniaDotNet `
        -Name 'publish Launcher.Avalonia' `
        -ValidationCommands $validationCommands `
        -Arguments @(
            'publish',
            $launcherProject,
            '--configuration',
            $Configuration,
            '--output',
            $launcherStagingRoot,
            '--nologo',
            '--verbosity',
            'minimal',
            '--disable-build-servers',
            '-p:BuildInParallel=false',
            '-p:RestoreDisableParallel=true',
            '-p:EnableLocalAvaloniaPluginModuleBuild=false',
            "-p:BaseOutputPath=$launcherBuildRoot",
            "-p:OutputPath=$launcherBuildRoot"
        )

    Invoke-AvaloniaDotNet `
        -Name 'publish AvaloniaShell' `
        -ValidationCommands $validationCommands `
        -Arguments @(
            'publish',
            $shellProject,
            '--configuration',
            $Configuration,
            '--output',
            $shellStagingRoot,
            '--nologo',
            '--verbosity',
            'minimal',
            '--disable-build-servers',
            '-p:BuildInParallel=false',
            '-p:RestoreDisableParallel=true',
            '-p:EnableLocalAvaloniaPluginModuleBuild=false',
            "-p:BaseOutputPath=$shellBuildRoot",
            "-p:OutputPath=$shellBuildRoot"
        )

    Invoke-AvaloniaDotNet `
        -Name 'publish Homogenization.Avalonia plugin' `
        -ValidationCommands $validationCommands `
        -Arguments @(
            'publish',
            $pluginProject,
            '--configuration',
            $Configuration,
            '--output',
            $pluginStagingRoot,
            '--nologo',
            '--verbosity',
            'minimal',
            '--disable-build-servers',
            '-p:BuildInParallel=false',
            '-p:RestoreDisableParallel=true',
            "-p:BaseOutputPath=$pluginBuildRoot",
            "-p:OutputPath=$pluginBuildRoot"
        )

    Copy-AvaloniaDirectoryContent -SourceDirectory $launcherStagingRoot -TargetDirectory $launcherRoot
    Copy-AvaloniaDirectoryContent -SourceDirectory $shellStagingRoot -TargetDirectory $shellRoot
    Copy-AvaloniaDirectoryContent -SourceDirectory $pluginStagingRoot -TargetDirectory (Join-Path $shellRoot 'Modules\Homogenization')
    Remove-AvaloniaLauncherShellArtifacts -LauncherRoot $launcherRoot

    if (-not [string]::IsNullOrWhiteSpace($LauncherAccountsSource)) {
        $resolvedAccountsSource = Resolve-AvaloniaFullPath -BasePath $repoRoot -PathValue $LauncherAccountsSource
        if (-not (Test-Path -LiteralPath $resolvedAccountsSource -PathType Leaf)) {
            throw "Launcher accounts source was not found: $resolvedAccountsSource"
        }

        Copy-Item -Path $resolvedAccountsSource -Destination (Join-Path $launcherRoot 'launcher.accounts.json') -Force
    }

    New-Item -Path $docsRoot -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $repoRoot $fieldChecklistRelativePath) -Destination (Join-Path $docsRoot $fieldChecklistName) -Force
    Copy-Item -Path (Join-Path $repoRoot $nugetExceptionRelativePath) -Destination (Join-Path $docsRoot $nugetExceptionName) -Force
    Copy-Item -Path (Join-Path $repoRoot $switchMatrixRelativePath) -Destination (Join-Path $docsRoot $switchMatrixName) -Force
    Copy-Item -Path (Join-Path $repoRoot $switchBlockerRelativePath) -Destination (Join-Path $docsRoot $switchBlockerName) -Force

    $validationCommands.Add([PSCustomObject]@{
        name = 'validate Avalonia release layout'
        command = 'scripts\PublishAvaloniaMigration.ps1 internal layout preflight'
    }) | Out-Null
    Test-AvaloniaReleaseLayout `
        -ReleaseRoot $releaseRoot `
        -LauncherRoot $launcherRoot `
        -ShellRoot $shellRoot `
        -FieldChecklistName $fieldChecklistName `
        -NuGetExceptionName $nugetExceptionName `
        -SwitchMatrixName $switchMatrixName `
        -SwitchBlockerName $switchBlockerName

    $validationCommands.Add([PSCustomObject]@{
        name = 'validate SkiaSharp preview exception'
        command = if ($SkipNuGetPreviewValidation) {
            'skipped by -SkipNuGetPreviewValidation'
        }
        else {
            'parse obj\project.assets.json for Launcher.Avalonia and AvaloniaShell'
        }
    }) | Out-Null

    $nugetPreviewException = if ($SkipNuGetPreviewValidation) {
        [PSCustomObject]@{
            approved = $true
            skipped = $true
            decisionDate = '2026-05-13'
            document = $nugetExceptionRelativePath
            allowedPackages = @(
                'SkiaSharp',
                'SkiaSharp.NativeAssets.Linux',
                'SkiaSharp.NativeAssets.macOS',
                'SkiaSharp.NativeAssets.WebAssembly',
                'SkiaSharp.NativeAssets.Win32'
            )
            detectedPackages = @()
        }
    }
    else {
        Test-AvaloniaNuGetPreviewException -ProjectPaths @($launcherProject, $shellProject) -ExceptionDocument $nugetExceptionRelativePath
    }

    $manifest = [PSCustomObject]@{
        schemaVersion = 1
        releaseKind = 'AvaloniaMigration'
        configuration = $Configuration
        commit = Get-AvaloniaGitCommitInfo -RepoRoot $repoRoot
        buildTimeUtc = [System.DateTimeOffset]::UtcNow.ToString('o')
        outputRoot = $releaseRoot
        outputs = [PSCustomObject]@{
            launcher = $launcherRoot
            shell = $shellRoot
            docs = $docsRoot
        }
        projects = @(
            Get-AvaloniaProjectMetadata -RepoRoot $repoRoot -ProjectPath $launcherProject -PublishedAssemblyPath (Join-Path $launcherRoot 'IIoT.Edge.Launcher.Avalonia.exe')
            Get-AvaloniaProjectMetadata -RepoRoot $repoRoot -ProjectPath $shellProject -PublishedAssemblyPath (Join-Path $shellRoot 'IIoT.Edge.AvaloniaShell.exe')
            Get-AvaloniaProjectMetadata -RepoRoot $repoRoot -ProjectPath $pluginProject -PublishedAssemblyPath (Join-Path $shellRoot 'Modules\Homogenization\IIoT.Edge.Module.Homogenization.Avalonia.dll')
        )
        skiaSharpPreviewException = $nugetPreviewException
        validationCommandSummary = @($validationCommands)
    }

    $manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestPath -Encoding UTF8
    Assert-AvaloniaRequiredFile -Root $releaseRoot -RelativePath 'release-manifest.json'

    Write-Host 'Avalonia migration publish complete.'
    Write-Host "  Output: $releaseRoot"
    Write-Host "  Launcher: $launcherRoot"
    Write-Host "  Shell: $shellRoot"
    Write-Host "  Manifest: $manifestPath"
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
