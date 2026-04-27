using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Homogenization.Config.Hardware;

public sealed class HomogenizationHardwareProfileProvider : IModuleHardwareProfileProvider
{
    public string ModuleId => DependencyInjection.ModuleKey;

    public ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.Mc.ToString(), 3000, 6000);

    public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate()
        => HomogenizationPlcSignalProfile.Signals
            .Select(static signal => new ModuleIoTemplateEntry(
                signal.Label,
                string.Empty,
                signal.AddressCount,
                signal.DataType,
                signal.Direction,
                signal.SortOrder,
                $"匀浆模板 - {signal.DisplayName}",
                signal.Category,
                signal.GroupName,
                signal.DisplayRole))
            .ToArray();

    public string GetProtocolSummary()
        => string.Join(
            Environment.NewLine,
            HomogenizationPlcSignalProfile.Signals.Select(static signal =>
                $"{signal.Label}：分类={signal.Category}，分组={signal.GroupName}，方向={signal.Direction}，类型={signal.DataType}，长度={signal.AddressCount}，排序={signal.SortOrder}"));

    public ModuleHardwareValidationResult ValidatePlcConfiguration(
        string deviceName,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
    {
        var issues = new List<ModuleHardwareValidationIssue>();
        var mappingsByLabel = mappings
            .GroupBy(static mapping => mapping.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var signal in HomogenizationPlcSignalProfile.Signals)
        {
            if (!mappingsByLabel.TryGetValue(signal.Label, out var candidates) || candidates.Count == 0)
            {
                issues.Add(new ModuleHardwareValidationIssue($"PLC[{deviceName}] 缺少信号 {signal.Label}。"));
                continue;
            }

            if (candidates.Count > 1)
            {
                issues.Add(new ModuleHardwareValidationIssue($"PLC[{deviceName}] 的信号 {signal.Label} 存在重复映射。"));
                continue;
            }

            var mapping = candidates[0];
            if (!string.Equals(mapping.Direction, signal.Direction, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ModuleHardwareValidationIssue(
                    $"PLC[{deviceName}] 的信号 {signal.Label} 方向不一致。期望：{signal.Direction}，实际：{mapping.Direction}。"));
            }

            if (mapping.AddressCount != signal.AddressCount)
            {
                issues.Add(new ModuleHardwareValidationIssue(
                    $"PLC[{deviceName}] 的信号 {signal.Label} 地址长度不一致。期望：{signal.AddressCount}，实际：{mapping.AddressCount}。"));
            }

            if (!string.Equals(mapping.DataType, signal.DataType, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ModuleHardwareValidationIssue(
                    $"PLC[{deviceName}] 的信号 {signal.Label} 数据类型不一致。期望：{signal.DataType}，实际：{mapping.DataType}。"));
            }

            if (string.IsNullOrWhiteSpace(mapping.PlcAddress))
            {
                issues.Add(new ModuleHardwareValidationIssue($"PLC[{deviceName}] 的信号 {signal.Label} PLC 地址不能为空。"));
            }

            if (string.IsNullOrWhiteSpace(mapping.Category))
            {
                issues.Add(new ModuleHardwareValidationIssue($"PLC[{deviceName}] 的信号 {signal.Label} IO 分类不能为空。"));
            }
        }

        ValidateSortOrders(deviceName, mappings, "Read", issues);
        ValidateSortOrders(deviceName, mappings, "Write", issues);

        return issues.Count == 0
            ? ModuleHardwareValidationResult.Success()
            : ModuleHardwareValidationResult.Failure(issues);
    }

    private static void ValidateSortOrders(
        string deviceName,
        IReadOnlyCollection<ModuleIoSnapshot> mappings,
        string direction,
        ICollection<ModuleHardwareValidationIssue> issues)
    {
        var directionMappings = mappings
            .Where(mapping => string.Equals(mapping.Direction, direction, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var mapping in directionMappings.Where(static mapping => mapping.SortOrder <= 0))
        {
            issues.Add(new ModuleHardwareValidationIssue(
                $"PLC[{deviceName}] 的信号 {mapping.Label} 在 {direction} 方向的排序值 {mapping.SortOrder} 无效。"));
        }

        foreach (var duplicateGroup in directionMappings
                     .GroupBy(static mapping => mapping.SortOrder)
                     .Where(static group => group.Key > 0 && group.Count() > 1))
        {
            var labels = string.Join(", ", duplicateGroup.Select(static mapping => mapping.Label));
            issues.Add(new ModuleHardwareValidationIssue(
                $"PLC[{deviceName}] 在 {direction} 方向的排序值 {duplicateGroup.Key} 被重复使用：{labels}。"));
        }
    }
}
