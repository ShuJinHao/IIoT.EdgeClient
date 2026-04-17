param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Z][A-Za-z0-9]+$')]
    [string]$ModuleName,

    [string]$ProcessType,

    [ValidateSet('Single', 'Batch')]
    [string]$UploadMode = 'Single'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ProcessType)) {
    $ProcessType = $ModuleName
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectName = "IIoT.Edge.Module.$ModuleName"
$moduleRoot = Join-Path $repoRoot "src\Modules\$projectName"
$moduleFile = Join-Path $moduleRoot "${ModuleName}Module.cs"
$projectFile = Join-Path $moduleRoot "$projectName.csproj"
$readmeFile = Join-Path $moduleRoot 'README.md'
$contractTestsRoot = Join-Path $repoRoot 'src\Tests\IIoT.Edge.Module.ContractTests'
$contractTestFile = Join-Path $contractTestsRoot "${ModuleName}ModuleContractTests.cs"

if (Test-Path $moduleRoot) {
    throw "Module folder already exists: $moduleRoot"
}

if (-not (Test-Path $contractTestsRoot)) {
    throw "Contract tests project not found: $contractTestsRoot"
}

if (Test-Path $contractTestFile) {
    throw "Contract test file already exists: $contractTestFile"
}

New-Item -Path $moduleRoot -ItemType Directory -Force | Out-Null

$csproj = @"
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <PackageId>$projectName</PackageId>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Application\IIoT.Edge.Application\IIoT.Edge.Application.csproj" />
    <ProjectReference Include="..\..\Infrastructure\IIoT.Edge.Infrastructure.Integration\IIoT.Edge.Infrastructure.Integration.csproj" />
    <ProjectReference Include="..\..\Presentation\IIoT.Edge.Presentation.Navigation\IIoT.Edge.Presentation.Navigation.csproj" />
    <ProjectReference Include="..\..\Runtime\IIoT.Edge.Runtime\IIoT.Edge.Runtime.csproj" />
    <ProjectReference Include="..\..\Shared\IIoT.Edge.Module.Abstractions\IIoT.Edge.Module.Abstractions.csproj" />
    <ProjectReference Include="..\..\Shared\IIoT.Edge.SharedKernel\IIoT.Edge.SharedKernel.csproj" />
    <ProjectReference Include="..\..\Shared\IIoT.Edge.UI.Shared\IIoT.Edge.UI.Shared.csproj" />
  </ItemGroup>

</Project>
"@

$moduleClass = @"
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Abstractions;
using IIoT.Edge.UI.Shared.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.$ModuleName;

public sealed class ${ModuleName}Module : IEdgeStationModule
{
    public const string ModuleKey = "$ModuleName";

    public string ModuleId => ModuleKey;

    public string ProcessType => "$ProcessType";

    public void RegisterServices(IServiceCollection services)
    {
        throw new NotImplementedException("Register $ModuleName services.");
    }

    public void RegisterViews(IViewRegistry viewRegistry)
    {
        throw new NotImplementedException("Register $ModuleName views.");
    }

    public void RegisterCellData(ICellDataRegistry registry)
    {
        throw new NotImplementedException("Register $ModuleName cell data.");
    }

    public void RegisterRuntime(IStationRuntimeRegistry registry)
    {
        throw new NotImplementedException("Register $ModuleName runtime factory.");
    }

    public void RegisterIntegrations(IProcessIntegrationRegistry registry)
    {
        registry.RegisterCloudUploader(ProcessType, ProcessUploadMode.$UploadMode);
    }
}
"@

$readme = @"
# $projectName

Module scaffold created by `scripts/NewEdgeModule.ps1`.

## Complete before opening a PR

- Replace all `NotImplementedException` placeholders.
- Add a module-specific `CellData` type.
- Add runtime factory and tasks.
- Add Cloud uploader registration.
- Add hardware profile provider registration if this module uses PLC devices.
- Register module routes with the `<ModuleId>.*` prefix only.
- Add or update module-specific non-UI coverage where needed.
- Update `${ModuleName}ModuleContractTests.cs` if this module requires PLC hardware profiles.
- Add a project reference to this module from the host or integration shell that should discover it.
- Decide whether the module should be enabled in configuration. No host module catalog edits are required.
"@

$contractTest = @"
using IIoT.Edge.Module.$ModuleName;

namespace IIoT.Edge.Module.ContractTests;

public sealed class ${ModuleName}ModuleContractTests : ModuleContractTestBase<${ModuleName}Module>
{
    // Set this to true if the module uses PLC devices and registers IModuleHardwareProfileProvider.
    protected override bool RequiresHardwareProfile => false;
}
"@

Set-Content -Path $projectFile -Value $csproj -Encoding UTF8
Set-Content -Path $moduleFile -Value $moduleClass -Encoding UTF8
Set-Content -Path $readmeFile -Value $readme -Encoding UTF8
Set-Content -Path $contractTestFile -Value $contractTest -Encoding UTF8

Write-Host "Created module scaffold at $moduleRoot"
Write-Host "Created contract test scaffold at $contractTestFile"
