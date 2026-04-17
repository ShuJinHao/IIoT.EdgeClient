using System.Reflection;

namespace IIoT.Edge.Module.Abstractions;

public sealed record CompiledModuleDescriptor(
    string ModuleId,
    string ProcessType,
    string AssemblyName,
    Type ModuleType)
{
    public Assembly Assembly => ModuleType.Assembly;

    public IEdgeStationModule CreateModule()
        => (IEdgeStationModule)(Activator.CreateInstance(ModuleType)
            ?? throw new InvalidOperationException(
                $"Failed to create module instance for '{ModuleType.FullName}'."));
}
