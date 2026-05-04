using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Module.Stacking.Config.Parameters;

/// <summary>
/// 叠片云端上传参数键，云端契约未对齐时默认关闭。
/// </summary>
public enum CloudParam
{
    [ModuleParam(ParamValueKind.Bool, DefaultValue = "false", Role = ModuleParamRole.CloudEnabled)]
    启用
}
