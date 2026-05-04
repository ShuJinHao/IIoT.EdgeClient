using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Module.Stacking.Config.Parameters;

/// <summary>
/// 叠片工序业务参数键，承载条码和 PLC ID 等插件内规则开关。
/// </summary>
public enum BusinessParam
{
    [ModuleParam(ParamValueKind.Bool, DefaultValue = "false")]
    启用条码重码验证,

    [ModuleParam(ParamValueKind.Bool, DefaultValue = "true")]
    启用PLCID重码替换
}
