using System.Linq.Expressions;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config.LocalParameterConfig;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Features.Config.ParamView.Models;
using IIoT.Edge.Application.Features.Config.UseCases.DeviceParam.Commands;
using IIoT.Edge.Application.Features.Config.UseCases.DeviceParam.Queries;
using IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Commands;
using IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Queries;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.SharedKernel.Specification;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class LocalParameterConfigBehaviorTests
{
    [Fact]
    public async Task LocalParameterConfigService_WhenSystemConfigsSaved_ShouldInvalidateCacheAndRaiseSystemEvent()
    {
        using var host = new ParameterConfigTestHost(
            systemConfigs:
            [
                CreateSystemConfig(1, SystemConfigKey.心跳间隔, "30")
            ]);
        var events = new List<ParameterConfigChangedEventArgs>();
        host.LocalParameterConfigService.ParameterConfigChanged += (_, args) => events.Add(args);

        await host.LocalParameterConfigService.GetSystemConfigsAsync();
        Assert.True(host.Cache.Contains(ParameterConfigTestHost.SystemCacheKey));

        var handler = new SaveSystemConfigsHandler(host.SystemRepo, host.Cache, host.ChangePublisher);
        var result = await handler.Handle(
            new SaveSystemConfigsCommand(
                [
                    new SystemConfigDto(SystemConfigKey.心跳间隔.ToString(), "45")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(host.Cache.Contains(ParameterConfigTestHost.SystemCacheKey));

        var value = await host.LocalParameterConfigService.GetSystemConfigValueAsync(SystemConfigKey.心跳间隔);
        var snapshots = await host.LocalParameterConfigService.GetSystemConfigsAsync();

        Assert.Equal("45", value);
        Assert.True(host.Cache.Contains(ParameterConfigTestHost.SystemCacheKey));
        Assert.Collection(
            snapshots,
            snapshot =>
            {
                Assert.Equal(SystemConfigKey.心跳间隔.ToString(), snapshot.Key);
                Assert.Equal("45", snapshot.Value);
            });
        Assert.Collection(
            events,
            evt =>
            {
                Assert.Equal(ParameterConfigChangeScope.System, evt.Scope);
                Assert.Null(evt.DeviceId);
            });
    }

    [Fact]
    public async Task LocalParameterConfigService_WhenDeviceParamsSaved_ShouldInvalidateCacheAndRaiseDeviceEvent()
    {
        const int deviceId = 7;

        using var host = new ParameterConfigTestHost(
            deviceParams:
            [
                CreateDeviceParam(1, deviceId, DeviceParamKey.切刀速度, "100")
            ]);
        var events = new List<ParameterConfigChangedEventArgs>();
        host.LocalParameterConfigService.ParameterConfigChanged += (_, args) => events.Add(args);

        await host.LocalParameterConfigService.GetDeviceParamsAsync(deviceId);
        Assert.True(host.Cache.Contains(ParameterConfigTestHost.GetDeviceParamCacheKey(deviceId)));

        var handler = new SaveDeviceParamsHandler(host.DeviceParamRepo, host.Cache, host.ChangePublisher);
        var result = await handler.Handle(
            new SaveDeviceParamsCommand(
                deviceId,
                [
                    new DeviceParamDto(DeviceParamKey.切刀速度.ToString(), "120", "pcs", "0", "200")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(host.Cache.Contains(ParameterConfigTestHost.GetDeviceParamCacheKey(deviceId)));

        var value = await host.LocalParameterConfigService.GetDeviceParamValueAsync(deviceId, DeviceParamKey.切刀速度);
        var snapshots = await host.LocalParameterConfigService.GetDeviceParamsAsync(deviceId);

        Assert.Equal("120", value);
        Assert.True(host.Cache.Contains(ParameterConfigTestHost.GetDeviceParamCacheKey(deviceId)));
        Assert.Collection(
            snapshots,
            snapshot =>
            {
                Assert.Equal(deviceId, snapshot.DeviceId);
                Assert.Equal(DeviceParamKey.切刀速度.ToString(), snapshot.Name);
                Assert.Equal("120", snapshot.Value);
                Assert.Equal("pcs", snapshot.Unit);
            });
        Assert.Collection(
            events,
            evt =>
            {
                Assert.Equal(ParameterConfigChangeScope.Device, evt.Scope);
                Assert.Equal(deviceId, evt.DeviceId);
            });
    }

    [Fact]
    public async Task ParamViewCrudService_WhenSaved_ShouldReloadLatestLocalParameterSnapshots()
    {
        const int deviceId = 9;

        using var host = new ParameterConfigTestHost(
            systemConfigs:
            [
                CreateSystemConfig(1, SystemConfigKey.MES服务地址, "http://old-mes")
            ],
            deviceParams:
            [
                CreateDeviceParam(1, deviceId, DeviceParamKey.切刀速度, "100")
            ],
            networkDevices:
            [
                CreateNetworkDevice(deviceId, "PLC-A")
            ]);
        var service = new ParamViewCrudService(host.Sender, host.PermissionService);

        var initialView = await service.LoadAsync();
        var initialDeviceParams = await service.LoadDeviceParamsAsync(deviceId);

        Assert.Equal("http://old-mes", initialView.GeneralParams.Single().Value);
        Assert.Equal("100", initialDeviceParams.Single().Value);

        var saveResult = await service.SaveAsync(
            [
                new GeneralParamVm
                {
                    Name = SystemConfigKey.MES服务地址.ToString(),
                    Value = "http://new-mes",
                    Description = "MES endpoint"
                }
            ],
            deviceId,
            [
                new DeviceParamVm
                {
                    Name = DeviceParamKey.切刀速度.ToString(),
                    Value = "125",
                    Unit = "pcs",
                    Min = "0",
                    Max = "200"
                }
            ]);

        Assert.True(saveResult.IsSuccess);
        Assert.Equal("已保存到本地参数配置。", saveResult.Message);

        var savedSystemValue = await host.LocalParameterConfigService.GetSystemConfigValueAsync(SystemConfigKey.MES服务地址);
        var reloadedDeviceParams = await service.LoadDeviceParamsAsync(deviceId);

        Assert.Equal("http://new-mes", savedSystemValue);
        Assert.Collection(
            reloadedDeviceParams,
            item =>
            {
                Assert.Equal(DeviceParamKey.切刀速度.ToString(), item.Name);
                Assert.Equal("125", item.Value);
                Assert.Equal("pcs", item.Unit);
                Assert.Equal("0", item.Min);
                Assert.Equal("200", item.Max);
            });
    }

    private static SystemConfigEntity CreateSystemConfig(int id, SystemConfigKey key, string value)
        => new(key.ToString(), value)
        {
            Id = id,
            SortOrder = id
        };

    private static DeviceParamEntity CreateDeviceParam(int id, int deviceId, DeviceParamKey key, string value)
        => new(deviceId, key.ToString(), value, "pcs")
        {
            Id = id,
            MinValue = "0",
            MaxValue = "200",
            SortOrder = id
        };

    private static NetworkDeviceEntity CreateNetworkDevice(int id, string name)
        => new(name, DeviceType.PLC, "192.168.0.10", 102)
        {
            Id = id,
            DeviceModel = "S7",
            ModuleId = "Injection",
            ConnectTimeout = 3000,
            IsEnabled = true
        };

    private sealed class ParameterConfigTestHost : IDisposable
    {
        public const string SystemCacheKey = "Config:SystemAll";

        private readonly ServiceProvider _serviceProvider;

        public ParameterConfigTestHost(
            IEnumerable<SystemConfigEntity>? systemConfigs = null,
            IEnumerable<DeviceParamEntity>? deviceParams = null,
            IEnumerable<NetworkDeviceEntity>? networkDevices = null)
        {
            SystemRepo = new InMemoryRepository<SystemConfigEntity>(systemConfigs?.ToArray() ?? []);
            DeviceParamRepo = new InMemoryRepository<DeviceParamEntity>(deviceParams?.ToArray() ?? []);
            Cache = new TestEdgeCacheService();
            PermissionService = new StubPermissionService { CanEditParams = true };

            var devices = (networkDevices ?? []).ToList();
            var services = new ServiceCollection();
            services.AddSingleton<IRepository<SystemConfigEntity>>(SystemRepo);
            services.AddSingleton<IReadRepository<SystemConfigEntity>>(sp => sp.GetRequiredService<IRepository<SystemConfigEntity>>());
            services.AddSingleton<IRepository<DeviceParamEntity>>(DeviceParamRepo);
            services.AddSingleton<IReadRepository<DeviceParamEntity>>(sp => sp.GetRequiredService<IRepository<DeviceParamEntity>>());
            services.AddSingleton<IEdgeCacheService>(Cache);
            services.AddSingleton<IClientPermissionService>(PermissionService);
            services.AddSingleton<LocalParameterConfigService>();
            services.AddSingleton<ILocalParameterConfigService>(sp => sp.GetRequiredService<LocalParameterConfigService>());
            services.AddSingleton<ILocalParameterConfigChangePublisher>(sp => sp.GetRequiredService<LocalParameterConfigService>());
            services.AddSingleton<ISender>(sp => new ParameterConfigSender(
                sp.GetRequiredService<IRepository<SystemConfigEntity>>(),
                sp.GetRequiredService<IRepository<DeviceParamEntity>>(),
                sp.GetRequiredService<IEdgeCacheService>(),
                sp.GetRequiredService<ILocalParameterConfigService>(),
                sp.GetRequiredService<ILocalParameterConfigChangePublisher>(),
                devices,
                sp.GetRequiredService<IClientPermissionService>()));

            _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            LocalParameterConfigService = _serviceProvider.GetRequiredService<ILocalParameterConfigService>();
            ChangePublisher = _serviceProvider.GetRequiredService<ILocalParameterConfigChangePublisher>();
            Sender = _serviceProvider.GetRequiredService<ISender>();
        }

        public InMemoryRepository<SystemConfigEntity> SystemRepo { get; }

        public InMemoryRepository<DeviceParamEntity> DeviceParamRepo { get; }

        public TestEdgeCacheService Cache { get; }

        public StubPermissionService PermissionService { get; }

        public ILocalParameterConfigService LocalParameterConfigService { get; }

        public ILocalParameterConfigChangePublisher ChangePublisher { get; }

        public ISender Sender { get; }

        public static string GetDeviceParamCacheKey(int deviceId) => $"Config:DeviceParam:{deviceId}";

        public void Dispose() => _serviceProvider.Dispose();
    }

    private sealed class ParameterConfigSender(
        IRepository<SystemConfigEntity> systemRepo,
        IRepository<DeviceParamEntity> deviceParamRepo,
        IEdgeCacheService cache,
        ILocalParameterConfigService localParameterConfigService,
        ILocalParameterConfigChangePublisher changePublisher,
        IReadOnlyList<NetworkDeviceEntity> networkDevices,
        IClientPermissionService permissionService)
        : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            return request switch
            {
                GetAllSystemConfigsQuery query => HandleGetAllSystemConfigs<TResponse>(query, cancellationToken),
                GetSystemConfigValueQuery query => HandleGetSystemConfigValue<TResponse>(query, cancellationToken),
                SaveSystemConfigsCommand command => HandleSaveSystemConfigs<TResponse>(command, cancellationToken),
                GetDeviceParamsQuery query => HandleGetDeviceParams<TResponse>(query, cancellationToken),
                GetDeviceParamValueQuery query => HandleGetDeviceParamValue<TResponse>(query, cancellationToken),
                SaveDeviceParamsCommand command => HandleSaveDeviceParams<TResponse>(command, cancellationToken),
                GetAllNetworkDevicesQuery query => HandleGetAllNetworkDevices<TResponse>(query),
                LoadParamViewQuery query => HandleLoadParamView<TResponse>(query, cancellationToken),
                LoadDeviceParamsQuery query => HandleLoadDeviceParams<TResponse>(query, cancellationToken),
                SaveParamViewCommand command => HandleSaveParamView<TResponse>(command, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().FullName)
            };
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException(request?.GetType().FullName);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().FullName);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().FullName);

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().FullName);

        private async Task<TResponse> HandleGetAllSystemConfigs<TResponse>(GetAllSystemConfigsQuery query, CancellationToken cancellationToken)
            => (TResponse)(object)await new GetAllSystemConfigsHandler(systemRepo, cache)
                .Handle(query, cancellationToken);

        private async Task<TResponse> HandleGetSystemConfigValue<TResponse>(GetSystemConfigValueQuery query, CancellationToken cancellationToken)
            => (TResponse)(object)await new GetSystemConfigValueHandler(this)
                .Handle(query, cancellationToken);

        private async Task<TResponse> HandleSaveSystemConfigs<TResponse>(SaveSystemConfigsCommand command, CancellationToken cancellationToken)
            => (TResponse)(object)await new SaveSystemConfigsHandler(systemRepo, cache, changePublisher)
                .Handle(command, cancellationToken);

        private async Task<TResponse> HandleGetDeviceParams<TResponse>(GetDeviceParamsQuery query, CancellationToken cancellationToken)
            => (TResponse)(object)await new GetDeviceParamsHandler(deviceParamRepo, cache)
                .Handle(query, cancellationToken);

        private async Task<TResponse> HandleGetDeviceParamValue<TResponse>(GetDeviceParamValueQuery query, CancellationToken cancellationToken)
            => (TResponse)(object)await new GetDeviceParamValueHandler(this)
                .Handle(query, cancellationToken);

        private async Task<TResponse> HandleSaveDeviceParams<TResponse>(SaveDeviceParamsCommand command, CancellationToken cancellationToken)
            => (TResponse)(object)await new SaveDeviceParamsHandler(deviceParamRepo, cache, changePublisher)
                .Handle(command, cancellationToken);

        private Task<TResponse> HandleGetAllNetworkDevices<TResponse>(GetAllNetworkDevicesQuery query)
            => Task.FromResult((TResponse)(object)Result.Success(networkDevices.ToList()));

        private async Task<TResponse> HandleLoadParamView<TResponse>(LoadParamViewQuery query, CancellationToken cancellationToken)
            => (TResponse)(object)await new LoadParamViewHandler(this, localParameterConfigService)
                .Handle(query, cancellationToken);

        private async Task<TResponse> HandleLoadDeviceParams<TResponse>(LoadDeviceParamsQuery query, CancellationToken cancellationToken)
            => (TResponse)(object)await new LoadDeviceParamsHandler(localParameterConfigService)
                .Handle(query, cancellationToken);

        private async Task<TResponse> HandleSaveParamView<TResponse>(SaveParamViewCommand command, CancellationToken cancellationToken)
            => (TResponse)(object)await new SaveParamViewHandler(this, permissionService)
                .Handle(command, cancellationToken);
    }

    private sealed class StubPermissionService : IClientPermissionService
    {
        public bool CanEditParams { get; init; }

        public bool CanEditHardware { get; init; }

        public bool IsLocalAdmin { get; init; }

        public event Action? PermissionStateChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission)
            => string.Equals(permission, Permissions.ParamConfig, StringComparison.OrdinalIgnoreCase)
                ? CanEditParams
                : CanEditHardware;
    }

    private sealed class TestEdgeCacheService : IEdgeCacheService
    {
        private readonly Dictionary<string, object> _entries = new(StringComparer.OrdinalIgnoreCase);

        public T? Get<T>(string key)
            => _entries.TryGetValue(key, out var value) && value is T typed
                ? typed
                : default;

        public void Set<T>(string key, T value)
        {
            if (value is not null)
            {
                _entries[key] = value!;
            }
        }

        public void Remove(string key) => _entries.Remove(key);

        public void RemoveByPrefix(string prefix)
        {
            var keys = _entries.Keys
                .Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keys)
            {
                _entries.Remove(key);
            }
        }

        public void Clear() => _entries.Clear();

        public bool Contains(string key) => _entries.ContainsKey(key);
    }

    private sealed class InMemoryRepository<T>(params T[] seedItems) : IRepository<T>
        where T : class, IEntity<int>, IAggregateRoot
    {
        private readonly List<T> _items = [.. seedItems];
        private int _nextId = seedItems.Length == 0 ? 1 : seedItems.Max(x => x.Id) + 1;

        public IQueryable<T> GetQueryable() => _items.AsQueryable();

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
            => Task.FromResult(_items.FirstOrDefault(x => EqualityComparer<TKey>.Default.Equals((TKey)(object)x.Id, id)));

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().FirstOrDefault(expression));

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Where(expression).ToList());

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Where(expression).ToList());

        public Task<List<T>> GetListAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> GetSingleOrDefaultAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> GetCountAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Count(expression));

        public Task<int> CountAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> AnyAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public T Add(T entity)
        {
            if (entity.Id == 0)
            {
                entity.Id = _nextId++;
            }

            _items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
            var index = _items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0)
            {
                _items[index] = entity;
            }
        }

        public void Delete(T entity)
        {
            var index = _items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0)
            {
                _items.RemoveAt(index);
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> ExecuteDeleteAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var toDelete = _items.AsQueryable().Where(predicate).ToList();
            foreach (var item in toDelete)
            {
                _items.Remove(item);
            }

            return Task.FromResult(toDelete.Count);
        }
    }
}
