namespace IIoT.Edge.Module.Homogenization;

/// <summary>
/// 匀浆 PLC 任务持久化 Key。任务绑定表和运行态都依赖这些稳定键，禁止散落手写。
/// </summary>
public static class HomogenizationTaskKeys
{
    public const string Heartbeat = "Homogenization.Heartbeat";

    public const string Inbound = "Homogenization.Inbound";

    public const string Outbound = "Homogenization.Outbound";

    public const string Recipe = "Homogenization.Recipe";

    public const string EquipmentStatus = "Homogenization.EquipmentStatus";

    public const string Realtime = "Homogenization.Realtime";
}
