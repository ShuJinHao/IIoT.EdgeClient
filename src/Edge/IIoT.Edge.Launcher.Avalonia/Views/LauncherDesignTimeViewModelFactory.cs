using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.UI.Avalonia.Localization;

namespace IIoT.Edge.Launcher.Avalonia.Views;

internal static class LauncherDesignTimeViewModelFactory
{
    public static LauncherMainViewModel Create()
    {
        var profile = new LauncherProfileDefinition(
            "HomogenizationLineAvalonia",
            "匀浆 Avalonia",
            "使用匀浆工序的 profile 启动 Avalonia 迁移客户端。",
            "Assets/Profiles/homogenization.png",
            "HomogenizationLine",
            "IIoT.Edge.AvaloniaShell.exe",
            "BeakerOutline",
            "Launcher.Accent.Default");

        var languageService = new DesignTimeLanguageService();
        var authService = new DesignTimeAuthService();
        var profileCatalog = new DesignTimeProfileCatalog([profile]);
        var launchService = new DesignTimeShellLaunchService();

        return new LauncherMainViewModel(
            new LauncherLoginViewModel(authService, languageService),
            new LauncherProfileViewModel(profileCatalog, launchService, languageService),
            languageService);
    }

    private sealed class DesignTimeProfileCatalog : ILauncherProfileCatalog
    {
        private readonly IReadOnlyList<LauncherProfileDefinition> _profiles;

        public DesignTimeProfileCatalog(IReadOnlyList<LauncherProfileDefinition> profiles)
        {
            _profiles = profiles;
        }

        public IReadOnlyList<LauncherProfileDefinition> LoadProfiles() => _profiles;
    }

    private sealed class DesignTimeAuthService : ILocalLauncherAuthService
    {
        public LauncherAuthenticationResult Authenticate(string? userName, string? password)
            => LauncherAuthenticationResult.Passed("现场启动管理员");

        public LauncherPasswordChangeResult ChangePassword(string? userName, string? oldPassword, string? newPassword)
            => LauncherPasswordChangeResult.Passed();
    }

    private sealed class DesignTimeShellLaunchService : IShellLaunchService
    {
        public void Launch(LauncherProfileDefinition profile)
        {
        }
    }

    private sealed class DesignTimeLanguageService : IAvaloniaLanguageService
    {
        public string CultureName => "zh-CN";

        public string ToggleLabel => "EN";

        public event EventHandler? LanguageChanged;

        public string GetText(string key)
        {
            return key switch
            {
                "Launcher_Status_PleaseLogin" => "请先使用本地账号登录。",
                "Launcher_Status_Verifying" => "正在验证本地账号...",
                "Launcher_Status_RetryAccount" => "请修正账号信息后重试。",
                "Launcher_Status_SelectProfile" => "请选择要启动的工序客户端。",
                "Launcher_Status_ProfileLoadFailed" => "本地登录通过，但工序清单加载失败。",
                "Launcher_Status_ChangingPassword" => "正在修改本地密码...",
                "Launcher_Status_RetryPassword" => "请修正密码信息后重试。",
                "Launcher_Status_PasswordChanged" => "本地密码已修改，请使用新密码登录。",
                "Launcher_Status_PasswordChangeFailed" => "本地密码修改失败。",
                "Launcher_Status_LaunchStarted" => "已启动 {0}，MachineProfile = {1}。",
                "Launcher_Status_LaunchFailed" => "启动 {0} 失败。",
                "Launcher_Error_LoginFailed" => "本地登录失败。",
                "Launcher_Error_PasswordChangeFailed" => "本地密码修改失败。",
                "Launcher_Profile_LoggedOut" => "未登录",
                "Launcher_Profile_SummaryZero" => "共 0 个工序",
                "Launcher_Profile_SummaryAll" => "共 {0} 个工序",
                "Launcher_Profile_SummaryFiltered" => "显示 {0} / {1} 个工序",
                "Launcher_Profile_Welcome" => "已登录：{0}",
                "Launcher_Meta_Platform" => "标准平台 / 本地登录 / 插件加载",
                "Launcher_Meta_Maintainer" => "维护：Edge Platform Team",
                "Launcher_Meta_Architecture" => "架构：Launcher + Shell + MachineProfile",
                "Launcher_Validation_UserNameRequired" => "账号不能为空。",
                "Launcher_Validation_OldPasswordRequired" => "旧密码不能为空。",
                "Launcher_Validation_NewPasswordRequired" => "新密码不能为空。",
                "Launcher_Validation_NewPasswordMinLength" => "新密码至少 6 位。",
                "Launcher_Validation_ConfirmPasswordMismatch" => "两次输入的新密码不一致。",
                _ => key
            };
        }

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
