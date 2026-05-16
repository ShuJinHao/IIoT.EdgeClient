using IIoT.Edge.Launcher.Models;
using System.Diagnostics;
using System.IO;

namespace IIoT.Edge.Launcher.Services;

public sealed class ShellLaunchService : IShellLaunchService
{
    private readonly IProcessStarter _processStarter;

    public ShellLaunchService(IProcessStarter processStarter)
    {
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
    }

    public void Launch(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!File.Exists(profile.ExecutablePath))
        {
            throw new FileNotFoundException(
                $"未找到目标客户端可执行文件：{profile.ExecutablePath}。请先确认目标工序运行目录已生成，或检查 launcher.profiles.json 中的 ExecutablePath 配置。",
                profile.ExecutablePath);
        }

        var startInfo = new ProcessStartInfo(profile.ExecutablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(profile.ExecutablePath) ?? AppContext.BaseDirectory
        };

        foreach (var argument in profile.Arguments ?? [])
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.EnvironmentVariables["Shell__MachineProfile"] = profile.MachineProfile;

        var process = _processStarter.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException($"客户端启动失败：{profile.DisplayName}");
        }
    }
}
