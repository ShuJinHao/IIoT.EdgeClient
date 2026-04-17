using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.Modules.Stacking;

namespace IIoT.Edge.Infrastructure.Integration.PassStation;

/// <summary>
/// Placeholder uploader for the Stacking module skeleton.
/// It fails loudly so an accidentally enabled skeleton module does not
/// silently pretend that production data was uploaded.
/// </summary>
public sealed class StackingPlaceholderCloudUploader : IProcessCloudUploader
{
    private readonly ILogService _logger;

    public StackingPlaceholderCloudUploader(ILogService logger)
    {
        _logger = logger;
    }

    public string ProcessType => StackingModuleConstants.ProcessType;

    public ProcessUploadMode UploadMode => ProcessUploadMode.Single;

    public Task<bool> UploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        _logger.Warn(
            $"[Cloud] Stacking placeholder uploader was invoked. This module is a skeleton and should not be enabled for production data. Count:{records.Count}");
        return Task.FromResult(false);
    }
}
