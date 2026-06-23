namespace IIoT.Edge.Module.DieCutting.Config;

/// <summary>
/// 模切模块运行配置，全部来自本地配置快照，不依赖在线 Cloud。
/// </summary>
public sealed class DieCuttingModuleOptions
{
    /// <summary>
    /// 模切页面刷新和展示配置。
    /// </summary>
    public DieCuttingPresentationOptions Presentation { get; set; } = new();

    /// <summary>
    /// 模切采样和上传默认运行参数。
    /// </summary>
    public DieCuttingRuntimeOptions Runtime { get; set; } = new();

    /// <summary>
    /// PLC 设备到 MES 设备身份的预留映射。
    /// </summary>
    public DieCuttingMesIdentityOptions MesIdentity { get; set; } = new();
}

/// <summary>
/// 模切数据页面配置。
/// </summary>
public sealed class DieCuttingPresentationOptions
{
    /// <summary>
    /// 数据页面刷新间隔，单位毫秒。
    /// </summary>
    public int DataViewRefreshIntervalMs { get; set; } = 1000;
}

/// <summary>
/// 模切运行时默认频率和新鲜度参数。
/// </summary>
public sealed class DieCuttingRuntimeOptions
{
    /// <summary>
    /// PLC 只读数据扫描默认间隔，单位毫秒。
    /// </summary>
    public int DataReadLoopIntervalMs { get; set; } = 1000;

    /// <summary>
    /// MES 采样上传默认间隔，单位毫秒。
    /// </summary>
    public int UploadLoopIntervalMs { get; set; } = 10000;

    /// <summary>
    /// PLC 数据超过该时间未刷新则不上报 MES，单位毫秒。
    /// </summary>
    public int DataFreshnessTimeoutMs { get; set; } = 5000;
}

/// <summary>
/// 模切 MES 设备身份映射配置，正式编码由 MES 提供后填入。
/// </summary>
public sealed class DieCuttingMesIdentityOptions
{
    /// <summary>
    /// 缺少正式 MES 设备编码时是否临时回退到 PLC 设备名。
    /// </summary>
    public bool UseDeviceNameWhenCodeMissing { get; set; }

    /// <summary>
    /// 按 PLC 设备名配置的 MES 设备身份。
    /// </summary>
    public Dictionary<string, DieCuttingMesDeviceIdentityOptions> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 解析单台 PLC 的 MES 设备身份，未配置时保留可运行的本地占位。
    /// </summary>
    public DieCuttingDeviceIdentity Resolve(string deviceName)
    {
        var normalizedDeviceName = string.IsNullOrWhiteSpace(deviceName) ? "DieCutting-PLC" : deviceName.Trim();
        Devices.TryGetValue(normalizedDeviceName, out var configured);
        var deviceCode = configured?.DeviceCode?.Trim();
        var displayName = configured?.DeviceName?.Trim();
        var upperComputerNo = configured?.UpperComputerNo?.Trim();

        if (string.IsNullOrWhiteSpace(deviceCode) && UseDeviceNameWhenCodeMissing)
        {
            deviceCode = normalizedDeviceName;
        }

        return new DieCuttingDeviceIdentity(
            deviceCode ?? string.Empty,
            string.IsNullOrWhiteSpace(displayName) ? normalizedDeviceName : displayName,
            string.IsNullOrWhiteSpace(upperComputerNo) ? deviceCode ?? normalizedDeviceName : upperComputerNo);
    }
}

/// <summary>
/// 单台模切设备的 MES 身份配置。
/// </summary>
public sealed class DieCuttingMesDeviceIdentityOptions
{
    /// <summary>
    /// MES 侧设备编码，待 MES 确认后填入。
    /// </summary>
    public string? DeviceCode { get; set; }

    /// <summary>
    /// MES 侧设备名称，未配置时使用 PLC 设备名。
    /// </summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// MES 上位机编码，未配置时使用设备编码。
    /// </summary>
    public string? UpperComputerNo { get; set; }
}

/// <summary>
/// 模切上传时使用的设备身份快照。
/// </summary>
public sealed record DieCuttingDeviceIdentity(
    string DeviceCode,
    string DeviceName,
    string UpperComputerNo);
