using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.SharedKernel.Enums;

namespace IIoT.Edge.Module.Stacking.Runtime;

public sealed class StackingHardwareProfileProvider : IModuleHardwareProfileProvider
{
    public string ModuleId => StackingModuleConstants.ModuleId;

    public ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.S7.ToString(), 3000, 102);

    public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate()
        => StackingPlcSignalProfile.Signals
            .Select(static x => new ModuleIoTemplateEntry(
                x.Label,
                x.DefaultAddress,
                x.AddressCount,
                x.DataType,
                x.Direction,
                x.SortOrder,
                $"Stacking v1 - {x.DisplayName}"))
            .ToArray();

    public string GetProtocolSummary()
        => string.Join(
            Environment.NewLine,
            StackingPlcSignalProfile.Signals.Select(static x =>
                $"{x.Label} -> {x.DefaultAddress} ({x.Direction}, {x.DataType}, Count={x.AddressCount})"));

    public ModuleHardwareValidationResult ValidatePlcConfiguration(
        string deviceName,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> mappings)
    {
        var issues = new List<ModuleHardwareValidationIssue>();
        var mappingsByLabel = mappings
            .GroupBy(static x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static x => x.Key, static x => x.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var signal in StackingPlcSignalProfile.Signals)
        {
            if (!mappingsByLabel.TryGetValue(signal.Label, out var candidates) || candidates.Count == 0)
            {
                issues.Add(new ModuleHardwareValidationIssue($"PLC[{deviceName}] missing {signal.Label}."));
                continue;
            }

            if (candidates.Count > 1)
            {
                issues.Add(new ModuleHardwareValidationIssue($"PLC[{deviceName}] has duplicate mappings for {signal.Label}."));
                continue;
            }

            var mapping = candidates[0];
            if (!string.Equals(mapping.Direction, signal.Direction, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ModuleHardwareValidationIssue(
                    $"PLC[{deviceName}] {signal.Label} direction mismatch. Expected:{signal.Direction}, Actual:{mapping.Direction}."));
            }

            if (mapping.AddressCount != signal.AddressCount)
            {
                issues.Add(new ModuleHardwareValidationIssue(
                    $"PLC[{deviceName}] {signal.Label} address count mismatch. Expected:{signal.AddressCount}, Actual:{mapping.AddressCount}."));
            }

            if (string.IsNullOrWhiteSpace(mapping.PlcAddress))
            {
                issues.Add(new ModuleHardwareValidationIssue($"PLC[{deviceName}] {signal.Label} PLC address is empty."));
            }

            if (!string.Equals(mapping.DataType, signal.DataType, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ModuleHardwareValidationIssue(
                    $"PLC[{deviceName}] {signal.Label} data type mismatch. Expected:{signal.DataType}, Actual:{mapping.DataType}."));
            }
        }

        ValidateOrder(deviceName, issues, mappings, StackingPlcSignalProfile.ReadSignals, "Read");
        ValidateOrder(deviceName, issues, mappings, StackingPlcSignalProfile.WriteSignals, "Write");

        return issues.Count == 0
            ? ModuleHardwareValidationResult.Success()
            : ModuleHardwareValidationResult.Failure(issues);
    }

    private static void ValidateOrder(
        string deviceName,
        List<ModuleHardwareValidationIssue> issues,
        IReadOnlyCollection<ModuleIoSnapshot> mappings,
        IReadOnlyList<StackingSignalDefinition> expectedSignals,
        string direction)
    {
        var ordered = mappings
            .Where(x => string.Equals(x.Direction, direction, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.SortOrder)
            .ToArray();

        for (var index = 0; index < expectedSignals.Count; index++)
        {
            if (ordered.Length <= index)
            {
                return;
            }

            var expected = expectedSignals[index];
            var actual = ordered[index];
            if (!string.Equals(actual.Label, expected.Label, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ModuleHardwareValidationIssue(
                    $"PLC[{deviceName}] {direction} order mismatch at index {index + 1}. Expected:{expected.Label}, Actual:{actual.Label}."));
                return;
            }
        }
    }
}
