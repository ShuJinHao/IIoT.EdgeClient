using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Module.Homogenization.Config.Parameters;

/// <summary>
/// 匀浆参数枚举集中容器。宿主仍按 MES、云端、插件业务三组读取和保存，插件只维护本模块自己的参数键。
/// </summary>
public static class HomogenizationParams
{
    /// <summary>
    /// 匀浆 MES 链路参数键，宿主负责读取、缓存和保存。
    /// </summary>
    public enum Mes
    {
        /// <summary>是否启用 MES 上传链路。</summary>
        [ModuleParam(
            ParamValueKind.Bool,
            DefaultValue = "true",
            Role = ModuleParamRole.MesEnabled,
            DisplayNameResourceKey = "Homogenization_Param_MesEnabled_DisplayName",
            DisplayNameFallback = "MES上传启用",
            DescriptionResourceKey = "Homogenization_Param_MesEnabled_Description",
            DescriptionFallback = "关闭后不探测 MES 心跳、不调用 MES 业务接口、不写 MES 补传；本地 PLC、Excel、产能和 UI 刷新继续运行。")]
        启用,

        /// <summary>MES 服务基础地址。</summary>
        [ModuleParam(
            ParamValueKind.String,
            Role = ModuleParamRole.MesBaseUrl,
            DisplayNameResourceKey = "Homogenization_Param_MesBaseUrl_DisplayName",
            DisplayNameFallback = "MES服务地址",
            DescriptionResourceKey = "Homogenization_Param_MesBaseUrl_Description",
            DescriptionFallback = "MES 接口基础地址，用于进站、出料、配方、设备状态和实时数据上传。")]
        服务地址,

        /// <summary>MES 工站编号，属于 MES 业务身份配置。</summary>
        [ModuleParam(
            ParamValueKind.String,
            Role = ModuleParamRole.StationNo,
            DisplayNameResourceKey = "Homogenization_Param_MesStationNo_DisplayName",
            DisplayNameFallback = "MES工站编号",
            DescriptionResourceKey = "Homogenization_Param_MesStationNo_Description",
            DescriptionFallback = "MES 侧识别当前工站的编号，随 MES 业务请求一起发送。")]
        工站编号,

        [ModuleParam(
            ParamValueKind.String,
            Role = ModuleParamRole.MesUpperComputerNo,
            DisplayNameResourceKey = "Homogenization_Param_MesUpperComputerNo_DisplayName",
            DisplayNameFallback = "MES上位机编码",
            DescriptionResourceKey = "Homogenization_Param_MesUpperComputerNo_Description",
            DescriptionFallback = "用于查询 MES 主批计划的 upperComputerNo，由参数设置维护。")]
        UpperComputerNo,

        /// <summary>MES 工序编码，用于生成追溯批次号。</summary>
        [ModuleParam(
            ParamValueKind.String,
            Role = ModuleParamRole.MesOperationCode,
            DisplayNameResourceKey = "Homogenization_Param_MesOperationCode_DisplayName",
            DisplayNameFallback = "MES工序编码",
            DescriptionResourceKey = "Homogenization_Param_MesOperationCode_Description",
            DescriptionFallback = "用于生成 MES 追溯批次号的 operationCode，例如正极制胶为 CG。")]
        OperationCode,

