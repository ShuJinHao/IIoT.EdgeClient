using System.Text.Json;
using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.Module.Contracts.Logging;

namespace IIoT.Edge.Host.DataPipeline.Context;

internal sealed class PersistentPlcIdentityAliasRegistry : IPlcIdentityAliasRegistry
{
    private const string PersistFileName = "plc_identity_aliases.json";

    private readonly Dictionary<string, HashSet<string>> _aliasesByPlcCode =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogService _logger;
    private readonly string _persistPath;
    private readonly object _lock = new();

    public PersistentPlcIdentityAliasRegistry(string persistDirectory, ILogService logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistDirectory);
        _logger = logger;
        Directory.CreateDirectory(persistDirectory);
        _persistPath = Path.Combine(persistDirectory, PersistFileName);
        Load();
    }

    public void ObserveVerifiedAlias(string plcCode, string deviceName)
    {
        var normalizedCode = plcCode?.Trim() ?? string.Empty;
        var normalizedName = deviceName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedCode)
            || string.IsNullOrWhiteSpace(normalizedName))
        {
            return;
        }

        lock (_lock)
        {
            if (!_aliasesByPlcCode.TryGetValue(normalizedCode, out var aliases))
            {
                aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _aliasesByPlcCode.Add(normalizedCode, aliases);
            }

            if (aliases.Add(normalizedName))
            {
                PersistLocked();
            }
        }
    }

    public IReadOnlyList<string> GetVerifiedAliases(string plcCode)
    {
        var normalizedCode = plcCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return [];
        }

        lock (_lock)
        {
            if (!_aliasesByPlcCode.TryGetValue(normalizedCode, out var aliases))
            {
                return [];
            }

            return aliases
                .Where(alias => _aliasesByPlcCode.Count(pair => pair.Value.Contains(alias)) == 1)
                .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private void Load()
    {
        if (!File.Exists(_persistPath))
        {
            return;
        }

        try
        {
            var persisted = JsonSerializer.Deserialize<Dictionary<string, string[]?>>(
                File.ReadAllText(_persistPath));
            if (persisted is null)
            {
                throw new JsonException($"{PersistFileName} 根节点不能为 null。");
            }

            var loadedAliases = new Dictionary<string, HashSet<string>>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var pair in persisted)
            {
                var plcCode = pair.Key?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(plcCode) || pair.Value is null)
                {
                    throw new JsonException(
                        $"{PersistFileName} 包含空 PlcCode 或 null 别名数组。");
                }

                loadedAliases[plcCode] = pair.Value
                    .Select(static alias => alias?.Trim() ?? string.Empty)
                    .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var pair in loadedAliases)
            {
                _aliasesByPlcCode[pair.Key] = pair.Value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.Warn(
                $"[PLC 身份别名] 无法读取 {PersistFileName}，原文件已保留且不会用于归属：{ex.Message}");
        }
    }

    private void PersistLocked()
    {
        var snapshot = _aliasesByPlcCode.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value
                .OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
        var tempPath = _persistPath + ".tmp";
        try
        {
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
            File.Move(tempPath, _persistPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn(
                $"[PLC 身份别名] 保存 {PersistFileName} 失败；内存别名继续有效，临时字节保留：{ex.Message}");
        }
    }
}
