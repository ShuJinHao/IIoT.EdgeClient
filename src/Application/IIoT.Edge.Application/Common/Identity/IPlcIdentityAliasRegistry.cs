namespace IIoT.Edge.Application.Common.Identity;

/// <summary>
/// 保存已经由稳定 PLC 身份确认过的历史现场名称。
/// 名称只用于读取旧 Cloud 分区，不得反向推导业务归属。
/// </summary>
public interface IPlcIdentityAliasRegistry
{
    void ObserveVerifiedAlias(string plcCode, string deviceName);

    IReadOnlyList<string> GetVerifiedAliases(string plcCode);
}

public sealed class InMemoryPlcIdentityAliasRegistry : IPlcIdentityAliasRegistry
{
    private readonly Dictionary<string, HashSet<string>> _aliasesByPlcCode =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

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

            aliases.Add(normalizedName);
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
}
