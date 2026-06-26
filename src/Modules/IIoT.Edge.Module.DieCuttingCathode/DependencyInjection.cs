using IIoT.Edge.Module.DieCutting;

namespace IIoT.Edge.Module.DieCuttingCathode;

/// <summary>
/// 正极模切 CP 插件入口，对应 OPENLINK_009 和 P2-CP01~P2-CP12。
/// </summary>
public sealed class DependencyInjection : DieCuttingModuleBase
{
    public const string ModuleKey = "DieCuttingCathode";

    public DependencyInjection()
        : base(new DieCuttingModuleDefinition(
            ModuleKey,
            "正极模切",
            "P2-CP",
            "10.110.1",
            "http://10.110.1.250:8081",
            "P2-CPUC",
            "CP",
            "正极模切 CP"))
    {
    }
}
