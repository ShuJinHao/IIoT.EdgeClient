using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.Collections;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Runtime;

/// <summary>
/// 匀浆模块的运行态共享状态，供本模块任务和 UI 读取最近一次业务结果。
/// </summary>
public sealed class HomogenizationContext : ProductionContext
{
    public HomogenizationContext()
        : this(new HomogenizationModuleOptions().Presentation.MaxOutboundRecords)
    {
    }

    public HomogenizationContext(int maxOutboundRecords)
    {
        OutboundRecords = new BoundedRecordQueue<HomogenizationCellData>(Math.Max(1, maxOutboundRecords));
    }

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

    public BoundedRecordQueue<HomogenizationCellData> OutboundRecords { get; }

    public void RecordOutbound(HomogenizationCellData record)
    {
        ArgumentNullException.ThrowIfNull(record);

        LastOutboundRecord = record;
        OutboundRecords.Enqueue(record);
    }
}

internal sealed class HomogenizationContextFactory : IProductionContextFactory
{
    private readonly HomogenizationModuleOptions _moduleOptions;

    public HomogenizationContextFactory(IOptions<HomogenizationModuleOptions> moduleOptions)
    {
        _moduleOptions = moduleOptions.Value;
    }

    public string ModuleId => DependencyInjection.ModuleKey;

    public Type ContextType => typeof(HomogenizationContext);

    public ProductionContext Create(string deviceName)
        => new HomogenizationContext(_moduleOptions.Presentation.MaxOutboundRecords)
        {
            DeviceName = deviceName
        };
}
