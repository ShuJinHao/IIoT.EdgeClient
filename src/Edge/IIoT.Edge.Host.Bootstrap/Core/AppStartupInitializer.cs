using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Module.Contracts.Diagnostics;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Application.Common.Identity;

namespace IIoT.Edge.Shell.Core;

public interface IAppStartupInitializer
{
    Task<IReadOnlyList<StartupDiagnosticIssue>> InitializeAsync(
        CancellationToken cancellationToken = default);
}

public sealed class AppStartupInitializer : IAppStartupInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogService _logger;
    private readonly IDevicePluginDatabaseStartup? _pluginDatabaseStartup;
    private readonly IDevicePluginRuntimeContext? _runtimeContext;

    public AppStartupInitializer(
        IServiceProvider serviceProvider,
        ILogService logger,
        IDevicePluginDatabaseStartup? pluginDatabaseStartup = null,
        IDevicePluginRuntimeContext? runtimeContext = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _pluginDatabaseStartup = pluginDatabaseStartup;
        _runtimeContext = runtimeContext;
    }

    public async Task<IReadOnlyList<StartupDiagnosticIssue>> InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var issues = new List<StartupDiagnosticIssue>();
        var isFormalV3 = _runtimeContext?.Current.IsV3 == true;
        if (!isFormalV3)
        {
            throw new DevicePluginDatabaseStartupException(
                "PLUGIN_DATABASE_V3_REQUIRED");
        }

        if (_pluginDatabaseStartup is null)
        {
            throw new DevicePluginDatabaseStartupException(
                "PLUGIN_DATABASE_STARTUP_PORT_MISSING");
        }

        await _pluginDatabaseStartup
            .InitializeAsync(cancellationToken)
            .ConfigureAwait(false);
        _logger.Info("[生命周期] 插件私有数据库迁移、接管和首次初始化完成。");

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

        await MigrateDataPipelineIdentityAsync(issues, cancellationToken).ConfigureAwait(false);

        _logger.Info("[生命周期] 正式 v3 已由插件私库拥有稳定配置，Host 不执行插件 migration、seed 或补写。");

        return issues;
    }

    private async Task MigrateDataPipelineIdentityAsync(
        List<StartupDiagnosticIssue> issues,
        CancellationToken cancellationToken)
    {
        try
        {
            var migrator = _serviceProvider.GetService(typeof(IDataPipelineIdentityMigration))
                as IDataPipelineIdentityMigration;
            if (migrator is null)
            {
                return;
            }

            var result = await migrator
                .MigrateAsync(cancellationToken)
                .ConfigureAwait(false);
            issues.AddRange(result.Issues.Select(issue =>
                new StartupDiagnosticIssue(
                    "DATA_PIPELINE_PLC_IDENTITY_BLOCKED",
                    $"{issue.DatabaseName}/{issue.TableName}/{issue.RecordId}：{issue.DiagnosticMessage}",
                    DeviceName: issue.DeviceName)
                {
                    PlcCode = string.IsNullOrWhiteSpace(issue.PlcCode) ? null : issue.PlcCode,
                    TaskKey = string.IsNullOrWhiteSpace(issue.TaskKey) ? null : issue.TaskKey
                }));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var message = $"数据管道 PlcCode 历史迁移失败，原记录已保留并停止消费：{ex.Message}";
            issues.Add(StartupDiagnosticIssueFactory.Create(
                "DATA_PIPELINE_PLC_IDENTITY_MIGRATION_FAILED",
                message));
            _logger.Warn($"[生命周期] {message}");
        }
    }

}
