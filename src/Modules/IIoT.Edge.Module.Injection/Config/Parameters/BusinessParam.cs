using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Module.Injection.Config.Parameters;

/// <summary>
/// 注液工序业务参数键，后续接入真实业务时继续在此枚举中扩展。
/// </summary>
public enum BusinessParam
{
    [ModuleParam(ParamValueKind.Bool, DefaultValue = "false")]
    启用条码重码验证
}
