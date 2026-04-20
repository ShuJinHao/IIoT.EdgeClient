using IIoT.Edge.Application.Abstractions.Auth;
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
        : base(dataViewService, InjectionViewIds.DataView, "生产数据")
    {
    }
}

public sealed class InjectionCapacityViewModel : CapacityViewModel
{
    public InjectionCapacityViewModel(ICapacityViewService capacityViewService)
        : base(capacityViewService, InjectionViewIds.CapacityView, "产能查询")
    {
    }
}

public sealed class InjectionMonitorViewModel : MonitorViewModel
{
    public InjectionMonitorViewModel(IMonitorViewService monitorViewService)
        : base(monitorViewService, InjectionViewIds.Monitor, "实时监控")
    {
    }
}

public sealed class InjectionIoViewModel : IoViewViewModel
{
    public InjectionIoViewModel(IPlcDataStore dataStore, ISender sender)
        : base(dataStore, sender, InjectionViewIds.IoView, "IO交互")
    {
    }
}

public sealed class InjectionRecipeViewModel : RecipeViewModel
{
    public InjectionRecipeViewModel(IRecipeViewCrudService crudService, IRecipeService recipeService)
        : base(crudService, recipeService, InjectionViewIds.RecipeView, "产品配方")
    {
    }
}

public sealed class InjectionParamViewModel : ParamViewModel
{
    public InjectionParamViewModel(IParamViewCrudService crudService)
        : base(crudService, InjectionViewIds.ParamView, "参数配置")
    {
    }
}

public sealed class InjectionHardwareConfigViewModel : HardwareConfigViewModel
{
    public InjectionHardwareConfigViewModel(IHardwareConfigCrudService crudService, IAuthService authService)
        : base(crudService, authService, InjectionViewIds.HardwareConfigView, "硬件配置")
    {
    }
}
