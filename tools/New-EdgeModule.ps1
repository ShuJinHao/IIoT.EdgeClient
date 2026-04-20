[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ModuleId,

    [Parameter(Mandatory = $true)]
    [string]$ProcessType,

    [Parameter(Mandatory = $true)]
    [string]$DisplayName,

    [Parameter()]
    [string]$RepositoryRoot = (Split-Path -Path $PSScriptRoot -Parent),

    [Parameter()]
    [string]$OutputRoot = "",

    [switch]$SkipSolutionUpdate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    $normalizedBasePath = Resolve-NormalizedPath $BasePath
    $normalizedTargetPath = Resolve-NormalizedPath $TargetPath

    if (-not $normalizedBasePath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $normalizedBasePath += [System.IO.Path]::DirectorySeparatorChar
    }

    $baseUri = [System.Uri]::new($normalizedBasePath)
    $targetUri = [System.Uri]::new($normalizedTargetPath)
    $relativeUri = $baseUri.MakeRelativeUri($targetUri)
    $relativePath = [System.Uri]::UnescapeDataString($relativeUri.ToString())
    return $relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $directory = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Apply-Tokens {
    param(
        [Parameter(Mandatory = $true)][string]$Template,
        [Parameter(Mandatory = $true)][hashtable]$Tokens
    )

    $result = $Template
    foreach ($key in $Tokens.Keys) {
        $result = $result.Replace($key, [string]$Tokens[$key])
    }

    return $result
}

function Assert-Identifier {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($Value -notmatch '^[A-Za-z][A-Za-z0-9]*$') {
        throw "$Name must start with a letter and only contain letters or digits. Actual: '$Value'."
    }
}

Assert-Identifier -Value $ModuleId -Name "ModuleId"
Assert-Identifier -Value $ProcessType -Name "ProcessType"

$repositoryRootPath = Resolve-NormalizedPath $RepositoryRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRootPath "src/Modules"
}

$outputRootPath = Resolve-NormalizedPath $OutputRoot
[System.IO.Directory]::CreateDirectory($outputRootPath) | Out-Null

$projectName = "IIoT.Edge.Module.$ModuleId"
$namespaceRoot = $projectName
$moduleDirectory = Join-Path $outputRootPath $projectName
if (Test-Path -LiteralPath $moduleDirectory) {
    throw "Module directory '$moduleDirectory' already exists."
}

$applicationProject = Resolve-NormalizedPath (Join-Path $repositoryRootPath "src/Application/IIoT.Edge.Application/IIoT.Edge.Application.csproj")
$sharedKernelProject = Resolve-NormalizedPath (Join-Path $repositoryRootPath "src/Shared/IIoT.Edge.SharedKernel/IIoT.Edge.SharedKernel.csproj")
$moduleAbstractionsProject = Resolve-NormalizedPath (Join-Path $repositoryRootPath "src/Shared/IIoT.Edge.Module.Abstractions/IIoT.Edge.Module.Abstractions.csproj")
$uiSharedProject = Resolve-NormalizedPath (Join-Path $repositoryRootPath "src/Shared/IIoT.Edge.UI.Shared/IIoT.Edge.UI.Shared.csproj")

$solutionPath = Resolve-NormalizedPath (Join-Path $repositoryRootPath "IIoT.EdgeClient.slnx")
$projectPath = Join-Path $moduleDirectory "$projectName.csproj"

$tokens = @{
    "__MODULE_ID__" = $ModuleId
    "__PROCESS_TYPE__" = $ProcessType
    "__DISPLAY_NAME__" = $DisplayName
    "__PROJECT_NAME__" = $projectName
    "__NAMESPACE__" = $namespaceRoot
    "__APPLICATION_PROJECT__" = Get-RelativePathCompat -BasePath $moduleDirectory -TargetPath $applicationProject
    "__SHAREDKERNEL_PROJECT__" = Get-RelativePathCompat -BasePath $moduleDirectory -TargetPath $sharedKernelProject
    "__MODULE_ABSTRACTIONS_PROJECT__" = Get-RelativePathCompat -BasePath $moduleDirectory -TargetPath $moduleAbstractionsProject
    "__UI_SHARED_PROJECT__" = Get-RelativePathCompat -BasePath $moduleDirectory -TargetPath $uiSharedProject
}

