using Avalonia.Threading;
using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Presentation.Shell.Services;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Shell.ViewModels;

/// <summary>
/// Shell 主窗口外框展示模型，仅承载窗口标题、头部和底部状态文本。
/// </summary>
public sealed class MainWindowViewModel : BaseNotifyPropertyChanged, IShellAuthContext, IDisposable
{
    private readonly IAppLanguageService _languageService;
    private readonly IConfiguration _configuration;
    private readonly IAuthService _authService;
    private readonly IDeviceService _deviceService;
    private readonly DispatcherTimer _clockTimer;
    private readonly DateTime _softwareStartedAt;

    public MainWindowViewModel(
        IAppLanguageService languageService,
        IConfiguration configuration,
        IAuthService authService,
        IDeviceService deviceService)
    {
        _languageService = languageService;
        _configuration = configuration;
        _authService = authService;
        _deviceService = deviceService;
        _languageService.LanguageChanged += OnLanguageChanged;
        _authService.AuthStateChanged += OnAuthStateChanged;
        _deviceService.DeviceIdentified += OnDeviceIdentified;
        _softwareStartedAt = DateTime.Now;

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();
    }

    public string AppTitle => _languageService.GetString("Shell_FrameTitle", "产线边缘控制台");

    public string HeaderStatus => _languageService.GetString("Shell_FrameStatusRunning", "运行中");

    public string HeaderMode => _languageService.GetString("Shell_FrameModeLocal", "本地模式");

    public string HeaderProfile => _languageService.Format(
        "Shell_FrameProfile",
        "产线: {0}",
        ResolveMachineProfile());

    public string OperatorName => _authService.CurrentUser?.DisplayName
        ?? _languageService.GetString("Shell_FrameOperatorNotLoggedIn", "\u672A\u767B\u5F55");

    public string OperatorCode => _authService.CurrentUser?.EmployeeNo ?? "--";

    public UserSession? CurrentUser => _authService.CurrentUser;

    public bool IsAuthenticated => _authService.IsAuthenticated;

    public bool HasCloudDeviceIdentity
        => _deviceService.CurrentDevice is not null && _deviceService.CurrentDevice.DeviceId != Guid.Empty;

    public LocalAdminCredentialStatus LocalAdminCredentialStatus => _authService.LocalAdminCredentialStatus;

    public string SystemStatusText => _languageService.GetString("Shell_FrameSystemStatus", "系统运行正常");

    public string VersionText => _languageService.Format(
        "Shell_FrameVersion",
        "版本 {0}",
        ResolveVersion());

    public string EdgeIdText => _languageService.Format(
        "Shell_FrameEdgeId",
        "Edge ID: {0}",
        ResolveEdgeId());

    public string LocalTimeText => _languageService.Format(
        "Shell_FrameLocalTime",
        "本地时间 {0:yyyy-MM-dd HH:mm:ss}",
        DateTime.Now);

    public string ContentTitle => _languageService.GetString("Shell_ContentTitle", "功能开发中");

    public string ContentMessage => _languageService.GetString(
        "Shell_ContentMessage",
        "当前 Phase 仅迁移启动壳和五区骨架，业务页面将在后续阶段按原项目原名迁移。");

    public string EquipmentTitle => _languageService.GetString("Shell_EquipmentInfo", "设备信息");

    public string EquipmentMessage => _languageService.GetString(
        "Shell_EquipmentEmpty",
        "设备状态组件尚未进入本阶段迁移，当前不展示模拟设备数据。");

    public string LogTitle => _languageService.GetString("Shell_SystemLog", "系统日志");

    public string LogMessage => _languageService.GetString(
        "Shell_LogEmpty",
        "日志面板尚未进入本阶段迁移，当前不展示模拟日志。");

    public string FooterProgramName => _languageService.GetString("Shell_Footer_DefaultProgram", "—");

    public string SoftwareRunDate
    {
        get
        {
            var minutes = Math.Max(0, (int)Math.Floor((DateTime.Now - _softwareStartedAt).TotalMinutes));
            return _languageService.Format("Shell_Footer_RunMinutesFormat", "{0} min", minutes);
        }
    }

