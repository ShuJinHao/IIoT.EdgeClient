namespace IIoT.Edge.SharedKernel.DataPipeline;

public interface IFallbackRecord
{
    long Id { get; }

    string ProcessType { get; }

    string CellDataJson { get; }

    string FailedTarget { get; }

    int? NetworkDeviceId { get; }

    string DeviceName { get; }

    string ModuleId { get; }

    string TaskKey { get; }

    string PlanSessionId { get; }

    string MainPlanCode { get; }

    string TraceBatchNumber { get; }
}
