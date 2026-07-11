using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Features.Config.SchemaReconciliation;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.EfCore;

namespace IIoT.Edge.Shell.Core;

public interface IAppStartupInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _serviceProvider.ApplyMigrations();
        _logger.Info("[生命周期] EF Core 迁移完成。");

        await _serviceProvider.InitializeDapperTablesAsync().ConfigureAwait(false);
        _logger.Info("[生命周期] Dapper 表初始化完成。");

        await RunNonBlockingInitializerStepAsync(
            "开发样例配置初始化",
            () => _developmentSampleInitializer.EnsureConfigurationSamplesAsync(cancellationToken)).ConfigureAwait(false);

        if (await TryMigrateCloudSystemSwitchAsync(cancellationToken).ConfigureAwait(false))
        {
            await RunNonBlockingInitializerStepAsync(
                "配置枚举对账",
                () => _configSchemaReconciler.ReconcileAsync(cancellationToken)).ConfigureAwait(false);
        }
        else
        {
            _logger.Warn("[生命周期] Cloud 系统开关迁移未完成，本次跳过配置枚举对账，保留旧键供下次安全重试。");
        }
    }

    private async Task RunNonBlockingInitializerStepAsync(string stepName, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            _logger.Info($"[生命周期] {stepName}完成。");
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
        catch (Exception ex)
        {
            _logger.Warn($"[生命周期] Cloud 系统开关迁移失败，已按非阻断处理：{ex.Message}");
            return false;
        }
    }
}
