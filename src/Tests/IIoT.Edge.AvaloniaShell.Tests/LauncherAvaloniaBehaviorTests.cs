using Avalonia.Headless.XUnit;
using IIoT.Edge.Launcher.Avalonia.Views;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;
using System.Diagnostics;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class LauncherAvaloniaBehaviorTests
{
    [AvaloniaFact]
    public async Task Launcher_window_can_bind_login_and_profile_selection()
    {
        var starter = new SpyProcessStarter();
        var executablePath = CreateTempExecutable();
        try
        {
            var profile = new LauncherProfileDefinition(
                "HomogenizationLineAvalonia",
                "匀浆 Avalonia",
                "启动 Avalonia 迁移客户端",
                null,
                "HomogenizationLine",
                executablePath,
                "BeakerOutline",
                "#2563EB");
            var viewModel = new LauncherMainViewModel(
                new LauncherLoginViewModel(
                    new StubLauncherAuthService(LauncherAuthenticationResult.Passed("现场启动管理员")),
                    TestLanguageService.CreateZh()),
                new LauncherProfileViewModel(
                    new StubLauncherProfileCatalog([profile]),
                    new ShellLaunchService(starter),
                    TestLanguageService.CreateZh()),
                TestLanguageService.CreateZh());

            var window = new MainWindow(viewModel);
            await viewModel.LoginAsync("101650", "123456");
            await viewModel.LaunchAsync(profile);

            Assert.NotNull(window);
            Assert.True(viewModel.IsAuthenticated);
            Assert.Single(viewModel.Profiles);
            Assert.NotNull(starter.StartInfo);
            Assert.Equal(executablePath, starter.StartInfo!.FileName);
            Assert.Equal("HomogenizationLine", starter.StartInfo.EnvironmentVariables["Shell__MachineProfile"]);
            window.Close();
        }
        finally
        {
            DeleteTempExecutable(executablePath);
        }
    }

    [AvaloniaFact]
    public void Change_password_window_can_be_created_with_launcher_view_model()
    {
        var viewModel = new LauncherMainViewModel(
            new LauncherLoginViewModel(
                new StubLauncherAuthService(LauncherAuthenticationResult.Passed("现场启动管理员")),
                TestLanguageService.CreateZh()),
            new LauncherProfileViewModel(
                new StubLauncherProfileCatalog([]),
                new StubShellLaunchService(),
                TestLanguageService.CreateZh()),
            TestLanguageService.CreateZh());

        var window = new ChangePasswordWindow(viewModel, "101650");

        Assert.NotNull(window);
        window.Close();
    }

    private static string CreateTempExecutable()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "edge-launcher-avalonia-tests",
            Guid.NewGuid().ToString("N"),
            "IIoT.Edge.AvaloniaShell.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private static void DeleteTempExecutable(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubLauncherProfileCatalog : ILauncherProfileCatalog
    {
        private readonly IReadOnlyList<LauncherProfileDefinition> _profiles;

        public StubLauncherProfileCatalog(IReadOnlyList<LauncherProfileDefinition> profiles)
        {
            _profiles = profiles;
        }

        public IReadOnlyList<LauncherProfileDefinition> LoadProfiles() => _profiles;
    }

    private sealed class StubLauncherAuthService : ILocalLauncherAuthService
    {
        private readonly LauncherAuthenticationResult _result;

        public StubLauncherAuthService(LauncherAuthenticationResult result)
        {
            _result = result;
        }

        public LauncherAuthenticationResult Authenticate(string? userName, string? password) => _result;

        public LauncherPasswordChangeResult ChangePassword(string? userName, string? oldPassword, string? newPassword)
            => LauncherPasswordChangeResult.Passed();
    }

    private sealed class StubShellLaunchService : IShellLaunchService
    {
        public void Launch(LauncherProfileDefinition profile)
        {
        }
    }

    private sealed class SpyProcessStarter : IProcessStarter
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public Process? Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            return Process.GetCurrentProcess();
        }
    }

    private sealed class TestLanguageService : IAvaloniaLanguageService
    {
        private readonly IReadOnlyDictionary<string, string> _texts = new Dictionary<string, string>
        {
            ["Launcher_Error_LoginFailed"] = "本地登录失败。",
            ["Launcher_Error_PasswordChangeFailed"] = "本地密码修改失败。",
            ["Launcher_Meta_Architecture"] = "架构：Launcher + Shell + MachineProfile",
            ["Launcher_Meta_Maintainer"] = "维护：Edge Platform Team",
            ["Launcher_Meta_Platform"] = "标准平台 / 本地登录 / 插件加载",
            ["Launcher_Profile_LoggedOut"] = "未登录",
            ["Launcher_Profile_SummaryAll"] = "共 {0} 个工序",
            ["Launcher_Profile_SummaryFiltered"] = "显示 {0} / {1} 个工序",
            ["Launcher_Profile_SummaryZero"] = "共 0 个工序",
            ["Launcher_Profile_Welcome"] = "已登录：{0}",
            ["Launcher_Status_ChangingPassword"] = "正在修改本地密码...",
            ["Launcher_Status_LaunchFailed"] = "启动 {0} 失败。",
            ["Launcher_Status_LaunchStarted"] = "已启动 {0}，MachineProfile = {1}。",
            ["Launcher_Status_PasswordChanged"] = "本地密码已修改，请使用新密码登录。",
            ["Launcher_Status_PasswordChangeFailed"] = "本地密码修改失败。",
            ["Launcher_Status_PleaseLogin"] = "请先使用本地账号登录。",
            ["Launcher_Status_ProfileLoadFailed"] = "本地登录通过，但工序清单加载失败。",
            ["Launcher_Status_RetryAccount"] = "请修正账号信息后重试。",
            ["Launcher_Status_RetryPassword"] = "请修正密码信息后重试。",
            ["Launcher_Status_SelectProfile"] = "请选择要启动的工序客户端。",
            ["Launcher_Status_Verifying"] = "正在验证本地账号...",
            ["Launcher_Validation_ConfirmPasswordMismatch"] = "两次输入的新密码不一致。",
            ["Launcher_Validation_NewPasswordMinLength"] = "新密码至少 6 位。",
            ["Launcher_Validation_NewPasswordRequired"] = "新密码不能为空。",
            ["Launcher_Validation_OldPasswordRequired"] = "旧密码不能为空。",
            ["Launcher_Validation_UserNameRequired"] = "账号不能为空。"
        };

        public static TestLanguageService CreateZh() => new();

        public string CultureName => "zh-CN";

        public string ToggleLabel => "EN";

        public event EventHandler? LanguageChanged;

        public string GetText(string key) => _texts.TryGetValue(key, out var value) ? value : key;

        public void Apply(string cultureName)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Toggle()
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
