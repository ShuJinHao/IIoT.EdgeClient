using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;

namespace IIoT.Edge.Infrastructure.Integration;

public abstract class ProcessUploaderConsumerBase<TUploader, TResult>
    where TUploader : IProcessUploader<TResult>
{
    private readonly Dictionary<string, TUploader> _uploaders;

    protected ProcessUploaderConsumerBase(
        IEnumerable<TUploader> uploaders,
        ILogService logger)
    {
        Logger = logger;
        _uploaders = uploaders.ToDictionary(x => x.ProcessType, StringComparer.OrdinalIgnoreCase);
    }

    protected ILogService Logger { get; }

    protected bool TryResolveUploader(
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
        return false;
    }
}
