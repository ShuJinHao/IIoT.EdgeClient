using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.Host.DataPipeline;
using IIoT.Edge.Host.DataPipeline.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Runtime.WorkflowTests;

public sealed class DataPipelineServiceBehaviorTests
{
    [Fact]
    public void AddEdgeRuntime_ShouldRequireDurableIngressAndExposeNoLegacyOverflowImplementation()
    {
        var paths = CreateRuntimePaths();
        var missingIngressServices = new ServiceCollection();
        missingIngressServices.AddSingleton<ILogService>(new FakeLogService());
        missingIngressServices.AddSingleton<IDevicePluginRuntimeContext>(
            new StubDevicePluginRuntimeContext());
        missingIngressServices.AddEdgeRuntime(paths);
        using (var missingIngressProvider = missingIngressServices.BuildServiceProvider())
        {
            Assert.Null(missingIngressProvider.GetService<IIngressOverflowPersistence>());
            Assert.Throws<InvalidOperationException>(
                () => missingIngressProvider.GetRequiredService<DataPipelineService>());
        }

        var services = new ServiceCollection();
        services.AddSingleton<ILogService>(new FakeLogService());
        services.AddSingleton<IDevicePluginRuntimeContext>(
            new StubDevicePluginRuntimeContext());
        services.AddSingleton<IDataPipelineIngressStore>(new FakeDataPipelineIngressStore());
        services.AddEdgeRuntime(paths);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<DataPipelineService>());
        Assert.Null(provider.GetService<IIngressOverflowPersistence>());
    }

    private sealed class StubDevicePluginRuntimeContext : IDevicePluginRuntimeContext
    {
        public DevicePluginRuntimeIdentity Current { get; } = new(
            3,
            "GEN-TEST",
            "P1-TEST",
            "TestProcess",
            "TestModule",
            "2.0.12",
            new string('A', 64));
    }

    [Fact]
    public async Task EnqueueAsync_WhenPlcContextMissing_ShouldRejectRecord()
    {
        var overflowPersistence = new FakeIngressOverflowPersistence();
        var logger = new FakeLogService();
        var pipeline = new DataPipelineService(overflowPersistence, logger);

        var result = await pipeline.EnqueueAsync(
            new CellCompletedRecord
            {
                CellData = new TestProcessCellData
                {
                    Barcode = "BC-MISSING",
                    WorkOrderNo = "WO-MISSING",
                    CompletedTime = DateTime.UtcNow
                }
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsDurablyAccepted);
        Assert.Equal("missing_plc_context", result.ReasonCode);
        Assert.Equal(0, pipeline.PendingCount);
        Assert.Empty(overflowPersistence.Records);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == "Warn"
                     && entry.Message.Contains("缺少 PLC 上下文", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnqueueAsync_WhenQueueOverflows_ShouldPersistOverflowAndTrackCounters()
    {
        var overflowPersistence = new FakeIngressOverflowPersistence
        {
            Result = DataPipelineEnqueueResult.OverflowPersisted(1, 1)
        };
        var pipeline = new DataPipelineService(overflowPersistence, new FakeLogService());

        DataPipelineEnqueueResult overflowResult = DataPipelineEnqueueResult.Rejected("not_reached");
        for (var i = 0; i < 6000; i++)
        {
            var result = await pipeline.EnqueueAsync(
                CreateRecord($"BC-{i:D4}"),
                TestContext.Current.CancellationToken);
            if (!result.WasOverflow)
            {
                continue;
            }

            overflowResult = result;
            break;
        }

        Assert.True(overflowResult.WasOverflow);
        Assert.Equal(1, overflowResult.PersistedTargetCount);
        Assert.Single(overflowPersistence.Records);
        Assert.True(pipeline.PendingCount <= 5000);
        Assert.Equal(1, pipeline.OverflowCount);
        Assert.Equal(1, pipeline.SpillCount);
    }

    [Fact]
    public async Task EnqueueAsync_WhenStableIdentityConflicts_ShouldRejectWithoutPersistence()
    {
        var overflowPersistence = new FakeIngressOverflowPersistence();
        var logger = new FakeLogService();
        var pipeline = new DataPipelineService(overflowPersistence, logger);
        var record = CreateRecord("BC-CONFLICT");
        record.PlcCode = "PLC-OTHER";

        var result = await pipeline.EnqueueAsync(
            record,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsDurablyAccepted);
        Assert.Equal("conflicting_plc_context", result.ReasonCode);
        Assert.Equal(0, pipeline.PendingCount);
        Assert.Empty(overflowPersistence.Records);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == "Warn"
                     && entry.Message.Contains(nameof(CellCompletedRecord.PlcCode), StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnqueueAsync_WhenExplicitPlcCodeIsMissing_ShouldNotInferItFromCellData()
    {
        var pipeline = new DataPipelineService(
            new FakeIngressOverflowPersistence(),
            new FakeLogService());
        var record = CreateRecord("BC-NO-INFERENCE");
        record.PlcCode = string.Empty;

        var result = await pipeline.EnqueueAsync(
            record,
            TestContext.Current.CancellationToken);

        Assert.False(result.IsDurablyAccepted);
        Assert.Equal("missing_plc_context", result.ReasonCode);
        Assert.Equal(0, pipeline.PendingCount);
    }

    [Fact]
    public async Task EnqueueAsync_WhenAccepted_ShouldFreezeStableV2Identity()
    {
        var pipeline = new DataPipelineService(
            new FakeIngressOverflowPersistence(),
            new FakeLogService());
        var record = CreateRecord("BC-V2");
        record.DeviceName = "Current-Display";

        var result = await pipeline.EnqueueAsync(
            record,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsDurablyAccepted);
        Assert.True(pipeline.TryDequeue(out var queued));
        Assert.NotNull(queued);
        Assert.Equal("PLC-A", queued!.PlcCode);
        Assert.Equal("Current-Display", queued.DeviceName);
        Assert.Equal(CloudIdempotencyKeyVersion.PlcStableV2, queued.IdempotencyKeyVersion);
    }

    [Fact]
    public async Task EnqueueAsync_WithDurableIngress_ShouldPersistBeforePublishingNotification()
    {
        var ingress = new FakeDataPipelineIngressStore();
        var overflow = new FakeIngressOverflowPersistence();
        var pipeline = new DataPipelineService(overflow, new FakeLogService(), ingress);
        var record = CreateRecord("BC-DURABLE");

        var result = await pipeline.EnqueueAsync(record, TestContext.Current.CancellationToken);

        Assert.True(result.IsDurablyAccepted);
        Assert.Equal(1, ingress.AcceptCallCount);
        Assert.Single(ingress.PendingCompletionIds);
        Assert.Empty(overflow.Records);
        Assert.True(pipeline.TryDequeue(out var queued));
        Assert.Equal("BC-DURABLE", Assert.IsType<TestProcessCellData>(queued!.CellData).Barcode);
    }

    [Fact]
    public async Task EnqueueAsync_WhenMemoryNotificationQueueIsFull_ShouldKeepEveryEnvelopeInDurableIngress()
    {
        var ingress = new FakeDataPipelineIngressStore();
        var overflow = new FakeIngressOverflowPersistence();
        var pipeline = new DataPipelineService(overflow, new FakeLogService(), ingress);
        DataPipelineEnqueueResult? last = null;

        for (var index = 0; index <= 5000; index++)
        {
            last = await pipeline.EnqueueAsync(
                CreateRecord($"BC-DURABLE-{index:D4}"),
                TestContext.Current.CancellationToken);
        }

        Assert.NotNull(last);
        Assert.True(last.WasOverflow);
        Assert.True(last.IsDurablyAccepted);
        Assert.Equal(5001, ingress.PendingCompletionIds.Count);
        Assert.Equal(5000, pipeline.PendingCount);
        Assert.Equal(1, pipeline.OverflowCount);
        Assert.Equal(1, pipeline.SpillCount);
        Assert.Empty(overflow.Records);
    }

    [Fact]
    public async Task EnqueueAsync_WhenDurableIngressFails_ShouldRejectWithoutPublishingNotification()
    {
        var ingress = new FakeDataPipelineIngressStore
        {
            AcceptException = new InvalidOperationException("storage unavailable")
        };
        var pipeline = new DataPipelineService(
            new FakeIngressOverflowPersistence(),
            new FakeLogService(),
            ingress);

        var result = await pipeline.EnqueueAsync(
            CreateRecord("BC-REJECT"),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsDurablyAccepted);
        Assert.Equal("durable_ingress_unavailable", result.ReasonCode);
        Assert.False(pipeline.TryDequeue(out _));
    }

    [Fact]
    public async Task EnqueueAsync_WhenCompletedFactIsSubmittedAgain_ShouldNotPublishDuplicateNotification()
    {
        var ingress = new FakeDataPipelineIngressStore();
        var pipeline = new DataPipelineService(
            new FakeIngressOverflowPersistence(),
            new FakeLogService(),
            ingress);
        var record = CreateRecord("BC-IDEMPOTENT");

        await pipeline.EnqueueAsync(record, TestContext.Current.CancellationToken);
        Assert.True(pipeline.TryDequeue(out _));
        var completionId = Assert.Single(ingress.PendingCompletionIds);
        Assert.True(await ingress.CompleteIfAllConsumersFinishedAsync(
            completionId,
            [],
            TestContext.Current.CancellationToken));

        var duplicate = await pipeline.EnqueueAsync(record, TestContext.Current.CancellationToken);

        Assert.True(duplicate.IsDurablyAccepted);
        Assert.False(pipeline.TryDequeue(out _));
        Assert.Empty(ingress.PendingCompletionIds);
    }

    private static CellCompletedRecord CreateRecord(string barcode)
        => new()
        {
            PlcCode = "PLC-A",
            NetworkDeviceId = 1,
            DeviceName = "PLC-A",
            ModuleId = "TestProcess",
            TaskKey = "TestProcess.Realtime",
            CellData = new TestProcessCellData
            {
                PlcDeviceId = 1,
                DeviceName = "PLC-A",
                DeviceCode = "PLC-A",
                Barcode = barcode,
                WorkOrderNo = $"WO-{barcode}",
                CompletedTime = DateTime.UtcNow
            }
        };

    private static EdgeRuntimePaths CreateRuntimePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "edge-data-pipeline-di");
        return new EdgeRuntimePaths(
            root,
            "test",
            root,
            Path.Combine(root, "db"),
            Path.Combine(root, "context"),
            Path.Combine(root, "recipe"),
            Path.Combine(root, "excel"),
            Path.Combine(root, "diagnostics"),
            Path.Combine(root, "logs"),
            Path.Combine(root, "device-cache.json"),
            Path.Combine(root, "crash.log"),
            Path.Combine(root, "crash-fallback.log"));
    }
}
