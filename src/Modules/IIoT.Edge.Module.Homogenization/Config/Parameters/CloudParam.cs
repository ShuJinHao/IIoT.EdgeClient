using IIoT.Edge.Application.Abstractions.Config;

namespace IIoT.Edge.Module.Homogenization.Config.Parameters;

/// <summary>
/// 匀浆云端上传参数键，云端契约未对齐时默认关闭。
/// </summary>
public enum CloudParam
{
    [ModuleParam(ParamValueKind.Bool, DefaultValue = "false", Role = ModuleParamRole.CloudEnabled)]
    启用
}
