param(
    [string]$Configuration = 'Release',

    [switch]$SkipIntegrationValidation
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$integrationRepo = Join-Path $repoRoot 'src\Tools\IIoT.Edge.PackageValidationClient'
$packageOutput = Join-Path $repoRoot '.artifacts\nuget'
$releaseOutput = Join-Path $repoRoot '.artifacts\releases'
$manifestPath = Join-Path $releaseOutput 'release-manifest.json'
$summaryPath = Join-Path $releaseOutput 'release-summary.md'

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-PackageMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $nuspecEntry = $archive.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        if (-not $nuspecEntry) {
            throw "Package '$PackagePath' does not contain a nuspec."
        }

        $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        [pscustomobject]@{
            id = $nuspec.package.metadata.id
            version = $nuspec.package.metadata.version
            fileName = [System.IO.Path]::GetFileName($PackagePath)
            path = $PackagePath
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-EnabledModules {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConfigPath,
        [Parameter(Mandatory = $true)]
        [string]$DefaultModuleId
    )

    if (-not (Test-Path $ConfigPath)) {
        return @($DefaultModuleId)
    }

    $config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
    $enabled = $null
    if ($null -ne $config.Modules -and $null -ne $config.Modules.Enabled) {
        $enabled = $config.Modules.Enabled
    }

    if ($null -eq $enabled) {
        return @($DefaultModuleId)
    }

    if ($enabled -is [System.Array]) {
        $values = @($enabled | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }
    else {
        $values = @($enabled)
    }

    if ($values.Count -eq 0) {
        return @($DefaultModuleId)
    }

    return @($values)
}

function Get-GitMetadata {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        return [pscustomobject]@{
            branch = $null
            commit = $null
        }
    }

    Push-Location $repoRoot
    try {
        $branch = (git rev-parse --abbrev-ref HEAD 2>$null)
        $commit = (git rev-parse HEAD 2>$null)
        return [pscustomobject]@{
            branch = if ($LASTEXITCODE -eq 0) { $branch.Trim() } else { $null }
            commit = if ($LASTEXITCODE -eq 0) { $commit.Trim() } else { $null }
        }
    }
    finally {
        Pop-Location
    }
}

function New-ReleaseManifest {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Checks
    )

    $defaultModules = Get-EnabledModules -ConfigPath (Join-Path $repoRoot 'src\Edge\IIoT.Edge.Shell\appsettings.json') -DefaultModuleId 'Injection'
    $developmentModules = Get-EnabledModules -ConfigPath (Join-Path $repoRoot 'src\Edge\IIoT.Edge.Shell\appsettings.Development.json') -DefaultModuleId 'Injection'
    $versionPrefix = [string]((([xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props'))).Project.PropertyGroup.VersionPrefix) | Select-Object -First 1)
    $git = Get-GitMetadata
    $packages = Get-ChildItem -Path $packageOutput -Filter 'IIoT.Edge*.nupkg' -File |
        Where-Object { $_.Name -notlike '*.snupkg' } |
        Sort-Object Name |
        ForEach-Object { Get-PackageMetadata -PackagePath $_.FullName }

    $manifest = [ordered]@{
        generatedAt = (Get-Date).ToString('o')
        git = [ordered]@{
            branch = $git.branch
            commit = $git.commit
        }
        versionPrefix = $versionPrefix
        packages = @($packages)
        nugetSources = [ordered]@{
            local = $packageOutput
            shared = if ([string]::IsNullOrWhiteSpace($env:EDGE_SHARED_NUGET_FEED)) { $null } else { $env:EDGE_SHARED_NUGET_FEED }
        }
        enabledModules = [ordered]@{
            default = @($defaultModules)
            development = @($developmentModules)
        }
        checks = $Checks
    }

    return $manifest
}

function Write-ReleaseArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Checks
    )

    New-Item -Path $releaseOutput -ItemType Directory -Force | Out-Null
    $manifest = New-ReleaseManifest -Checks $Checks
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestPath -Encoding UTF8

    $summary = @(
        '# Release Rehearsal Summary',
        '',
        "- GeneratedAt: $($manifest.generatedAt)",
        "- Branch: $(if ([string]::IsNullOrWhiteSpace($manifest.git.branch)) { 'N/A' } else { $manifest.git.branch })",
        "- Commit: $(if ([string]::IsNullOrWhiteSpace($manifest.git.commit)) { 'N/A' } else { $manifest.git.commit })",
        "- VersionPrefix: $($manifest.versionPrefix)",
        "- Default Modules: $([string]::Join(', ', $manifest.enabledModules.default))",
        "- Development Modules: $([string]::Join(', ', $manifest.enabledModules.development))",
        '',
        '## Checks',
        ''
    )

    foreach ($entry in $manifest.checks.GetEnumerator()) {
        $summary += "- $($entry.Key): $($entry.Value)"
    }

    $summary += ''
    $summary += '## Packages'
    $summary += ''

    foreach ($package in $manifest.packages) {
        $summary += "- $($package.id) $($package.version) (" + [char]96 + "$($package.fileName)" + [char]96 + ")"
    }

    $summary | Set-Content -Path $summaryPath -Encoding UTF8
}

$checks = [ordered]@{
    shellBuild = 'pending'
    contractTests = 'pending'
    shellTests = 'pending'
    nonUiRegression = 'pending'
    packagePack = 'pending'
    integrationValidation = if ($SkipIntegrationValidation) { 'skipped' } else { 'pending' }
}

Write-Host 'Step 1/5: Build Edge shell'
dotnet build (Join-Path $repoRoot 'src\Edge\IIoT.Edge.Shell\IIoT.Edge.Shell.csproj') `
    -p:BuildInParallel=false `
    --disable-build-servers
$checks.shellBuild = 'passed'

Write-Host 'Step 2/6: Run module contract tests'
dotnet test (Join-Path $repoRoot 'src\Tests\IIoT.Edge.Module.ContractTests\IIoT.Edge.Module.ContractTests.csproj') `
    -p:BuildInParallel=false `
    --disable-build-servers
$checks.contractTests = 'passed'

Write-Host 'Step 3/6: Run shell contract tests'
dotnet test (Join-Path $repoRoot 'src\Tests\IIoT.Edge.Shell.Tests\IIoT.Edge.Shell.Tests.csproj') `
    -p:BuildInParallel=false `
    --disable-build-servers
$checks.shellTests = 'passed'

Write-Host 'Step 4/6: Run non-UI regression tests'
dotnet test (Join-Path $repoRoot 'src\Tests\IIoT.Edge.NonUiRegressionTests\IIoT.Edge.NonUiRegressionTests.csproj') `
    -p:BuildInParallel=false `
    --disable-build-servers
$checks.nonUiRegression = 'passed'

Write-Host 'Step 5/6: Pack reusable host and module packages'
& (Join-Path $PSScriptRoot 'PackEdgePackages.ps1') -Group all -Configuration $Configuration -CleanOutput
$checks.packagePack = 'passed'
Write-ReleaseArtifacts -Checks $checks

if ($SkipIntegrationValidation) {
    Write-Host 'Step 6/6: Integration validation skipped by request.'
    Write-ReleaseArtifacts -Checks $checks
    return
}

if (-not (Test-Path $integrationRepo)) {
    throw "Integration validation repository not found: $integrationRepo"
}

Write-Host 'Step 6/6: Validate package-only integration build'
& (Join-Path $integrationRepo 'scripts\BuildRelease.ps1') -Configuration $Configuration -ManifestPath $manifestPath
$checks.integrationValidation = 'passed'
Write-ReleaseArtifacts -Checks $checks
