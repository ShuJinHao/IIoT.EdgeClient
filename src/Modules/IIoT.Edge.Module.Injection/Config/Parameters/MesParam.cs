using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Module.Injection.Config.Parameters;

/// <summary>
/// 注液 MES 链路参数键，当前业务未实现时默认关闭。
/// </summary>
public enum MesParam
{
    [ModuleParam(ParamValueKind.Bool, DefaultValue = "false", Role = ModuleParamRole.MesEnabled)]
    启用,

    [ModuleParam(ParamValueKind.String, Role = ModuleParamRole.MesBaseUrl)]
    服务地址,

    [ModuleParam(ParamValueKind.String, Role = ModuleParamRole.StationNo)]
    工站编号
}
