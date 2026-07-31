using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Configuration;
using System.Diagnostics;
using System.Text.Json;

namespace IIoT.Edge.Launcher.Services;

public sealed class ShellInstanceIdResolver : IShellInstanceIdResolver
{
    public string? ResolveInstanceId(LauncherProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var hostDirectory = Path.GetDirectoryName(profile.ExecutablePath) ?? AppContext.BaseDirectory;
        var configPath = ResolveMachineProfileConfigPath(hostDirectory, profile.MachineProfile);
        if (!File.Exists(configPath))
        {
            Trace.TraceWarning($"未找到 Shell 机器配置，无法探测运行态：{configPath}");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!document.RootElement.TryGetProperty("InstanceId", out var instanceIdElement)
                || instanceIdElement.ValueKind != JsonValueKind.String)
            {
                Trace.TraceWarning($"Shell 机器配置缺少 InstanceId，无法探测运行态：{configPath}");
                return null;
            }

            var instanceId = instanceIdElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                Trace.TraceWarning($"Shell 机器配置 InstanceId 为空，无法探测运行态：{configPath}");
                return null;
            }

            return instanceId;
        }
        catch (JsonException ex)
        {
            Trace.TraceWarning($"解析 Shell 机器配置失败，无法探测运行态：{configPath} ({ex.GetType().Name})");
        }
        catch (IOException ex)
        {
            Trace.TraceWarning($"读取 Shell 机器配置失败，无法探测运行态：{configPath} ({ex.GetType().Name})");
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.TraceWarning($"无权限读取 Shell 机器配置，无法探测运行态：{configPath} ({ex.GetType().Name})");
        }

        return null;
    }

    private static string ResolveMachineProfileConfigPath(string hostDirectory, string machineProfile)
    {
        var externalConfigPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(machineProfile, hostDirectory);
        return File.Exists(externalConfigPath)
            ? externalConfigPath
            : Path.Combine(hostDirectory, $"appsettings.machine.{machineProfile}.json");
    }
}
