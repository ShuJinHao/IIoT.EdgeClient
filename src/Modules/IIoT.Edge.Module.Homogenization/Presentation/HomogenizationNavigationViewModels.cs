using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Features.Formula.RecipeView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Production.CapacityView;
using IIoT.Edge.Application.Features.Production.DataView;
using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Presentation.Navigation.Features.Config.ParamView;
using IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;
using IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;
using IIoT.Edge.Presentation.Navigation.Features.Production.DataView;
using IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;
using MediatR;

namespace IIoT.Edge.Module.Homogenization.Presentation;

public sealed class HomogenizationDataViewModel : DataViewModel
{
    public HomogenizationDataViewModel(IDataViewService dataViewService)
        : base(dataViewService, HomogenizationViewIds.DataView, "鐢熶骇鏁版嵁")
    {
    }
}

public sealed class HomogenizationCapacityViewModel : CapacityViewModel
{
    public HomogenizationCapacityViewModel(ICapacityViewService capacityViewService)
        : base(capacityViewService, HomogenizationViewIds.CapacityView, "浜ц兘鏌ヨ")
    {
    }
}

public sealed class HomogenizationMonitorViewModel : MonitorViewModel
{
    public HomogenizationMonitorViewModel(IMonitorViewService monitorViewService)
        : base(monitorViewService, HomogenizationViewIds.Monitor, "瀹炴椂鐩戞帶")
    {
    }
}

public sealed class HomogenizationIoViewModel : IoViewViewModel
{
    public HomogenizationIoViewModel(
        IPlcDataStore dataStore,
        IPlcConnectionManager plcConnectionManager,
        ISender sender)
        : base(dataStore, plcConnectionManager, sender, HomogenizationViewIds.IoView, "IO浜や簰")
    {
    }
}

public sealed class HomogenizationRecipeViewModel : RecipeViewModel
{
    public HomogenizationRecipeViewModel(IRecipeViewCrudService crudService, IRecipeService recipeService)
        : base(crudService, recipeService, HomogenizationViewIds.RecipeView, "浜у搧閰嶆柟")
    {
    }
}

public sealed class HomogenizationParamViewModel : ParamViewModel
{
    public HomogenizationParamViewModel(
        IParamViewCrudService crudService,
        IClientPermissionService permissionService)
        : base(crudService, permissionService, HomogenizationViewIds.ParamView, "鍙傛暟閰嶇疆")
    {
    }
}

public sealed class HomogenizationHardwareConfigViewModel : HardwareConfigViewModel
{
    public HomogenizationHardwareConfigViewModel(
        IHardwareConfigCrudService crudService,
        IClientPermissionService permissionService)
        : base(crudService, permissionService, HomogenizationViewIds.HardwareConfigView, "纭欢閰嶇疆")
    {
    }
}
