using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Shell.Core;

public sealed class ProcessIntegrationRegistry : IProcessIntegrationRegistry
{
    private readonly UploaderRegistry _cloudUploaders = new("云端");
    private readonly UploaderRegistry _mesUploaders = new("MES");

    public void RegisterCloudUploader(string processType, ProcessUploadMode uploadMode)
        => _cloudUploaders.Register(processType, uploadMode);

    public void RegisterMesUploader(string processType, ProcessUploadMode uploadMode)
        => _mesUploaders.Register(processType, uploadMode);

    public bool HasCloudUploader(string processType) => _cloudUploaders.Has(processType);

    public bool HasMesUploader(string processType) => _mesUploaders.Has(processType);

    public bool TryGetCloudUploader(string processType, out ProcessUploaderRegistration registration)
        => _cloudUploaders.TryGet(processType, out registration);

    public bool TryGetMesUploader(string processType, out ProcessUploaderRegistration registration)
        => _mesUploaders.TryGet(processType, out registration);

    public IReadOnlyDictionary<string, ProcessUploaderRegistration> GetCloudUploaders() => _cloudUploaders.GetAll();

    public IReadOnlyDictionary<string, ProcessUploaderRegistration> GetMesUploaders() => _mesUploaders.GetAll();

    private sealed class UploaderRegistry
    {
        private readonly string _name;
        private readonly Dictionary<string, ProcessUploaderRegistration> _uploaders = new(StringComparer.OrdinalIgnoreCase);

        public UploaderRegistry(string name)
        {
            _name = name;
        }

        public void Register(string processType, ProcessUploadMode uploadMode)
        {
            if (string.IsNullOrWhiteSpace(processType))
            {
                throw new InvalidOperationException($"注册{_name}上传集成时 ProcessType 不能为空。");
            }

            if (_uploaders.ContainsKey(processType))
            {
                throw new InvalidOperationException(
                    $"工序“{processType}”的{_name}上传器已注册。");
            }

            _uploaders[processType] = new ProcessUploaderRegistration(processType, uploadMode);
        }

        public bool Has(string processType) => _uploaders.ContainsKey(processType);

        public bool TryGet(string processType, out ProcessUploaderRegistration registration)
            => _uploaders.TryGetValue(processType, out registration!);

        public IReadOnlyDictionary<string, ProcessUploaderRegistration> GetAll() => _uploaders;
    }
}
