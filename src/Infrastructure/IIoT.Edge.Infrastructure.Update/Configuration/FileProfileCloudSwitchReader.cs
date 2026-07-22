using System.Text.Json;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Infrastructure.Update.Configuration;

/// <summary>
/// 读取 Shell 从 profile SQLite 唯一开关生成的只读投影；缺失、损坏或版本未知时一律关闭。
/// </summary>
public sealed class FileProfileCloudSwitchReader(
    string baseDirectory) : IEdgeProfileCloudSwitchReader
{
    public bool IsEnabled(EdgeUpdateTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var path = EdgeClientProgramDataPaths.ResolveProfileCloudSwitchProjectionPath(
            target.MachineProfile,
            baseDirectory);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return root.TryGetProperty("version", out var version)
                   && version.TryGetInt32(out var parsedVersion)
                   && parsedVersion == 1
                   && root.TryGetProperty("enabled", out var enabled)
                   && enabled.ValueKind is JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
