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

namespace IIoT.Edge.Module.Injection.Presentation;

public sealed class InjectionDataViewModel : DataViewModel
{
    public InjectionDataViewModel(IDataViewService dataViewService)
        : base(dataViewService, InjectionViewIds.DataView, "鐢熶骇鏁版嵁")
    {
    }
}

public sealed class InjectionCapacityViewModel : CapacityViewModel
{
    public InjectionCapacityViewModel(ICapacityViewService capacityViewService)
        : base(capacityViewService, InjectionViewIds.CapacityView, "浜ц兘鏌ヨ")
    {
    }
}

public sealed class InjectionMonitorViewModel : MonitorViewModel
{
    public InjectionMonitorViewModel(IMonitorViewService monitorViewService)
        : base(monitorViewService, InjectionViewIds.Monitor, "瀹炴椂鐩戞帶")
    {
    }
}

public sealed class InjectionIoViewModel : IoViewViewModel
{
    public InjectionIoViewModel(
        IPlcDataStore dataStore,
        IPlcConnectionManager plcConnectionManager,
        ISender sender)
        : base(dataStore, plcConnectionManager, sender, InjectionViewIds.IoView, "IO浜や簰")
    {
    }
}

public sealed class InjectionRecipeViewModel : RecipeViewModel
{
    public InjectionRecipeViewModel(IRecipeViewCrudService crudService, IRecipeService recipeService)
        : base(crudService, recipeService, InjectionViewIds.RecipeView, "浜у搧閰嶆柟")
    {
    }
}

public sealed class InjectionParamViewModel : ParamViewModel
{
    public InjectionParamViewModel(
        IParamViewCrudService crudService,
        IClientPermissionService permissionService)
        : base(crudService, permissionService, InjectionViewIds.ParamView, "鍙傛暟閰嶇疆")
    {
    }
}

public sealed class InjectionHardwareConfigViewModel : HardwareConfigViewModel
{
    public InjectionHardwareConfigViewModel(
        IHardwareConfigCrudService crudService,
        IClientPermissionService permissionService)
        : base(crudService, permissionService, InjectionViewIds.HardwareConfigView, "纭欢閰嶇疆")
    {
    }
}