$csprojTemplate = @'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <IsPackable>true</IsPackable>
    <PackageId>__PROJECT_NAME__</PackageId>
    <IsEdgePluginModule>true</IsEdgePluginModule>
    <PluginModuleId>__MODULE_ID__</PluginModuleId>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="__APPLICATION_PROJECT__" />
    <ProjectReference Include="__SHAREDKERNEL_PROJECT__" />
    <ProjectReference Include="__MODULE_ABSTRACTIONS_PROJECT__" />
    <ProjectReference Include="__UI_SHARED_PROJECT__" />
  </ItemGroup>

  <ItemGroup>
    <None Update="plugin.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
    </None>
  </ItemGroup>

</Project>
'@

$pluginManifestTemplate = @'
{
  "moduleId": "__MODULE_ID__",
  "displayName": "__DISPLAY_NAME__",
  "version": "1.0.0",
  "hostApiVersion": "1.0.0",
  "minHostVersion": "1.0.0",
  "maxHostVersion": "99.0.0",
  "entryAssembly": "__PROJECT_NAME__.dll",
  "entryType": "__NAMESPACE__.__MODULE_ID__Module",
  "supportedProcessType": "__PROCESS_TYPE__",
  "dependencies": []
}
'@

$moduleTemplate = @'
using __NAMESPACE__.Constants;
using __NAMESPACE__.Diagnostics;
using __NAMESPACE__.Integration;
using __NAMESPACE__.Payload;
using __NAMESPACE__.Presentation;
using __NAMESPACE__.Presentation.ViewModels;
using __NAMESPACE__.Runtime;
using __NAMESPACE__.Samples;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace __NAMESPACE__;

public sealed class __MODULE_ID__Module : IEdgeStationModule
{
    public string ModuleId => __MODULE_ID__ModuleConstants.ModuleId;

    public string ProcessType => __MODULE_ID__ModuleConstants.ProcessType;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IProcessCloudUploader, __MODULE_ID__CloudUploader>();
        services.AddSingleton<IDevelopmentSampleContributor, __MODULE_ID__DevelopmentSampleContributor>();
        services.AddSingleton<__MODULE_ID__DashboardViewModel>();
    }

    public void RegisterViews(IViewRegistry viewRegistry)
    {
        viewRegistry.Register__MODULE_ID__Views();
    }

    public void RegisterCellData(ICellDataRegistry registry)
    {
        registry.Register<__MODULE_ID__CellData>(ProcessType);
    }

    public void RegisterRuntime(IStationRuntimeRegistry registry)
    {
        registry.Register(new __MODULE_ID__StationRuntimeFactory());
    }

    public void RegisterIntegrations(IProcessIntegrationRegistry registry)
    {
        registry.RegisterCloudUploader(ProcessType, ProcessUploadMode.Single);
    }
}
'@

$constantsTemplate = @'
namespace __NAMESPACE__.Constants;

public static class __MODULE_ID__ModuleConstants
{
    public const string ModuleId = "__MODULE_ID__";
    public const string ProcessType = "__PROCESS_TYPE__";
    public const string DashboardViewId = "__MODULE_ID__.Dashboard";
}
'@

$payloadTemplate = @'
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace __NAMESPACE__.Payload;

public sealed class __MODULE_ID__CellData : CellDataBase
{
    public override string ProcessType => "__PROCESS_TYPE__";

    public override string DisplayLabel => Barcode;

    public string Barcode { get; set; } = string.Empty;

    public string WorkOrderNo { get; set; } = string.Empty;

    public string RuntimeStatus { get; set; } = "Pending";
}
'@

$validatorTemplate = @'
namespace __NAMESPACE__.Payload;

