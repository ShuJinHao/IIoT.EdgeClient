using IIoT.Edge.Application.Modules.Samples;
using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Modules;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Shell.Core;

/// <summary>
/// Runs the formal one-time plugin initialization contract. Modules that expose the
/// new contract never fall back to the legacy every-start contributor path.
/// </summary>
public sealed class ModuleSeedInitializer : IModuleSeedInitializer
{
    private readonly ILogService _logger;
    private readonly IReadOnlyList<IDevelopmentSampleContributor> _legacyContributors;
    private readonly IReadOnlyList<IModuleFirstInitializationContributor> _firstInitializationContributors;
    private readonly IModuleFirstInitializationStore? _firstInitializationStore;
    private readonly string _clientCode;

    public ModuleSeedInitializer(
        ILogService logger,
        IEnumerable<IDevelopmentSampleContributor> contributors)
        : this(logger, contributors, [], null, string.Empty)
    {
    }

    public ModuleSeedInitializer(
        ILogService logger,
        IEnumerable<IDevelopmentSampleContributor> contributors,
        IEnumerable<IModuleFirstInitializationContributor> firstInitializationContributors,
        IModuleFirstInitializationStore firstInitializationStore,
        IConfiguration configuration)
        : this(
            logger,
            contributors,
            firstInitializationContributors,
            firstInitializationStore,
            configuration["DevicePluginBinding:ClientCode"]
                ?? configuration["CloudApi:ClientCode"]
                ?? string.Empty)
    {
    }

    private ModuleSeedInitializer(
        ILogService logger,
        IEnumerable<IDevelopmentSampleContributor> contributors,
        IEnumerable<IModuleFirstInitializationContributor> firstInitializationContributors,
        IModuleFirstInitializationStore? firstInitializationStore,
        string clientCode)
    {
        _logger = logger;
        _legacyContributors = contributors
            .OrderBy(static contributor => contributor.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
        _firstInitializationContributors = firstInitializationContributors
            .OrderBy(static contributor => contributor.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
        _firstInitializationStore = firstInitializationStore;
        _clientCode = clientCode.Trim();
    }

    public async Task<IReadOnlyList<StartupDiagnosticIssue>> ApplyConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var issues = new List<StartupDiagnosticIssue>();
        var firstInitializationModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contributor in _firstInitializationContributors)
        {
            var moduleId = ResolveModuleId(contributor.ModuleId);
            firstInitializationModules.Add(moduleId);
            if (_firstInitializationStore is null || string.IsNullOrWhiteSpace(_clientCode))
            {
                issues.Add(CreateIssue(
                    "MODULE_FIRST_INITIALIZATION_CONTEXT_MISSING",
                    "首次初始化缺少 ClientCode 或持久化端口，已阻断播种。",
                    moduleId));
                continue;
            }

            try
            {
                var request = await contributor
                    .CreateRequestAsync(_clientCode, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.Equals(request.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(request.ClientCode, _clientCode, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Plugin initialization identity does not match host binding.");
                }

                var result = await _firstInitializationStore
                    .ApplyAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                _logger.Info(
                    $"[ModuleSeed][Module={moduleId}][ClientCode={_clientCode}] " +
                    (result.AlreadyInitialized
                        ? "初始化标记已存在，本次不播种。"
                        : result.ExistingDatabaseAdopted
                            ? "已识别现有数据库并补写标记，未重播删除项。"
                            : "首次初始化和标记已在同一事务提交。"));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var message = $"首次初始化失败，事务已回滚，下次启动将重试（{ex.GetType().Name}）。";
                issues.Add(CreateIssue("MODULE_FIRST_INITIALIZATION_FAILED", message, moduleId));
                _logger.Warn($"[ModuleSeed][Module={moduleId}] {message}");
            }
        }

        issues.AddRange(await RunLegacyAsync(
            "MODULE_SEED_APPLY_FAILED",
            "应用旧 ABI 配置播种",
            firstInitializationModules,
            static (contributor, token) => contributor.EnsureConfigurationSamplesAsync(token),
            cancellationToken).ConfigureAwait(false));
        return issues;
    }

    public Task<IReadOnlyList<StartupDiagnosticIssue>> RestoreRuntimeStateAsync(
        CancellationToken cancellationToken = default)
        => RunLegacyAsync(
            "MODULE_RUNTIME_STATE_RESTORE_FAILED",
            "恢复旧 ABI 模块运行状态",
            _firstInitializationContributors
                .Select(static contributor => contributor.ModuleId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            static (contributor, token) => contributor.EnsureRuntimeSamplesAsync(token),
            cancellationToken);

    private async Task<IReadOnlyList<StartupDiagnosticIssue>> RunLegacyAsync(
        string failureCode,
        string operation,
        IReadOnlySet<string> excludedModules,
        Func<IDevelopmentSampleContributor, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        var issues = new List<StartupDiagnosticIssue>();
        foreach (var contributor in _legacyContributors)
        {
            var moduleId = ResolveModuleId(contributor.ModuleId);
            if (excludedModules.Contains(moduleId))
            {
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
                var message = $"{operation}失败，已隔离该模块（{ex.GetType().Name}）。";
                issues.Add(CreateIssue(failureCode, message, moduleId));
                _logger.Warn($"[ModuleSeed][Module={moduleId}] {message}");
            }
        }

        return issues;
    }

    private static string ResolveModuleId(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("ModuleId is missing.")
            : value.Trim();

    private static StartupDiagnosticIssue CreateIssue(string code, string message, string moduleId)
        => StartupDiagnosticIssueFactory.Create(code, message, moduleId);
}
