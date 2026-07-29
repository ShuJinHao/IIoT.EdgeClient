namespace IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;

public interface IPlcTaskBindingTransactionService
{
    Task<PlcTaskBindingSaveApplyResult> SaveAndApplyAsync(
        int networkDeviceId,
        string moduleId,
        IReadOnlyDictionary<string, bool> taskStates,
        CancellationToken cancellationToken = default);
}

public interface IPlcTaskBindingPersistenceTransaction
{
    Task<PlcTaskBindingSavePreparation> PrepareAsync(
        int networkDeviceId,
        string moduleId,
        IReadOnlyDictionary<string, bool> taskStates,
        CancellationToken cancellationToken = default);

    Task CommitAsync(
        PlcTaskBindingSavePreparation preparation,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(
        PlcTaskBindingSavePreparation preparation,
        CancellationToken cancellationToken = default);
}

public sealed class UnavailablePlcTaskBindingTransactionService
    : IPlcTaskBindingTransactionService
{
    public Task<PlcTaskBindingSaveApplyResult> SaveAndApplyAsync(
        int networkDeviceId,
        string moduleId,
        IReadOnlyDictionary<string, bool> taskStates,
        CancellationToken cancellationToken = default)
        => Task.FromException<PlcTaskBindingSaveApplyResult>(
            new InvalidOperationException(
                "PLC 任务绑定事务服务不可用，已禁止保存，避免数据库与运行时状态分叉。"));
}
