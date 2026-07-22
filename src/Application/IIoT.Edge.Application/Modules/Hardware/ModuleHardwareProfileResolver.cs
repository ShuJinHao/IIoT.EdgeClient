using IIoT.Edge.Module.Contracts.Modules;

namespace IIoT.Edge.Application.Modules.Hardware;

/// <summary>
/// 解析当前运行库唯一的插件硬件模板，统一客户端单插件库约束。
/// </summary>
public sealed class ModuleHardwareProfileResolver(IEnumerable<IModuleHardwareProfileProvider> hardwareProfiles)
{
    private readonly IModuleHardwareProfileProvider[] _hardwareProfiles = hardwareProfiles
        .ToArray();

    public IModuleHardwareProfileProvider? Resolve()
        => _hardwareProfiles.Length == 1 ? _hardwareProfiles[0] : null;

    public IModuleHardwareProfileProvider? Resolve(out string? errorMessage)
    {
        if (_hardwareProfiles.Length == 0)
        {
            errorMessage = "当前插件库没有注册标准 IO 点位模板。";
            return null;
        }

        if (_hardwareProfiles.Length > 1)
        {
            errorMessage = "当前数据库应只对应一个插件模板；请按插件独立库运行，不能在设备表里用模块 ID 区分工序。";
            return null;
        }

        errorMessage = null;
        return _hardwareProfiles[0];
    }
}
