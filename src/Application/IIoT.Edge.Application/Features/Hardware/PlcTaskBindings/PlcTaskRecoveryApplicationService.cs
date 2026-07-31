using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Module.Contracts.Plc.Checkpoints;

namespace IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;

public static class PlcTaskRecoveryDiagnosticCodes
{
    public const string ProviderUnavailable = nameof(ProviderUnavailable);
    public const string ProviderConflict = nameof(ProviderConflict);
    public const string IdentityMismatch = nameof(IdentityMismatch);
    public const string LocalAdminRequired = nameof(LocalAdminRequired);
    public const string OperatorIdentityMissing = nameof(OperatorIdentityMissing);
    public const string InvalidDiagnosticCode = nameof(InvalidDiagnosticCode);
}

public interface IPlcTaskRecoveryApplicationService
{
    Task<PlcTaskRecoverySnapshot?> QueryAsync(
        string moduleId,
        string plcCode,
        string taskKey,
        CancellationToken cancellationToken = default);

    Task<PlcTaskRecoveryConfirmationResult> ConfirmAsync(
        string moduleId,
        string plcCode,
        string taskKey,
        long expectedRevision,
        PlcTaskRecoveryConfirmationAction action,
        CancellationToken cancellationToken = default);
}

public sealed class PlcTaskRecoveryApplicationService(
    IEnumerable<IPlcTaskRecoveryQuery> queries,
    IEnumerable<IPlcTaskRecoveryConfirmationHandler> handlers,
    IClientPermissionService permissionService,
    IAuthService authService,
    TimeProvider? timeProvider = null)
    : IPlcTaskRecoveryApplicationService
{
    private readonly IReadOnlyList<IPlcTaskRecoveryQuery> _queries = queries.ToArray();
    private readonly IReadOnlyList<IPlcTaskRecoveryConfirmationHandler> _handlers = handlers.ToArray();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<PlcTaskRecoverySnapshot?> QueryAsync(
        string moduleId,
        string plcCode,
        string taskKey,
        CancellationToken cancellationToken = default)
    {
        var identity = new PlcTaskCheckpointIdentity(moduleId, plcCode, taskKey);
        var query = ResolveSingleProvider(_queries, identity.ModuleId, static provider => provider.ModuleId);
        if (query is null)
        {
            return null;
        }

        var snapshot = await query
            .QueryAsync(identity, cancellationToken)
            .ConfigureAwait(false);
        EnsureMatchingIdentity(identity, snapshot?.Identity);
        return NormalizeSnapshot(snapshot);
    }

    public async Task<PlcTaskRecoveryConfirmationResult> ConfirmAsync(
        string moduleId,
        string plcCode,
        string taskKey,
        long expectedRevision,
        PlcTaskRecoveryConfirmationAction action,
        CancellationToken cancellationToken = default)
    {
        if (!permissionService.IsLocalAdmin
            || authService.CurrentUser?.IsLocalAdmin != true)
        {
            throw new UnauthorizedAccessException(
                $"单任务恢复确认被拒绝，原因码={PlcTaskRecoveryDiagnosticCodes.LocalAdminRequired}。");
        }

        var operatorId = authService.CurrentUser.EmployeeNo?.Trim();
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            throw new InvalidOperationException(
                $"本地管理员缺少审计身份，原因码={PlcTaskRecoveryDiagnosticCodes.OperatorIdentityMissing}。");
        }

        var identity = new PlcTaskCheckpointIdentity(moduleId, plcCode, taskKey);
        var handler = ResolveSingleProvider(
            _handlers,
            identity.ModuleId,
            static provider => provider.ModuleId);
        if (handler is null)
        {
            return PlcTaskRecoveryConfirmationResult.Rejected(
                PlcTaskRecoveryConfirmationOutcome.NotFound,
                PlcTaskRecoveryDiagnosticCodes.ProviderUnavailable);
        }

        var result = await handler
            .ConfirmAsync(
                new PlcTaskRecoveryConfirmationCommand(
                    identity,
                    action,
                    expectedRevision,
                    operatorId,
                    _timeProvider.GetUtcNow()),
                cancellationToken)
            .ConfigureAwait(false);
        EnsureMatchingIdentity(identity, result.CurrentSnapshot?.Identity);
        return new PlcTaskRecoveryConfirmationResult(
            result.Outcome,
            NormalizeDiagnosticCode(
                result.DiagnosticCode,
                result.IsSuccess ? null : result.Outcome.ToString())
            ?? string.Empty,
            NormalizeSnapshot(result.CurrentSnapshot));
    }

    private static TProvider? ResolveSingleProvider<TProvider>(
        IReadOnlyCollection<TProvider> providers,
        string moduleId,
        Func<TProvider, string> moduleIdSelector)
        where TProvider : class
    {
        var matches = providers
            .Where(provider => string.Equals(
                moduleIdSelector(provider)?.Trim(),
                moduleId,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"模块恢复端口不唯一，原因码={PlcTaskRecoveryDiagnosticCodes.ProviderConflict}。")
        };
    }

    private static void EnsureMatchingIdentity(
        PlcTaskCheckpointIdentity expected,
        PlcTaskCheckpointIdentity? actual)
    {
        if (actual is null)
        {
            return;
        }

        if (!string.Equals(expected.ModuleId, actual.ModuleId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(expected.PlcCode, actual.PlcCode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(expected.TaskKey, actual.TaskKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"插件恢复结果身份不匹配，原因码={PlcTaskRecoveryDiagnosticCodes.IdentityMismatch}。");
        }
    }

    private static PlcTaskRecoverySnapshot? NormalizeSnapshot(
        PlcTaskRecoverySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new PlcTaskRecoverySnapshot(
            snapshot.Identity,
            snapshot.Slot,
            snapshot.CheckpointMagazineCode,
            snapshot.ObservedMagazineCode,
            snapshot.State,
            snapshot.Revision,
            snapshot.CheckpointSavedAtUtc,
            snapshot.ObservedAtUtc,
            NormalizeDiagnosticCode(snapshot.DiagnosticCode, fallback: null));
    }

    private static string? NormalizeDiagnosticCode(
        string? diagnosticCode,
        string? fallback)
    {
        if (string.IsNullOrWhiteSpace(diagnosticCode))
        {
            return fallback;
        }

        var normalized = diagnosticCode.Trim();
        return normalized.Length <= 128
               && normalized.All(static character =>
                   char.IsAsciiLetterOrDigit(character)
                   || character is '.' or '_' or '-')
            ? normalized
            : PlcTaskRecoveryDiagnosticCodes.InvalidDiagnosticCode;
    }
}
