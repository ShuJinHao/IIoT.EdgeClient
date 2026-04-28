using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Application.Modules;

public sealed record ModuleHardwareSignalRequirement(
    string Label,
    int AddressCount,
    string DataType,
    string Direction,
    int SortOrder);

public static class ModuleHardwareProfileValidator
{
    public static ModuleHardwareValidationResult Validate(
        string deviceName,
        IReadOnlyCollection<ModuleIoSnapshot> mappings,
        IReadOnlyCollection<ModuleHardwareSignalRequirement> requirements,
        bool requireCategory = false,
        bool validateSequentialOrder = false)
    {
        var issues = new List<ModuleHardwareValidationIssue>();
        var mappingsByLabel = mappings
            .GroupBy(static mapping => mapping.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in requirements)
        {
            if (!mappingsByLabel.TryGetValue(requirement.Label, out var candidates) || candidates.Count == 0)
            {
                issues.Add(new ModuleHardwareValidationIssue($"PLC[{deviceName}] 缺少信号 {requirement.Label}。"));
                continue;
            }

            if (candidates.Count > 1)
            {
                issues.Add(new ModuleHardwareValidationIssue($"PLC[{deviceName}] 的信号 {requirement.Label} 存在重复映射。"));
                continue;
            }

            ValidateMapping(deviceName, candidates[0], requirement, requireCategory, issues);
        }

        ValidateSortOrders(deviceName, mappings, "Read", issues);
        ValidateSortOrders(deviceName, mappings, "Write", issues);

        if (validateSequentialOrder)
        {
            ValidateSequentialOrder(deviceName, mappings, requirements, "Read", issues);
            ValidateSequentialOrder(deviceName, mappings, requirements, "Write", issues);
        }

        return issues.Count == 0
            ? ModuleHardwareValidationResult.Success()
            : ModuleHardwareValidationResult.Failure(issues);
    }

    private static void ValidateMapping(
        string deviceName,
        ModuleIoSnapshot mapping,
        ModuleHardwareSignalRequirement requirement,
        bool requireCategory,
        ICollection<ModuleHardwareValidationIssue> issues)
    {
        if (!string.Equals(mapping.Direction, requirement.Direction, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ModuleHardwareValidationIssue(
                $"PLC[{deviceName}] 的信号 {requirement.Label} 方向不一致。期望：{requirement.Direction}，实际：{mapping.Direction}。"));
        }

        if (mapping.AddressCount != requirement.AddressCount)
        {
            issues.Add(new ModuleHardwareValidationIssue(
                $"PLC[{deviceName}] 的信号 {requirement.Label} 地址长度不一致。期望：{requirement.AddressCount}，实际：{mapping.AddressCount}。"));
        }

        if (!string.Equals(mapping.DataType, requirement.DataType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ModuleHardwareValidationIssue(
                $"PLC[{deviceName}] 的信号 {requirement.Label} 数据类型不一致。期望：{requirement.DataType}，实际：{mapping.DataType}。"));
        }

        if (string.IsNullOrWhiteSpace(mapping.PlcAddress))
        {
            issues.Add(new ModuleHardwareValidationIssue($"PLC[{deviceName}] 的信号 {requirement.Label} PLC 地址不能为空。"));
        }

        if (requireCategory && string.IsNullOrWhiteSpace(mapping.Category))
        {
            issues.Add(new ModuleHardwareValidationIssue($"PLC[{deviceName}] 的信号 {requirement.Label} IO 分类不能为空。"));
        }
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

    private static void ValidateSequentialOrder(
        string deviceName,
        IReadOnlyCollection<ModuleIoSnapshot> mappings,
        IReadOnlyCollection<ModuleHardwareSignalRequirement> requirements,
        string direction,
        ICollection<ModuleHardwareValidationIssue> issues)
    {
        var expectedSignals = requirements
            .Where(requirement => string.Equals(requirement.Direction, direction, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static requirement => requirement.SortOrder)
            .ToArray();

        var orderedMappings = mappings
            .Where(mapping => string.Equals(mapping.Direction, direction, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static mapping => mapping.SortOrder)
            .ToArray();

        for (var index = 0; index < expectedSignals.Length; index++)
        {
            if (orderedMappings.Length <= index)
            {
                return;
            }

            var expected = expectedSignals[index];
            var actual = orderedMappings[index];
            if (!string.Equals(actual.Label, expected.Label, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ModuleHardwareValidationIssue(
                    $"PLC[{deviceName}] {direction} 方向第 {index + 1} 个信号排序不一致。期望：{expected.Label}，实际：{actual.Label}。"));
                return;
            }
        }
    }
}
