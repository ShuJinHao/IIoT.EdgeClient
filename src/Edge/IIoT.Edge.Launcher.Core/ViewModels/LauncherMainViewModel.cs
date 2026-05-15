using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using System.Collections.ObjectModel;
using System.Reflection;

namespace IIoT.Edge.Launcher.ViewModels;

public sealed class LauncherMainViewModel : ObservableObject
{
    private readonly ILauncherProfileCatalog _profileCatalog;
    private readonly ILocalLauncherAuthService _authService;
    private readonly IShellLaunchService _launchService;
    private readonly List<LauncherProfileDefinition> _allProfiles = [];

    private string _errorMessage = string.Empty;
    private string _statusMessage = "请先使用本地账号登录。";
    private string _welcomeText = "未登录";
    private string _profileSearchText = string.Empty;
    private string _profileSummaryText = "共 0 个工序";
    private bool _isAuthenticated;
    private bool _isBusy;

    public LauncherMainViewModel(
        ILauncherProfileCatalog profileCatalog,
        ILocalLauncherAuthService authService,
        IShellLaunchService launchService)
    {
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));

        AppVersionText = BuildAppVersionText();
        PlatformMetaText = "标准平台 / 本地登录 / 插件加载";
        MaintainerText = "维护：Edge Platform Team";
        ArchitectureText = "架构：Launcher + Shell + MachineProfile";
    }

    public ObservableCollection<LauncherProfileDefinition> Profiles { get; } = [];

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string WelcomeText
    {
        get => _welcomeText;
        private set => SetProperty(ref _welcomeText, value);
    }

    public string ProfileSearchText
    {
        get => _profileSearchText;
        set
        {
            if (SetProperty(ref _profileSearchText, value))
            {
                ApplyProfileFilter();
            }
        }
    }

    public string ProfileSummaryText
    {
        get => _profileSummaryText;
        private set => SetProperty(ref _profileSummaryText, value);
    }

    public string AppVersionText { get; }

    public string PlatformMetaText { get; }

    public string MaintainerText { get; }

    public string ArchitectureText { get; }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set => SetProperty(ref _isAuthenticated, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public async Task LoginAsync(string? userName, string? password)
    {
        ErrorMessage = string.Empty;
        StatusMessage = "正在验证本地账号...";
        IsBusy = true;

        try
        {
            await Task.Yield();

            var result = _authService.Authenticate(userName, password);
            if (!result.Success)
            {
                ResetToLoggedOutState();
                ErrorMessage = result.ErrorMessage ?? "本地登录失败。";
                StatusMessage = "请修正账号信息后重试。";
                return;
            }

            _allProfiles.Clear();
            _allProfiles.AddRange(_profileCatalog.LoadProfiles());

            IsAuthenticated = true;
            WelcomeText = $"已登录：{result.DisplayName}";
            ProfileSearchText = string.Empty;
            ApplyProfileFilter();
            StatusMessage = "请选择要启动的工序客户端。";
        }
        catch (Exception ex)
        {
            ResetToLoggedOutState();
            ErrorMessage = ex.Message;
            StatusMessage = "本地登录通过，但工序清单加载失败。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ChangePasswordAsync(string? userName, string? oldPassword, string? newPassword)
    {
        ErrorMessage = string.Empty;
        StatusMessage = "正在修改本地密码...";
        IsBusy = true;

        try
        {
            await Task.Yield();
            var result = _authService.ChangePassword(userName, oldPassword, newPassword);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "本地密码修改失败。";
                StatusMessage = "请修正密码信息后重试。";
                return false;
            }

            StatusMessage = "本地密码已修改，请使用新密码登录。";
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "本地密码修改失败。";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task LaunchAsync(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        ErrorMessage = string.Empty;
        try
        {
            _launchService.Launch(profile);
            StatusMessage = $"已启动 {profile.DisplayName}，MachineProfile = {profile.MachineProfile}。";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = $"启动 {profile.DisplayName} 失败。";
        }

        return Task.CompletedTask;
    }

    private void ApplyProfileFilter()
    {
        var keyword = ProfileSearchText?.Trim();
        IEnumerable<LauncherProfileDefinition> filtered = _allProfiles;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            filtered = filtered.Where(profile =>
                Contains(profile.DisplayName, keyword) ||
                Contains(profile.Description, keyword) ||
                Contains(profile.ProfileId, keyword) ||
                Contains(profile.MachineProfile, keyword));
        }

        Profiles.Clear();
        foreach (var profile in filtered)
        {
            Profiles.Add(profile);
        }

        ProfileSummaryText = _allProfiles.Count == 0
            ? "共 0 个工序"
            : string.IsNullOrWhiteSpace(keyword)
                ? $"共 {_allProfiles.Count} 个工序"
                : $"显示 {Profiles.Count} / {_allProfiles.Count} 个工序";
    }

    private void ResetToLoggedOutState()
    {
        IsAuthenticated = false;
        WelcomeText = "未登录";
        ProfileSearchText = string.Empty;
        _allProfiles.Clear();
        Profiles.Clear();
        ProfileSummaryText = "共 0 个工序";
    }

    private static bool Contains(string? source, string keyword)
        => !string.IsNullOrWhiteSpace(source) &&
           source.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static string BuildAppVersionText()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null)
        {
            return "v1.0.0";
        }

        return $"v{version.Major}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";
    }
}
