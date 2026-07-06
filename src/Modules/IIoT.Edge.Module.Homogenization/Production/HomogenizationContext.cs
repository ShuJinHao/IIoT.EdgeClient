using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Io;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.Collections;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Production;

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

    /// <summary>
    /// 最近一次进站握手读取到的托盘码。
    /// </summary>
    public string? LastInboundTrayCode { get; set; }

    /// <summary>
    /// 最近一次进站 MES 校验的完成时间。
    /// </summary>
    public DateTime? LastInboundAt { get; set; }

    /// <summary>
    /// 最近一次进站 MES 校验结果文本。
    /// </summary>
    public string? LastInboundResult { get; set; }

    /// <summary>
    /// 最近一次出料握手读取到的托盘码。
    /// </summary>
    public string? LastOutboundTrayCode { get; set; }

    /// <summary>
    /// 最近一次出料数据进入 DataPipeline 的时间。
    /// </summary>
    public DateTime? LastOutboundAt { get; set; }

    /// <summary>
    /// 最近一次出料数据接收、校验或入队结果。
    /// </summary>
    public string? LastOutboundResult { get; set; }

    /// <summary>
    /// 最近一次出料完整电芯记录，Cloud/MES 补偿序列化的业务数据也来自该类型。
    /// </summary>
    public HomogenizationCellData? LastOutboundRecord { get; set; }

    /// <summary>
    /// 最近一次配方上传完成时间。
    /// </summary>
    public DateTime? LastRecipeAt { get; set; }

    /// <summary>
    /// 最近一次配方 MES 上传结果文本。
    /// </summary>
    public string? LastRecipeResult { get; set; }

    /// <summary>
    /// 最近一次配方快照，来源于 PLC 配方连续读区。
    /// </summary>
    public HomogenizationRecipeSnapshot? LastRecipeSnapshot { get; set; }

    /// <summary>
    /// 最近一次实时数据上传完成时间。
    /// </summary>
    public DateTime? LastRealtimeAt { get; set; }

    /// <summary>
    /// 最近一次实时数据 MES 上传结果文本。
    /// </summary>
    public string? LastRealtimeResult { get; set; }

    /// <summary>
    /// 最近一次实时数据快照，来源于 PLC 实时信号。
    /// </summary>
    public HomogenizationRealtimeSnapshot? LastRealtimeSnapshot { get; set; }

    /// <summary>
    /// 最近一次已被上传队列接收的实时数据数字指纹；采集时间不参与比较。
    /// </summary>
    public string? LastRealtimeFingerprint { get; set; }

    /// <summary>
    /// 最近一次设备状态上传完成时间。
    /// </summary>
    public DateTime? LastEquipmentStatusAt { get; set; }

    /// <summary>
    /// 最近一次设备状态 MES 上传结果文本。
    /// </summary>
    public string? LastEquipmentStatusResult { get; set; }

    /// <summary>
    /// 最近一次设备状态快照，来源于 PLC 状态码和匀浆状态文本映射。
    /// </summary>
    public HomogenizationEquipmentStatusSnapshot? LastEquipmentStatusSnapshot { get; set; }

    /// <summary>
    /// 当前已确认的 MES 主批计划，由生产前选择流程写入，供运行门禁和 UI 摘要读取。
    /// </summary>
    public ProductionPlanOption? SelectedProductionPlan { get; set; }

    /// <summary>
    /// 本次软件启动后选择主批计划生成的运行会话号；不跨进程复用。
    /// </summary>
    public string? PlanSessionId { get; set; }

    /// <summary>
    /// 当前主批计划生成得到的 MES 追溯批次号。没有该值时，MES 启用状态下不允许进入生产上传链路。
    /// </summary>
    public string? TraceBatchNumber { get; set; }

    /// <summary>
    /// 追溯批次号生成时间，用于现场追踪当前批次选择是否已经生效。
    /// </summary>
    public DateTime? TraceBatchGeneratedAt { get; set; }

    /// <summary>
    /// 最近一次追溯批次号生成失败原因，只用于 UI 和诊断展示，不参与 PLC 应答码设计。
    /// </summary>
    public string? TraceBatchError { get; set; }

    /// <summary>
    /// 最近一次 PLC 心跳镜像时间，用于判断匀浆运行任务是否仍在循环。
    /// </summary>
    public DateTime LastHeartbeatAt { get; set; }

    /// <summary>
    /// UI 保留的最近出料记录队列，只用于运行态展示，不承担长期存储。
    /// </summary>
    public BoundedRecordQueue<HomogenizationCellData> OutboundRecords { get; }

    public void RecordOutbound(HomogenizationCellData record)
    {
        ArgumentNullException.ThrowIfNull(record);

        LastOutboundRecord = record;
        OutboundRecords.Enqueue(record);
    }

    /// <summary>
    /// 判断指定阶段是否已经处理过该托盘码。进站和出站独立记录，避免正常出站被进站记录误判。
    /// </summary>
    public bool HasProcessedTray(HomogenizationTrayCodeStage stage, string trayCode)
        => HasCell(BuildTrayKey(stage, trayCode));

    /// <summary>
    /// 标记指定阶段的托盘码已处理，用于插件业务重码校验。
    /// </summary>
    public void MarkProcessedTray(
        HomogenizationTrayCodeStage stage,
        string trayCode,
        string status,
        DateTime occurredAt)
    {
        var normalizedTrayCode = NormalizeTrayCode(trayCode);
        AddCell(
            BuildTrayKey(stage, normalizedTrayCode),
            new HomogenizationCellData
            {
                TrayCode = normalizedTrayCode,
                DeviceName = DeviceName,
                RuntimeStatus = status,
                CompletedTime = occurredAt
            });
    }

    private static string BuildTrayKey(HomogenizationTrayCodeStage stage, string trayCode)
        => $"Homogenization.{stage}:{NormalizeTrayCode(trayCode)}";

    private static string NormalizeTrayCode(string trayCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trayCode);
        return trayCode.Trim();
    }
}

/// <summary>
/// 匀浆托盘码重码校验范围。进站和出站独立记录，保证同一托盘正常完成入站后仍可出站。
/// </summary>
public enum HomogenizationTrayCodeStage
{
    Inbound,
    Outbound
}

/// <summary>
/// 匀浆运行上下文工厂，按模块配置创建带出料记录缓存上限的运行态上下文。
/// </summary>
internal sealed class HomogenizationContextFactory : IProductionContextFactory
{
    private readonly HomogenizationModuleOptions _moduleOptions;

    public HomogenizationContextFactory(IOptions<HomogenizationModuleOptions> moduleOptions)
    {
        _moduleOptions = moduleOptions.Value;
    }

    /// <summary>
    /// 当前工厂所属匀浆模块标识。
    /// </summary>
    public string ModuleId => DependencyInjection.ModuleKey;

    /// <summary>
    /// 宿主可创建的匀浆上下文类型。
    /// </summary>
    public Type ContextType => typeof(HomogenizationContext);

    public ProductionContext Create(string deviceName)
        => new HomogenizationContext(_moduleOptions.Presentation.MaxOutboundRecords)
        {
            DeviceName = deviceName
        };
}
