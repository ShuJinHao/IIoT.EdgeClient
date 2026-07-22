using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Application.Features.Config;

namespace IIoT.Edge.Application.Features.Config.ModuleParameters;

/// <summary>
/// 模块参数缓存和数据库键的统一生成入口。
/// </summary>
public static class ModuleParamKeys
{
    public const string StoragePrefix = "Module:";

    public const string SnapshotPrefix = ParameterCacheKeys.ModuleSnapshotPrefix;

    public static string StorageKey(string moduleId, ModuleParamCategory category, string name)
        => $"{StoragePrefix}{moduleId}:{category}:{name}";

    public static string SnapshotKey(string moduleId)
        => ParameterCacheKeys.ModuleSnapshot(moduleId);

    public static bool IsModuleStorageKey(string key)
        => key.StartsWith(StoragePrefix, StringComparison.OrdinalIgnoreCase);
}
