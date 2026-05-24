using System.Collections.ObjectModel;
using System.Windows.Input;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;

namespace IIoT.Edge.Presentation.Panels.Features.Equipment;

public sealed class ProductionPlanSelectionWindowViewModel : PresentationViewModelBase
{
    private const string EmptyFallback = "—";

    private readonly IProductionPlanSelectionService? planSelectionService;
    private readonly IAppLanguageService languageService;
    private ProductionPlanOption? selectedPlan;

    public ProductionPlanSelectionWindowViewModel(
        IEnumerable<IProductionPlanSelectionService> planSelectionServices,
        IAppLanguageService languageService)
    {
        planSelectionService = planSelectionServices.FirstOrDefault();
        this.languageService = languageService;
        RefreshCommand = new AsyncCommand(LoadAsync);
    }

    public override string ViewId => "Panels.ProductionPlanSelection";

    public override string ViewTitle => languageService.GetString(
        "Panels_PlanDialog_Title",
        "选择主批计划");

    public ObservableCollection<ProductionPlanOption> Plans { get; } = new();

    public ProductionPlanOption? SelectedPlan
    {
        get => selectedPlan;
        set
        {
            selectedPlan = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasPlans => Plans.Count > 0;

    public bool IsEmpty => !IsBusy && !HasError && Plans.Count == 0;

    public bool HasSelection => SelectedPlan is not null;

    public ICommand RefreshCommand { get; }

    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ClearFeedback();
        IsBusy = true;

        try
        {
            if (planSelectionService is null)
            {
                SetError(languageService.GetString(
                    "Panels_Error_PlanServiceMissing",
                    "当前模块没有提供主批计划查询能力。"));
                return;
            }

            SetStatus(languageService.GetString(
                "Panels_Message_LoadingProductionPlans",
                "正在查询 MES 主批计划..."));

            var plans = await planSelectionService.LoadPlansAsync();
            Plans.Clear();
            foreach (var plan in plans)
            {
                Plans.Add(plan);
            }

            SelectedPlan = Plans.FirstOrDefault();
            if (Plans.Count == 0)
            {
                SetStatus(languageService.GetString(
                    "Panels_Empty_NoProductionPlans_Message",
                    "MES 当前没有返回可选择的主批计划。"));
            }
            else
            {
                ClearFeedback();
            }

            NotifyPlanStateChanged();
        }
        catch (Exception ex)
        {
            SetError(FormatLoadError(ex));
        }
        finally
        {
            IsBusy = false;
            NotifyPlanStateChanged();
        }
    }

    private string FormatLoadError(Exception ex)
    {
        var prefix = languageService.GetString(
            "Panels_Error_LoadProductionPlansFailed",
            "查询主批计划失败");
        var detail = ex.Message == ProductionPlanSelectionErrorCodes.MissingUpperComputerNo
            ? languageService.GetString(
                "Panels_Error_MesUpperComputerNoMissing",
                "MES 上位机编码未配置。")
            : string.IsNullOrWhiteSpace(ex.Message) ? EmptyFallback : ex.Message;

        return $"{prefix}: {detail}";
    }

    private void NotifyPlanStateChanged()
    {
        OnPropertyChanged(nameof(HasPlans));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasSelection));
    }
}
