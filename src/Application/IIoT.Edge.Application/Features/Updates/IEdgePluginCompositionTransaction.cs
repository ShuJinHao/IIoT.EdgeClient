using IIoT.Edge.Module.Contracts.Updates;

namespace IIoT.Edge.Application.Features.Updates;

/// <summary>
/// EdgeClient 内部组合安装事务；不扩展插件 SDK 公共契约。
/// </summary>
public interface IEdgePluginCompositionTransaction
{
    Task<EdgePluginInstallResult> InstallAsync(
        IReadOnlyList<EdgePluginCompositionTarget> targets,
        IReadOnlyList<EdgePluginCompositionRelease> releases,
        string compatibilityHostVersion,
        string compatibilityHostApiVersion,
        string? pendingHostVersion,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    EdgePluginInstallResult RollbackPendingHostHandoff();
}

public sealed record EdgePluginCompositionTarget(
    EdgeUpdateTarget Target,
    IReadOnlyList<string> ModuleIds);

public sealed record EdgePluginCompositionRelease(
    EdgePluginVersionRelease Release,
    EdgeUpdateCloudApiOptions CloudOptions);

public interface IEdgeUpdateTransactionRecovery
{
    EdgeUpdateTransactionRecoveryResult RecoverPendingTransaction();

    bool IsProfileBlocked(string machineProfile);
}

public sealed record EdgeUpdateTransactionRecoveryResult(
    bool Success,
    bool Recovered,
    bool Blocked,
    string? ErrorMessage = null);
