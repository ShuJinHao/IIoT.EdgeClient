namespace IIoT.Edge.Application.Abstractions.Modules;

public sealed record ProcessUploaderRegistration(string ProcessType, ProcessUploadMode UploadMode);

public interface IProcessIntegrationRegistry
{
    void RegisterCloudUploader(string processType, ProcessUploadMode uploadMode);

    void RegisterMesUploader(string processType, ProcessUploadMode uploadMode);

    bool HasCloudUploader(string processType);

    bool HasMesUploader(string processType);

    bool TryGetCloudUploader(string processType, out ProcessUploaderRegistration registration);

    bool TryGetMesUploader(string processType, out ProcessUploaderRegistration registration);

    IReadOnlyDictionary<string, ProcessUploaderRegistration> GetCloudUploaders();

    IReadOnlyDictionary<string, ProcessUploaderRegistration> GetMesUploaders();
}
