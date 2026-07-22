using System.Text.Json;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Infrastructure.Integration.Config;

public sealed class FileCloudProfileSwitchProjectionWriter(
    EdgeRuntimePaths runtimePaths) : ICloudProfileSwitchProjectionWriter
{
    public async Task WriteAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        var path = EdgeClientProgramDataPaths.ResolveProfileCloudSwitchProjectionPath(
            runtimePaths.ProfileName,
            runtimePaths.BaseDirectory);
        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("Cloud 开关投影目录无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(new { version = 1, enabled });
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
