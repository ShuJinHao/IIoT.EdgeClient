using System.Linq.Expressions;
using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Cache;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Features.Config;
using IIoT.Edge.Application.Features.Config.LocalParameterConfig;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.Application.Features.Config.ModuleParameters;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Features.Config.UseCases.ModuleParam;
using IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Commands;
using IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Queries;
using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.SharedKernel.Specification;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class LocalParameterConfigBehaviorTests
{
    private const string ModuleId = "Homogenization";

    [Fact]
    public async Task LocalParameterConfigService_WhenSystemConfigsLoaded_ShouldUseSharedCacheKey()
    {
        var key = ModuleParamKeys.StorageKey(ModuleId, ModuleParamCategory.Mes, "服务地址");
        using var host = new ParameterConfigTestHost(
            systemConfigs:
            [
                CreateSystemConfig(1, key, "http://mes.local")
            ]);

        var first = await host.LocalParameterConfigService.GetSystemConfigsAsync();
        var second = await host.LocalParameterConfigService.GetSystemConfigsAsync();

        Assert.True(host.Cache.Contains(ParameterCacheKeys.SystemAll));
        Assert.Equal(first.Single().Key, second.Single().Key);
        Assert.Equal(first.Single().Value, second.Single().Value);
    }

    [Fact]
    public async Task SaveModuleParamsHandler_WhenSaved_ShouldInvalidateSystemAndModuleCachesAndRaiseModuleEvent()
    {
        var key = ModuleParamKeys.StorageKey(ModuleId, ModuleParamCategory.Mes, "服务地址");
        using var host = new ParameterConfigTestHost(
            systemConfigs:
            [
                CreateSystemConfig(1, key, "http://old-mes")
            ]);
        var events = new List<ParameterConfigChangedEventArgs>();
        host.LocalParameterConfigService.ParameterConfigChanged += (_, args) => events.Add(args);
        host.Cache.Set(ParameterCacheKeys.SystemAll, host.SystemRepo.Items.ToList());
        host.Cache.Set(ParameterCacheKeys.ModuleSnapshot(ModuleId), new ModuleParamValueSnapshot(ModuleId, new Dictionary<string, string>()));

        var handler = new SaveModuleParamsHandler(host.SystemRepo, host.Cache, host.ChangePublisher);
        var result = await handler.Handle(
            new SaveModuleParamsCommand(
            [
                new ModuleParamDto(key, "http://new-mes")
            ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(host.Cache.Contains(ParameterCacheKeys.SystemAll));
        Assert.False(host.Cache.Contains(ParameterCacheKeys.ModuleSnapshot(ModuleId)));
        Assert.Collection(
            host.SystemRepo.Items,
            item =>
            {
                Assert.Equal(key, item.Key);
                Assert.Equal("http://new-mes", item.Value);
            });
        Assert.Collection(
            events,
            evt =>
            {
                Assert.Equal(ParameterConfigChangeScope.Module, evt.Scope);
            });
    }

    [Fact]
    public async Task ParamViewCrudService_WhenSaved_ShouldOnlyPersistModuleParameters()
    {
        var registry = new ModuleParamRegistry();
        registry.Register(
            ModuleId,
            typeof(TestMesParams),
            typeof(TestCloudParams),
            typeof(TestBusinessParams));
        var moduleKey = ModuleParamKeys.StorageKey(ModuleId, ModuleParamCategory.Business, "启用托盘码重码验证");
        using var host = new ParameterConfigTestHost(
            systemConfigs:
            [
                CreateSystemConfig(1, moduleKey, "false")
            ],
            moduleParamRegistry: registry);
        var service = new ParamViewCrudService(host.Sender, host.PermissionService);

        var saveResult = await service.SaveAsync(
            [
                new ParamViewValueDto(moduleKey, "true")
            ]);

        Assert.True(saveResult.IsSuccess);
        var savedSystemValue = (await host.LocalParameterConfigService.GetSystemConfigsAsync())
            .Single(x => x.Key == moduleKey)
            .Value;
        Assert.Equal("true", savedSystemValue);
    }

    [Fact]
    public async Task ParamViewCrudService_WhenHiddenMesSignTokenSubmitted_ShouldReject()
    {
        var registry = new ModuleParamRegistry();
        registry.Register(
            ModuleId,
            typeof(SensitiveMesParams),
            typeof(TestCloudParams),
            typeof(TestBusinessParams));
        var tokenKey = ModuleParamKeys.StorageKey(ModuleId, ModuleParamCategory.Mes, nameof(SensitiveMesParams.签名令牌));
        using var host = new ParameterConfigTestHost(moduleParamRegistry: registry);
        var service = new ParamViewCrudService(host.Sender, host.PermissionService);

        var saveResult = await service.SaveAsync(
            [
                new ParamViewValueDto(tokenKey, "secret")
            ]);

        Assert.False(saveResult.IsSuccess);
        Assert.Contains(tokenKey, saveResult.Message);
        Assert.Empty(await host.LocalParameterConfigService.GetSystemConfigsAsync());
    }

    [Fact]
    public async Task ParamViewCrudService_LoadAsync_ShouldHideSensitiveFieldsAndPutSystemCloudEnableFirst()
    {
        var registry = new ModuleParamRegistry();
        registry.Register(
            ModuleId,
            typeof(SensitiveMesParams),
            typeof(TestCloudParams),
            typeof(TestBusinessParams));
        using var host = new ParameterConfigTestHost(moduleParamRegistry: registry);
        var service = new ParamViewCrudService(host.Sender, host.PermissionService);

        var result = await service.LoadAsync();

        Assert.DoesNotContain(
            result.MesParamGroups.SelectMany(static group => group.Params),
            param => param.Key.EndsWith(":签名令牌", StringComparison.OrdinalIgnoreCase));
        var cloudParams = Assert.Single(result.CloudParamGroups).Params;
        Assert.Equal(
            CloudApiConfigParamSchema.Enabled,
            cloudParams[0].Key);
        Assert.DoesNotContain(
            cloudParams,
            param => param.Key.EndsWith(":Cloud:启用", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cloudParams, param => param.Key == CloudApiConfigParamSchema.BaseUrl);
        Assert.Contains(cloudParams, param => param.Key == CloudApiConfigParamSchema.ProcessUploadPath);
        Assert.Contains(cloudParams, param => param.Key == CloudApiConfigParamSchema.PassStationBatchTemplatePath);
        Assert.Contains(cloudParams, param => param.Key == CloudApiConfigParamSchema.EdgeHostPlcRuntimeStatesPath);
        Assert.DoesNotContain(cloudParams, param => param.Key == CloudApiConfigParamSchema.ClientCode);
        Assert.DoesNotContain(cloudParams, param => param.Key == CloudApiConfigParamSchema.BootstrapSecret);
    }

    [Fact]
    public async Task ParamViewCrudService_WhenCloudApiConfigSaved_ShouldOnlyPersistEditableCloudApiKeys()
    {
        using var host = new ParameterConfigTestHost();
        var service = new ParamViewCrudService(host.Sender, host.PermissionService);

        var saveResult = await service.SaveAsync(
            [
                new ParamViewValueDto(CloudApiConfigParamSchema.BaseUrl, "https://cloud.local"),
                new ParamViewValueDto(CloudApiConfigParamSchema.ProcessUploadPath, "/edge/process")
            ]);

        Assert.True(saveResult.IsSuccess, saveResult.Message);
        var values = (await host.LocalParameterConfigService.GetSystemConfigsAsync())
            .ToDictionary(static x => x.Key, static x => x.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("https://cloud.local", values[CloudApiConfigParamSchema.BaseUrl]);
        Assert.Equal("/edge/process", values[CloudApiConfigParamSchema.ProcessUploadPath]);
    }

    [Fact]
    public async Task ParamViewCrudService_WhenSensitiveCloudApiKeySubmitted_ShouldReject()
    {
        using var host = new ParameterConfigTestHost();
        var service = new ParamViewCrudService(host.Sender, host.PermissionService);

        var saveResult = await service.SaveAsync(
            [
                new ParamViewValueDto(CloudApiConfigParamSchema.BootstrapSecret, "secret"),
                new ParamViewValueDto(CloudApiConfigParamSchema.ProcessUploadPath, "/edge/process")
            ]);

        Assert.False(saveResult.IsSuccess);
        Assert.Contains(CloudApiConfigParamSchema.BootstrapSecret, saveResult.Message);
        Assert.Empty(await host.LocalParameterConfigService.GetSystemConfigsAsync());
    }

    [Fact]
    public async Task ParamViewCrudService_WhenReset_ShouldOverwriteModuleParametersWithEnumDefaults()
    {
        var registry = new ModuleParamRegistry();
        registry.Register(
            ModuleId,
            typeof(TestMesParams),
            typeof(TestCloudParams),
            typeof(TestBusinessParams));
        var cloudKey = ModuleParamKeys.StorageKey(ModuleId, ModuleParamCategory.Cloud, nameof(TestCloudParams.服务地址));
        var businessKey = ModuleParamKeys.StorageKey(ModuleId, ModuleParamCategory.Business, nameof(TestBusinessParams.启用托盘码重码验证));
        using var host = new ParameterConfigTestHost(
            systemConfigs:
            [
                CreateSystemConfig(1, cloudKey, "http://custom.local"),
                CreateSystemConfig(2, businessKey, "false")
            ],
            moduleParamRegistry: registry);
        var service = new ParamViewCrudService(host.Sender, host.PermissionService);

        var resetResult = await service.ResetAsync();

        Assert.True(resetResult.IsSuccess, resetResult.Message);
        var values = (await host.LocalParameterConfigService.GetSystemConfigsAsync())
            .ToDictionary(static x => x.Key, static x => x.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("http://localhost:5180", values[cloudKey]);
        Assert.Equal("true", values[businessKey]);
        Assert.Equal("https://config-cloud.test", values[CloudApiConfigParamSchema.BaseUrl]);
    }

    [Fact]
    public async Task LocalParameterConfigService_WhenInsertedOrDeleted_ShouldInvalidateCachesAndRaiseModuleEvent()
    {
        var key = ModuleParamKeys.StorageKey(ModuleId, ModuleParamCategory.Cloud, "服务地址");
        using var host = new ParameterConfigTestHost();
        var events = new List<ParameterConfigChangedEventArgs>();
        host.LocalParameterConfigService.ParameterConfigChanged += (_, args) => events.Add(args);
        host.Cache.Set(ParameterCacheKeys.SystemAll, new List<SystemConfigEntity>());
        host.Cache.Set(ParameterCacheKeys.ModuleSnapshot(ModuleId), new ModuleParamValueSnapshot(ModuleId, new Dictionary<string, string>()));

        await host.LocalParameterConfigService.InsertSystemConfigAsync(
            key,
            "http://cloud.local",
            "Cloud URL",
            sortOrder: 7,
            TestContext.Current.CancellationToken);

        var inserted = Assert.Single(host.SystemRepo.Items);
        Assert.Equal(key, inserted.Key);
        Assert.Equal("http://cloud.local", inserted.Value);
        Assert.Equal("Cloud URL", inserted.Description);
        Assert.Equal(7, inserted.SortOrder);
        Assert.False(host.Cache.Contains(ParameterCacheKeys.SystemAll));
        Assert.False(host.Cache.Contains(ParameterCacheKeys.ModuleSnapshot(ModuleId)));
        Assert.Single(events);

        host.Cache.Set(ParameterCacheKeys.SystemAll, host.SystemRepo.Items.ToList());
        host.Cache.Set(ParameterCacheKeys.ModuleSnapshot(ModuleId), new ModuleParamValueSnapshot(ModuleId, new Dictionary<string, string>()));

        await host.LocalParameterConfigService.DeleteSystemConfigAsync(
            key,
            TestContext.Current.CancellationToken);

        Assert.Empty(host.SystemRepo.Items);
        Assert.False(host.Cache.Contains(ParameterCacheKeys.SystemAll));
        Assert.False(host.Cache.Contains(ParameterCacheKeys.ModuleSnapshot(ModuleId)));
        Assert.Equal(2, events.Count);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, true, false)]
    public async Task CloudSystemSwitchMigration_ShouldUseSystemAndAllLegacySwitchesFailClosed(
        bool systemEnabled,
        bool hasLegacySwitches,
        bool allLegacyEnabled,
        bool expectedEnabled)
    {
        var configs = new List<SystemConfigEntity>
        {
            CreateSystemConfig(1, CloudApiConfigParamSchema.Enabled, systemEnabled.ToString())
        };
        if (hasLegacySwitches)
        {
            configs.Add(CreateSystemConfig(2, "Module:Homogenization:Cloud:启用", allLegacyEnabled.ToString()));
            configs.Add(CreateSystemConfig(3, "Module:DieCuttingAnode:Cloud:启用", "true"));
        }

        var repository = new InMemoryRepository<SystemConfigEntity>([.. configs]);
        var projection = new RecordingCloudProfileSwitchProjectionWriter();
        var migration = new CloudSystemSwitchMigration(repository, new TestEdgeCacheService(), projection);

        var result = await migration.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal([expectedEnabled], projection.Values);
        Assert.Equal(
            expectedEnabled.ToString(),
            repository.Items.Single(config => config.Key == CloudApiConfigParamSchema.Enabled).Value,
            ignoreCase: true);
        Assert.Single(repository.Items, config => config.Key == CloudSystemSwitchMigration.MigrationMarkerKey);
    }

    [Fact]
    public async Task CloudSystemSwitchMigration_WhenMarkerExists_ShouldOnlyRefreshLauncherProjection()
    {
        var repository = new InMemoryRepository<SystemConfigEntity>(
            CreateSystemConfig(1, CloudApiConfigParamSchema.Enabled, "false"),
            CreateSystemConfig(2, CloudSystemSwitchMigration.MigrationMarkerKey, "true"));
        var projection = new RecordingCloudProfileSwitchProjectionWriter();
        var migration = new CloudSystemSwitchMigration(repository, new TestEdgeCacheService(), projection);

        var result = await migration.MigrateAsync(TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal([false], projection.Values);
        Assert.Equal(2, repository.Items.Count);
    }

    [Fact]
    public async Task SaveCloudApiConfigParamsHandler_WhenEnabled_ShouldRefreshRuntimeAndWriteProjection()
    {
        using var host = new ParameterConfigTestHost();
        var runtime = new RecordingRuntimeConfigService();
        var projection = new RecordingCloudProfileSwitchProjectionWriter();
        var handler = new SaveCloudApiConfigParamsHandler(
            host.SystemRepo,
            host.Cache,
            host.ChangePublisher,
            runtime,
            projection);

        var result = await handler.Handle(
            new SaveCloudApiConfigParamsCommand(
            [
                new CloudApiConfigParamDto(CloudApiConfigParamSchema.Enabled, "true")
            ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal([true], projection.Values);
        Assert.Equal(1, runtime.RefreshCount);
        Assert.Equal(
            "true",
            host.SystemRepo.Items.Single(config => config.Key == CloudApiConfigParamSchema.Enabled).Value,
            ignoreCase: true);
    }

    [Fact]
    public async Task SaveCloudApiConfigParamsHandler_WhenEnableProjectionFails_ShouldRollBackSystemSwitch()
    {
        using var host = new ParameterConfigTestHost();
        var runtime = new RecordingRuntimeConfigService();
        var projection = new RecordingCloudProfileSwitchProjectionWriter { ThrowWhenEnabling = true };
        var handler = new SaveCloudApiConfigParamsHandler(
            host.SystemRepo,
            host.Cache,
            host.ChangePublisher,
            runtime,
            projection);

        var result = await handler.Handle(
            new SaveCloudApiConfigParamsCommand(
            [
                new CloudApiConfigParamDto(CloudApiConfigParamSchema.Enabled, "true")
            ]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("已回滚为关闭", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal([true], projection.Values);
        Assert.Equal(2, runtime.RefreshCount);
        Assert.Equal(
            "false",
            host.SystemRepo.Items.Single(config => config.Key == CloudApiConfigParamSchema.Enabled).Value,
            ignoreCase: true);
    }

    private static SystemConfigEntity CreateSystemConfig(int id, string key, string value)
    {
        var entity = SystemConfigEntity.Create(key, value);
        entity.WithId(id);
        entity.UpdateSortOrder(id);
        return entity;
    }

    private sealed class ParameterConfigTestHost : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        public ParameterConfigTestHost(
            IEnumerable<SystemConfigEntity>? systemConfigs = null,
            IModuleParamRegistry? moduleParamRegistry = null)
        {
            SystemRepo = new InMemoryRepository<SystemConfigEntity>(systemConfigs?.ToArray() ?? []);
            Cache = new TestEdgeCacheService();
            PermissionService = new StubPermissionService { CanEditParams = true };

            var services = new ServiceCollection();
            services.AddSingleton<IRepository<SystemConfigEntity>>(SystemRepo);
            services.AddSingleton<IReadRepository<SystemConfigEntity>>(sp => sp.GetRequiredService<IRepository<SystemConfigEntity>>());
            services.AddSingleton<IEdgeCacheService>(Cache);
            services.AddSingleton<IClientPermissionService>(PermissionService);
            services.AddSingleton<LocalParameterConfigService>();
            services.AddSingleton<ILocalParameterConfigService>(sp => sp.GetRequiredService<LocalParameterConfigService>());
            services.AddSingleton<ILocalParameterConfigChangePublisher>(sp => sp.GetRequiredService<LocalParameterConfigService>());
            services.AddSingleton<ISender>(sp => new ParameterConfigSender(
                sp.GetRequiredService<IRepository<SystemConfigEntity>>(),
                sp.GetRequiredService<IEdgeCacheService>(),
                sp.GetRequiredService<ILocalParameterConfigService>(),
                sp.GetRequiredService<ILocalParameterConfigChangePublisher>(),
                sp.GetRequiredService<IClientPermissionService>(),
                moduleParamRegistry ?? new ModuleParamRegistry()));

            _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true
            });

            LocalParameterConfigService = _serviceProvider.GetRequiredService<ILocalParameterConfigService>();
            ChangePublisher = _serviceProvider.GetRequiredService<ILocalParameterConfigChangePublisher>();
            Sender = _serviceProvider.GetRequiredService<ISender>();
        }

        public InMemoryRepository<SystemConfigEntity> SystemRepo { get; }

        public TestEdgeCacheService Cache { get; }

        public StubPermissionService PermissionService { get; }

        public ILocalParameterConfigService LocalParameterConfigService { get; }

        public ILocalParameterConfigChangePublisher ChangePublisher { get; }

        public ISender Sender { get; }

        public void Dispose() => _serviceProvider.Dispose();
    }

    private sealed class ParameterConfigSender(
        IRepository<SystemConfigEntity> systemRepo,
        IEdgeCacheService cache,
        ILocalParameterConfigService localParameterConfigService,
        ILocalParameterConfigChangePublisher changePublisher,
        IClientPermissionService permissionService,
        IModuleParamRegistry moduleParamRegistry)
        : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            return request switch
            {
                GetAllSystemConfigsQuery query => HandleGetAllSystemConfigs<TResponse>(query, cancellationToken),
                SaveModuleParamsCommand command => HandleSaveModuleParams<TResponse>(command, cancellationToken),
                SaveCloudApiConfigParamsCommand command => HandleSaveCloudApiConfigParams<TResponse>(command, cancellationToken),
                LoadParamViewQuery query => HandleLoadParamView<TResponse>(query, cancellationToken),
                SaveParamViewCommand command => HandleSaveParamView<TResponse>(command, cancellationToken),
                ResetParamViewCommand command => HandleResetParamView<TResponse>(command, cancellationToken),
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

        private async Task<TResponse> HandleSaveModuleParams<TResponse>(SaveModuleParamsCommand command, CancellationToken cancellationToken)
            => (TResponse)(object)await new SaveModuleParamsHandler(systemRepo, cache, changePublisher)
                .Handle(command, cancellationToken);

        private async Task<TResponse> HandleLoadParamView<TResponse>(LoadParamViewQuery query, CancellationToken cancellationToken)
            => (TResponse)(object)await new LoadParamViewHandler(
                    localParameterConfigService,
                    moduleParamRegistry,
                    [],
                    new StubCloudApiConfigSnapshotProvider())
                .Handle(query, cancellationToken);

        private async Task<TResponse> HandleSaveParamView<TResponse>(SaveParamViewCommand command, CancellationToken cancellationToken)
            => (TResponse)(object)await new SaveParamViewHandler(this, permissionService, moduleParamRegistry)
                .Handle(command, cancellationToken);

        private async Task<TResponse> HandleResetParamView<TResponse>(ResetParamViewCommand command, CancellationToken cancellationToken)
            => (TResponse)(object)await new ResetParamViewHandler(
                    this,
                    permissionService,
                    moduleParamRegistry,
                    new StubCloudApiConfigSnapshotProvider())
                .Handle(command, cancellationToken);

        private async Task<TResponse> HandleSaveCloudApiConfigParams<TResponse>(
            SaveCloudApiConfigParamsCommand command,
            CancellationToken cancellationToken)
            => (TResponse)(object)await new SaveCloudApiConfigParamsHandler(
                    systemRepo,
                    cache,
                    changePublisher,
                    new FakeLocalSystemRuntimeConfigService(),
                    new StubCloudProfileSwitchProjectionWriter())
                .Handle(command, cancellationToken);
    }

    private sealed class StubCloudApiConfigSnapshotProvider : ICloudApiConfigSnapshotProvider
    {
        public CloudApiConfigSnapshot GetCurrent()
            => new(
                "https://config-cloud.test",
                "CONFIG-CLIENT",
                "secret",
                "/config/device-instance",
                "/config/bootstrap-refresh",
                "/config/login",
                "/config/human-refresh",
                "/config/logs",
                "/config/process",
                "/config/pass-stations/{typeKey}/batch",
                "/config/capacity-hourly",
                "/config/capacity-summary",
                "/config/capacity-range",
                "/config/recipes/{deviceId}",
                "/config/client-releases/device/{deviceId}/catalog",
                "/config/client-version-reports");
    }

    private enum TestMesParams
    {
    }

    private enum SensitiveMesParams
    {
        [ModuleParam(ParamValueKind.String, Role = ModuleParamRole.MesSignToken)]
        签名令牌
    }

    private enum TestCloudParams
    {
        [ModuleParam(ParamValueKind.String, DefaultValue = "http://localhost:5180")]
        服务地址
    }

    private sealed class StubCloudProfileSwitchProjectionWriter : ICloudProfileSwitchProjectionWriter
    {
        public Task WriteAsync(bool enabled, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingCloudProfileSwitchProjectionWriter : ICloudProfileSwitchProjectionWriter
    {
        public List<bool> Values { get; } = [];

        public bool ThrowWhenEnabling { get; init; }

        public Task WriteAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            Values.Add(enabled);
            if (enabled && ThrowWhenEnabling)
            {
                throw new IOException("projection unavailable");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRuntimeConfigService : ILocalSystemRuntimeConfigService
    {
        public SystemRuntimeConfigSnapshot Current { get; } = SystemRuntimeConfigSnapshot.Default;

        public int RefreshCount { get; private set; }

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return Task.CompletedTask;
        }
    }

    private enum TestBusinessParams
    {
        [ModuleParam(ParamValueKind.Bool, DefaultValue = "true")]
        启用托盘码重码验证
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

    private sealed class InMemoryRepository<T>(params T[] seedItems) : IRepository<T>
        where T : class, IEntity<int>, IAggregateRoot
    {
        private readonly List<T> _items = [.. seedItems];
        private int _nextId = seedItems.Length == 0 ? 1 : seedItems.Max(x => x.Id) + 1;

        public IReadOnlyList<T> Items => _items;

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
                EntityIdTestHelper.SetId(entity, _nextId++);
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

    private sealed class TestEdgeCacheService : IEdgeCacheService
    {
        private readonly Dictionary<string, object?> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

        public T? Get<T>(string key)
            => _entries.TryGetValue(key, out var value) && value is T typed
                ? typed
                : default;

        public void Set<T>(string key, T value)
        {
            _entries[key] = value;
        }

        public void Remove(string key) => _entries.Remove(key);

        public void RemoveByPrefix(string prefix)
        {
            foreach (var key in _entries.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                _entries.Remove(key);
            }
        }

        public void Clear() => _entries.Clear();

        public bool Contains(string key) => _entries.ContainsKey(key);

        public async Task<T?> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T?>> factory,
            TimeSpan? absoluteExpirationRelativeToNow = null,
            TimeSpan? nullValueExpirationRelativeToNow = null,
            CancellationToken cancellationToken = default)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                return cached is T typed ? typed : default;
            }

            var gate = GetLock(key);
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (_entries.TryGetValue(key, out cached))
                {
                    return cached is T typed ? typed : default;
                }

                var value = await factory(cancellationToken);
                _entries[key] = value;
                return value;
            }
            finally
            {
                gate.Release();
            }
        }

        private SemaphoreSlim GetLock(string key)
        {
            lock (_locks)
            {
                if (!_locks.TryGetValue(key, out var gate))
                {
                    gate = new SemaphoreSlim(1, 1);
                    _locks[key] = gate;
                }

                return gate;
            }
        }
    }
}
