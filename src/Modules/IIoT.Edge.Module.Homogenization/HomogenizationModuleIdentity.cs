namespace IIoT.Edge.Module.Homogenization;

/// <summary>
/// 匀浆插件身份常量。ModuleId、ProcessType 和配置 Section 是模块契约，不允许在运行配置中改写。
/// </summary>
public static class HomogenizationModuleIdentity
{
    public const string ModuleId = "Homogenization";

    public const string ProcessType = "Homogenization";

    public const string ConfigurationSection = "Modules:Homogenization";

    public const string DeviceSeedSection = "Modules:Homogenization:DeviceSeed";

    public const string DisplayNameFallback = "匀浆";

    public const string EntryType = "IIoT.Edge.Module.Homogenization.DependencyInjection";
}
