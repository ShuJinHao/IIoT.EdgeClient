using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Features.Config.SchemaReconciliation;
using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.EfCore;

namespace IIoT.Edge.Shell.Core;

public interface IAppStartupInitializer
{
    Task<IReadOnlyList<StartupDiagnosticIssue>> InitializeAsync(
        CancellationToken cancellationToken = default);
}

public sealed class AppStartupInitializer : IAppStartupInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDevelopmentSampleInitializer _developmentSampleInitializer;
    private readonly ICloudSystemSwitchMigration _cloudSystemSwitchMigration;
    private readonly IConfigSchemaReconciler _configSchemaReconciler;
    private readonly ILogService _logger;

    public AppStartupInitializer(
        IServiceProvider serviceProvider,
        IDevelopmentSampleInitializer developmentSampleInitializer,
        ICloudSystemSwitchMigration cloudSystemSwitchMigration,
        IConfigSchemaReconciler configSchemaReconciler,
        ILogService logger)
    {
        _serviceProvider = serviceProvider;
        _developmentSampleInitializer = developmentSampleInitializer;
        _cloudSystemSwitchMigration = cloudSystemSwitchMigration;
        _configSchemaReconciler = configSchemaReconciler;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StartupDiagnosticIssue>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var issues = new List<StartupDiagnosticIssue>();
        try
        {
            _serviceProvider.ApplyMigrations();
            _logger.Info("[生命周期] EF Core 迁移完成。");
        }
        catch (Exception ex)
        {
            var message = $"EF Core 迁移或 SQLite 运行 pragma 初始化失败，已按非阻断处理：{ex.Message}";
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "STARTUP_EF_MIGRATION_FAILED",
                message));
            _logger.Warn($"[生命周期] {message}");
        }

        var dapperFailures = await _serviceProvider
            .InitializeDapperTablesAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var failure in dapperFailures)
        {
            var message =
                $"Dapper 表初始化失败，已按非阻断处理：DbName={failure.DbName}，" +
                $"initializer={failure.InitializerType}，{failure.Exception.Message}";
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "STARTUP_DAPPER_TABLE_INITIALIZATION_FAILED",
                message));
            _logger.Warn($"[生命周期] {message}");
        }

        _logger.Info($"[生命周期] Dapper 表初始化完成，失败 {dapperFailures.Count} 项。");

        await RunNonBlockingInitializerStepAsync(
            "开发样例配置初始化",
            () => _developmentSampleInitializer.EnsureConfigurationSamplesAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (await TryMigrateCloudSystemSwitchAsync(cancellationToken).ConfigureAwait(false))
        {
            await RunNonBlockingInitializerStepAsync(
                "配置枚举对账",
                () => _configSchemaReconciler.ReconcileAsync(cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.Warn("[生命周期] Cloud 系统开关迁移未完成，本次跳过配置枚举对账，保留旧键供下次安全重试。");
        }

        return issues;
    }

    private async Task RunNonBlockingInitializerStepAsync(
        string stepName,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action().ConfigureAwait(false);
            _logger.Info($"[生命周期] {stepName}完成。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[生命周期] {stepName}失败，已按非阻断处理：{ex.Message}");
        }
    }

    private async Task<bool> TryMigrateCloudSystemSwitchAsync(CancellationToken cancellationToken)
    {
        try
        {
            var migrated = await _cloudSystemSwitchMigration
                .MigrateAsync(cancellationToken)
                .ConfigureAwait(false);
            if (migrated)
            {
                _logger.Info("[生命周期] Cloud 系统开关迁移完成。");
            }

            return migrated;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn($"[生命周期] Cloud 系统开关迁移失败，已按非阻断处理：{ex.Message}");
            return false;
        }
    }
}