public sealed class __MODULE_ID__CellDataValidator
{
    public bool TryValidate(__MODULE_ID__CellData cellData, out string? error)
    {
        ArgumentNullException.ThrowIfNull(cellData);

        if (string.IsNullOrWhiteSpace(cellData.Barcode))
        {
            error = "Barcode is required.";
            return false;
        }

        error = null;
        return true;
    }
}
'@

$runtimeTemplate = @'
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.SharedKernel.Context;

namespace __NAMESPACE__.Runtime;

public sealed class __MODULE_ID__StationRuntimeFactory : IStationRuntimeFactory
{
    public string ModuleId => "__MODULE_ID__";

    public List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(context);
        return [];
    }
}
'@

$integrationTemplate = @'
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace __NAMESPACE__.Integration;

public sealed class __MODULE_ID__CloudUploader : IProcessCloudUploader
{
    public string ProcessType => "__PROCESS_TYPE__";

    public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

    public Task<CloudCallResult> UploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(records);

        return Task.FromResult(
            CloudCallResult.Failure(
                CloudCallOutcome.Exception,
                "__PROCESS_TYPE___uploader_stub_not_implemented"));
    }
}
'@

$diagnosticsTemplate = @'
namespace __NAMESPACE__.Diagnostics;

public static class __MODULE_ID__DiagnosticsFormatter
{
    public static string FormatModuleStatus(bool isEnabled)
        => isEnabled
            ? "__DISPLAY_NAME__ module is enabled."
            : "__DISPLAY_NAME__ module is disabled.";
}
'@

$sampleContributorTemplate = @'
using IIoT.Edge.Module.Abstractions;

namespace __NAMESPACE__.Samples;

public sealed class __MODULE_ID__DevelopmentSampleContributor : IDevelopmentSampleContributor
{
    public string ModuleId => "__MODULE_ID__";

    public Task EnsureConfigurationSamplesAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task EnsureRuntimeSamplesAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
'@

$viewIdsTemplate = @'
using __NAMESPACE__.Constants;

namespace __NAMESPACE__.Presentation;

public static class __MODULE_ID__ViewIds
{
    public const string Dashboard = __MODULE_ID__ModuleConstants.DashboardViewId;
}
'@

$navigationTemplate = @'
using __NAMESPACE__.Presentation.ViewModels;
using __NAMESPACE__.Presentation.Views;
using IIoT.Edge.UI.Shared.Modularity;

namespace __NAMESPACE__.Presentation;

public static class __MODULE_ID__NavigationRegistration
{
    public static IViewRegistry Register__MODULE_ID__Views(this IViewRegistry registry)
    {
        registry.RegisterRoute(
            __MODULE_ID__ViewIds.Dashboard,
            typeof(__MODULE_ID__DashboardPage),
            typeof(__MODULE_ID__DashboardViewModel),
            cacheView: true);

        registry.RegisterMenu(new MenuInfo
        {
            Title = "__DISPLAY_NAME__",
            ViewId = __MODULE_ID__ViewIds.Dashboard,
            Icon = "CubeOutline",
            Order = 50,
            RequiredPermission = string.Empty
        });

        return registry;
    }
}
'@

$viewModelTemplate = @'
using __NAMESPACE__.Constants;
using __NAMESPACE__.Diagnostics;
using __NAMESPACE__.Presentation;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace __NAMESPACE__.Presentation.ViewModels;

public sealed class __MODULE_ID__DashboardViewModel : PresentationViewModelBase
{
    public override string ViewId => __MODULE_ID__ViewIds.Dashboard;

    public override string ViewTitle => "__DISPLAY_NAME__";

    public string ModuleId => __MODULE_ID__ModuleConstants.ModuleId;

    public string ProcessType => __MODULE_ID__ModuleConstants.ProcessType;

    public string Summary => "__DISPLAY_NAME__ plugin skeleton generated by New-EdgeModule.ps1.";

