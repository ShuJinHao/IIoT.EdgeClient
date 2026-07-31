using IIoT.Edge.Module.Contracts.DataPipeline;

namespace IIoT.Edge.Application.Features.DataPipeline.DeadLetters;

public interface ICloudDeadLetterRequeueStore
{
    Task RequeueAndRemoveAsync(
        long deadLetterId,
        string operatorId,
        string businessIdentifier,
        CancellationToken cancellationToken = default);
}

public interface IMesDeadLetterRequeueStore
{
    Task RequeueAndRemoveAsync(
        long deadLetterId,
        string operatorId,
        string businessIdentifier,
        CancellationToken cancellationToken = default);
}

public interface ICloudRetryDeadLetterTransitionStore
{
    Task MoveExhaustedRetryToDeadLetterAsync(
        FailedCellRecord sourceRecord,
        int finalRetryCount,
        string failureReason,
        CancellationToken cancellationToken = default);
}

public interface IMesRetryDeadLetterTransitionStore
{
    Task MoveExhaustedRetryToDeadLetterAsync(
        FailedCellRecord sourceRecord,
        int finalRetryCount,
        string failureReason,
        CancellationToken cancellationToken = default);
}
