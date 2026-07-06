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

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Application.Modules.Mes;

/// <summary>
/// MES 多场景通道基类。负责类型校验、出料补传适配、工站号、信封、签名和请求执行，插件只实现场景字段映射。
/// </summary>
public abstract class MesScenarioChannelBase<TCellData> : IProcessMesUploader
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

    public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

    /// <summary>
    /// 生产业务时间服务，供插件 payload 在缺省业务时间时复用同一时区规则。
    /// </summary>
    protected IProductionTimeProvider ProductionTime => _productionTime;

    /// <summary>
    /// 出料补传入口。DataPipeline 补偿链路反序列化完整 CellDataJson 后，会回到这里继续调用插件映射。
    /// </summary>
    protected abstract Task<MesCallResult> UploadOutboundRecordAsync(
        DeviceSession? device,
        TCellData cellData,
        CellCompletedRecord record,
        CancellationToken cancellationToken);

    public async Task<MesCallResult> UploadAsync(
        ProcessUploadContext context,
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

    protected async Task<MesCallResult> ExecuteMesAsync(
        DeviceSession? device,
        string relativePath,
        Func<MesEnvelope, object> payloadFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payloadFactory);

        var signToken = await ResolveSignTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(signToken))
        {
            return MesCallResult.InvalidContext($"工序 {ProcessType} 未配置 MES 签名密钥。");
        }

        return await _requestExecutor.ExecuteAsync(
            ProcessType,
            device,
            relativePath,
            async (currentDevice, ct) =>
            {
                var stationNo = await ResolveStationNoAsync(currentDevice, ct).ConfigureAwait(false);
                var envelope = CreateEnvelope(currentDevice, stationNo, signToken);
                // payloadFactory 是插件侧字段映射边界，Application 不知道也不保存具体业务字段。
                return payloadFactory(envelope);
            },
            cancellationToken).ConfigureAwait(false);
    }

    protected async Task<MesCallResult> ExecuteRequiredMesAsync(
        string scenarioName,
        Func<CancellationToken, Task<string?>> relativePathResolver,
        DeviceSession? device,
        Func<MesEnvelope, object> payloadFactory,
        CancellationToken cancellationToken)
    {
        var relativePath = await ResolveRequiredPathAsync(scenarioName, relativePathResolver, cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(relativePath)
            ? MesCallResult.InvalidContext($"必选 MES 场景 {scenarioName} 未配置路径。")
            : await ExecuteMesAsync(device, relativePath, payloadFactory, cancellationToken).ConfigureAwait(false);
    }

    protected async Task<MesCallResult> ExecuteOptionalMesAsync(
        string scenarioName,
        Func<CancellationToken, Task<string?>> relativePathResolver,
        DeviceSession? device,
        Func<MesEnvelope, object> payloadFactory,
        CancellationToken cancellationToken)
    {
        var relativePath = await ResolveOptionalPathAsync(scenarioName, relativePathResolver, cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(relativePath)
            ? MesCallResult.Disabled($"可选 MES 场景 {scenarioName} 未配置，已跳过。")
            : await ExecuteMesAsync(device, relativePath, payloadFactory, cancellationToken).ConfigureAwait(false);
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

    protected async Task<MesCallResult<TData>> ExecuteOptionalMesGetAsync<TData>(
        string scenarioName,
        Func<CancellationToken, Task<string?>> relativePathResolver,
        IReadOnlyDictionary<string, string?> query,
        Func<JsonElement, TData> dataParser,
        CancellationToken cancellationToken)
    {
        var relativePath = await ResolveOptionalPathAsync(scenarioName, relativePathResolver, cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(relativePath)
            ? MesCallResult<TData>.Disabled($"可选 MES 场景 {scenarioName} 未配置，已跳过。")
            : await ExecuteMesGetAsync(relativePath, query, dataParser, cancellationToken).ConfigureAwait(false);
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

    protected async Task<MesCallResult<TData>> ExecuteOptionalMesPostAsync<TData>(
        string scenarioName,
        Func<CancellationToken, Task<string?>> relativePathResolver,
        object payload,
        Func<JsonElement, TData> dataParser,
        CancellationToken cancellationToken)
    {
        var relativePath = await ResolveOptionalPathAsync(scenarioName, relativePathResolver, cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(relativePath)
            ? MesCallResult<TData>.Disabled($"可选 MES 场景 {scenarioName} 未配置，已跳过。")
            : await ExecuteMesPostAsync(relativePath, payload, dataParser, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 工站号优先取本地参数配置；没有配置时回退到设备名/ClientCode，保持现有 MES 寻址规则。
    /// </summary>
    protected async Task<string> ResolveStationNoAsync(DeviceSession device, CancellationToken cancellationToken)
    {
        var configuredValue = await _moduleParamRoleProvider
            .GetMesStringAsync(
                ProcessType,
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

    /// <summary>
    /// MES 签名令牌走模块参数角色读取，避免插件再维护一套平行 Options 配置。
    /// </summary>
    protected async Task<string?> ResolveSignTokenAsync(CancellationToken cancellationToken)
    {
        var configuredValue = await _moduleParamRoleProvider
            .GetMesStringAsync(
                ProcessType,
                ModuleParamRole.MesSignToken,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(configuredValue)
            ? null
            : configuredValue.Trim();
    }

    protected string FormatTimestamp(DateTime time)
        => _productionTime.FormatBusinessTimestamp(time);

    protected static object CreateStandardMesPayload(MesEnvelope envelope, object data)
        => new
        {
            upperComputerNo = envelope.UpperComputerNo,
            timestamp = envelope.Timestamp,
            sign = envelope.Sign,
            stationNo = envelope.StationNo,
            data
        };

    private async Task<string?> ResolveRequiredPathAsync(
        string scenarioName,
        Func<CancellationToken, Task<string?>> relativePathResolver,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioName);
        ArgumentNullException.ThrowIfNull(relativePathResolver);

        var relativePath = await relativePathResolver(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            return relativePath.Trim();
        }

        Logger.Error($"[MES] 必选场景 {scenarioName} 未配置路径，数据将保留在补偿链路。工序={ProcessType}");
        return null;
    }

    private async Task<string?> ResolveOptionalPathAsync(
        string scenarioName,
        Func<CancellationToken, Task<string?>> relativePathResolver,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioName);
        ArgumentNullException.ThrowIfNull(relativePathResolver);

        var relativePath = await relativePathResolver(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            return relativePath.Trim();
        }

        Logger.Warn($"[MES] 可选场景 {scenarioName} 未配置路径，已跳过。工序={ProcessType}");
        return null;
    }

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
        var key = Encoding.UTF8.GetBytes(signToken);
        var bytes = Encoding.UTF8.GetBytes($"{upperComputerNo}{timestamp}");
        var hash = HMACSHA256.HashData(key, bytes);
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
