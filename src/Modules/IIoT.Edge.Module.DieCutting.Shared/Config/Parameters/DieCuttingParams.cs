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
        /// MES 上位机编码，用于查询主批计划。
        /// </summary>
        [ModuleParam(
            ParamValueKind.String,
            Role = ModuleParamRole.MesUpperComputerNo,
            DisplayNameFallback = "MES上位机编码",
            DescriptionFallback = "获取主批计划接口使用的 upperComputerNo。负极默认 P1-APUC，正极默认 P2-CPUC。")]
        UpperComputerNo,

        /// <summary>
        /// MES 工序编码，用于生成追溯批次号。
        /// </summary>
        [ModuleParam(
            ParamValueKind.String,
            Role = ModuleParamRole.MesOperationCode,
            DisplayNameFallback = "MES工序编码",
            DescriptionFallback = "生成追溯批次号接口使用的 operationCode。负极默认 AP，正极默认 CP。")]
        OperationCode,

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
        /// MES 主批计划查询路径。
        /// </summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/dev/dev/get/order",
            DisplayNameFallback = "MES主批计划查询路径",
            DescriptionFallback = "按 upperComputerNo 和 timestamp 查询主批计划的接口相对路径。")]
        OrderPath,

        /// <summary>
        /// MES 追溯批次号生成路径。
        /// </summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/dev/dev/get/batchNumber",
            DisplayNameFallback = "MES追溯批次号生成路径",
            DescriptionFallback = "按 masterPlanCode 和 operationCode 生成追溯批次号的接口相对路径。")]
        BatchNumberPath,

        /// <summary>
        /// 电极段进站检测路径。
        /// </summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/dev/dev/electrode/getIn/check",
            DisplayNameFallback = "MES电极段进站检测路径",
            DescriptionFallback = "电极段进站检测接口相对路径。当前模切出站上传链路不调用该接口，先作为现场接口契约参数播种。")]
        InboundPath,

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
        /// MES 设备状态接口路径。
        /// </summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/dev/dev/realTime/status",
            DisplayNameFallback = "MES设备状态路径",
            DescriptionFallback = "模切 R100 设备状态上传接口相对路径。")]
        EquipmentStatusPath
    }

    /// <summary>
    /// 模切暂未注册生产数据 Cloud payload，因此本分类暂不声明插件级 Cloud 参数。
    /// 系统日志、设备识别和 Cloud 补传不受插件参数控制。
    /// </summary>
    public enum Cloud
    {
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
            DescriptionFallback = "PLC 只读数据扫描和模切采样处理共用的每轮间隔。采集后有变化才上传 MES，不依赖在线 Cloud。")]
        采集频率毫秒
    }
}
