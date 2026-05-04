using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Module.Homogenization.Config.Parameters;

/// <summary>
/// 匀浆工序业务参数键，仅描述插件自己的现场规则。
/// </summary>
public enum BusinessParam
{
    [ModuleParam(ParamValueKind.Bool, DefaultValue = "false")]
    启用托盘码重码验证
}
