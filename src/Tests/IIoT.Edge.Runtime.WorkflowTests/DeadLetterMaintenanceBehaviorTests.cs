using IIoT.Edge.Application.Features.DataPipeline.DeadLetters;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.Auth;

namespace IIoT.Edge.Runtime.WorkflowTests;

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
        Assert.Equal("PLC-DEADLETTER", retry.PlcCode);
        Assert.Equal(CloudIdempotencyKeyVersion.LegacyV1, retry.IdempotencyKeyVersion);
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
    public async Task DeadLetter_Delete_ShouldAlwaysFailClosedAndKeepBothChannels()
    {
        var cloudDeadLetters = new FakeCloudDeadLetterStore();
        cloudDeadLetters.Records.Add(CreateDeadLetter(40, "Cloud", "failed_cloud_records"));
        var mesDeadLetters = new FakeMesDeadLetterStore();
        mesDeadLetters.Records.Add(CreateDeadLetter(40, "MES", "failed_mes_records"));
        var service = CreateService(cloudDeadLetters: cloudDeadLetters, mesDeadLetters: mesDeadLetters);

        var result = await service.DeleteAsync(DataPipelineRetryChannel.Cloud, 40);

        Assert.False(result.IsSuccess);
        Assert.Contains("禁止人工硬删除", result.Message, StringComparison.Ordinal);
        Assert.Single(cloudDeadLetters.Records);
        Assert.Single(mesDeadLetters.Records);
    }

    [Fact]
    public async Task DeadLetter_RequeueSourceRemovalFailure_ShouldRollbackRetryAndKeepSourceRecord()
    {
        var cloudDeadLetters = new FakeCloudDeadLetterStore
        {
            DeleteException = new InvalidOperationException("delete down")
        };
        cloudDeadLetters.Records.Add(CreateDeadLetter(60, "Cloud", "failed_cloud_records"));
        var cloudRetry = new FakeFailedRecordStore();
        var logger = new FakeLogService();
        var service = CreateService(cloudDeadLetters: cloudDeadLetters, cloudRetry: cloudRetry, logger: logger);
        cloudRetry.RequeueSourceRemover = _ => throw new InvalidOperationException("delete down");

        var result = await service.RequeueAsync(DataPipelineRetryChannel.Cloud, 60);

        Assert.False(result.IsSuccess);
        Assert.Single(cloudDeadLetters.Records);
        Assert.Empty(cloudRetry.PendingRecords);
        Assert.Contains(logger.Entries, x => x.Level == "Error" && x.Message.Contains("60", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeadLetter_Requeue_WhenCallerIsNotLocalAdmin_ShouldKeepSource()
    {
        var cloudDeadLetters = new FakeCloudDeadLetterStore();
        cloudDeadLetters.Records.Add(CreateDeadLetter(65, "Cloud", "failed_cloud_records"));
        var cloudRetry = new FakeFailedRecordStore();
        var service = CreateService(
            cloudDeadLetters: cloudDeadLetters,
            cloudRetry: cloudRetry,
            isLocalAdmin: false);

        var result = await service.RequeueAsync(DataPipelineRetryChannel.Cloud, 65);

        Assert.False(result.IsSuccess);
        Assert.Single(cloudDeadLetters.Records);
        Assert.Empty(cloudRetry.PendingRecords);
    }

    [Fact]
    public async Task DeadLetter_WhenStableIdentityUnresolved_ShouldRejectRequeueAndDelete()
    {
        var cloudDeadLetters = new FakeCloudDeadLetterStore();
        var unresolved = CreateDeadLetter(70, "Cloud", "failed_cloud_records");
        unresolved.PlcCode = string.Empty;
        cloudDeadLetters.Records.Add(unresolved);
        var cloudRetry = new FakeFailedRecordStore();
        var service = CreateService(cloudDeadLetters: cloudDeadLetters, cloudRetry: cloudRetry);

        var requeue = await service.RequeueAsync(DataPipelineRetryChannel.Cloud, 70);
        var delete = await service.DeleteAsync(DataPipelineRetryChannel.Cloud, 70);

        Assert.False(requeue.IsSuccess);
        Assert.False(delete.IsSuccess);
        Assert.Contains("身份未解析", requeue.Message, StringComparison.Ordinal);
        Assert.Single(cloudDeadLetters.Records);
        Assert.Empty(cloudRetry.PendingRecords);
    }

    private static DeadLetterMaintenanceService CreateService(
        FakeCloudDeadLetterStore? cloudDeadLetters = null,
        FakeMesDeadLetterStore? mesDeadLetters = null,
        FakeFailedRecordStore? cloudRetry = null,
        FakeFailedRecordStore? mesRetry = null,
        FakeLogService? logger = null,
        bool isLocalAdmin = true)
    {
        var resolvedCloudDeadLetters = cloudDeadLetters ?? new FakeCloudDeadLetterStore();
        var resolvedMesDeadLetters = mesDeadLetters ?? new FakeMesDeadLetterStore();
        var resolvedCloudRetry = cloudRetry ?? new FakeFailedRecordStore();
        var resolvedMesRetry = mesRetry ?? new FakeFailedRecordStore();
        resolvedCloudRetry.RequeueSourceResolver ??= id =>
            resolvedCloudDeadLetters.Records.SingleOrDefault(record => record.Id == id);
        resolvedCloudRetry.RequeueSourceRemover ??= id =>
            resolvedCloudDeadLetters.Records.RemoveAll(record => record.Id == id);
        resolvedMesRetry.RequeueSourceResolver ??= id =>
            resolvedMesDeadLetters.Records.SingleOrDefault(record => record.Id == id);
        resolvedMesRetry.RequeueSourceRemover ??= id =>
            resolvedMesDeadLetters.Records.RemoveAll(record => record.Id == id);
        return new DeadLetterMaintenanceService(
            resolvedCloudDeadLetters,
            resolvedMesDeadLetters,
            resolvedCloudRetry,
            resolvedMesRetry,
            resolvedCloudRetry,
            resolvedMesRetry,
            new FakePermissionService(isLocalAdmin),
            new FakeAuthService(new UserSession
            {
                IsLocalAdmin = isLocalAdmin,
                EmployeeNo = "LOCAL-ADMIN-01"
            }),
            logger ?? new FakeLogService());
    }

    private static DeadLetterRecord CreateDeadLetter(long id, string failedTarget, string sourceTable)
        => new()
        {
            Id = id,
            ProcessType = "TestProcess",
            CellDataJson = $"TRAY-{id}",
            FailedTarget = failedTarget,
            SourceTable = sourceTable,
            SourceRecordId = id,
            FailureStage = nameof(DeadLetterStage.FallbackPersist),
            FailureReason = "test",
            CreatedAt = DateTime.UtcNow.AddMinutes(-id),
            PlcCode = "PLC-DEADLETTER",
            NetworkDeviceId = 10,
            DeviceName = "Display-Deadletter",
            ModuleId = "TestModule",
            TaskKey = "TestModule.Task",
            IdempotencyKeyVersion = CloudIdempotencyKeyVersion.LegacyV1
        };

    private sealed class FakePermissionService(bool isLocalAdmin) : IClientPermissionService
    {
        public bool CanEditParams => isLocalAdmin;
        public bool CanEditHardware => isLocalAdmin;
        public bool IsLocalAdmin => isLocalAdmin;
        public bool HasPermission(string permission) => isLocalAdmin;
        public event Action? PermissionStateChanged { add { } remove { } }
    }

    private sealed class FakeAuthService(UserSession? currentUser) : IAuthService
    {
        public UserSession? CurrentUser { get; private set; } = currentUser;
        public bool IsAuthenticated => CurrentUser is not null;
        public LocalAdminCredentialStatus LocalAdminCredentialStatus => LocalAdminCredentialStatus.Ready;
        public bool HasPermission(string permission) => CurrentUser?.IsLocalAdmin == true;
        public Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(IsAuthenticated);
        public Task<AuthResult> LoginLocalAsync(string password) => throw new NotSupportedException();
        public Task<AuthResult> InitializeLocalAdminAsync(string newPassword) => throw new NotSupportedException();
        public Task<AuthResult> ResetLocalAdminPasswordAsync(string currentPassword, string newPassword)
            => throw new NotSupportedException();
        public Task<AuthResult> LoginCloudAsync(string employeeNo, string password, Guid deviceId)
            => throw new NotSupportedException();
        public void Logout()
        {
            CurrentUser = null;
            AuthStateChanged?.Invoke(null);
        }
        public event Action<UserSession?>? AuthStateChanged;
    }
}
