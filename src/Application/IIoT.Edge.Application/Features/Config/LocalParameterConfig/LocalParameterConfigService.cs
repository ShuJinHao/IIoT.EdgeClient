using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Queries;
using IIoT.Edge.Domain.Config.Aggregates;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Application.Features.Config.LocalParameterConfig;

/// <summary>
/// 统一封装本地模块参数读取与变更通知。
/// </summary>
public sealed class LocalParameterConfigService(
    IServiceScopeFactory scopeFactory)
    : ILocalParameterConfigService, ILocalParameterConfigChangePublisher
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged;

    public async Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var result = await sender.Send(new GetAllSystemConfigsQuery(), cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return [];
        }

        return result.Value
            .OrderBy(x => x.SortOrder)
            .Select(MapSystemConfig)
            .ToList();
    }

    public void NotifyModuleChanged()
        => ParameterConfigChanged?.Invoke(
            this,
            new ParameterConfigChangedEventArgs(ParameterConfigChangeScope.Module));

    private static LocalSystemConfigSnapshot MapSystemConfig(SystemConfigEntity entity)
        => new(
            entity.Id,
            entity.Key,
            entity.Value,
            entity.Description,
            entity.SortOrder);
}
