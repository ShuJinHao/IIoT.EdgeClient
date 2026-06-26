using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Application.Features.Production.DataView;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Features.Production.DataView;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.Presentation.VisualTestData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class ProductionDataViewBehaviorTests
{
    [Fact]
    public async Task ProductionDataQueryFacade_DefaultRuntime_ShouldReturnEmptyRealRecordSet()
    {
        var facade = new ProductionDataQueryFacade();

        var snapshot = await facade.QueryAsync(
            IDeviceSelectionService.AllFilterKey,
            TestContext.Current.CancellationToken);

        Assert.Empty(snapshot.Records);
    }

    [Fact]
    public async Task DataViewModel_WhenRecordsExist_ShouldExposeDeviceNameColumnData()
    {
        var facade = new RecordingProductionDataQueryFacade([
            new ProductionRecordItem(
                DeviceName: "P1-AP01",
                Time: "13:00",
                BatchNo: "BATCH-REAL-001",
                Total: 1,
                OkCount: 1,
                NgCount: 0,
                Yield: "100.0%")
        ]);
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("P1-AP01");
        var viewModel = new DataViewModel(facade, new TestAppLanguageService(), selectionService);

        await viewModel.OnActivatedAsync();

        var row = Assert.Single(viewModel.Records);
        Assert.Equal("P1-AP01", row.DeviceName);
        Assert.Equal("P1-AP01", facade.LastSelectedDeviceKey);
    }

    [Fact]
    public async Task DataAndCapacityViews_ShouldUseSameGlobalDeviceSelectionKey()
    {
        var selectionService = new DeviceSelectionService();
        selectionService.SelectDevice("P1-AP02");
        var productionFacade = new RecordingProductionDataQueryFacade([]);
        var capacityFacade = new RecordingCapacityQueryFacade();
        var dataViewModel = new DataViewModel(productionFacade, new TestAppLanguageService(), selectionService);
        var capacityViewModel = new CapacityViewModel(capacityFacade, new TestAppLanguageService(), selectionService);

        await dataViewModel.OnActivatedAsync();
        await capacityViewModel.OnActivatedAsync();

        Assert.Equal("P1-AP02", productionFacade.LastSelectedDeviceKey);
        Assert.Equal("P1-AP02", capacityFacade.LastLoadTodayDeviceName);
    }

    [Fact]
    public void VisualTestData_WhenDisabled_ShouldNotReplaceRuntimeProductionFacade()
    {
        var services = new ServiceCollection();
        services.AddTransient<IProductionDataQueryFacade, ProductionDataQueryFacade>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{VisualTestDataOptions.SectionName}:Enabled"] = "false"
            })
            .Build();

        services.AddVisualTestDataPresentation(configuration);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<ProductionDataQueryFacade>(provider.GetRequiredService<IProductionDataQueryFacade>());
    }

    [Fact]
    public void HostBootstrap_ReleaseBuild_ShouldNotRegisterVisualTestProductionDataFacade()
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
    public void DataViewPage_WhenEmpty_ShouldStillShowTableHeaders()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation",
            "Features",
            "Production",
            "DataView",
            "Views",
            "DataViewPage.axaml"));

        Assert.Contains("ShowContentWhenEmpty=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{DynamicResource Navigation_Column_DeviceName}\"", xaml, StringComparison.Ordinal);
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

    private sealed class RecordingProductionDataQueryFacade(IReadOnlyList<ProductionRecordItem> records)
        : IProductionDataQueryFacade
    {
        public string? LastSelectedDeviceKey { get; private set; }

        public Task<DataViewSnapshot> QueryAsync(string selectedDeviceKey, CancellationToken cancellationToken = default)
        {
            LastSelectedDeviceKey = selectedDeviceKey;
            return Task.FromResult(new DataViewSnapshot(records.ToList()));
        }
    }

    private sealed class RecordingCapacityQueryFacade : ICapacityQueryFacade
    {
        public event Action<EdgeUploadGateSnapshot>? UploadGateChanged;

        public bool IsOnline => true;

        public string? LastLoadTodayDeviceName { get; private set; }

        public IReadOnlyList<string> GetDeviceNames() => ["P1-AP01", "P1-AP02"];

        public Task<CapacityViewResult> LoadTodayAsync(string plcName, CancellationToken cancellationToken = default)
        {
            LastLoadTodayDeviceName = plcName;
            return Task.FromResult(new CapacityViewResult([], 0, 0, 0, "0%", "0"));
        }

        public Task<CapacityViewResult> QueryHistoryAsync(
            string queryMode,
            DateTime queryDate,
            string plcName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new CapacityViewResult([], 0, 0, 0, "0%", "0"));

        public void RaiseUploadGateChanged(EdgeUploadGateSnapshot snapshot)
            => UploadGateChanged?.Invoke(snapshot);
    }
}
