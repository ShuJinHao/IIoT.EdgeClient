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

        /// <summary>MES 签名令牌。</summary>
        [ModuleParam(
            ParamValueKind.String,
            DisplayNameResourceKey = "Homogenization_Param_MesSignToken_DisplayName",
            DisplayNameFallback = "MES签名令牌",
            DescriptionResourceKey = "Homogenization_Param_MesSignToken_Description",
            DescriptionFallback = "MES 接口签名令牌，用于生成 MES 请求签名。")]
        签名令牌
    }

    /// <summary>
    /// 匀浆云端上传参数键，云端契约未对齐时默认关闭。
    /// </summary>
    public enum Cloud
    {
        /// <summary>是否启用云端上传链路。</summary>
        [ModuleParam(
            ParamValueKind.Bool,
            DefaultValue = "false",
            Role = ModuleParamRole.CloudEnabled,
            DisplayNameResourceKey = "Homogenization_Param_CloudEnabled_DisplayName",
            DisplayNameFallback = "云端上传启用",
            DescriptionResourceKey = "Homogenization_Param_CloudEnabled_Description",
            DescriptionFallback = "关闭后不访问 Cloud bootstrap、refresh 和同步接口、不写 Cloud 补传；本地 PLC、Excel、产能和 UI 刷新继续运行。")]
        启用
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
