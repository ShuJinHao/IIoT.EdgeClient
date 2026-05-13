using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Host.Bootstrap.Core;

public sealed class StationRuntimeRegistry : IStationRuntimeRegistry
{
    private readonly Dictionary<string, IStationRuntimeFactory> _registrations = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IStationRuntimeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentException.ThrowIfNullOrWhiteSpace(factory.ModuleId);

        if (_registrations.ContainsKey(factory.ModuleId))
        {
            throw new InvalidOperationException($"模块“{factory.ModuleId}”的 PLC 运行时工厂已注册。");
        }

        _registrations[factory.ModuleId] = factory;
    }

    public bool HasFactory(string moduleId) => _registrations.ContainsKey(moduleId);

    public bool TryGetFactory(string moduleId, out IStationRuntimeFactory factory)
        => _registrations.TryGetValue(moduleId, out factory!);

    public IReadOnlyDictionary<string, IStationRuntimeFactory> GetRegistrations() => _registrations;
}
