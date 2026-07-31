using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Module.Contracts.Plc.Checkpoints;

namespace IIoT.Edge.Application.Tests;

public sealed class PlcTaskRecoveryApplicationServiceBehaviorTests
{
    private static readonly PlcTaskCheckpointIdentity Identity =
        new("Module.AP", "PLC-01", "Task.MG1");

    [Fact]
    public async Task QueryAsync_ShouldRouteByModuleAndRejectMismatchedIdentity()
    {
        var matching = new RecordingQuery("module.ap")
        {
            Snapshot = CreateSnapshot(Identity, revision: 7)
        };
        var unrelated = new RecordingQuery("Module.CP");
        var service = CreateService([matching, unrelated], []);

        var result = await service.QueryAsync(
            "MODULE.AP",
            "plc-01",
            "task.mg1",
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(7, result!.Revision);
        Assert.Equal(1, matching.Calls);
        Assert.Equal(0, unrelated.Calls);

        matching.Snapshot = CreateSnapshot(
            new PlcTaskCheckpointIdentity("Module.AP", "PLC-02", "Task.MG1"),
            revision: 8);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.QueryAsync(
                "Module.AP",
                "PLC-01",
                "Task.MG1",
                TestContext.Current.CancellationToken));
        Assert.Contains(
            PlcTaskRecoveryDiagnosticCodes.IdentityMismatch,
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_WhenProviderIsNotUnique_ShouldFailClosed()
    {
        var service = CreateService(
            [new RecordingQuery("Module.AP"), new RecordingQuery("module.ap")],
            []);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.QueryAsync(
                Identity.ModuleId,
                Identity.PlcCode,
                Identity.TaskKey,
                TestContext.Current.CancellationToken));

        Assert.Contains(
            PlcTaskRecoveryDiagnosticCodes.ProviderConflict,
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAsync_ShouldReplaceUnsafePluginDiagnosticWithStableCode()
    {
        var query = new RecordingQuery("Module.AP")
        {
            Snapshot = CreateSnapshot(
                Identity,
                revision: 2,
                diagnosticCode: "raw database path: /private/checkpoints")
        };
        var service = CreateService([query], []);

        var result = await service.QueryAsync(
            Identity.ModuleId,
            Identity.PlcCode,
            Identity.TaskKey,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            PlcTaskRecoveryDiagnosticCodes.InvalidDiagnosticCode,
            result?.DiagnosticCode);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ConfirmAsync_ShouldRequireCurrentLocalAdministrator(
        bool permissionIsLocalAdmin,
        bool sessionIsLocalAdmin)
    {
        var handler = new RecordingHandler("Module.AP");
        var service = CreateService(
            [],
            [handler],
            permissionIsLocalAdmin,
            sessionIsLocalAdmin);

        var failure = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ConfirmAsync(
                Identity.ModuleId,
                Identity.PlcCode,
                Identity.TaskKey,
                expectedRevision: 4,
                PlcTaskRecoveryConfirmationAction.ResumeCheckpoint,
                TestContext.Current.CancellationToken));

        Assert.Contains(
            PlcTaskRecoveryDiagnosticCodes.LocalAdminRequired,
            failure.Message,
            StringComparison.Ordinal);
        Assert.Null(handler.LastCommand);
    }

    [Fact]
    public async Task ConfirmAsync_ShouldForwardExpectedRevisionActionOperatorAndUtcTime()
    {
        var confirmedAtUtc = new DateTimeOffset(
            2026,
            7,
            31,
            8,
            9,
            10,
            TimeSpan.Zero);
        var handler = new RecordingHandler("module.ap")
        {
            Result = PlcTaskRecoveryConfirmationResult.Succeeded(
                CreateSnapshot(Identity, revision: 10))
        };
        var service = CreateService(
            [],
            [handler],
            timeProvider: new FixedTimeProvider(confirmedAtUtc));

        var result = await service.ConfirmAsync(
            "MODULE.AP",
            "plc-01",
            "task.mg1",
            expectedRevision: 9,
            PlcTaskRecoveryConfirmationAction.AuditTerminateIncomplete,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var command = Assert.IsType<PlcTaskRecoveryConfirmationCommand>(
            handler.LastCommand);
        Assert.Equal(9, command.ExpectedRevision);
        Assert.Equal(
            PlcTaskRecoveryConfirmationAction.AuditTerminateIncomplete,
            command.Action);
        Assert.Equal("LOCAL-ADMIN-01", command.OperatorId);
        Assert.Equal(confirmedAtUtc, command.ConfirmedAtUtc);
        Assert.Equal("MODULE.AP", command.Identity.ModuleId);
        Assert.Equal("plc-01", command.Identity.PlcCode);
        Assert.Equal("task.mg1", command.Identity.TaskKey);
    }

    [Fact]
    public async Task ConfirmAsync_WhenOperatorIdentityIsMissing_ShouldFailClosed()
    {
        var handler = new RecordingHandler("Module.AP");
        var auth = new FakeAuthService(new UserSession
        {
            IsLocalAdmin = true,
            EmployeeNo = " "
        });
        var service = new PlcTaskRecoveryApplicationService(
            [],
            [handler],
            new FakePermissionService(true),
            auth);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ConfirmAsync(
                Identity.ModuleId,
                Identity.PlcCode,
                Identity.TaskKey,
                expectedRevision: 1,
                PlcTaskRecoveryConfirmationAction.ResumeCheckpoint,
                TestContext.Current.CancellationToken));

        Assert.Contains(
            PlcTaskRecoveryDiagnosticCodes.OperatorIdentityMissing,
            failure.Message,
            StringComparison.Ordinal);
        Assert.Null(handler.LastCommand);
    }

    private static PlcTaskRecoveryApplicationService CreateService(
        IEnumerable<IPlcTaskRecoveryQuery> queries,
        IEnumerable<IPlcTaskRecoveryConfirmationHandler> handlers,
        bool permissionIsLocalAdmin = true,
        bool sessionIsLocalAdmin = true,
        TimeProvider? timeProvider = null)
        => new(
            queries,
            handlers,
            new FakePermissionService(permissionIsLocalAdmin),
            new FakeAuthService(new UserSession
            {
                IsLocalAdmin = sessionIsLocalAdmin,
                EmployeeNo = "LOCAL-ADMIN-01"
            }),
            timeProvider);

    private static PlcTaskRecoverySnapshot CreateSnapshot(
        PlcTaskCheckpointIdentity identity,
        long revision,
        string diagnosticCode = "MagazineMismatch")
        => new(
            identity,
            slot: "MG1",
            checkpointMagazineCode: "MAG-001",
            observedMagazineCode: "MAG-002",
            PlcTaskRecoveryState.AwaitingConfirmation,
            revision,
            checkpointSavedAtUtc: DateTimeOffset.UnixEpoch,
            observedAtUtc: DateTimeOffset.UnixEpoch.AddMinutes(1),
            diagnosticCode);

    private sealed class RecordingQuery(string moduleId)
        : IPlcTaskRecoveryQuery
    {
        public string ModuleId { get; } = moduleId;

        public int Calls { get; private set; }

        public PlcTaskRecoverySnapshot? Snapshot { get; set; }

        public ValueTask<PlcTaskRecoverySnapshot?> QueryAsync(
            PlcTaskCheckpointIdentity identity,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult(Snapshot);
        }
    }

    private sealed class RecordingHandler(string moduleId)
        : IPlcTaskRecoveryConfirmationHandler
    {
        public string ModuleId { get; } = moduleId;

        public PlcTaskRecoveryConfirmationCommand? LastCommand { get; private set; }

        public PlcTaskRecoveryConfirmationResult Result { get; init; }
            = PlcTaskRecoveryConfirmationResult.Succeeded();

        public ValueTask<PlcTaskRecoveryConfirmationResult> ConfirmAsync(
            PlcTaskRecoveryConfirmationCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCommand = command;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class FakePermissionService(bool isLocalAdmin)
        : IClientPermissionService
    {
        public bool CanEditParams => isLocalAdmin;

        public bool CanEditHardware => isLocalAdmin;

        public bool IsLocalAdmin => isLocalAdmin;

        public bool HasPermission(string permission) => isLocalAdmin;

        public event Action? PermissionStateChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class FakeAuthService(UserSession? currentUser)
        : IAuthService
    {
        public UserSession? CurrentUser { get; private set; } = currentUser;

        public bool IsAuthenticated => CurrentUser is not null;

        public LocalAdminCredentialStatus LocalAdminCredentialStatus
            => LocalAdminCredentialStatus.Ready;

        public bool HasPermission(string permission)
            => CurrentUser?.IsLocalAdmin == true;

        public Task<bool> EnsureAuthenticatedAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(IsAuthenticated);

        public Task<AuthResult> LoginLocalAsync(string password)
            => throw new NotSupportedException();

        public Task<AuthResult> InitializeLocalAdminAsync(string newPassword)
            => throw new NotSupportedException();

        public Task<AuthResult> ResetLocalAdminPasswordAsync(
            string currentPassword,
            string newPassword)
            => throw new NotSupportedException();

        public Task<AuthResult> LoginCloudAsync(
            string employeeNo,
            string password,
            Guid deviceId)
            => throw new NotSupportedException();

        public void Logout()
        {
            CurrentUser = null;
            AuthStateChanged?.Invoke(null);
        }

        public event Action<UserSession?>? AuthStateChanged;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
