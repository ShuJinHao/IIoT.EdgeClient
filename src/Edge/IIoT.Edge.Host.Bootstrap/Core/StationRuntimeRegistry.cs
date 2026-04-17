using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Shell.Core;

public sealed class StationRuntimeRegistry : IStationRuntimeRegistry
{
    private readonly Dictionary<string, IStationRuntimeFactory> _registrations = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IStationRuntimeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (string.IsNullOrWhiteSpace(factory.ModuleId))
        {
            throw new InvalidOperationException("ModuleId cannot be empty when registering PLC runtime.");
        }

        if (_registrations.ContainsKey(factory.ModuleId))
        {
            throw new InvalidOperationException(
                $"PLC runtime factory for module '{factory.ModuleId}' is already registered.");
        }

        _registrations[factory.ModuleId] = factory;
    }

    public bool HasFactory(string moduleId) => _registrations.ContainsKey(moduleId);

    public bool TryGetFactory(string moduleId, out IStationRuntimeFactory factory)
        => _registrations.TryGetValue(moduleId, out factory!);

    public IReadOnlyDictionary<string, IStationRuntimeFactory> GetRegistrations() => _registrations;
}
