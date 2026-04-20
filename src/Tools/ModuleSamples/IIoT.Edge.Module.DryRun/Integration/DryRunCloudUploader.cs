using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.DryRun.Constants;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.DryRun.Integration;

public sealed class DryRunCloudUploader : IProcessCloudUploader
{
    private readonly ILogService _logger;

    public DryRunCloudUploader(ILogService logger)
    {
        _logger = logger;
    }

    public string ProcessType => DryRunModuleConstants.ProcessType;

    public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

    public Task<CloudCallResult> UploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        _logger.Warn(
            $"[DryRun] Cloud uploader intentionally fails for module '{DryRunModuleConstants.ModuleId}'. Records:{records.Count}");
        return Task.FromResult(CloudCallResult.Failure(CloudCallOutcome.Exception, "dry_run_forced_failure"));
    }
}
