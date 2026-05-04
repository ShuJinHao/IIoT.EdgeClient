using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Module.Homogenization.Config.Parameters;

/// <summary>
/// 匀浆 MES 链路参数键，宿主负责读取、缓存和保存。
/// </summary>
public enum MesParam
{
    [ModuleParam(ParamValueKind.Bool, DefaultValue = "true", Role = ModuleParamRole.MesEnabled)]
    启用,

    [ModuleParam(ParamValueKind.String, Role = ModuleParamRole.MesBaseUrl)]
    服务地址,

    [ModuleParam(ParamValueKind.String, Role = ModuleParamRole.StationNo)]
    工站编号,

    [ModuleParam(ParamValueKind.String)]
    签名令牌
}
