using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.Modules.DryRun;

namespace IIoT.Edge.Infrastructure.Integration.PassStation;

public sealed class DryRunCloudUploader : IProcessCloudUploader
{
    private readonly ILogService _logger;

    public DryRunCloudUploader(ILogService logger)
    {
        _logger = logger;
    }

    public string ProcessType => DryRunModuleConstants.ProcessType;

    public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

    public Task<bool> UploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        _logger.Warn(
            $"[DryRun] Cloud uploader intentionally fails for module '{DryRunModuleConstants.ModuleId}'. Records:{records.Count}");
        return Task.FromResult(false);
    }
}
