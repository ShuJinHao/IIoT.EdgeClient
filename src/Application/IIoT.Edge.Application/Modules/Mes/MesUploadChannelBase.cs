using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Application.Modules.Mes;

/// <summary>
/// MES 上传通道基础骨架。负责设备上下文、工站编号、签名和 HTTP 执行，不保存插件业务字段。
/// </summary>
public abstract class MesUploadChannelBase<TCellData> : ProcessMesUploaderBase<TCellData>
    where TCellData : CellDataBase
{
    private readonly MesRequestExecutor _requestExecutor;
    private readonly ILocalParameterConfigService _parameterConfigService;

    protected MesUploadChannelBase(
        string processType,
        ILogService logger,
        MesRequestExecutor requestExecutor,
        ILocalParameterConfigService parameterConfigService)
        : base(processType, logger)
    {
        _requestExecutor = requestExecutor;
        _parameterConfigService = parameterConfigService;
    }

    protected abstract string SignToken { get; }

    /// <summary>
    /// 出料补传入口。DataPipeline 补偿链路反序列化完整 CellDataJson 后，会回到这里继续调用插件映射。
    /// </summary>
    protected abstract Task<MesCallResult> UploadOutboundRecordAsync(
        DeviceSession? device,
        TCellData cellData,
        CellCompletedRecord record,
        CancellationToken cancellationToken);

    protected sealed override Task<MesCallResult> UploadCellAsync(
        ProcessMesUploadContext context,
        TCellData cellData,
        CellCompletedRecord record,
        CancellationToken cancellationToken)
        => UploadOutboundRecordAsync(context.Device, cellData, record, cancellationToken);

    protected Task<MesCallResult> ExecuteMesAsync(
        DeviceSession? device,
        string relativePath,
        Func<MesEnvelope, object> payloadFactory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payloadFactory);

        return _requestExecutor.ExecuteAsync(
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

    /// <summary>
    /// 工站号优先取本地参数配置；没有配置时回退到设备名/ClientCode，保持现有 MES 寻址规则。
    /// </summary>
    protected async Task<string> ResolveStationNoAsync(DeviceSession device, CancellationToken cancellationToken)
    {
        var configuredValue = await _parameterConfigService
            .GetSystemConfigValueAsync(SystemConfigKey.工站编号, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return configuredValue.Trim();
        }

        return string.IsNullOrWhiteSpace(device.DeviceName)
            ? device.ClientCode
            : device.DeviceName;
    }

    protected static string FormatTimestamp(DateTime time)
        => time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static MesEnvelope CreateEnvelope(DeviceSession device, string stationNo, string signToken)
    {
        var upperComputerNo = string.IsNullOrWhiteSpace(device.ClientCode)
            ? device.DeviceName
            : device.ClientCode;
        var timestamp = FormatTimestamp(DateTime.UtcNow);
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