        /// <summary>MES 健康检查接口路径。</summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/heath",
            Role = ModuleParamRole.MesHealthPath,
            DisplayNameResourceKey = "Homogenization_Param_MesHealthPath_DisplayName",
            DisplayNameFallback = "MES健康检查路径",
            DescriptionResourceKey = "Homogenization_Param_MesHealthPath_Description",
            DescriptionFallback = "MES 健康检查接口相对路径，当前测试环境为 /heath。")]
        MesHealthPath,

        /// <summary>MES 进站校验接口路径。</summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/dev/dev/getIn/check",
            DisplayNameResourceKey = "Homogenization_Param_MesInboundPath_DisplayName",
            DisplayNameFallback = "MES进站校验路径",
            DescriptionResourceKey = "Homogenization_Param_MesInboundPath_Description",
            DescriptionFallback = "MES 进站校验接口相对路径。")]
        InboundPath,

        /// <summary>MES 出站出料接口路径。</summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/dev/dev/electrode/exit/push",
            DisplayNameResourceKey = "Homogenization_Param_MesOutboundPath_DisplayName",
            DisplayNameFallback = "MES出料上传路径",
            DescriptionResourceKey = "Homogenization_Param_MesOutboundPath_Description",
            DescriptionFallback = "MES 出料数据上传接口相对路径。")]
        OutboundPath,

        /// <summary>MES 配方参数接口路径。</summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/dev/dev/process/param",
            DisplayNameResourceKey = "Homogenization_Param_MesRecipePath_DisplayName",
            DisplayNameFallback = "MES配方上传路径",
            DescriptionResourceKey = "Homogenization_Param_MesRecipePath_Description",
            DescriptionFallback = "MES 配方参数上传接口相对路径。")]
        RecipePath,

        /// <summary>MES 实时数据接口路径。</summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/dev/dev/run/info",
            DisplayNameResourceKey = "Homogenization_Param_MesRealtimePath_DisplayName",
            DisplayNameFallback = "MES实时数据路径",
            DescriptionResourceKey = "Homogenization_Param_MesRealtimePath_Description",
            DescriptionFallback = "MES 实时运行数据上传接口相对路径。")]
        RealtimePath,

        /// <summary>MES 设备状态接口路径。</summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/dev/dev/realTime/status",
            DisplayNameResourceKey = "Homogenization_Param_MesEquipmentStatusPath_DisplayName",
            DisplayNameFallback = "MES设备状态路径",
            DescriptionResourceKey = "Homogenization_Param_MesEquipmentStatusPath_Description",
            DescriptionFallback = "MES 设备状态上传接口相对路径。")]
        EquipmentStatusPath,

        /// <summary>MES 主批计划查询接口路径。</summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/dev/dev/get/order",
            DisplayNameResourceKey = "Homogenization_Param_MesOrderPath_DisplayName",
            DisplayNameFallback = "MES主批计划查询路径",
            DescriptionResourceKey = "Homogenization_Param_MesOrderPath_Description",
            DescriptionFallback = "MES 主批计划查询接口相对路径。本批只参数化，不消费返回数据。")]
        OrderPath,

        /// <summary>MES 追溯批次号生成接口路径。</summary>
        [ModuleParam(
            ParamValueKind.String,
            DefaultValue = "/dev/dev/get/batchNumber",
            DisplayNameResourceKey = "Homogenization_Param_MesBatchNumberPath_DisplayName",
            DisplayNameFallback = "MES追溯批次号路径",
            DescriptionResourceKey = "Homogenization_Param_MesBatchNumberPath_Description",
            DescriptionFallback = "MES 追溯批次号生成接口相对路径，用于主批计划确认后生成 trace batch number。")]
        BatchNumberPath,

        /// <summary>MES 签名令牌。</summary>
        [ModuleParam(
            ParamValueKind.String,
            Role = ModuleParamRole.MesSignToken,
            DisplayNameResourceKey = "Homogenization_Param_MesSignToken_DisplayName",
            DisplayNameFallback = "MES签名令牌",
            DescriptionResourceKey = "Homogenization_Param_MesSignToken_Description",
            DescriptionFallback = "MES 接口签名令牌，用于生成 MES 请求签名。")]
        签名令牌
    }

    /// <summary>保留空类别作为强类型参数快照占位；Cloud 启停只允许使用系统级开关。</summary>
    public enum Cloud
    {
    }

    /// <summary>
    /// 匀浆工序业务参数键，仅描述插件自己的现场规则。
    /// </summary>
    public enum Business
    {
        /// <summary>是否启用托盘码重复进出站拦截。</summary>
        [ModuleParam(
            ParamValueKind.Bool,
            DefaultValue = "false",
            DisplayNameResourceKey = "Homogenization_Param_DuplicateTrayCheck_DisplayName",
            DisplayNameFallback = "托盘码重码验证启用",
            DescriptionResourceKey = "Homogenization_Param_DuplicateTrayCheck_Description",
            DescriptionFallback = "启用后，同一托盘码在进站或出料阶段重复触发时会被拦截。")]
        启用托盘码重码验证
    }
}
