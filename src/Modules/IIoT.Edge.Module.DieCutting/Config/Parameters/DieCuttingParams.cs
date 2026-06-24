using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Module.DieCutting.Config.Parameters;

/// <summary>
/// 模切插件参数枚举，宿主负责生成参数页、保存到本地参数快照并供运行任务读取。
/// </summary>
public static class DieCuttingParams
{
    /// <summary>
    /// 模切 MES 链路参数。
    /// </summary>
    public enum Mes
    {
        /// <summary>
        /// 是否启用 MES 采样上传。
        /// </summary>
        [ModuleParam(
            ParamValueKind.Bool,
            DefaultValue = "true",
            Role = ModuleParamRole.MesEnabled,
            DisplayNameFallback = "MES上传启用",
            DescriptionFallback = "关闭后不探测 MES、不上传模切采样数据；PLC 只读采集和本地页面继续运行。")]
        启用,

        /// <summary>
        /// MES 服务基础地址。
        /// </summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "http://10.98.101.247:8080",
            Role = ModuleParamRole.MesBaseUrl,
            DisplayNameFallback = "MES服务地址",
            DescriptionFallback = "MES 接口基础地址。")]
        服务地址,

        /// <summary>
        /// MES 健康检查路径。
        /// </summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/heath",
            Role = ModuleParamRole.MesHealthPath,
            DisplayNameFallback = "MES健康检查路径",
            DescriptionFallback = "MES 健康检查接口相对路径。")]
        MesHealthPath,

        /// <summary>
        /// 模切追溯出站上传路径。
        /// </summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/dev/dev/electrode/exit/push",
            DisplayNameFallback = "MES追溯出站上传路径",
            DescriptionFallback = "模切追溯报表出站上传接口相对路径。")]
        OutboundPath,

        /// <summary>
        /// MES 上传频率，单位毫秒。
        /// </summary>
        [ModuleParam(
            ParamValueKind.Int,
            DefaultValue = "10000",
            Unit = "ms",
            MinValue = "1000",
            DisplayNameFallback = "MES上传频率",
            DescriptionFallback = "模切采样快照上传 MES 的周期。")]
        上传频率毫秒,

        /// <summary>
        /// PLC 数据新鲜度超时，单位毫秒。
        /// </summary>
        [ModuleParam(
            ParamValueKind.Int,
            DefaultValue = "5000",
            Unit = "ms",
            MinValue = "1000",
            DisplayNameFallback = "数据新鲜度超时",
            DescriptionFallback = "PLC 只读数据超过该时间未刷新时不上报 MES，避免旧值伪装成新采样。")]
        数据新鲜度超时毫秒
    }

    /// <summary>
    /// 模切 Cloud 上传参数，本插件首版不上传 Cloud。
    /// </summary>
    public enum Cloud
    {
        /// <summary>
        /// 是否启用云端上传，首版默认关闭。
        /// </summary>
        [ModuleParam(
            ParamValueKind.Bool,
            DefaultValue = "false",
            Role = ModuleParamRole.CloudEnabled,
            DisplayNameFallback = "云端上传启用",
            DescriptionFallback = "模切只读采样首版不走 Cloud 上传链路。")]
        启用
    }

    /// <summary>
    /// 模切本地业务参数。
    /// </summary>
    public enum Business
    {
        /// <summary>
        /// PLC 只读数据扫描频率，单位毫秒。
        /// </summary>
        [ModuleParam(
            ParamValueKind.Int,
            DefaultValue = "1000",
            Unit = "ms",
            MinValue = "500",
            Role = ModuleParamRole.DataReadLoopIntervalMs,
            DisplayNameFallback = "PLC采集频率",
            DescriptionFallback = "DeviceComm 只读数据扫描任务每轮间隔，运行时读取本地参数快照，不依赖在线 Cloud。")]
        采集频率毫秒
    }
}
