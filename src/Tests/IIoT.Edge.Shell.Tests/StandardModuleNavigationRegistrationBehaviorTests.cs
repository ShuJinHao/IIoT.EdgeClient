using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Descriptors;
using IIoT.Edge.Presentation.Navigation.PluginSystem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class StandardModuleNavigationRegistrationBehaviorTests
{
    [Fact]
    public void RegisterStandardModuleViews_WhenRecipeUnsupported_ShouldSkipRecipeRouteAndMenu()
    {
        var builder = new FakeProcessModuleBuilder("NoRecipe");

        builder.RegisterStandardModuleViews(
            "NoRecipe",
            "数据",
            "Navigation_Menu_Data",
            supportsRecipe: false);

        Assert.DoesNotContain(builder.RouteIds, viewId => viewId == "NoRecipe.RecipeView");
        Assert.DoesNotContain(builder.MenuIds, viewId => viewId == "NoRecipe.RecipeView");
        Assert.Contains("NoRecipe.ParamView", builder.RouteIds);
        Assert.Contains("NoRecipe.HardwareConfigView", builder.RouteIds);
    }

    [Fact]
    public void RegisterStandardModuleViews_WhenRecipeSupportedByDefault_ShouldRegisterRecipeRouteAndMenu()
    {
        var builder = new FakeProcessModuleBuilder("RecipeModule");

        builder.RegisterStandardModuleViews(
            "RecipeModule",
            "数据",
            "Navigation_Menu_Data");

        Assert.Contains("RecipeModule.RecipeView", builder.RouteIds);
        Assert.Contains("RecipeModule.RecipeView", builder.MenuIds);
    }

    private sealed class FakeProcessModuleBuilder(string moduleId) : IEdgeProcessModuleBuilder
    {
        public string ModuleId { get; } = moduleId;

        public string ProcessType => ModuleId;

        public IServiceCollection Services { get; } = new ServiceCollection();

        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();

        public List<string> RouteIds { get; } = [];

        public List<string> MenuIds { get; } = [];

        public void RegisterRoute(string viewId, Type viewType, Type viewModelType, bool cacheView = true)
            => RouteIds.Add(viewId);

        public void RegisterRoute(
            string viewId,
            Type viewType,
            Type viewModelType,
            Func<IServiceProvider, object> viewModelFactory,
            bool cacheView = true)
            => RouteIds.Add(viewId);

        public void RegisterMenu(ModuleMenuDescriptor menuInfo) => MenuIds.Add(menuInfo.ViewId);

        public void RegisterDocumentPanel(
            ModulePanelDescriptor descriptor,
            Type viewType,
            Type viewModelType,
            bool cacheView = true)
        {
        }

        public void RegisterDocumentPanel(
            ModulePanelDescriptor descriptor,
            Type viewType,
            Type viewModelType,
            Func<IServiceProvider, object> viewModelFactory,
            bool cacheView = true)
        {
        }

        public void RegisterToolPanel(
            ModulePanelDescriptor descriptor,
            Type viewType,
            Type viewModelType,
            bool cacheView = true)
        {
        }

        public void RegisterToolPanel(
            ModulePanelDescriptor descriptor,
            Type viewType,
            Type viewModelType,
            Func<IServiceProvider, object> viewModelFactory,
            bool cacheView = true)
        {
        }

        public void RegisterCellData(Type cellDataType)
        {
        }

        public void RegisterRuntimeFactory(object runtimeFactory)
        {
        }

        public void RegisterCloudUploader(ProcessUploadMode uploadMode)
        {
        }

        public void RegisterMesUploader(MesUploadMode uploadMode)
        {
        }

        public void RegisterPlcSignalProfile<TSignalKey, TProfile>()
            where TSignalKey : struct, Enum
            where TProfile : class, IModulePlcSignalProfile<TSignalKey>
        {
        }

        public void RegisterStandardPlcSignalProfiles<TInteraction, TSingleRead, TContinuousRead, TSingleWrite, TContinuousWrite>()
            where TInteraction : struct, Enum
            where TSingleRead : struct, Enum
            where TContinuousRead : struct, Enum
            where TSingleWrite : struct, Enum
            where TContinuousWrite : struct, Enum
        {
        }

        public void RegisterHardwareProfile<TProvider>()
            where TProvider : class, IModuleHardwareProfileProvider
        {
        }

        public void RegisterDevelopmentSample<TContributor>()
            where TContributor : class, IDevelopmentSampleContributor
        {
        }

        public void RegisterParameters<TMes, TCloud, TBusiness>()
            where TMes : struct, Enum
            where TCloud : struct, Enum
            where TBusiness : struct, Enum
        {
        }
    }
}
