using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Infrastructure.Persistence.Dapper;
using IIoT.Edge.Infrastructure.Persistence.EfCore;

namespace IIoT.Edge.Host.Bootstrap;

public interface IAppStartupInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public sealed class AppStartupInitializer : IAppStartupInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDevelopmentSampleInitializer _developmentSampleInitializer;
    private readonly ILogService _logger;

    public AppStartupInitializer(
        IServiceProvider serviceProvider,
        IDevelopmentSampleInitializer developmentSampleInitializer,
        ILogService logger)
    {
        _serviceProvider = serviceProvider;
        _developmentSampleInitializer = developmentSampleInitializer;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _serviceProvider.ApplyMigrations();
        _logger.Info("[生命周期] EF Core 迁移完成。");

        await _serviceProvider.InitializeDapperTablesAsync().ConfigureAwait(false);
        _logger.Info("[生命周期] Dapper 表初始化完成。");

        await _developmentSampleInitializer.EnsureConfigurationSamplesAsync(cancellationToken).ConfigureAwait(false);
        _logger.Info("[生命周期] 开发样例配置初始化完成。");
    }
}
