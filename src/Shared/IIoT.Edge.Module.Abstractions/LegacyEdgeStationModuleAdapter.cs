using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Plugin.Shared.Modules;
using IIoT.Edge.UI.Shared.Modularity;

namespace IIoT.Edge.Module.Abstractions;

internal sealed class LegacyEdgeStationModuleAdapter : IEdgeProcessModule
{
    private readonly IEdgeStationModule _inner;

    public LegacyEdgeStationModuleAdapter(IEdgeStationModule inner, string displayName)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? inner.ModuleId : displayName;
    }

    public string ModuleId => _inner.ModuleId;

    public string ProcessType => _inner.ProcessType;

    public string DisplayName { get; }

    public void Configure(IEdgeProcessModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _inner.RegisterServices(builder.Services);
        _inner.RegisterCellData(new LegacyCellDataRegistry(builder));
        _inner.RegisterRuntime(new LegacyRuntimeRegistry(builder));
        _inner.RegisterIntegrations(new LegacyIntegrationRegistry(builder, ProcessType));
        _inner.RegisterViews(new LegacyViewRegistry(builder));
    }

    private sealed class LegacyViewRegistry(IEdgeProcessModuleBuilder builder) : IViewRegistry
    {
        private readonly IEdgeProcessModuleBuilder _builder = builder;

        public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
            => _builder.RegisterRoute(viewId, viewType, viewModelType, cacheView);

        public void RegisterMenu(MenuInfo menuInfo)
        {
            ArgumentNullException.ThrowIfNull(menuInfo);
            _builder.RegisterMenu(new EdgeMenuInfo
            {
                Title = menuInfo.Title,
                ViewId = menuInfo.ViewId,
                Icon = menuInfo.Icon,
                Order = menuInfo.Order,
                RequiredPermission = menuInfo.RequiredPermission
            });
        }

        public void RegisterAnchorable(AnchorableInfo info, Type viewType, Type viewModelType, bool cacheView = true)
        {
            ArgumentNullException.ThrowIfNull(info);
            _builder.RegisterAnchorable(
                new EdgeAnchorableInfo
                {
                    Title = info.Title,
                    ContentId = info.ContentId,
                    InitialPosition = info.InitialPosition switch
                    {
                        AnchorablePosition.Left => EdgeAnchorablePosition.Left,
                        AnchorablePosition.Right => EdgeAnchorablePosition.Right,
                        AnchorablePosition.Bottom => EdgeAnchorablePosition.Bottom,
                        _ => EdgeAnchorablePosition.Main
                    },
                    IsVisible = info.IsVisible
                },
                viewType,
                viewModelType,
                cacheView);
        }

        public ViewRegistration? GetViewRegistration(string viewId) => null;

        public IReadOnlyList<MenuInfo> GetAllMenus() => [];

        public IReadOnlyList<AnchorableInfo> GetAllAnchorables() => [];
    }

    private sealed class LegacyCellDataRegistry(IEdgeProcessModuleBuilder builder) : ICellDataRegistry
    {
        private readonly IEdgeProcessModuleBuilder _builder = builder;

        public void Register<TCellData>(string processType) where TCellData : IIoT.Edge.SharedKernel.DataPipeline.CellData.CellDataBase
            => Register(processType, typeof(TCellData));

        public void Register(string processType, Type cellDataType)
        {
            if (!string.Equals(processType, _builder.ProcessType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Legacy module attempted to register CellData for process '{processType}', expected '{_builder.ProcessType}'.");
            }

            _builder.RegisterCellData(cellDataType);
        }

        public bool IsRegistered(string processType)
            => string.Equals(processType, _builder.ProcessType, StringComparison.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, Type> GetRegistrations() => new Dictionary<string, Type>();
    }

    private sealed class LegacyRuntimeRegistry(IEdgeProcessModuleBuilder builder) : IStationRuntimeRegistry
    {
        private readonly IEdgeProcessModuleBuilder _builder = builder;

        public void Register(IStationRuntimeFactory factory)
            => _builder.RegisterRuntimeFactory(factory);

        public bool HasFactory(string moduleId)
            => string.Equals(moduleId, _builder.ModuleId, StringComparison.OrdinalIgnoreCase);

        public bool TryGetFactory(string moduleId, out IStationRuntimeFactory factory)
        {
            factory = default!;
            return false;
        }

        public IReadOnlyDictionary<string, IStationRuntimeFactory> GetRegistrations()
            => new Dictionary<string, IStationRuntimeFactory>();
    }

    private sealed class LegacyIntegrationRegistry(IEdgeProcessModuleBuilder builder, string processType) : IProcessIntegrationRegistry
    {
        private readonly IEdgeProcessModuleBuilder _builder = builder;
        private readonly string _processType = processType;

        public void RegisterCloudUploader(string processType, ProcessUploadMode uploadMode)
        {
            ValidateProcessType(processType);
            _builder.RegisterCloudUploader(uploadMode switch
            {
                ProcessUploadMode.Batch => PluginCloudUploadMode.Batch,
                _ => PluginCloudUploadMode.Single
            });
        }

        public void RegisterMesUploader(string processType, MesUploadMode uploadMode)
        {
            ValidateProcessType(processType);
            _builder.RegisterMesUploader(PluginMesUploadMode.Single);
        }

        public bool HasCloudUploader(string processType)
            => string.Equals(processType, _processType, StringComparison.OrdinalIgnoreCase);

        public bool HasMesUploader(string processType)
            => string.Equals(processType, _processType, StringComparison.OrdinalIgnoreCase);

        public bool TryGetCloudUploader(string processType, out CloudUploaderRegistration registration)
        {
            registration = new CloudUploaderRegistration(processType, ProcessUploadMode.Single);
            return string.Equals(processType, _processType, StringComparison.OrdinalIgnoreCase);
        }

        public bool TryGetMesUploader(string processType, out MesUploaderRegistration registration)
        {
            registration = new MesUploaderRegistration(processType, MesUploadMode.Single);
            return string.Equals(processType, _processType, StringComparison.OrdinalIgnoreCase);
        }

        public IReadOnlyDictionary<string, CloudUploaderRegistration> GetCloudUploaders()
            => new Dictionary<string, CloudUploaderRegistration>();

        public IReadOnlyDictionary<string, MesUploaderRegistration> GetMesUploaders()
            => new Dictionary<string, MesUploaderRegistration>();

        private void ValidateProcessType(string processType)
        {
            if (!string.Equals(processType, _processType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Legacy module attempted to register integration for process '{processType}', expected '{_processType}'.");
            }
        }
    }
}
