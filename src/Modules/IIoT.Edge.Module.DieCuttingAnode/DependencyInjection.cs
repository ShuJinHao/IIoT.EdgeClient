using IIoT.Edge.Module.DieCutting;

namespace IIoT.Edge.Module.DieCuttingAnode;

/// <summary>
/// 负极模切 AP 插件入口，对应 OPENLINK_008 和 P1-AP01~P1-AP12。
/// </summary>
public sealed class DependencyInjection : DieCuttingModuleBase
{
    public const string ModuleKey = "DieCuttingAnode";

    public DependencyInjection()
        : base(new DieCuttingModuleDefinition(
            ModuleKey,
            "负极模切",
            "P1-AP",
            "10.110.0",
            "P1-APUC",
            "AP",
            "负极模切 AP"))
    {
    }
}
