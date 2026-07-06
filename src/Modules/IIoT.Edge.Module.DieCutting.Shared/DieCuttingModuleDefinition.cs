namespace IIoT.Edge.Module.DieCutting;

/// <summary>
/// 模切插件线体定义。AP/CP 插件共享采集逻辑，只在模块身份和默认设备清单上区分。
/// </summary>
public sealed class DieCuttingModuleDefinition
{
    public const int DefaultPlcPort = 65531;
    public const int LegacyDefaultPlcPort = 65530;

    public DieCuttingModuleDefinition(
        string moduleId,
        string displayName,
        string deviceNamePrefix,
        string deviceEndpointPrefix,
        string mesBaseUrl,
        string upperComputerNo,
        string operationCode,
        string seedRemark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceNamePrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceEndpointPrefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(upperComputerNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedRemark);

        ModuleId = moduleId;
        ProcessType = moduleId;
        DisplayName = displayName;
        DeviceNamePrefix = deviceNamePrefix;
        DeviceEndpointPrefix = deviceEndpointPrefix;
        MesBaseUrl = mesBaseUrl?.Trim() ?? string.Empty;
        UpperComputerNo = upperComputerNo;
        OperationCode = operationCode;
        SeedRemark = seedRemark;
        LegacyMesBaseUrls = ResolveLegacyMesBaseUrls(moduleId);
        DefaultDevices = BuildLineDevices();
    }

    public string ModuleId { get; }

    public string ProcessType { get; }

    public string DisplayName { get; }

    public string DeviceNamePrefix { get; }

    public string DeviceEndpointPrefix { get; }

    public string MesBaseUrl { get; }

    public string UpperComputerNo { get; }

    public string OperationCode { get; }

    public string SeedRemark { get; }

    public IReadOnlyCollection<string> LegacyMesBaseUrls { get; }

    public IReadOnlyList<DieCuttingDeviceSeed> DefaultDevices { get; }

    public string RealtimeDiagnosticsChannel => $"{ModuleId}.Realtime";

    public string DeviceStatusDiagnosticsChannel => $"{ModuleId}.DeviceStatus";

    public string RealtimeSampleUploadTaskKey => $"{ModuleId}.RealtimeSampleUpload";

    public string DeviceStatusUploadTaskKey => $"{ModuleId}.DeviceStatusUpload";

    private IReadOnlyList<DieCuttingDeviceSeed> BuildLineDevices()
        => Enumerable.Range(1, 12)
            .Select(index => new DieCuttingDeviceSeed(
                DeviceName: $"{DeviceNamePrefix}{index:D2}",
                IpAddress: $"{DeviceEndpointPrefix}{index:D2}.local",
                DeviceCode: $"{DeviceNamePrefix}{index:D2}",
                DeviceDisplayName: $"{DeviceNamePrefix}{index:D2}",
                UpperComputerNo: UpperComputerNo,
                Remark: SeedRemark,
                IsEnabled: false,
                DeviceModel: "Mc",
                Port1: DefaultPlcPort,
                ConnectTimeout: 3000,
                ProtocolFrame: "E4"))
            .ToArray();

    private static IReadOnlyCollection<string> ResolveLegacyMesBaseUrls(string moduleId)
        => [];
}

/// <summary>
/// 模切默认设备样本。后续云端或现场配置可覆盖启用状态和身份信息。
/// </summary>
public sealed record DieCuttingDeviceSeed(
    string DeviceName,
    string IpAddress,
    string DeviceCode,
    string DeviceDisplayName,
    string UpperComputerNo,
    string Remark,
    bool IsEnabled,
    string DeviceModel,
    int Port1,
    int ConnectTimeout,
    string? ProtocolFrame = null);
