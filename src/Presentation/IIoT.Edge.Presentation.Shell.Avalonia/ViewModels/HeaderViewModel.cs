using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Mvvm;
using IIoT.Edge.UI.Avalonia.Services;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace IIoT.Edge.Presentation.Shell.Avalonia.ViewModels;

public sealed partial class HeaderViewModel : AvaloniaViewModelBase
{
    private readonly IAuthService _authService;
    private readonly IAvaloniaLanguageService _languageService;
    private readonly IAvaloniaWindowService _windowService;
    private readonly IAvaloniaRuntimeState _runtimeState;
    private readonly IConfiguration _configuration;

    public HeaderViewModel(
        IAuthService authService,
        IAvaloniaLanguageService languageService,
        IAvaloniaWindowService windowService,
        IAvaloniaRuntimeState runtimeState,
        IConfiguration configuration)
    {
        _authService = authService;
        _languageService = languageService;
        _windowService = windowService;
        _runtimeState = runtimeState;
        _configuration = configuration;
        _authService.AuthStateChanged += _ => RefreshUser();
        _windowService.StateChanged += (_, _) => MaxRestoreIcon = _windowService.MaxRestoreIcon;
        _runtimeState.StateChanged += (_, _) => RefreshRuntimeState();
        _languageService.LanguageChanged += (_, _) => RefreshLocalizedHeaderText();
        RefreshUser();
        RefreshRuntimeState();
        RefreshLocalizedHeaderText();
        MaxRestoreIcon = _windowService.MaxRestoreIcon;
    }

    public override string ViewId => "Core.SystemHeader";

    [ObservableProperty]
    private string currentUser = string.Empty;

    [ObservableProperty]
    private string maxRestoreIcon = "WindowMaximize";

    [ObservableProperty]
    private string runtimeStatusText = string.Empty;

    [ObservableProperty]
    private string runtimeDetailText = string.Empty;

    [ObservableProperty]
    private bool runtimeStatusIsSuccess;

    [ObservableProperty]
    private bool runtimeStatusIsWarning = true;

    [ObservableProperty]
    private bool runtimeStatusIsError;

    [ObservableProperty]
    private string localModeText = string.Empty;

    [ObservableProperty]
    private string productionLineText = string.Empty;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private int notificationCount;

    public bool HasNotifications => NotificationCount > 0;

    partial void OnNotificationCountChanged(int value)
        => OnPropertyChanged(nameof(HasNotifications));

    [RelayCommand]
    private void Minimize() => _windowService.Minimize();

    [RelayCommand]
    private void ToggleMaximize()
    {
        _windowService.ToggleMaximize();
        MaxRestoreIcon = _windowService.MaxRestoreIcon;
    }

    [RelayCommand]
    private void Close() => _windowService.Close();

    private void RefreshUser()
    {
        CurrentUser = _authService.IsAuthenticated
            ? _authService.CurrentUser?.DisplayName ?? _authService.CurrentUser?.EmployeeNo ?? _languageService.GetText("Shell_Login_NotAuthenticated")
            : _languageService.GetText("Shell_Login_NotAuthenticated");
    }

    private void RefreshRuntimeState()
    {
        var snapshot = _runtimeState.Snapshot;
        RuntimeStatusText = snapshot.StatusText;
        RuntimeDetailText = snapshot.DetailText;
        RuntimeStatusIsSuccess = snapshot.Status == AvaloniaRuntimeStatus.Running;
        RuntimeStatusIsError = snapshot.Status == AvaloniaRuntimeStatus.StartFailed;
        RuntimeStatusIsWarning = !RuntimeStatusIsSuccess && !RuntimeStatusIsError;
    }

    private void RefreshLocalizedHeaderText()
    {
        LocalModeText = _languageService.GetText("Shell_Header_LocalMode");
        var machineProfile = _configuration["Shell:MachineProfile"];
        if (string.IsNullOrWhiteSpace(machineProfile))
        {
            ProductionLineText = _languageService.GetText("Shell_Header_ProductionLineDefault");
            return;
        }

        var displayName = _languageService.GetText($"Shell_Header_MachineProfile_{machineProfile}");
        if (string.Equals(displayName, $"Shell_Header_MachineProfile_{machineProfile}", StringComparison.Ordinal))
        {
            displayName = machineProfile;
        }

        ProductionLineText = string.Format(
            CultureInfo.CurrentCulture,
            _languageService.GetText("Shell_Header_ProductionLineFormat"),
            displayName);
    }
}
