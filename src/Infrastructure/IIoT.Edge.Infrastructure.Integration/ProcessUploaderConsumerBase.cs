using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;

namespace IIoT.Edge.Infrastructure.Integration;

public abstract class ProcessUploaderConsumerBase<TUploader, TResult>
    where TUploader : IProcessUploader<TResult>
{
    private readonly IDeviceService _deviceService;
    private readonly Dictionary<string, TUploader> _uploaders;

    protected ProcessUploaderConsumerBase(
        IDeviceService deviceService,
        IEnumerable<TUploader> uploaders,
        ILogService logger)
    {
        _deviceService = deviceService;
        Logger = logger;
        _uploaders = uploaders.ToDictionary(x => x.ProcessType, StringComparer.OrdinalIgnoreCase);
    }

    protected ILogService Logger { get; }

    protected DeviceSession? CurrentDevice => _deviceService.CurrentDevice;

    protected bool TryResolveUploader(
        string targetName,
        string processType,
        bool isRegistered,
        out TUploader uploader,
        out bool shouldFail)
    {
        if (_uploaders.TryGetValue(processType, out uploader!))
        {
            shouldFail = false;
            return true;
        }

        shouldFail = isRegistered;
        if (shouldFail)
        {
            Logger.Error($"[{targetName}] 工序 {processType} 已注册上传器，但 DI 中未找到实现。");
        }

        return false;
    }
}
