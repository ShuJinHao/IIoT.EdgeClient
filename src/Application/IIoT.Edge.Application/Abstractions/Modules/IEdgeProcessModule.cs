namespace IIoT.Edge.Application.Abstractions.Modules;

public interface IEdgeProcessModule
{
    string ModuleId { get; }

    string ProcessType { get; }

    string DisplayName { get; }

    bool RequiresCloudUploader => false;

    bool RequiresMesUploader => false;

    void Configure(IEdgeProcessModuleBuilder builder);
}
