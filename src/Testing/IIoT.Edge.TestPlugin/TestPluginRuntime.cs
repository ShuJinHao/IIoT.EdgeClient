using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Module.Sdk.Base;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.TestPlugin;

public sealed class TestPluginRuntimeFactory : IStationRuntimeFactory
{
    public const string SnapshotTaskKey = DependencyInjection.ModuleKey + ".Snapshot";

    private static readonly IReadOnlyCollection<TaskCandidate> Candidates =
    [
        new(
            SnapshotTaskKey,
            "Test snapshot",
            [],
            DefaultEnabled: true)
    ];

    public string ModuleId => DependencyInjection.ModuleKey;

    public IReadOnlyCollection<TaskCandidate> GetTaskCandidates()
        => Candidates;

    public List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context,
        IReadOnlySet<string> enabledTaskKeys)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(enabledTaskKeys);

        if (!enabledTaskKeys.Contains(SnapshotTaskKey))
        {
            return [];
        }

        return
        [
            new TestPluginSnapshotTask(
                buffer,
                context,
                serviceProvider.GetRequiredService<IDataPipelineService>(),
                serviceProvider.GetRequiredService<ILogService>())
        ];
    }
}

public sealed record TestPluginSnapshot(DateTime CapturedAtUtc);

public sealed class TestPluginSnapshotTask : PeriodicSnapshotUploadTaskBase<TestPluginSnapshot>
{
    private readonly IDataPipelineService _dataPipelineService;
    private int _captureCount;
    private int _callbackCount;

    public TestPluginSnapshotTask(
        IPlcBuffer buffer,
        ProductionContext context,
        IDataPipelineService dataPipelineService,
        ILogService logger)
        : base(buffer, context, logger)
    {
        _dataPipelineService = dataPipelineService;
    }

    public override string TaskName => TestPluginRuntimeFactory.SnapshotTaskKey;

    public int CaptureCount => Volatile.Read(ref _captureCount);

    public int CallbackCount => Volatile.Read(ref _callbackCount);

    public DataPipelineEnqueueResult? LastEnqueueResult { get; private set; }

    public MesCallResult? LastCallbackResult { get; private set; }

    public Task ExecuteOnceAsync(CancellationToken cancellationToken)
    {
        SetTaskCancellationToken(cancellationToken);
        return DoCoreAsync();
    }

    protected override TestPluginSnapshot CaptureSnapshot()
    {
        Interlocked.Increment(ref _captureCount);
        return new TestPluginSnapshot(DateTime.UtcNow);
    }

    protected override async Task<MesCallResult> UploadSnapshotAsync(
        TestPluginSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var record = new CellCompletedRecord
        {
            PlcCode = Context.PlcCode,
            NetworkDeviceId = Context.NetworkDeviceId,
            DeviceName = Context.DeviceName,
            ModuleId = DependencyInjection.ModuleKey,
            TaskKey = TaskName,
            CreatedAtUtc = snapshot.CapturedAtUtc,
            CellData = new TestPluginCellData
            {
                PlcDeviceId = Context.NetworkDeviceId,
                DeviceName = Context.DeviceName,
                DeviceCode = Context.PlcCode,
                CompletedTime = snapshot.CapturedAtUtc,
                UploadTargets = DataPipelineUploadTargets.Cloud
            }
        };

        var enqueueResult = await _dataPipelineService
            .EnqueueAsync(record, cancellationToken)
            .ConfigureAwait(false);
        LastEnqueueResult = enqueueResult;

        return enqueueResult.IsDurablyAccepted
            ? MesCallResult.Success("Test snapshot accepted by the local data pipeline.")
            : MesCallResult.TransportFailure(
                $"Test snapshot was rejected by the local data pipeline: {enqueueResult.ReasonCode}.");
    }

    protected override Task OnSnapshotUploadedAsync(
        TestPluginSnapshot snapshot,
        MesCallResult result,
        CancellationToken cancellationToken)
    {
        LastCallbackResult = result;
        Interlocked.Increment(ref _callbackCount);
        return Task.CompletedTask;
    }
}
