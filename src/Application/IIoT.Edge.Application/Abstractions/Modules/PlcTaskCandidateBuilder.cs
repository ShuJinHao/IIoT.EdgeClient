using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Application.Modules.Hardware;

namespace IIoT.Edge.Application.Abstractions.Modules;

public sealed class PlcTaskCandidateBuilder
{
    private readonly string _key;
    private readonly string _displayName;
    private readonly List<TaskRequiredSignal> _requiredSignals = [];
    private readonly List<string> _supportedDeviceModels = [];
    private bool _isHeartbeatLike;
    private bool _defaultEnabled;

    private PlcTaskCandidateBuilder(string key, string displayName)
    {
        _key = string.IsNullOrWhiteSpace(key)
            ? throw new ArgumentException("任务 Key 不能为空。", nameof(key))
            : key;
        _displayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("任务显示名不能为空。", nameof(displayName))
            : displayName;
    }

    public static PlcTaskCandidateBuilder Create(string key, string displayName)
        => new(key, displayName);

    public PlcTaskCandidateBuilder HeartbeatLike()
    {
        _isHeartbeatLike = true;
        return this;
    }

    public PlcTaskCandidateBuilder DefaultEnabled()
    {
        _defaultEnabled = true;
        return this;
    }

    public PlcTaskCandidateBuilder SupportsDeviceModels(params string[] deviceModels)
    {
        foreach (var deviceModel in deviceModels)
        {
            if (!string.IsNullOrWhiteSpace(deviceModel)
                && !_supportedDeviceModels.Contains(deviceModel.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                _supportedDeviceModels.Add(deviceModel.Trim());
            }
        }

        return this;
    }

    public PlcTaskCandidateBuilder RequiresInteraction<TSignalKey>(params TSignalKey[] signals)
        where TSignalKey : struct, Enum
    {
        foreach (var signal in signals)
        {
            var signalKey = EnumPlcSignalMetadata.GetInteraction(signal).SignalKey;
            AddRequired(signalKey, ModuleSignalDirection.Read);
            AddRequired(signalKey, ModuleSignalDirection.Write);
        }

        return this;
    }

    public PlcTaskCandidateBuilder RequiresRead<TSignalKey>(params TSignalKey[] signals)
        where TSignalKey : struct, Enum
    {
        foreach (var signal in signals)
        {
            AddRequired(ResolveReadSignalKey(signal), ModuleSignalDirection.Read);
        }

        return this;
    }

    public PlcTaskCandidateBuilder RequiresWrite<TSignalKey>(params TSignalKey[] signals)
        where TSignalKey : struct, Enum
    {
        foreach (var signal in signals)
        {
            AddRequired(ResolveWriteSignalKey(signal), ModuleSignalDirection.Write);
        }

        return this;
    }

    public TaskCandidate Build()
        => new(
            _key,
            _displayName,
            [.. _requiredSignals],
            _isHeartbeatLike,
            _supportedDeviceModels.Count == 0 ? null : _supportedDeviceModels.ToArray(),
            _defaultEnabled);

    private static string ResolveReadSignalKey<TSignalKey>(TSignalKey signal)
        where TSignalKey : struct, Enum
        => EnumPlcSignalMetadata.TryGetInteraction(signal)?.SignalKey
            ?? EnumPlcSignalMetadata.TryGetRead(signal)?.SignalKey
            ?? throw new InvalidOperationException($"PLC 信号【{typeof(TSignalKey).FullName}.{signal}】未声明可读点位。");

    private static string ResolveWriteSignalKey<TSignalKey>(TSignalKey signal)
        where TSignalKey : struct, Enum
        => EnumPlcSignalMetadata.TryGetInteraction(signal)?.SignalKey
            ?? EnumPlcSignalMetadata.TryGetWrite(signal)?.SignalKey
            ?? throw new InvalidOperationException($"PLC 信号【{typeof(TSignalKey).FullName}.{signal}】未声明可写点位。");

    private void AddRequired(string signalKey, ModuleSignalDirection direction)
    {
        var directionText = direction.ToString();
        if (_requiredSignals.Any(signal =>
                string.Equals(signal.SignalKey, signalKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(signal.Direction, directionText, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _requiredSignals.Add(new TaskRequiredSignal(signalKey, directionText));
    }
}
