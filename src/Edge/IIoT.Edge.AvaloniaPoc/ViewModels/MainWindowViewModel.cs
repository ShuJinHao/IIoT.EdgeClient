using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using IIoT.Edge.AvaloniaPoc.Localization;
using IIoT.Edge.AvaloniaPoc.Models;
using IIoT.Edge.AvaloniaPoc.Services;
using IIoT.Edge.AvaloniaPoc.Views;

namespace IIoT.Edge.AvaloniaPoc.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IAppLanguageService _languageService;

    public MainWindowViewModel(IAppLanguageService languageService)
    {
        _languageService = languageService;
        Monitor = new MonitorViewModel();
        Io = new IoViewModel();
        Equipment = new EquipmentViewModel();
        Log = new LogViewModel();
        DockFactory = new Factory();
        DockLayout = CreateDockLayout();
        CultureName = _languageService.CultureName;
        LanguageToggleText = _languageService.ToggleLabel;
    }

    public MonitorViewModel Monitor { get; }

    public IoViewModel Io { get; }

    public EquipmentViewModel Equipment { get; }

    public LogViewModel Log { get; }

    public Factory DockFactory { get; }

    public RootDock DockLayout { get; }

    [ObservableProperty]
    private string cultureName;

    [ObservableProperty]
    private string languageToggleText;

    [ObservableProperty]
    private bool isDialogOpen;

    [RelayCommand]
    private void ToggleLanguage()
    {
        _languageService.Toggle();
        CultureName = _languageService.CultureName;
        LanguageToggleText = _languageService.ToggleLabel;
        LocalizedDataGrid.RefreshHeaders();
        RefreshDockTitles();
    }

    [RelayCommand]
    private void OpenDialog()
    {
        IsDialogOpen = true;
    }

    [RelayCommand]
    private void CloseDialog()
    {
        IsDialogOpen = false;
    }

    private RootDock CreateDockLayout()
    {
        var monitor = CreateDocument("monitor", "Poc_Tab_Monitor", new MonitorView { DataContext = Monitor });
        var io = CreateDocument("io", "Poc_Tab_IO", new IoView { DataContext = Io });
        var equipment = CreateTool("equipment", "Poc_Tool_Equipment", new EquipmentView { DataContext = Equipment });
        var log = CreateTool("log", "Poc_Tool_Log", new LogView { DataContext = Log });

        var documents = new DocumentDock
        {
            Id = "documents",
            Title = "Documents",
            CanCreateDocument = false,
            VisibleDockables = [monitor, io],
            ActiveDockable = monitor,
            CanCloseLastDockable = false
        };

        var tools = new ToolDock
        {
            Id = "right-tools",
            Title = "Tools",
            Alignment = Alignment.Right,
            Proportion = 0.28,
            VisibleDockables = [equipment, log],
            ActiveDockable = equipment,
            IsExpanded = true
        };

        var mainDock = new ProportionalDock
        {
            Id = "main-dock",
            Title = "Main",
            Orientation = Orientation.Horizontal,
            VisibleDockables = [documents, new ProportionalDockSplitter(), tools],
            ActiveDockable = documents
        };

        var root = new RootDock
        {
            Id = "root",
            Title = "Root",
            VisibleDockables = [mainDock],
            ActiveDockable = mainDock,
            DefaultDockable = mainDock,
            IsCollapsable = false
        };

        DockFactory.InitLayout(root);
        return root;
    }

    private PocDockable CreateDocument(string id, string titleKey, Control view)
    {
        return new PocDockable(id, _languageService.GetText(titleKey), view)
        {
            DockGroup = "documents",
            CanPin = false,
            CanFloat = true,
            CanClose = false
        };
    }

    private PocDockable CreateTool(string id, string titleKey, Control view)
    {
        return new PocDockable(id, _languageService.GetText(titleKey), view)
        {
            DockGroup = "tools",
            CanFloat = true,
            CanClose = false,
            MinWidth = 260
        };
    }

    private void RefreshDockTitles()
    {
        RefreshTitle(DockLayout, "monitor", "Poc_Tab_Monitor");
        RefreshTitle(DockLayout, "io", "Poc_Tab_IO");
        RefreshTitle(DockLayout, "equipment", "Poc_Tool_Equipment");
        RefreshTitle(DockLayout, "log", "Poc_Tool_Log");
    }

    private void RefreshTitle(IDockable dockable, string id, string titleKey)
    {
        if (dockable.Id == id)
        {
            dockable.Title = _languageService.GetText(titleKey);
        }

        if (dockable is IDock dock && dock.VisibleDockables is not null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                RefreshTitle(child, id, titleKey);
            }
        }
    }
}
