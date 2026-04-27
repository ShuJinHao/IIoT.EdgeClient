param(
    [string]$Configuration = 'Release',

    [string]$OutputRoot = 'publish\edge-runtime',

    [string]$ManifestPath = 'scripts\edge-runtime.publish.json',

    [string]$LauncherAccountsSource,

    [switch]$CleanOutput
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'EdgeRuntime.Common.ps1')

$manifest = Load-EdgeRuntimePublishManifest -RepoRoot $repoRoot -ManifestPath $ManifestPath
$resolvedOutputRoot = Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $OutputRoot
$stagingRoot = Join-Path $resolvedOutputRoot ('.staging\' + [Guid]::NewGuid().ToString('N'))
$launcherPublishRoot = Join-Path $stagingRoot 'launcher'
$shellPublishRoot = Join-Path $stagingRoot 'shell'
$launcherRuntimeRoot = Join-Path $resolvedOutputRoot $manifest.launcherDirectory

function Publish-Project {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,

        [Parameter(Mandatory = $true)]
        [string]$PublishRoot
    )

    dotnet publish $ProjectPath `
        --configuration $Configuration `
        --output $PublishRoot `
        --nologo `
        --verbosity minimal `
        --disable-build-servers `
        -p:BuildInParallel=false `
        -p:RestoreDisableParallel=true `
        -p:SkipEdgeRuntimeLayoutSync=true
}

try {
    if ($CleanOutput -and (Test-Path $resolvedOutputRoot)) {
        Remove-Item -Path $resolvedOutputRoot -Recurse -Force
    }

    New-Item -Path $resolvedOutputRoot -ItemType Directory -Force | Out-Null
    New-Item -Path $stagingRoot -ItemType Directory -Force | Out-Null

    Publish-Project `
        -ProjectPath (Join-Path $repoRoot 'src\Edge\IIoT.Edge.Launcher\IIoT.Edge.Launcher.csproj') `
        -PublishRoot $launcherPublishRoot

    Publish-Project `
        -ProjectPath (Join-Path $repoRoot 'src\Edge\IIoT.Edge.Shell\IIoT.Edge.Shell.csproj') `
        -PublishRoot $shellPublishRoot

    if (Test-Path $launcherRuntimeRoot) {
        Remove-Item -Path $launcherRuntimeRoot -Recurse -Force
    }

    Copy-EdgeDirectoryContent -SourceDirectory $launcherPublishRoot -TargetDirectory $launcherRuntimeRoot
    Write-EdgeLauncherProfiles -Manifest $manifest -OutputPath (Join-Path $launcherRuntimeRoot 'launcher.profiles.json')
    Remove-EdgeLauncherShellArtifacts -LauncherRuntimeRoot $launcherRuntimeRoot

    $accountsSource = if (-not [string]::IsNullOrWhiteSpace($LauncherAccountsSource)) {
        Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $LauncherAccountsSource
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:EDGE_LAUNCHER_ACCOUNTS_FILE)) {
        Resolve-EdgeAbsolutePath -BasePath $repoRoot -PathValue $env:EDGE_LAUNCHER_ACCOUNTS_FILE
    }
    else {
        $null
    }

    if (-not [string]::IsNullOrWhiteSpace($accountsSource)) {
        if (-not (Test-Path $accountsSource)) {
            throw "Launcher accounts source was not found: $accountsSource"
        }

        Copy-Item `
            -Path $accountsSource `
            -Destination (Join-Path $launcherRuntimeRoot 'launcher.accounts.json') `
            -Force
    }
    else {
        Write-Warning "launcher.accounts.json was not injected. The runtime package contains launcher.accounts.sample.json only."
    }

    foreach ($runtime in $manifest.runtimes) {
        Sync-EdgeProcessRuntime `
            -RepoRoot $repoRoot `
            -Configuration $Configuration `
            -ShellRuntimeSource $shellPublishRoot `
            -RuntimeDefinition $runtime `
            -LayoutRoot $resolvedOutputRoot
    }

    Write-Host "Published runtime layout root: $resolvedOutputRoot"
}
finally {
    if (Test-Path $stagingRoot) {
        Remove-Item -Path $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
