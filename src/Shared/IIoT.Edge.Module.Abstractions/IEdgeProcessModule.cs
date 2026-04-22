namespace IIoT.Edge.Plugin.Shared.Modules;

public interface IEdgeProcessModule
{
    string ModuleId { get; }

    string ProcessType { get; }

    string DisplayName { get; }

    void Configure(IEdgeProcessModuleBuilder builder);
}
