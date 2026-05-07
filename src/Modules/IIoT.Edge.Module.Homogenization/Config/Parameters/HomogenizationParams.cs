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
        [ModuleParam(ParamValueKind.Bool, DefaultValue = "true", Role = ModuleParamRole.MesEnabled)]
        启用,

        /// <summary>MES 服务基础地址。</summary>
        [ModuleParam(ParamValueKind.String, Role = ModuleParamRole.MesBaseUrl)]
        服务地址,

        /// <summary>MES 工站编号，属于 MES 业务身份配置。</summary>
        [ModuleParam(ParamValueKind.String, Role = ModuleParamRole.StationNo)]
        工站编号,

        /// <summary>MES 签名令牌。</summary>
        [ModuleParam(ParamValueKind.String)]
        签名令牌
    }

    /// <summary>
    /// 匀浆云端上传参数键，云端契约未对齐时默认关闭。
    /// </summary>
    public enum Cloud
    {
        /// <summary>是否启用云端上传链路。</summary>
        [ModuleParam(ParamValueKind.Bool, DefaultValue = "false", Role = ModuleParamRole.CloudEnabled)]
        启用
    }

    /// <summary>
    /// 匀浆工序业务参数键，仅描述插件自己的现场规则。
    /// </summary>
    public enum Business
    {
        /// <summary>是否启用托盘码重复进出站拦截。</summary>
        [ModuleParam(ParamValueKind.Bool, DefaultValue = "false")]
        启用托盘码重码验证
    }
}