    public __MODULE_ID__DashboardViewModel()
    {
        SetStatus(__MODULE_ID__DiagnosticsFormatter.FormatModuleStatus(isEnabled: false));
    }
}
'@

$pageTemplate = @'
<Page x:Class="__NAMESPACE__.Presentation.Views.__MODULE_ID__DashboardPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      Background="Transparent">
    <Grid Margin="24"
          Background="#111827">
        <Border Padding="24"
                CornerRadius="10"
                BorderBrush="#374151"
                BorderThickness="1"
                Background="#0F172A">
            <StackPanel>
                <TextBlock FontSize="24"
                           FontWeight="Bold"
                           Foreground="#E5E7EB"
                           Text="__DISPLAY_NAME__ Plugin" />
                <TextBlock Margin="0,12,0,0"
                           Foreground="#CBD5E1"
                           Text="{Binding Summary}"
                           TextWrapping="Wrap" />
                <TextBlock Margin="0,8,0,0"
                           Foreground="#A5B4FC"
                           Text="{Binding ModuleId}" />
                <TextBlock Margin="0,4,0,0"
                           Foreground="#93C5FD"
                           Text="{Binding ProcessType}" />
                <TextBlock Margin="0,12,0,0"
                           Foreground="#FDE68A"
                           Text="{Binding StatusMessage}"
                           TextWrapping="Wrap" />
            </StackPanel>
        </Border>
    </Grid>
</Page>
'@

$pageCodeBehindTemplate = @'
using System.Windows.Controls;

namespace __NAMESPACE__.Presentation.Views;

public partial class __MODULE_ID__DashboardPage : Page
{
    public __MODULE_ID__DashboardPage()
    {
        InitializeComponent();
    }
}
'@

Write-Utf8NoBom -Path $projectPath -Content (Apply-Tokens -Template $csprojTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "plugin.json") -Content (Apply-Tokens -Template $pluginManifestTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "$ModuleId`Module.cs") -Content (Apply-Tokens -Template $moduleTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "Constants\$ModuleId`ModuleConstants.cs") -Content (Apply-Tokens -Template $constantsTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "Payload\$ModuleId`CellData.cs") -Content (Apply-Tokens -Template $payloadTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "Payload\$ModuleId`CellDataValidator.cs") -Content (Apply-Tokens -Template $validatorTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "Runtime\$ModuleId`StationRuntimeFactory.cs") -Content (Apply-Tokens -Template $runtimeTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "Integration\$ModuleId`CloudUploader.cs") -Content (Apply-Tokens -Template $integrationTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "Diagnostics\$ModuleId`DiagnosticsFormatter.cs") -Content (Apply-Tokens -Template $diagnosticsTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "Samples\$ModuleId`DevelopmentSampleContributor.cs") -Content (Apply-Tokens -Template $sampleContributorTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "Presentation\$ModuleId`ViewIds.cs") -Content (Apply-Tokens -Template $viewIdsTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "Presentation\$ModuleId`NavigationRegistration.cs") -Content (Apply-Tokens -Template $navigationTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "Presentation\ViewModels\$ModuleId`DashboardViewModel.cs") -Content (Apply-Tokens -Template $viewModelTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "Presentation\Views\$ModuleId`DashboardPage.xaml") -Content (Apply-Tokens -Template $pageTemplate -Tokens $tokens)
Write-Utf8NoBom -Path (Join-Path $moduleDirectory "Presentation\Views\$ModuleId`DashboardPage.xaml.cs") -Content (Apply-Tokens -Template $pageCodeBehindTemplate -Tokens $tokens)

if (-not $SkipSolutionUpdate -and (Test-Path -LiteralPath $solutionPath)) {
    & dotnet sln $solutionPath add $projectPath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet sln add failed for '$projectPath'."
    }
}

Write-Host "Generated Edge module skeleton:"
Write-Host "  ModuleId: $ModuleId"
Write-Host "  ProcessType: $ProcessType"
Write-Host "  Path: $moduleDirectory"
