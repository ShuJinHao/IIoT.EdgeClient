using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Payload;

namespace IIoT.Edge.Module.Homogenization.Runtime;

/// <summary>
/// 匀浆托盘码重码守卫。入站和出站分别记录，避免正常出站被进站记录误判为重码。
/// </summary>
public sealed class HomogenizationTrayCodeGuard
{
    public bool IsDuplicateEnabled(ModuleParamSnapshot<MesParam, CloudParam, BusinessParam> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return parameters.Business<bool>(BusinessParam.启用托盘码重码验证);
    }

    public bool IsDuplicate(
        HomogenizationContext context,
        HomogenizationTrayCodeStage stage,
        string trayCode)
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = BuildKey(stage, trayCode);
        return context.HasCell(key);
    }

    public void MarkProcessed(
        HomogenizationContext context,
        HomogenizationTrayCodeStage stage,
        string trayCode,
        string status,
        DateTime occurredAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        var normalizedTrayCode = Normalize(trayCode);
        context.AddCell(
            BuildKey(stage, normalizedTrayCode),
            new HomogenizationCellData
            {
                TrayCode = normalizedTrayCode,
                DeviceName = context.DeviceName,
                RuntimeStatus = status,
                CompletedTime = occurredAt
            });
    }

    public string FormatDuplicateMessage(HomogenizationTrayCodeStage stage, string trayCode)
    {
        var stageName = stage == HomogenizationTrayCodeStage.Inbound ? "进站" : "出站";
        return $"托盘码重复，已按业务 NG 拒绝{stageName}：{Normalize(trayCode)}。";
    }

    private static string BuildKey(HomogenizationTrayCodeStage stage, string trayCode)
        => $"Homogenization.{stage}:{Normalize(trayCode)}";

    private static string Normalize(string trayCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trayCode);
        return trayCode.Trim();
    }
}

/// <summary>
/// 匀浆托盘码重码校验范围。进站和出站独立记录，保证同一托盘正常完成入站后仍可出站。
/// </summary>
public enum HomogenizationTrayCodeStage
{
    Inbound,
    Outbound
}
