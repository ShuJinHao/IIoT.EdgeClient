using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Shell.Core;

public sealed class ProcessIntegrationRegistry : IProcessIntegrationRegistry
{
    private readonly Dictionary<string, CloudUploaderRegistration> _cloudUploaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MesUploaderRegistration> _mesUploaders = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterCloudUploader(string processType, ProcessUploadMode uploadMode)
    {
        if (string.IsNullOrWhiteSpace(processType))
        {
            throw new InvalidOperationException("ProcessType cannot be empty when registering cloud integration.");
        }

        if (_cloudUploaders.ContainsKey(processType))
        {
            throw new InvalidOperationException(
                $"Cloud uploader for process type '{processType}' is already registered.");
        }

        _cloudUploaders[processType] = new CloudUploaderRegistration(processType, uploadMode);
    }

    public void RegisterMesUploader(string processType, MesUploadMode uploadMode)
    {
        if (string.IsNullOrWhiteSpace(processType))
        {
            throw new InvalidOperationException("ProcessType cannot be empty when registering MES integration.");
        }

        if (_mesUploaders.ContainsKey(processType))
        {
            throw new InvalidOperationException(
                $"MES uploader for process type '{processType}' is already registered.");
        }

        _mesUploaders[processType] = new MesUploaderRegistration(processType, uploadMode);
    }

    public bool HasCloudUploader(string processType) => _cloudUploaders.ContainsKey(processType);

    public bool HasMesUploader(string processType) => _mesUploaders.ContainsKey(processType);

    public bool TryGetCloudUploader(string processType, out CloudUploaderRegistration registration)
        => _cloudUploaders.TryGetValue(processType, out registration!);

    public bool TryGetMesUploader(string processType, out MesUploaderRegistration registration)
        => _mesUploaders.TryGetValue(processType, out registration!);

    public IReadOnlyDictionary<string, CloudUploaderRegistration> GetCloudUploaders() => _cloudUploaders;

    public IReadOnlyDictionary<string, MesUploaderRegistration> GetMesUploaders() => _mesUploaders;
}
