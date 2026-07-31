using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;

namespace IIoT.Edge.Shell.Core;

/// <summary>
/// Host API 2.0.x 继续消费旧 contributor ABI，但宿主只把它解释为正式 ModuleSeed。
/// </summary>
public sealed class ModuleSeedInitializer : IModuleSeedInitializer
{
    private readonly ILogService _logger;
    private readonly IReadOnlyList<IDevelopmentSampleContributor> _contributors;

    public ModuleSeedInitializer(
        ILogService logger,
        IEnumerable<IDevelopmentSampleContributor> contributors)
    {
        _logger = logger;
        _contributors = contributors
            .OrderBy(static contributor => contributor.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
    }

    public Task<IReadOnlyList<StartupDiagnosticIssue>> ApplyConfigurationAsync(
        CancellationToken cancellationToken = default)
        => RunAsync(
            "MODULE_SEED_APPLY_FAILED",
            "应用正式配置播种",
            static (contributor, token) =>
                contributor.EnsureConfigurationSamplesAsync(token),
            cancellationToken);

    public Task<IReadOnlyList<StartupDiagnosticIssue>> RestoreRuntimeStateAsync(
        CancellationToken cancellationToken = default)
        => RunAsync(
            "MODULE_RUNTIME_STATE_RESTORE_FAILED",
            "恢复模块运行状态",
            static (contributor, token) =>
                contributor.EnsureRuntimeSamplesAsync(token),
            cancellationToken);

    private async Task<IReadOnlyList<StartupDiagnosticIssue>> RunAsync(
        string failureCode,
        string operation,
        Func<IDevelopmentSampleContributor, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var issues = new List<StartupDiagnosticIssue>();
        foreach (var contributor in _contributors)
        {
            string moduleId;
            try
            {
                var declaredModuleId = contributor.ModuleId;
                moduleId = string.IsNullOrWhiteSpace(declaredModuleId)
                    ? throw new InvalidOperationException("ModuleId 为空。")
                    : declaredModuleId.Trim();
            }
            catch (Exception ex)
            {
                const string unknownModule = "未解析";
                var message = $"模块身份读取失败，已隔离该 ModuleSeed（{ex.GetType().Name}）。";
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    "MODULE_SEED_IDENTITY_FAILED",
                    message,
                    unknownModule));
                _logger.Warn($"[ModuleSeed][Module={unknownModule}] {message}");
                continue;
            }

            try
            {
                await action(contributor, cancellationToken).ConfigureAwait(false);
                _logger.Info($"[ModuleSeed][Module={moduleId}] {operation}完成。");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var message =
                    $"{operation}失败，已保留现场数据并隔离该模块（{ex.GetType().Name}）。";
                issues.Add(StartupDiagnosticIssueFactory.Create(
                    failureCode,
                    message,
                    moduleId));
                _logger.Warn($"[ModuleSeed][Module={moduleId}] {message}");
            }
        }

        return issues;
    }
}
