using System.Collections.ObjectModel;
using System.Windows.Input;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

public sealed class NavigationRailViewModel : BaseNotifyPropertyChanged
{
    private static readonly HashSet<string> FixedViewIds = new(StringComparer.OrdinalIgnoreCase)
    {
        CoreViewIds.Dashboard,
        "Production.DataView",
        "Production.CapacityView",
        "Hardware.IOView",
        "Production.Monitor",
        "Formula.RecipeView",
        "Config.ParamView",
        "Hardware.HardwareConfigView",
        "Hardware.PlcTaskBindingView",
        CoreViewIds.Diagnostics
    };

    private readonly IAppLanguageService _languageService;
    private NavigationItemViewModel _selectedItem = null!;

    public NavigationRailViewModel(IAppLanguageService languageService, IViewRegistry viewRegistry)
    {
        _languageService = languageService;
        Items = CreateItems(languageService, viewRegistry);
        Items[0].IsSelected = true;
        SelectedItem = Items[0];
        SelectCommand = new BaseCommand(parameter =>
        {
            if (parameter is NavigationItemViewModel item)
            {
                Select(item);
            }
        });
        _languageService.LanguageChanged += (_, _) => RefreshLanguage();
    }

    public ObservableCollection<NavigationItemViewModel> Items { get; }

    public NavigationItemViewModel SelectedItem
    {
        get => _selectedItem;
        private set
        {
            if (ReferenceEquals(_selectedItem, value))
            {
                return;
            }

            _selectedItem = value;
            OnPropertyChanged();
        }
    }

    public ICommand SelectCommand { get; }

    private void Select(NavigationItemViewModel item)
    {
        if (!item.IsEnabled)
        {
            return;
        }

        foreach (var navigationItem in Items)
        {
            navigationItem.IsSelected = ReferenceEquals(navigationItem, item);
        }

        SelectedItem = item;
    }

    private void RefreshLanguage()
    {
        foreach (var item in Items)
        {
            item.RefreshLanguage();
        }
    }

    private static ObservableCollection<NavigationItemViewModel> CreateItems(
        IAppLanguageService languageService,
        IViewRegistry viewRegistry)
    {
        var items = new ObservableCollection<NavigationItemViewModel>
        {
            new(languageService, CoreViewIds.Dashboard, "Navigation_Menu_Dashboard", "首页", "M4,5 L20,5 L20,19 L4,19 Z M7,8 L11,8 L11,12 L7,12 Z M13,8 L17,8 L17,16 L13,16 Z M7,14 L11,14 L11,16 L7,16 Z", true),
            new(languageService, "Production.DataView", "Navigation_Menu_Data", "数据", "M5,5 L19,5 L19,8 L5,8 Z M5,10 L19,10 L19,13 L5,13 Z M5,15 L19,15 L19,18 L5,18 Z", true),
            new(languageService, "Production.CapacityView", "Navigation_Menu_Capacity", "产能", "M5,18 L5,12 L9,12 L9,18 Z M11,18 L11,7 L15,7 L15,18 Z M17,18 L17,10 L21,10 L21,18 Z", true),
            new(languageService, "Hardware.IOView", "Navigation_Menu_Io", "IO", "M4,7 L10,7 L10,10 L4,10 Z M14,7 L20,7 L20,10 L14,10 Z M4,14 L10,14 L10,17 L4,17 Z M14,14 L20,14 L20,17 L14,17 Z M10,8.5 L14,8.5 M10,15.5 L14,15.5", true),
            new(languageService, "Production.Monitor", "Navigation_Menu_Monitor", "监控", "M4,18 L7,18 L9,13 L12,16 L15,8 L18,12 L20,6", true),
            new(languageService, "Formula.RecipeView", "Navigation_Menu_Recipe", "配方", "M6,4 L18,4 L18,20 L6,20 Z M9,8 L15,8 M9,12 L15,12 M9,16 L13,16", true),
            new(languageService, "Config.ParamView", "Navigation_Menu_ParamConfig", "参数", "M7,7 L17,7 M7,12 L17,12 M7,17 L17,17 M9,5 L9,9 M15,10 L15,14 M11,15 L11,19", true),
            new(languageService, "Hardware.HardwareConfigView", "Navigation_Menu_HardwareConfig", "硬件", "M6,8 L18,8 L18,16 L6,16 Z M9,5 L9,8 M15,5 L15,8 M9,16 L9,19 M15,16 L15,19 M4,11 L6,11 M18,11 L20,11 M4,13 L6,13 M18,13 L20,13", true),
            new(languageService, "Hardware.PlcTaskBindingView", "Navigation_Menu_PlcTaskBinding", "任务绑定", "M5,6 L10,6 L10,11 L5,11 Z M14,13 L19,13 L19,18 L14,18 Z M10,8.5 L14,15.5 M11,15.5 L14,15.5 L14,12.5", true),
            new(languageService, CoreViewIds.Diagnostics, "Navigation_Menu_CoreDiagnostics", "诊断", "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M12,7 L12,13 M12,16 L12,17", true)
        };

        foreach (var menu in viewRegistry.GetAllMenus()
            .Where(menu => !FixedViewIds.Contains(menu.ViewId))
            .OrderBy(menu => menu.Order)
            .ThenBy(menu => menu.Title, StringComparer.CurrentCulture))
        {
            items.Add(new NavigationItemViewModel(languageService, menu));
        }

        return items;
    }
}
