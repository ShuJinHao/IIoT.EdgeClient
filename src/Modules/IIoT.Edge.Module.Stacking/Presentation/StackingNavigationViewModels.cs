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

namespace IIoT.Edge.Module.Stacking.Presentation;

public sealed class StackingDataViewModel : DataViewModel
{
    public StackingDataViewModel(IDataViewService dataViewService)
        : base(dataViewService, StackingViewIds.DataView, "鐢熶骇鏁版嵁")
    {
    }
}

public sealed class StackingCapacityViewModel : CapacityViewModel
{
    public StackingCapacityViewModel(ICapacityViewService capacityViewService)
        : base(capacityViewService, StackingViewIds.CapacityView, "浜ц兘鏌ヨ")
    {
    }
}

public sealed class StackingMonitorViewModel : MonitorViewModel
{
    public StackingMonitorViewModel(IMonitorViewService monitorViewService)
        : base(monitorViewService, StackingViewIds.Monitor, "瀹炴椂鐩戞帶")
    {
    }
}

public sealed class StackingIoViewModel : IoViewViewModel
{
    public StackingIoViewModel(
        IPlcDataStore dataStore,
        IPlcConnectionManager plcConnectionManager,
        ISender sender)
        : base(dataStore, plcConnectionManager, sender, StackingViewIds.IoView, "IO 浜や簰")
    {
    }
}

public sealed class StackingRecipeViewModel : RecipeViewModel
{
    public StackingRecipeViewModel(IRecipeViewCrudService crudService, IRecipeService recipeService)
        : base(crudService, recipeService, StackingViewIds.RecipeView, "浜у搧閰嶆柟")
    {
    }
}

public sealed class StackingParamViewModel : ParamViewModel
{
    public StackingParamViewModel(
        IParamViewCrudService crudService,
        IClientPermissionService permissionService)
        : base(crudService, permissionService, StackingViewIds.ParamView, "鍙傛暟閰嶇疆")
    {
    }
}

public sealed class StackingHardwareConfigViewModel : HardwareConfigViewModel
{
    public StackingHardwareConfigViewModel(
        IHardwareConfigCrudService crudService,
        IClientPermissionService permissionService)
        : base(crudService, permissionService, StackingViewIds.HardwareConfigView, "纭欢閰嶇疆")
    {
    }
}
