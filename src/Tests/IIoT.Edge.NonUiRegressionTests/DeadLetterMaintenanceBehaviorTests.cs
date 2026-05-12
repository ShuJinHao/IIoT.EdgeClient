using IIoT.Edge.Application.Features.DataPipeline.DeadLetters;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class DeadLetterMaintenanceBehaviorTests
{
    [Fact]
    public async Task CloudDeadLetter_Requeue_ShouldWriteCloudRetryAndDeleteSource()
    {
        var cloudDeadLetters = new FakeCloudDeadLetterStore();
        cloudDeadLetters.Records.Add(CreateDeadLetter(10, "Cloud", "failed_cloud_records"));
        var cloudRetry = new FakeFailedRecordStore();
        var service = CreateService(cloudDeadLetters: cloudDeadLetters, cloudRetry: cloudRetry);

        var result = await service.RequeueAsync(DataPipelineRetryChannel.Cloud, 10);

        Assert.True(result.IsSuccess);
        Assert.Empty(cloudDeadLetters.Records);
        var retry = Assert.Single(cloudRetry.PendingRecords);
        Assert.Equal("Cloud", retry.Channel);
        Assert.Equal("TRAY-10", retry.CellDataJson);
    }

    [Fact]
    public async Task MesDeadLetter_Requeue_ShouldWriteMesRetryAndDeleteSource()
    {
        var mesDeadLetters = new FakeMesDeadLetterStore();
        mesDeadLetters.Records.Add(CreateDeadLetter(20, "MES", "failed_mes_records"));
        var mesRetry = new FakeFailedRecordStore();
        var service = CreateService(mesDeadLetters: mesDeadLetters, mesRetry: mesRetry);

        var result = await service.RequeueAsync(DataPipelineRetryChannel.Mes, 20);

        Assert.True(result.IsSuccess);
        Assert.Empty(mesDeadLetters.Records);
        var retry = Assert.Single(mesRetry.PendingRecords);
        Assert.Equal("MES", retry.Channel);
        Assert.Equal("TRAY-20", retry.CellDataJson);
    }

    [Fact]
    public async Task DeadLetter_RequeueFailure_ShouldKeepSourceRecord()
    {
        var cloudDeadLetters = new FakeCloudDeadLetterStore();
        cloudDeadLetters.Records.Add(CreateDeadLetter(30, "Cloud", "failed_cloud_records"));
        var cloudRetry = new FakeFailedRecordStore
        {
            SaveException = new InvalidOperationException("retry down")
        };
        var service = CreateService(cloudDeadLetters: cloudDeadLetters, cloudRetry: cloudRetry);

        var result = await service.RequeueAsync(DataPipelineRetryChannel.Cloud, 30);

        Assert.False(result.IsSuccess);
        Assert.Single(cloudDeadLetters.Records);
        Assert.Empty(cloudRetry.PendingRecords);
    }

    [Fact]
    public async Task DeadLetter_Delete_ShouldRemoveOnlySelectedChannelRecord()
    {
        var cloudDeadLetters = new FakeCloudDeadLetterStore();
        cloudDeadLetters.Records.Add(CreateDeadLetter(40, "Cloud", "failed_cloud_records"));
        var mesDeadLetters = new FakeMesDeadLetterStore();
        mesDeadLetters.Records.Add(CreateDeadLetter(40, "MES", "failed_mes_records"));
        var service = CreateService(cloudDeadLetters: cloudDeadLetters, mesDeadLetters: mesDeadLetters);

        var result = await service.DeleteAsync(DataPipelineRetryChannel.Cloud, 40);

        Assert.True(result.IsSuccess);
        Assert.Empty(cloudDeadLetters.Records);
        Assert.Single(mesDeadLetters.Records);
    }

    [Fact]
    public async Task DeadLetter_DeleteFailure_ShouldReturnFailureAndLogError()
    {
        var cloudDeadLetters = new FakeCloudDeadLetterStore
        {
            DeleteException = new InvalidOperationException("delete down")
        };
        cloudDeadLetters.Records.Add(CreateDeadLetter(50, "Cloud", "failed_cloud_records"));
        var logger = new FakeLogService();
        var service = CreateService(cloudDeadLetters: cloudDeadLetters, logger: logger);

        var result = await service.DeleteAsync(DataPipelineRetryChannel.Cloud, 50);

        Assert.False(result.IsSuccess);
        Assert.Single(cloudDeadLetters.Records);
        Assert.Contains(logger.Entries, x => x.Level == "Error" && x.Message.Contains("50", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeadLetter_RequeueDeleteFailure_ShouldReturnFailureAndKeepSourceRecord()
    {
        var cloudDeadLetters = new FakeCloudDeadLetterStore
        {
            DeleteException = new InvalidOperationException("delete down")
        };
        cloudDeadLetters.Records.Add(CreateDeadLetter(60, "Cloud", "failed_cloud_records"));
        var cloudRetry = new FakeFailedRecordStore();
        var logger = new FakeLogService();
        var service = CreateService(cloudDeadLetters: cloudDeadLetters, cloudRetry: cloudRetry, logger: logger);

        var result = await service.RequeueAsync(DataPipelineRetryChannel.Cloud, 60);

        Assert.False(result.IsSuccess);
        Assert.Single(cloudDeadLetters.Records);
        Assert.Single(cloudRetry.PendingRecords);
        Assert.Contains(logger.Entries, x => x.Level == "Error" && x.Message.Contains("60", StringComparison.Ordinal));
    }

    private static DeadLetterMaintenanceService CreateService(
        FakeCloudDeadLetterStore? cloudDeadLetters = null,
        FakeMesDeadLetterStore? mesDeadLetters = null,
        FakeFailedRecordStore? cloudRetry = null,
        FakeFailedRecordStore? mesRetry = null,
        FakeLogService? logger = null)
        => new(
            cloudDeadLetters ?? new FakeCloudDeadLetterStore(),
            mesDeadLetters ?? new FakeMesDeadLetterStore(),
            cloudRetry ?? new FakeFailedRecordStore(),
            mesRetry ?? new FakeFailedRecordStore(),
            logger ?? new FakeLogService());

    private static DeadLetterRecord CreateDeadLetter(long id, string failedTarget, string sourceTable)
        => new()
        {
            Id = id,
            ProcessType = "Homogenization",
            CellDataJson = $"TRAY-{id}",
            FailedTarget = failedTarget,
            SourceTable = sourceTable,
            SourceRecordId = id,
            FailureStage = nameof(DeadLetterStage.FallbackPersist),
            FailureReason = "test",
            CreatedAt = DateTime.UtcNow.AddMinutes(-id)
        };
}
