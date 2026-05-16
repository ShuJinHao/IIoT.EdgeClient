using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Presentation.Shell.Avalonia.Features.SysMenu.ViewModels;
using IIoT.Edge.Presentation.Shell.Avalonia.ViewModels;
using IIoT.Edge.UI.Avalonia.Docking;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Modularity;
using IIoT.Edge.UI.Avalonia.Services;
using System.Collections.ObjectModel;

namespace IIoT.Edge.AvaloniaShell.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IAvaloniaViewRegistry _viewRegistry;
    private readonly IAvaloniaDialogService _dialogService;
    private readonly IAvaloniaDispatcherService _dispatcherService;
    private readonly IAuthService _authService;
    private readonly Dictionary<string, string> _dockTitleKeys = new(StringComparer.OrdinalIgnoreCase);

    public MainWindowViewModel(
        IServiceProvider services,
        IAvaloniaLanguageService languageService,
        IAvaloniaViewRegistry viewRegistry,
        IAvaloniaDialogService dialogService,
        IAvaloniaDispatcherService dispatcherService,
        IAuthService authService,
        HeaderViewModel headerViewModel,
        FooterViewModel footerViewModel,
        LoginViewModel loginViewModel,
        SysMenuViewModel sysMenuViewModel)
    {
        _services = services;
        _languageService = languageService;
        _viewRegistry = viewRegistry;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        _authService = authService;
        HeaderViewModel = headerViewModel;
        FooterViewModel = footerViewModel;
        LoginViewModel = loginViewModel;
        SysMenuViewModel = sysMenuViewModel;
        LoginViewModel.LoginSucceeded += () => IsDialogOpen = false;
        SysMenuViewModel.LoginRequested += () => IsDialogOpen = true;
        SysMenuViewModel.NavigationRequested += OnSysMenuNavigationRequested;
        _dialogService.DialogRequested += OnDialogRequested;
        _authService.AuthStateChanged += _ => _dispatcherService.Post(RefreshAuthState);

        DockFactory = new Factory();
        DockLayout = CreateDockLayout();
        MenuItems = SysMenuViewModel.MenuItems;
        SelectMenuItem(DockLayout.ActiveDockable?.Id);

        CultureName = _languageService.CultureName;
        LanguageToggleText = _languageService.ToggleLabel;
    }

    public Factory DockFactory { get; }

    public RootDock DockLayout { get; }

    public ObservableCollection<AvaloniaDockable> RightToolDockables { get; } = [];

    public IReadOnlyList<SysMenuItemViewModel> MenuItems { get; }

    public HeaderViewModel HeaderViewModel { get; }

    public FooterViewModel FooterViewModel { get; }

    public LoginViewModel LoginViewModel { get; }

    public SysMenuViewModel SysMenuViewModel { get; }

    [ObservableProperty]
    private string cultureName;

    [ObservableProperty]
    private string languageToggleText;

    [ObservableProperty]
    private bool isDialogOpen;

    [ObservableProperty]
    private bool isSystemDialogOpen;

    [ObservableProperty]
    private AvaloniaDockable? activeRightToolDockable;

    [ObservableProperty]
    private AvaloniaDockable? equipmentToolDockable;

    [ObservableProperty]
    private AvaloniaDockable? logToolDockable;

    [ObservableProperty]
    private AvaloniaDialogRequest? systemDialogRequest;

    public bool IsSystemDialogConfirm => SystemDialogRequest?.Kind == AvaloniaDialogRequestKind.Confirm;

    public string SystemDialogPrimaryActionText
        => _languageService.GetText(IsSystemDialogConfirm ? "Shell_Action_Confirm" : "Shell_Action_Ok");

    public string LoginButtonText => SysMenuViewModel.LoginButtonText;

    partial void OnSystemDialogRequestChanged(AvaloniaDialogRequest? value)
    {
        OnPropertyChanged(nameof(IsSystemDialogConfirm));
        OnPropertyChanged(nameof(SystemDialogPrimaryActionText));
    }

    [RelayCommand]
    private void ToggleLanguage()
    {
        _languageService.Toggle();
        CultureName = _languageService.CultureName;
        LanguageToggleText = _languageService.ToggleLabel;
        LocalizedDataGrid.RefreshHeaders();
        RefreshDockTitles();
        SysMenuViewModel.RefreshLocalization();
        OnPropertyChanged(nameof(SystemDialogPrimaryActionText));
        OnPropertyChanged(nameof(LoginButtonText));
    }

    [RelayCommand]
    private void OpenLogin()
    {
        SysMenuViewModel.ExecuteLoginAction();
    }

    [RelayCommand]
    private void CloseDialog() => IsDialogOpen = false;

    [RelayCommand]
    private void CompleteSystemDialog()
    {
        var request = SystemDialogRequest;
        IsSystemDialogOpen = false;
        SystemDialogRequest = null;
        request?.Complete(true);
    }

    [RelayCommand]
    private void CancelSystemDialog()
    {
        var request = SystemDialogRequest;
        IsSystemDialogOpen = false;
        SystemDialogRequest = null;
        request?.Complete(false);
    }

    private void OnDialogRequested(object? sender, AvaloniaDialogRequest request)
    {
        SystemDialogRequest = request;
        IsSystemDialogOpen = true;
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

        RightToolDockables.Clear();
        foreach (var dockable in toolDockables
            .OrderBy(static item => string.Equals(item.Id, "Core.SysLog", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(static item => item.Title, StringComparer.CurrentCulture))
        {
            RightToolDockables.Add((AvaloniaDockable)dockable);
        }

        ActiveRightToolDockable = RightToolDockables.FirstOrDefault(static item =>
                string.Equals(item.Id, "Core.SysLog", StringComparison.OrdinalIgnoreCase))
            ?? RightToolDockables.FirstOrDefault();
        EquipmentToolDockable = RightToolDockables.FirstOrDefault(static item =>
            string.Equals(item.Id, "Core.Equipment", StringComparison.OrdinalIgnoreCase));
        LogToolDockable = RightToolDockables.FirstOrDefault(static item =>
            string.Equals(item.Id, "Core.SysLog", StringComparison.OrdinalIgnoreCase));

        var mainDock = new ProportionalDock
        {
            Id = "main-dock",
            Title = "Main",
            Orientation = Orientation.Horizontal,
            VisibleDockables = [documents],
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
            CanPin = false,
            CanFloat = false,
            CanClose = false,
            MinWidth = pane.IsToolPane ? 320 : 0
        };

        _dockTitleKeys[pane.ViewId] = pane.TitleResourceKey;
        return dockable;
    }

    private void OnSysMenuNavigationRequested(string viewId)
    {
        if (ActivateRightTool(viewId))
        {
            SelectMenuItem(viewId);
            return;
        }

        ActivateDockable(DockLayout, viewId);
        SelectMenuItem(viewId);
    }

    private bool ActivateRightTool(string viewId)
    {
        var tool = RightToolDockables.FirstOrDefault(item =>
            string.Equals(item.Id, viewId, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            return false;
        }

        ActiveRightToolDockable = tool;
        return true;
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
            var tool = RightToolDockables.FirstOrDefault(item =>
                string.Equals(item.Id, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (tool is not null)
            {
                tool.Title = _languageService.GetText(pair.Value);
            }
        }
    }

    private void RefreshAuthState()
    {
        OnPropertyChanged(nameof(LoginButtonText));
        SysMenuViewModel.RefreshAuthState();
    }

    private void SelectMenuItem(string? viewId)
        => SysMenuViewModel.SelectMenuItem(viewId);

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
