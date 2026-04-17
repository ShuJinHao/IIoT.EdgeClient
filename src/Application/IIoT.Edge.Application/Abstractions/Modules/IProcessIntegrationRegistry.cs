namespace IIoT.Edge.Application.Abstractions.Modules;

public sealed record CloudUploaderRegistration(string ProcessType, ProcessUploadMode UploadMode);
public sealed record MesUploaderRegistration(string ProcessType, MesUploadMode UploadMode);

public interface IProcessIntegrationRegistry
{
    void RegisterCloudUploader(string processType, ProcessUploadMode uploadMode);

    void RegisterMesUploader(string processType, MesUploadMode uploadMode);

    bool HasCloudUploader(string processType);

    bool HasMesUploader(string processType);

    bool TryGetCloudUploader(string processType, out CloudUploaderRegistration registration);

    bool TryGetMesUploader(string processType, out MesUploaderRegistration registration);

    IReadOnlyDictionary<string, CloudUploaderRegistration> GetCloudUploaders();

    IReadOnlyDictionary<string, MesUploaderRegistration> GetMesUploaders();
}
