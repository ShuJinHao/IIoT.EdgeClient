using IIoT.Edge.Application.Abstractions.Modules;

using IIoT.Edge.Application.Abstractions.Cloud;
namespace IIoT.Edge.Host.DataPipeline.Services;

public interface ICloudRetryHousekeepingService : IRetryTaskHousekeepingService
{
    bool DidPauseForRecovery(CloudUploadDiagnosticsSnapshot previousSnapshot, string processType);
}
