using IIoT.Edge.Launcher.Models;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;

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
            "#2563EB");

        return new LauncherMainViewModel(
            new DesignTimeProfileCatalog([profile]),
            new DesignTimeAuthService(),
            new DesignTimeShellLaunchService());
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
}
