using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Recipe;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Module.Contracts.Production;
using IIoT.Edge.Presentation.Panels.Features.DeviceSelection;
using IIoT.Edge.Presentation.Panels.Features.Equipment;
using IIoT.Edge.Module.Contracts.DataPipeline.Recipe;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.PluginSystem;
using Xunit;

namespace IIoT.Edge.Shell.UiTests;

public sealed class EquipmentViewModelBehaviorTests
{
    [AvaloniaFact]
    public void EquipmentView_WhenCurrentProcessIsAvailable_ShouldRenderVisibleBusinessProcessSlot()
    {
        var view = new EquipmentView
        {
            DataContext = new { CurrentProcessDisplayName = "Neutral process" }
        };
        var window = new Window { Content = view };

        try
        {
            window.Show();

            var processText = Assert.Single(view
                .GetVisualDescendants()
                .OfType<TextBlock>(),
                text => text.Classes.Contains("edge-equipment-plan-value")
                        && string.Equals(text.Text, "Neutral process", StringComparison.Ordinal));
            Assert.True(processText.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void CurrentProcessDisplayName_WhenSingleDataViewUsesGenericMenuTitle_ShouldUseModuleDisplayName()
    {
        var viewModel = CreateViewModel(
            [
                CreateMenu("TestPlugin.DataView", "数据", "TestPlugin_Menu_Data")
            ],
            [
                new FakeProcessModule("TestPlugin", "测试工序")
            ]);

        Assert.Equal("测试工序", viewModel.CurrentProcessDisplayName);
    }

    [Fact]
    public void CurrentProcessDisplayName_WhenBusinessProcessNameExists_ShouldOverrideGenericFallback()
    {
        var viewModel = CreateViewModel(
            [
                CreateMenu("TestPlugin.DataView", "数据", "TestPlugin_Menu_Data")
            ],
            [
                new FakeProcessModule("TestPlugin", "测试工序")
            ]);

        viewModel.ProcessName = "测试插件运行态";

        Assert.Equal("测试插件运行态", viewModel.CurrentProcessDisplayName);
    }

    [Fact]
    public void CurrentProcessDisplayName_WhenSingleDataViewTitleIsSpecific_ShouldKeepSpecificBusinessTitle()
    {
        var viewModel = CreateViewModel(
            [
                CreateMenu("TestPlugin.DataView", "测试插件甲采样", "TestPlugin_Menu_ProcessData")
            ],
            [
                new FakeProcessModule("TestPlugin", "测试插件")
            ]);

        Assert.Equal("测试插件甲采样", viewModel.CurrentProcessDisplayName);
    }

    [Fact]
    public void CurrentProcessDisplayName_WhenMultipleGenericDataViewsExist_ShouldStayAmbiguous()
    {
        var viewModel = CreateViewModel(
            [
                CreateMenu("TestPluginA.DataView", "数据", "TestPluginA_Menu_Data"),
                CreateMenu("TestPluginB.DataView", "数据", "TestPluginB_Menu_Data")
            ],
            [
                new FakeProcessModule("TestPluginA", "测试工序甲"),
                new FakeProcessModule("TestPluginB", "测试工序乙")
            ]);

        Assert.Equal("未确定", viewModel.CurrentProcessDisplayName);
    }

    [Fact]
    public void CurrentProcessDisplayName_WhenLanguageChanges_ShouldRefreshModuleDisplayName()
    {
        var languageService = new TestAppLanguageService();
        var viewModel = CreateViewModel(
            [
                CreateMenu("TestPlugin.DataView", "数据", "TestPlugin_Menu_Data")
            ],
            [
                new FakeProcessModule(
                    "TestPlugin",
                    () => languageService.Current.Name == "en-US" ? "TestPlugin" : "测试工序")
            ],
            languageService);
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        languageService.Change(CultureInfo.GetCultureInfo("en-US"));

        Assert.Contains(nameof(EquipmentViewModel.CurrentProcessDisplayName), changedProperties);
        Assert.Equal("TestPlugin", viewModel.CurrentProcessDisplayName);
    }

    [Fact]
    public void DeviceFilter_WhenLanguageChanges_ShouldRefreshAllSummaryDisplayWithoutChangingSelection()
    {
        var languageService = new TestAppLanguageService();
        var viewModel = CreateViewModel([], [], languageService);

        Assert.Equal(IDeviceSelectionService.AllFilterKey, viewModel.SelectedDeviceFilter?.Key);
        Assert.Equal("全部/汇总", viewModel.SelectedDeviceFilter?.DisplayName);

        languageService.Change(CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(IDeviceSelectionService.AllFilterKey, viewModel.SelectedDeviceFilter?.Key);
        Assert.Equal("All / Summary", viewModel.SelectedDeviceFilter?.DisplayName);
        Assert.Single(viewModel.DeviceFilters);
    }

    [AvaloniaFact]
    public async Task DeviceFilter_WhenDeviceIsRenamed_ShouldKeepRealNameKeyAndResolveStablePlcCode()
    {
        var panelService = new FakeEquipmentPanelService
        {
            HardwareSnapshots =
            [
                new HardwareSnapshot("旧名称", "192.168.1.10", "PLC", true)
                {
                    PlcCode = "P1-AP01"
                }
            ]
        };
        var selectionService = new DeviceSelectionService();
        var viewModel = CreateViewModel(
            [],
            [],
            equipmentPanelService: panelService,
            deviceSelectionService: selectionService);

        try
        {
            await viewModel.OnActivatedAsync();
            viewModel.SelectedDeviceFilter = Assert.Single(
                viewModel.DeviceFilters,
                static option => option.Key == "旧名称");

            Assert.Equal("旧名称", selectionService.SelectedDeviceKey);
            Assert.Equal("P1-AP01", selectionService.SelectedPlcCode);
            Assert.Equal("旧名称", viewModel.SelectedDeviceFilter?.Key);
            Assert.Equal("P1-AP01 · 旧名称", viewModel.SelectedDeviceFilter?.DisplayName);

            panelService.HardwareSnapshots =
            [
                new HardwareSnapshot("新名称", "192.168.1.10", "PLC", true)
                {
                    PlcCode = "P1-AP01"
                }
            ];

            await viewModel.OnActivatedAsync();

            Assert.Equal("旧名称", selectionService.SelectedDeviceKey);
            Assert.Null(selectionService.SelectedPlcCode);

            viewModel.SelectedDeviceFilter = Assert.Single(
                viewModel.DeviceFilters,
                static option => option.Key == "新名称");

            Assert.Equal("新名称", selectionService.SelectedDeviceKey);
            Assert.Equal("P1-AP01", selectionService.SelectedPlcCode);
            Assert.Equal("新名称", viewModel.SelectedDeviceFilter?.Key);
            Assert.Equal("P1-AP01 · 新名称", viewModel.SelectedDeviceFilter?.DisplayName);
        }
        finally
        {
            await viewModel.OnDeactivatedAsync();
        }
    }

    private static EquipmentViewModel CreateViewModel(
        IEnumerable<MenuInfo> menus,
        IEnumerable<IEdgeProcessModule> processModules,
        TestAppLanguageService? languageService = null,
        IEquipmentPanelService? equipmentPanelService = null,
        DeviceSelectionService? deviceSelectionService = null)
    {
        var viewRegistry = new FakeViewRegistry();
        foreach (var menu in menus)
        {
            viewRegistry.RegisterMenu(menu);
        }

        return new EquipmentViewModel(
            equipmentPanelService ?? new FakeEquipmentPanelService(),
            new FakeRecipeService(),
            new FakeProductionPlanSelectionServiceResolver(),
            new FakeProductionPlanSelectionPopupService(),
            languageService ?? new TestAppLanguageService(),
            deviceSelectionService ?? new DeviceSelectionService(),
            viewRegistry,
            processModules);
    }

    private static MenuInfo CreateMenu(string viewId, string title, string titleResourceKey)
        => new()
        {
            ViewId = viewId,
            Title = title,
            TitleResourceKey = titleResourceKey
        };

    private sealed class FakeViewRegistry : IViewRegistry
    {
        private readonly List<MenuInfo> _menus = [];

        public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
        {
        }

        public void RegisterRoute(
            string viewId,
            Type viewType,
            Type viewModelType,
            Func<IServiceProvider, ViewModelBase> viewModelFactory,
            bool cacheView = true)
        {
        }

        public void RegisterMenu(MenuInfo menuInfo) => _menus.Add(menuInfo);

        public void RegisterAnchorable(AnchorableInfo info, Type viewType, Type viewModelType, bool cacheView = true)
        {
        }

        public void RegisterAnchorable(
            AnchorableInfo info,
            Type viewType,
            Type viewModelType,
            Func<IServiceProvider, ViewModelBase> viewModelFactory,
            bool cacheView = true)
        {
        }

        public ViewRegistration? GetViewRegistration(string viewId) => null;

        public IReadOnlyList<MenuInfo> GetAllMenus() => _menus;

        public IReadOnlyList<AnchorableInfo> GetAllAnchorables() => [];
    }

    private sealed class FakeEquipmentPanelService : IEquipmentPanelService
    {
        public IReadOnlyList<HardwareSnapshot> HardwareSnapshots { get; set; } = [];

        public Task<List<HardwareSnapshot>> GetHardwareStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(HardwareSnapshots.ToList());

        public Task<RecipeSnapshot?> GetRecipeSnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<RecipeSnapshot?>(null);

        public Task<CapacitySnapshot> GetCapacitySnapshotAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CapacitySnapshot(
                0,
                0,
                0,
                "0.0%",
                "--",
                0,
                0,
                0,
                "00:00-00:00"));
    }

    private sealed class FakeRecipeService : IRecipeService
    {
        public RecipeSource ActiveSource => RecipeSource.Cloud;

        public RecipeData? ActiveRecipe => null;

        public RecipeData? CloudRecipe => null;

        public RecipeData? LocalRecipe => null;

        public event Action? RecipeChanged
        {
            add { }
            remove { }
        }

        public RecipeParam? GetParam(string name) => null;

        public IReadOnlyDictionary<string, RecipeParam> GetAllParams()
            => new Dictionary<string, RecipeParam>(StringComparer.OrdinalIgnoreCase);

        public Task<bool> PullFromCloudAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public void SwitchSource(RecipeSource source)
        {
        }

        public void SetLocalParam(string name, double? min, double? max, string unit)
        {
        }

        public void RemoveLocalParam(string name)
        {
        }

        public void LoadFromFile()
        {
        }

        public void SaveToFile()
        {
        }
    }

    private sealed class FakeProductionPlanSelectionServiceResolver : IProductionPlanSelectionServiceResolver
    {
        public IProductionPlanSelectionService? ResolveCurrent() => null;
    }

    private sealed class FakeProductionPlanSelectionPopupService : IProductionPlanSelectionPopupService
    {
        public Task<ProductionPlanOption?> ShowAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<ProductionPlanOption?>(null);
    }

    private sealed class FakeProcessModule : IEdgeProcessModule
    {
        private readonly Func<string> _displayName;

        public FakeProcessModule(string moduleId, string displayName)
            : this(moduleId, () => displayName)
        {
        }

        public FakeProcessModule(string moduleId, Func<string> displayName)
        {
            ModuleId = moduleId;
            _displayName = displayName;
        }

        public string ModuleId { get; }

        public string ProcessType => ModuleId;

        public string DisplayName => _displayName();

        public void Configure(IEdgeProcessModuleBuilder builder)
        {
        }
    }
}
