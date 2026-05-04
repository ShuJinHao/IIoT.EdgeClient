namespace IIoT.Edge.Application.Features.Config;

/// <summary>
/// 本地参数缓存键统一入口。
/// </summary>
public static class ParameterCacheKeys
{
    public const string SystemAll = "Config:SystemAll";

    public const string ModuleSnapshotPrefix = "Param:Module:";

    public static string ModuleSnapshot(string moduleId)
        => $"{ModuleSnapshotPrefix}{moduleId}";
}
