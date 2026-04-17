param(
    [ValidateSet('sdk', 'modules', 'all')]
    [string]$Group = 'all',

    [string]$Configuration = 'Release',

    [switch]$CleanOutput,

    [switch]$PublishSharedFeed
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageOutput = Join-Path $repoRoot '.artifacts\nuget'

$sdkProjects = @(
    'src\Shared\IIoT.Edge.Module.Abstractions\IIoT.Edge.Module.Abstractions.csproj',
    'src\Shared\IIoT.Edge.SharedKernel\IIoT.Edge.SharedKernel.csproj',
    'src\Shared\IIoT.Edge.UI.Shared\IIoT.Edge.UI.Shared.csproj',
    'src\Core\IIoT.Edge.Domain\IIoT.Edge.Domain.csproj',
    'src\Application\IIoT.Edge.Application\IIoT.Edge.Application.csproj',
    'src\Infrastructure\IIoT.Edge.Infrastructure.DeviceComm\IIoT.Edge.Infrastructure.DeviceComm.csproj',
    'src\Infrastructure\IIoT.Edge.Infrastructure.Integration\IIoT.Edge.Infrastructure.Integration.csproj',
    'src\Infrastructure\IIoT.Edge.Infrastructure.Persistence.Dapper\IIoT.Edge.Infrastructure.Persistence.Dapper.csproj',
    'src\Infrastructure\IIoT.Edge.Infrastructure.Persistence.EfCore\IIoT.Edge.Infrastructure.Persistence.EfCore.csproj',
    'src\Presentation\IIoT.Edge.Presentation.Navigation\IIoT.Edge.Presentation.Navigation.csproj',
    'src\Presentation\IIoT.Edge.Presentation.Panels\IIoT.Edge.Presentation.Panels.csproj',
    'src\Presentation\IIoT.Edge.Presentation.Shell\IIoT.Edge.Presentation.Shell.csproj',
    'src\Runtime\IIoT.Edge.Runtime\IIoT.Edge.Runtime.csproj',
    'src\Edge\IIoT.Edge.Host.Bootstrap\IIoT.Edge.Host.Bootstrap.csproj'
)

$moduleProjects = @(
    'src\Tools\ModuleSamples\IIoT.Edge.Module.DryRun\IIoT.Edge.Module.DryRun.csproj',
    'src\Modules\IIoT.Edge.Module.Injection\IIoT.Edge.Module.Injection.csproj',
    'src\Modules\IIoT.Edge.Module.Stacking\IIoT.Edge.Module.Stacking.csproj'
)

$projects = switch ($Group) {
    'sdk' { $sdkProjects }
    'modules' { $moduleProjects }
    'all' { $sdkProjects + $moduleProjects }
}

if ($CleanOutput -and (Test-Path $packageOutput)) {
    Remove-Item -Path (Join-Path $packageOutput '*.nupkg') -Force -ErrorAction SilentlyContinue
    Remove-Item -Path (Join-Path $packageOutput '*.snupkg') -Force -ErrorAction SilentlyContinue
}

New-Item -Path $packageOutput -ItemType Directory -Force | Out-Null

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    Write-Host "Packing $projectPath"

    dotnet pack $projectPath `
        --configuration $Configuration `
        --output $packageOutput `
        --nologo `
        --disable-build-servers `
        --verbosity minimal `
        -p:BuildInParallel=false `
        -p:RestoreDisableParallel=true
}

if (-not $PublishSharedFeed) {
    return
}

$sharedFeed = $env:EDGE_SHARED_NUGET_FEED
if ([string]::IsNullOrWhiteSpace($sharedFeed)) {
    Write-Host 'EDGE_SHARED_NUGET_FEED is not configured; skipping shared feed publish.'
    return
}

$packages = Get-ChildItem -Path $packageOutput -Filter '*.nupkg' |
    Where-Object { $_.Name -notlike '*.snupkg' }

if ($sharedFeed -match '^(http|https)://') {
    foreach ($package in $packages) {
        Write-Host "Publishing $($package.Name) to $sharedFeed"
        dotnet nuget push $package.FullName --source $sharedFeed --skip-duplicate
    }
    return
}

New-Item -Path $sharedFeed -ItemType Directory -Force | Out-Null
foreach ($package in $packages) {
    $destination = Join-Path $sharedFeed $package.Name
    Copy-Item -Path $package.FullName -Destination $destination -Force
}
