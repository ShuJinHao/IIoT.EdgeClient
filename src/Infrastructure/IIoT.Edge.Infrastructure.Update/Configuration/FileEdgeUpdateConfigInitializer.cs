using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.Application.Abstractions.Updates;

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
        if (File.Exists(_paths.ConfigPath) || !File.Exists(_paths.SampleConfigPath))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_paths.ConfigPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(_paths.SampleConfigPath, _paths.ConfigPath, overwrite: false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public bool TrySyncUpdateSource(string updateSource)
    {
        if (string.IsNullOrWhiteSpace(updateSource))
        {
            return false;
        }

        try
        {
            JsonObject config;
            if (File.Exists(_paths.ConfigPath))
            {
                var existing = File.ReadAllText(_paths.ConfigPath);
                config = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
            }
            else
            {
                config = new JsonObject();
            }

            var hasCamelCaseKey = config.ContainsKey("source");
            var currentSource = config["Source"]?.GetValue<string>()
                ?? config["source"]?.GetValue<string>();
            if (string.Equals(currentSource, updateSource.Trim(), StringComparison.Ordinal)
                && !hasCamelCaseKey)
            {
                return false;
            }

            config.Remove("source");
            config["Source"] = updateSource.Trim();

            var directory = Path.GetDirectoryName(_paths.ConfigPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                _paths.ConfigPath,
                config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            return true;
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
