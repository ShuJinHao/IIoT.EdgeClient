using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class ProductionDataViewBehaviorTests
{
    [Fact]
    public void HostRuntime_ShouldNotContainProductionDataBusinessSchemaFallback()
    {
        var root = FindRepositoryRoot();

        Assert.False(File.Exists(Path.Combine(
            root,
            "src",
            "Application",
            "IIoT.Edge.Application",
            "Features",
            "Production",
            "DataView",
            "ProductionDataQueryFacade.cs")));
        Assert.False(File.Exists(Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation",
            "Features",
            "Production",
            "DataView",
            "Views",
            "DataViewPage.axaml")));
        Assert.False(File.Exists(Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation",
            "Features",
            "Production",
            "DataView",
            "ViewModels",
            "DataViewModel.cs")));
    }

    [Fact]
    public void HostDependencyInjection_ShouldNotRegisterProductionDataFallbackFacade()
    {
        var root = FindRepositoryRoot();
        var applicationDi = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Application",
            "IIoT.Edge.Application",
            "DependencyInjection.cs"));
        var navigationDi = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation",
            "DependencyInjection.cs"));

        Assert.DoesNotContain("IProductionDataQueryFacade", applicationDi, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductionDataQueryFacade", applicationDi, StringComparison.Ordinal);
        Assert.DoesNotContain("DataViewModel", navigationDi, StringComparison.Ordinal);
        Assert.DoesNotContain("DataViewPage", navigationDi, StringComparison.Ordinal);
    }

    [Fact]
    public void HostNavigation_ShouldOnlyUsePluginProvidedDataViewRoutes()
    {
        var root = FindRepositoryRoot();
        var hostViewSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation",
            "Features",
            "Shell",
            "Views",
            "NavigationHostView.axaml.cs"));
        var standardRegistrationSource = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation",
            "PluginSystem",
            "StandardModuleNavigationRegistration.cs"));

        Assert.DoesNotContain("\"Production.DataView\"", hostViewSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DataViewPage", hostViewSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterStandardDataView", standardRegistrationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(DataViewPage)", standardRegistrationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualTestData_ShouldNotProvideProductionDataFacadeReplacement()
    {
        var root = FindRepositoryRoot();
        var visualTestDi = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.VisualTestData",
            "DependencyInjection.cs"));

        Assert.DoesNotContain("IProductionDataQueryFacade", visualTestDi, StringComparison.Ordinal);
        Assert.DoesNotContain("VisualTestProductionDataQueryFacade", visualTestDi, StringComparison.Ordinal);
    }

    [Fact]
    public void HostBootstrap_ReleaseBuild_ShouldNotRegisterVisualTestDataPresentation()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "Edge", "IIoT.Edge.Host.Bootstrap", "DependencyInjection.cs"));
        var project = File.ReadAllText(Path.Combine(root, "src", "Edge", "IIoT.Edge.Host.Bootstrap", "IIoT.Edge.Host.Bootstrap.csproj"));

        Assert.Contains("#if DEBUG", source, StringComparison.Ordinal);
        Assert.Contains("services.AddVisualTestDataPresentation(configuration);", source, StringComparison.Ordinal);
        Assert.Contains("Condition=\"'$(Configuration)' == 'Debug'\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Condition=\"'$(Configuration)' != 'Release'\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void HomogenizationDataView_ShouldNotInjectVisualTestRowsFromUiConfig()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Modules",
            "IIoT.Edge.Module.Homogenization",
            "Presentation",
            "HomogenizationNavigationViewModels.cs"));

        Assert.DoesNotContain("UI:VisualTestData:Enabled", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UI:VisualTestData:BatchCode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildVisualTestRows", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "IIoT.EdgeClient.slnx")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Could not locate IIoT.EdgeClient repository root.");
    }
}
