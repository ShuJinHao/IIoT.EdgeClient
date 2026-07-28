using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IIoT.Edge.Infrastructure.Update.Configuration;

internal sealed record LauncherUpdateConfiguration(
    string Source,
    string Channel,
    string TargetRuntime);

internal static class LauncherUpdateConfigurationFile
{
    private static readonly string[] SourceKeys = ["source", "Source", "updateSource", "UpdateSource", "url", "Url"];
    private static readonly string[] ChannelKeys = ["channel", "Channel"];
    private static readonly string[] TargetRuntimeKeys = ["targetRuntime", "TargetRuntime"];

    public static bool TryReadCurrent(
        string path,
        out LauncherUpdateConfiguration? configuration,
        out string? error)
    {
        configuration = null;
        error = null;
        if (!File.Exists(path))
        {
            error = "Launcher 更新配置不存在。";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Launcher 更新配置根节点必须是对象。";
                return false;
            }

            var source = ReadCurrentString(root, "source");
            var channel = ReadCurrentString(root, "channel");
            var targetRuntime = ReadCurrentString(root, "targetRuntime");
            if (string.IsNullOrWhiteSpace(source)
                || string.IsNullOrWhiteSpace(channel)
                || string.IsNullOrWhiteSpace(targetRuntime))
            {
                error = "Launcher 更新配置缺少 source、channel 或 targetRuntime。";
                return false;
            }

            configuration = new LauncherUpdateConfiguration(
                source.Trim(),
                channel.Trim(),
                targetRuntime.Trim());
            return true;
        }
        catch (JsonException)
        {
            error = "Launcher 更新配置 JSON 无效。";
            return false;
        }
        catch (IOException)
        {
            error = "Launcher 更新配置不可读取。";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            error = "Launcher 更新配置不可读取。";
            return false;
        }
    }

    public static void EnsureCurrentFile(string configPath, string samplePath)
    {
        if (!File.Exists(configPath))
        {
            if (!File.Exists(samplePath))
            {
                return;
            }

            var sample = File.ReadAllText(samplePath);
            WriteAtomically(configPath, sample);
        }

        MigrateLegacyKeys(configPath);
    }

    private static void MigrateLegacyKeys(string configPath)
    {
        var json = File.ReadAllText(configPath);
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new JsonException("Launcher 更新配置根节点必须是对象。");

        var source = ReadLegacyString(root, SourceKeys);
        var channel = ReadLegacyString(root, ChannelKeys);
        var targetRuntime = ReadLegacyString(root, TargetRuntimeKeys);
        var requiresMigration = HasLegacyOrDuplicateKeys(root, SourceKeys, "source")
                                || HasLegacyOrDuplicateKeys(root, ChannelKeys, "channel")
                                || HasLegacyOrDuplicateKeys(root, TargetRuntimeKeys, "targetRuntime");
        if (!requiresMigration)
        {
            return;
        }

        RemoveKeys(root, SourceKeys);
        RemoveKeys(root, ChannelKeys);
        RemoveKeys(root, TargetRuntimeKeys);
        root["source"] = source;
        root["channel"] = channel;
        root["targetRuntime"] = targetRuntime;
        WriteAtomically(
            configPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string? ReadCurrentString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadLegacyString(JsonObject root, IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            if (root.TryGetPropertyValue(key, out var node)
                && node is JsonValue value
                && value.TryGetValue<string>(out var text)
                && !string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }

    private static bool HasLegacyOrDuplicateKeys(
        JsonObject root,
        IReadOnlyList<string> keys,
        string currentKey)
    {
        var matching = root
            .Select(static property => property.Key)
            .Where(key => keys.Any(candidate =>
                string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        return matching.Length != 1
               || !string.Equals(matching[0], currentKey, StringComparison.Ordinal);
    }

    private static void RemoveKeys(JsonObject root, IReadOnlyList<string> keys)
    {
        foreach (var key in root
                     .Select(static property => property.Key)
                     .Where(key => keys.Any(candidate =>
                         string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)))
                     .ToArray())
        {
            root.Remove(key);
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Launcher 更新配置缺少目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
