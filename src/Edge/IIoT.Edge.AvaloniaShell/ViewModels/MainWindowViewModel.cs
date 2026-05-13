using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using IIoT.Edge.UI.Avalonia.Docking;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Modularity;
using IIoT.Edge.UI.Avalonia.Services;

namespace IIoT.Edge.AvaloniaShell.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IAvaloniaViewRegistry _viewRegistry;
    private readonly IAvaloniaNavigationService _navigationService;
    private readonly IAvaloniaDialogService _dialogService;
    private readonly Dictionary<string, string> _dockTitleKeys = new(StringComparer.OrdinalIgnoreCase);

    public MainWindowViewModel(
        IServiceProvider services,
        IAvaloniaLanguageService languageService,
        IAvaloniaViewRegistry viewRegistry,
        IAvaloniaNavigationService navigationService,
        IAvaloniaDialogService dialogService)
    {
        _services = services;
        _languageService = languageService;
        _viewRegistry = viewRegistry;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _dialogService.DialogRequested += HandleDialogRequested;

        DockFactory = new Factory();
        DockLayout = CreateDockLayout();
        MenuItems = _viewRegistry.GetAllMenus()
            .Select(item => new ShellMenuItemViewModel(item.ViewId, _languageService.GetText(item.TitleResourceKey), Navigate))
            .ToArray();

        CultureName = _languageService.CultureName;
        LanguageToggleText = _languageService.ToggleLabel;
        DialogTitle = _languageService.GetText("Shell_Dialog_Title");
        DialogMessage = _languageService.GetText("Shell_Dialog_Message");
    }

    public Factory DockFactory { get; }

    public RootDock DockLayout { get; }

    public IReadOnlyList<ShellMenuItemViewModel> MenuItems { get; }

    [ObservableProperty]
    private string cultureName;

    [ObservableProperty]
    private string languageToggleText;

    [ObservableProperty]
    private bool isDialogOpen;

    [ObservableProperty]
    private string dialogTitle;

    [ObservableProperty]
    private string dialogMessage;

    [RelayCommand]
    private void ToggleLanguage()
    {
        _languageService.Toggle();
        CultureName = _languageService.CultureName;
        LanguageToggleText = _languageService.ToggleLabel;
        DialogTitle = _languageService.GetText("Shell_Dialog_Title");
        DialogMessage = _languageService.GetText("Shell_Dialog_Message");
        LocalizedDataGrid.RefreshHeaders();
        RefreshDockTitles();
        RefreshMenuTitles();
    }

    [RelayCommand]
    private Task OpenDialogAsync()
    {
        return _dialogService.ShowInfoAsync(
            _languageService.GetText("Shell_Dialog_Title"),
            _languageService.GetText("Shell_Dialog_Message"));
    }

    [RelayCommand]
    private void CloseDialog()
    {
        IsDialogOpen = false;
    }

    private RootDock CreateDockLayout()
    {
        var documentDockables = new List<IDockable>();
        var toolDockables = new List<IDockable>();

        foreach (var pane in _viewRegistry.GetAllDockPanes())
        {
            var dockable = CreateDockable(pane);
            if (pane.IsToolPane)
            {
                toolDockables.Add(dockable);
            }
            else
            {
                documentDockables.Add(dockable);
            }
        }

        var documents = new DocumentDock
        {
            Id = "documents",
            Title = "Documents",
            CanCreateDocument = false,
            VisibleDockables = documentDockables,
            ActiveDockable = documentDockables.FirstOrDefault(),
            CanCloseLastDockable = false
        };

        var tools = new ToolDock
        {
            Id = "right-tools",
            Title = "Tools",
            Alignment = Alignment.Right,
            Proportion = 0.28,
            VisibleDockables = toolDockables,
            ActiveDockable = toolDockables.FirstOrDefault(),
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

    private AvaloniaDockable CreateDockable(AvaloniaDockPaneInfo pane)
    {
        var registration = _viewRegistry.GetViewRegistration(pane.ViewId)
            ?? throw new InvalidOperationException($"View '{pane.ViewId}' is not registered.");

        var view = registration.CreateView(_services);
        var dockable = new AvaloniaDockable(pane.ViewId, _languageService.GetText(pane.TitleResourceKey), view)
        {
            DockGroup = pane.DockGroup,
            CanPin = pane.IsToolPane,
            CanFloat = true,
            CanClose = false,
            MinWidth = pane.IsToolPane ? 260 : 0
        };

        _dockTitleKeys[pane.ViewId] = pane.TitleResourceKey;
        return dockable;
    }

    private void Navigate(string viewId)
    {
        _navigationService.NavigateTo(viewId);
        ActivateDockable(DockLayout, viewId);
    }

    private static bool ActivateDockable(IDockable dockable, string id)
    {
        if (dockable is IDock dock && dock.VisibleDockables is not null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                if (child.Id == id)
                {
                    dock.ActiveDockable = child;
                    return true;
                }

                if (ActivateDockable(child, id))
                {
                    dock.ActiveDockable = child;
                    return true;
                }
            }
        }

        return false;
    }

    private void RefreshDockTitles()
    {
        foreach (var pair in _dockTitleKeys)
        {
            RefreshTitle(DockLayout, pair.Key, pair.Value);
        }
    }

    private void RefreshMenuTitles()
    {
        foreach (var menu in MenuItems)
        {
            var info = _viewRegistry.GetAllMenus().FirstOrDefault(item => item.ViewId == menu.ViewId);
            if (info is not null)
            {
                menu.Title = _languageService.GetText(info.TitleResourceKey);
            }
        }
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

    private void HandleDialogRequested(object? sender, AvaloniaDialogRequest request)
    {
        DialogTitle = request.Title;
        DialogMessage = request.Message;
        IsDialogOpen = true;
    }
}
