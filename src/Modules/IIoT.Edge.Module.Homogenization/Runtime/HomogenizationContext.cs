using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.Collections;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Module.Homogenization.Runtime;

/// <summary>
/// 匀浆模块的运行态共享状态，供本模块任务和 UI 读取最近一次业务结果。
/// </summary>
public sealed class HomogenizationContext : ProductionContext
{
    private const int MaxOutboundRecords = 500;

    public string? LastInboundTrayCode { get; set; }

    public DateTime? LastInboundAt { get; set; }

    public string? LastInboundResult { get; set; }

    public string? LastOutboundTrayCode { get; set; }

    public DateTime? LastOutboundAt { get; set; }

    public string? LastOutboundResult { get; set; }

    public HomogenizationCellData? LastOutboundRecord { get; set; }

    public DateTime? LastRecipeAt { get; set; }

    public string? LastRecipeResult { get; set; }

    public HomogenizationRecipeSnapshot? LastRecipeSnapshot { get; set; }

    public DateTime? LastRealtimeAt { get; set; }

    public string? LastRealtimeResult { get; set; }

    public HomogenizationRealtimeSnapshot? LastRealtimeSnapshot { get; set; }

    public DateTime? LastEquipmentStatusAt { get; set; }

    public string? LastEquipmentStatusResult { get; set; }

    public HomogenizationEquipmentStatusSnapshot? LastEquipmentStatusSnapshot { get; set; }

    public DateTime LastHeartbeatAt { get; set; }

    public BoundedRecordQueue<HomogenizationCellData> OutboundRecords { get; } = new(MaxOutboundRecords);

    public void RecordOutbound(HomogenizationCellData record)
    {
        ArgumentNullException.ThrowIfNull(record);

        LastOutboundRecord = record;
        OutboundRecords.Enqueue(record);
    }
}

internal sealed class HomogenizationContextFactory : IProductionContextFactory
{
    public string ModuleId => HomogenizationModuleConstants.ModuleId;

    public Type ContextType => typeof(HomogenizationContext);

    public ProductionContext Create(string deviceName)
        => new HomogenizationContext
        {
            DeviceName = deviceName
        };
}