    public string FooterTimeAndDateText => DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");

    public string FooterText => FooterTimeAndDateText;

    public void Dispose()
    {
        _clockTimer.Stop();
        _clockTimer.Tick -= OnClockTick;
        _languageService.LanguageChanged -= OnLanguageChanged;
        _authService.AuthStateChanged -= OnAuthStateChanged;
        _deviceService.DeviceIdentified -= OnDeviceIdentified;
    }

    public Task<AuthResult> LoginLocalEmergencyAsync(string? password)
        => _authService.LoginLocalAsync(password ?? string.Empty);

    public Task<AuthResult> InitializeLocalEmergencyAdminAsync(string? newPassword)
        => _authService.InitializeLocalAdminAsync(newPassword ?? string.Empty);

    public Task<AuthResult> ResetLocalEmergencyPasswordAsync(string? currentPassword, string? newPassword)
        => _authService.ResetLocalAdminPasswordAsync(currentPassword ?? string.Empty, newPassword ?? string.Empty);

    public Task<AuthResult> LoginCloudEmployeeAsync(string? employeeNo, string? password)
    {
        var device = _deviceService.CurrentDevice;
        if (device is null || device.DeviceId == Guid.Empty)
        {
            return Task.FromResult(AuthResult.Fail("当前设备未连接到云平台，无法使用云端登录。请检查网络连接或联系管理员配置设备。"));
        }

        return _authService.LoginCloudAsync(employeeNo?.Trim() ?? string.Empty, password ?? string.Empty, device.DeviceId);
    }

    public void Logout()
        => _authService.Logout();

    private void OnAuthStateChanged(UserSession? session)
    {
        OnPropertyChanged(nameof(OperatorName));
        OnPropertyChanged(nameof(OperatorCode));
        OnPropertyChanged(nameof(CurrentUser));
        OnPropertyChanged(nameof(IsAuthenticated));
    }

    private void OnDeviceIdentified(DeviceSession? session)
        => OnPropertyChanged(nameof(HasCloudDeviceIdentity));

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLanguageProperties();
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(LocalTimeText));
        OnPropertyChanged(nameof(SoftwareRunDate));
        OnPropertyChanged(nameof(FooterTimeAndDateText));
        OnPropertyChanged(nameof(FooterText));
    }

    private void RefreshLanguageProperties()
    {
        OnPropertyChanged(nameof(AppTitle));
        OnPropertyChanged(nameof(HeaderStatus));
        OnPropertyChanged(nameof(HeaderMode));
        OnPropertyChanged(nameof(HeaderProfile));
        OnPropertyChanged(nameof(OperatorName));
        OnPropertyChanged(nameof(OperatorCode));
        OnPropertyChanged(nameof(SystemStatusText));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(EdgeIdText));
        OnPropertyChanged(nameof(LocalTimeText));
        OnPropertyChanged(nameof(ContentTitle));
        OnPropertyChanged(nameof(ContentMessage));
        OnPropertyChanged(nameof(EquipmentTitle));
        OnPropertyChanged(nameof(EquipmentMessage));
        OnPropertyChanged(nameof(LogTitle));
        OnPropertyChanged(nameof(LogMessage));
        OnPropertyChanged(nameof(FooterProgramName));
        OnPropertyChanged(nameof(SoftwareRunDate));
        OnPropertyChanged(nameof(FooterTimeAndDateText));
        OnPropertyChanged(nameof(FooterText));
    }

    private string ResolveMachineProfile()
    {
        var profile = _configuration["Shell:MachineProfile"]?.Trim();
        return string.IsNullOrWhiteSpace(profile)
            ? _languageService.GetString("Shell_FrameDefaultProfile", "默认配置")
            : profile;
    }

    private string ResolveEdgeId()
    {
        var instanceId = _configuration["InstanceId"]?.Trim();
        return string.IsNullOrWhiteSpace(instanceId)
            ? "IIoT-Edge-Default"
            : instanceId;
    }

    private static string ResolveVersion()
    {
        var version = typeof(MainWindowViewModel).Assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
