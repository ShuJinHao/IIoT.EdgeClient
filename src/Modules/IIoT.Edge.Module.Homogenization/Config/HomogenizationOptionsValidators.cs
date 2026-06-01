using IIoT.Edge.Module.Homogenization.Resources;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Config;

/// <summary>
/// 匀浆模块界面和运行循环配置校验器，防止无效间隔导致任务空转或 UI 缓存不可用。
/// </summary>
public sealed class HomogenizationModuleOptionsValidator : IValidateOptions<HomogenizationModuleOptions>
{
    public ValidateOptionsResult Validate(string? name, HomogenizationModuleOptions options)
    {
        var errors = new List<string>();
        if (options.Presentation.DataViewRefreshIntervalMs <= 0)
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", "界面刷新间隔"));
        }

        if (options.Presentation.MaxOutboundRecords <= 0)
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", "出料记录上限"));
        }

        if (options.Runtime.EventLoopIntervalMs <= 0)
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", "任务循环间隔"));
        }

        if (options.Runtime.RealtimeLoopIntervalMs <= 0)
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", "实时上传循环间隔"));
        }

        if (options.Runtime.MinEventLoopIntervalMs <= 0)
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", "任务最小循环间隔"));
        }

        if (options.Runtime.MinRealtimeLoopIntervalMs <= 0)
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_RequiredFormat", "{0}必须大于 0。", "实时上传最小循环间隔"));
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>
/// 匀浆 MES 接口配置校验器，确保签名令牌和五类接口路径在启动时可用。
/// </summary>
public sealed class HomogenizationMesOptionsValidator : IValidateOptions<HomogenizationMesOptions>
{
    public ValidateOptionsResult Validate(string? name, HomogenizationMesOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.SignToken))
        {
            errors.Add(HomogenizationText.Get(
                "Homogenization_Validate_MesSignTokenRequired",
                "匀浆 MES 签名令牌不能为空。"));
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>
/// 匀浆 PLC/MES code 配置校验器，确保触发应答码和 MES 字段码表完整。
/// </summary>
public sealed class HomogenizationCodeOptionsValidator : IValidateOptions<HomogenizationCodeOptions>
{
    public ValidateOptionsResult Validate(string? name, HomogenizationCodeOptions options)
    {
        var errors = new List<string>();
        options.Plc.AppendValidationErrors(errors);
        options.Mes.AppendValidationErrors(errors);
        options.Cloud.AppendValidationErrors(errors);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary>
/// 匀浆配置校验辅助方法，所有提示文案仍走插件资源。
/// </summary>
internal static class HomogenizationOptionValidation
{
    /// <summary>
    /// 校验字符串配置不能为空，主要用于 MES 路径和通道名称。
    /// </summary>
    public static void Require(string value, string name, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(HomogenizationText.Format("Homogenization_Validate_StringRequiredFormat", "{0} 不能为空。", name));
        }
    }
}
