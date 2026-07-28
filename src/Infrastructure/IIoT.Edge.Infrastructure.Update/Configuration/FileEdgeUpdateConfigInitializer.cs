using IIoT.Edge.Module.Contracts.Updates;

namespace IIoT.Edge.Infrastructure.Update.Configuration;

public sealed class FileEdgeUpdateConfigInitializer : IEdgeUpdateConfigInitializer
{
    public const string SampleConfigFileName = "launcher.update.sample.json";

    private readonly EdgeUpdateConfigPaths _paths;

    public FileEdgeUpdateConfigInitializer(EdgeUpdateConfigPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.ConfigPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(paths.SampleConfigPath);

        _paths = paths;
    }

    public void EnsureConfigExists()
    {
        try
        {
            LauncherUpdateConfigurationFile.EnsureCurrentFile(
                _paths.ConfigPath,
                _paths.SampleConfigPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.Text.Json.JsonException)
        {
        }
    }

    // SDK 2.0.6 仍保留该兼容端口；生产运行时禁止 catalog 改写正式更新源。
    public bool TrySyncUpdateSource(string updateSource) => false;
}
