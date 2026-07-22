using System.Globalization;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Application.Features.Production.Monitor;

internal sealed class MonitorStateMachineTaskProjection : IMonitorStateMachineTaskProjection
{
    public static IReadOnlyDictionary<string, int> EmptyStepStates { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MonitorStateMachineTaskSnapshot> BuildRows(
        NetworkDeviceEntity? device,
        IReadOnlyDictionary<string, int> stepStates,
        IReadOnlyDictionary<int, PlcTaskBindingDeviceDto> taskBindingsByDevice)
    {
        if (device is null
            || !taskBindingsByDevice.TryGetValue(device.Id, out var deviceBinding))
        {
            return [];
        }

        return deviceBinding.Tasks
            .Select(task =>
            {
                var stepValue = stepStates.TryGetValue(task.Key, out var value)
                    ? value
                    : (int?)null;
                return new MonitorStateMachineTaskSnapshot(
                    Key: task.Key,
                    DisplayName: task.DisplayName,
                    Enabled: task.Enabled,
                    CanRun: task.CanRun,
                    HasSavedBinding: task.HasSavedBinding,
                    StepValue: stepValue,
                    StepText: FormatStateMachineStepText(stepValue),
                    UnavailableReason: task.CanRun ? string.Empty : task.UnavailableReason,
                    IsHeartbeatLike: task.IsHeartbeatLike,
                    RequiredSignalCount: task.RequiredSignals.Count,
                    MissingRequiredSignalCount: task.MissingRequiredSignals.Count,
                    MissingRequiredSignalsSummary: FormatRequiredSignalSummary(task.MissingRequiredSignals));
            })
            .ToList();
    }

    private static string FormatStateMachineStepText(int? stepValue)
        => stepValue switch
        {
            null => "--",
            0 => "等待触发",
            10 => "处理中",
            30 => "等待 PLC 复位",
            _ => $"步骤 {stepValue.Value.ToString(CultureInfo.InvariantCulture)}"
        };

    private static string FormatRequiredSignalSummary(IReadOnlyCollection<TaskRequiredSignal> signals)
        => signals.Count == 0
            ? "--"
            : string.Join("；", signals.Select(static signal => $"{signal.SignalKey}/{signal.Direction}"));
}
