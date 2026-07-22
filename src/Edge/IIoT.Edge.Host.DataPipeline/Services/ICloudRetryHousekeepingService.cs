using IIoT.Edge.Module.Contracts.Modules;

using IIoT.Edge.Module.Contracts.Cloud;
namespace IIoT.Edge.Host.DataPipeline.Services;

public interface ICloudRetryHousekeepingService : IRetryTaskHousekeepingService
{
    bool DidPauseForRecovery(CloudUploadDiagnosticsSnapshot previousSnapshot, string processType);
}
