using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.SharedKernel.Context;

using IIoT.Edge.Application.Abstractions.Mes;
namespace IIoT.Edge.Module.Sdk.Base;

public abstract class PeriodicSnapshotUploadTaskBase<TSnapshot> : PlcTaskBase
{
    protected PeriodicSnapshotUploadTaskBase(IPlcBuffer buffer, ProductionContext context, ILogService logger)
        : base(buffer, context, logger)
    {
    }

    protected abstract TSnapshot CaptureSnapshot();

    protected abstract Task<MesCallResult> UploadSnapshotAsync(TSnapshot snapshot, CancellationToken cancellationToken);

    protected virtual Task OnSnapshotUploadedAsync(
        TSnapshot snapshot,
        MesCallResult result,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected override async Task DoCoreAsync()
    {
        var capturedAt = DateTime.UtcNow;
        var snapshot = CaptureSnapshot();
        RecordTaskState("LastCaptureAtUtc", capturedAt);

        var result = await UploadSnapshotAsync(snapshot, TaskCancellationToken).ConfigureAwait(false);
        RecordTaskState("LastUploadAtUtc", DateTime.UtcNow);
        RecordTaskState("LastUploadOutcome", result.Outcome.ToString());
        RecordTaskState("LastUploadMessage", result.Message);

        await OnSnapshotUploadedAsync(snapshot, result, TaskCancellationToken).ConfigureAwait(false);
    }

    protected void RecordTaskState<TValue>(string key, TValue value)
        => Context.Set($"Runtime.Tasks.{TaskName}.{key}", value);
}
