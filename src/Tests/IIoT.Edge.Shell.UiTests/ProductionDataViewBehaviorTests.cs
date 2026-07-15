using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Shell.UiTests;

public sealed class ProductionDataViewBehaviorTests
{
    private static readonly string[] RemovedTypeNames =
    [
        "IProductionDataQueryFacade",
        "ProductionDataQueryFacade",
        "VisualTestProductionDataQueryFacade",
        "DataViewModel",
        "DataViewPage"
    ];

    [Fact]
    public void HostAssemblies_ShouldNotDefineRemovedProductionDataFallbackTypes()
    {
        var hostAssemblies = new[]
        {
            typeof(IIoT.Edge.Application.DependencyInjection).Assembly,
            typeof(IIoT.Edge.Presentation.Navigation.DependencyInjection).Assembly,
            typeof(IIoT.Edge.Presentation.VisualTestData.DependencyInjection).Assembly
        };

        var offenders = hostAssemblies
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(type => RemovedTypeNames.Contains(type.Name, StringComparer.Ordinal))
            .Select(static type => type.FullName)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void NavigationDependencyInjection_ShouldNotRegisterProductionDataFallback()
    {
        var services = new ServiceCollection();

        IIoT.Edge.Presentation.Navigation.DependencyInjection.AddNavigationPresentation(services);

        Assert.DoesNotContain(services, ContainsRemovedProductionDataType);
    }

    [Fact]
    public void VisualTestDataDependencyInjection_ShouldOnlyReplaceGenericPresentationFacades()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UI:VisualTestData:Enabled"] = "true"
            })
            .Build();
        var services = new ServiceCollection();

        IIoT.Edge.Presentation.VisualTestData.DependencyInjection
            .AddVisualTestDataPresentation(services, configuration);

        Assert.DoesNotContain(services, ContainsRemovedProductionDataType);
    }

    [Fact]
    public void HostBootstrap_ShouldReferenceVisualTestDataOnlyInDebugBuild()
    {
        var references = typeof(IIoT.Edge.Host.Bootstrap.DependencyInjection)
            .Assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name)
            .ToArray();

#if DEBUG
        Assert.Contains("IIoT.Edge.Presentation.VisualTestData", references);
#else
        Assert.DoesNotContain("IIoT.Edge.Presentation.VisualTestData", references);
#endif
    }

    private static bool ContainsRemovedProductionDataType(ServiceDescriptor descriptor)
        => ContainsRemovedProductionDataType(descriptor.ServiceType)
           || ContainsRemovedProductionDataType(descriptor.ImplementationType)
           || ContainsRemovedProductionDataType(descriptor.ImplementationInstance?.GetType());

    private static bool ContainsRemovedProductionDataType(Type? type)
        => type is not null && RemovedTypeNames.Contains(type.Name, StringComparer.Ordinal);
}
