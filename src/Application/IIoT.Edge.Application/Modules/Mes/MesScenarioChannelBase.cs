using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Time;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Application.Modules.Mes;

/// <summary>
/// MES 多场景通道基类。负责类型校验、出料补传适配、工站号、信封、签名和请求执行，插件只实现场景字段映射。
/// </summary>
public abstract class MesScenarioChannelBase<
        TCellData,
        TInbound,
        TRealtime,
        TRecipe,
        TEquipmentStatus,
        TMainPlanRequest,
        TMainPlanResult,
        TTraceBatchRequest,
        TTraceBatchResult>
    : IMesScenarioChannel<
        TCellData,
        TInbound,
        TRealtime,
        TRecipe,
        TEquipmentStatus,
        TMainPlanRequest,
        TMainPlanResult,
        TTraceBatchRequest,
        TTraceBatchResult>
    where TCellData : CellDataBase
{
    private readonly MesRequestExecutor _requestExecutor;
    private readonly IModuleParamRoleProvider _moduleParamRoleProvider;
    private readonly IProductionTimeProvider _productionTime;
    private readonly string _processType;
    protected readonly ILogService Logger;

    protected MesScenarioChannelBase(
        string processType,
        ILogService logger,
        MesRequestExecutor requestExecutor,
        IModuleParamRoleProvider moduleParamRoleProvider,
        IProductionTimeProvider productionTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processType);

        _processType = processType;
        Logger = logger;
        _requestExecutor = requestExecutor;
        _moduleParamRoleProvider = moduleParamRoleProvider;
        _productionTime = productionTime;
    }

    public string ProcessType => _processType;

    public MesUploadMode UploadMode => MesUploadMode.Single;

    /// <summary>
    /// 生产业务时间服务，供插件 payload 在缺省业务时间时复用同一时区规则。
    /// </summary>
    protected IProductionTimeProvider ProductionTime => _productionTime;

    /// <summary>
    /// MES 签名令牌，由插件配置提供，Application 只使用它计算 sign。
    /// </summary>
    protected abstract string SignToken { get; }

    public abstract Task<MesCallResult> UploadInboundAsync(
        DeviceSession? device,
        TInbound inbound,
        CancellationToken cancellationToken = default);

    public abstract Task<MesCallResult> UploadOutboundAsync(
        DeviceSession? device,
        TCellData cellData,
        CancellationToken cancellationToken = default);

    public abstract Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        TRealtime snapshot,
        CancellationToken cancellationToken = default);

    public abstract Task<MesCallResult> UploadRecipeAsync(
        DeviceSession? device,
        TRecipe snapshot,
        CancellationToken cancellationToken = default);

    public abstract Task<MesCallResult> UploadEquipmentStatusAsync(
        DeviceSession? device,
        TEquipmentStatus snapshot,
        CancellationToken cancellationToken = default);

    public abstract Task<MesCallResult<TMainPlanResult>> GetMainPlanAsync(
        TMainPlanRequest request,
        CancellationToken cancellationToken = default);

    public abstract Task<MesCallResult<TTraceBatchResult>> GenerateTraceBatchNumberAsync(
        TTraceBatchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 出料补传入口。DataPipeline 补偿链路反序列化完整 CellDataJson 后，会回到这里继续调用插件映射。
    /// </summary>
    protected abstract Task<MesCallResult> UploadOutboundRecordAsync(
        DeviceSession? device,
        TCellData cellData,
        CellCompletedRecord record,
        CancellationToken cancellationToken);

    public async Task<MesCallResult> UploadAsync(
        ProcessMesUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return MesCallResult.Success("没有需要上传 MES 的记录。");
        }

        foreach (var record in records)
        {
            if (record.CellData is not TCellData cellData)
            {
                var message = $"MES 上传器 {GetType().Name} 收到不匹配的工序数据：{record.CellData.ProcessType}。";
                Logger.Error($"[MES] {message}");
                return MesCallResult.InvalidContext(message);
            }

            var result = await UploadOutboundRecordAsync(context.Device, cellData, record, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                Logger.Error($"[MES] 工序 {ProcessType} 上传失败：{result.Message}");
                return result;
            }
        }

        return MesCallResult.Success();
    }

    protected Task<MesCallResult> ExecuteMesAsync(
        DeviceSession? device,
        string relativePath,
        Func<MesEnvelope, object> payloadFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payloadFactory);

        return _requestExecutor.ExecuteAsync(
            ProcessType,
            device,
            relativePath,
            async (currentDevice, ct) =>
            {
                var stationNo = await ResolveStationNoAsync(currentDevice, ct).ConfigureAwait(false);
                var envelope = CreateEnvelope(currentDevice, stationNo, SignToken);
                // payloadFactory 是插件侧字段映射边界，Application 不知道也不保存具体业务字段。
                return payloadFactory(envelope);
            },
            cancellationToken);
    }

    protected Task<MesCallResult<TData>> ExecuteMesGetAsync<TData>(
        string relativePath,
        IReadOnlyDictionary<string, string?> query,
        Func<JsonElement, TData> dataParser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(dataParser);

        return _requestExecutor.ExecuteGetAsync(
            ProcessType,
            relativePath,
            query,
            dataParser,
            cancellationToken);
    }

    protected Task<MesCallResult<TData>> ExecuteMesPostAsync<TData>(
        string relativePath,
        object payload,
        Func<JsonElement, TData> dataParser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(dataParser);

        return _requestExecutor.ExecutePostAsync(
            ProcessType,
            relativePath,
            payload,
            dataParser,
            cancellationToken);
    }

    /// <summary>
    /// 工站号优先取本地参数配置；没有配置时回退到设备名/ClientCode，保持现有 MES 寻址规则。
    /// </summary>
    protected async Task<string> ResolveStationNoAsync(DeviceSession device, CancellationToken cancellationToken)
    {
        var configuredValue = await _moduleParamRoleProvider
            .GetStringAsync(
                ProcessType,
                ModuleParamCategory.Mes,
                ModuleParamRole.StationNo,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return configuredValue.Trim();
        }

        return string.IsNullOrWhiteSpace(device.DeviceName)
            ? device.ClientCode
            : device.DeviceName;
    }

    protected string FormatTimestamp(DateTime time)
        => _productionTime.FormatBusinessTimestamp(time);

    private MesEnvelope CreateEnvelope(DeviceSession device, string stationNo, string signToken)
    {
        var upperComputerNo = string.IsNullOrWhiteSpace(device.ClientCode)
            ? device.DeviceName
            : device.ClientCode;
        var timestamp = FormatTimestamp(_productionTime.UtcNow);
        var sign = BuildSign(upperComputerNo, timestamp, signToken);
        return new MesEnvelope(upperComputerNo, timestamp, sign, stationNo);
    }

    private static string BuildSign(string upperComputerNo, string timestamp, string signToken)
    {
        var bytes = Encoding.UTF8.GetBytes($"{upperComputerNo}{timestamp}{signToken}");
        var hash = MD5.HashData(bytes);
        var builder = new StringBuilder(hash.Length * 2);

        foreach (var value in hash)
        {
            builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    protected sealed record MesEnvelope(
        string UpperComputerNo,
        string Timestamp,
        string Sign,
        string StationNo);
}
