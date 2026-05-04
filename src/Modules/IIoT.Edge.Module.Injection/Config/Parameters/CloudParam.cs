using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Module.Injection.Config.Parameters;

/// <summary>
/// 注液云端上传参数键，云端契约未对齐时默认关闭。
/// </summary>
public enum CloudParam
{
    [ModuleParam(ParamValueKind.Bool, DefaultValue = "false", Role = ModuleParamRole.CloudEnabled)]
    启用
}
