using System.ComponentModel;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public interface IHardwareConfigEditSession
{
    void OpenAddInteractionMappingDialog(HardwareConfigViewModel viewModel);
    void OpenAddDataPointMappingDialog(HardwareConfigViewModel viewModel);
    bool ConfirmAddIoMapping(HardwareConfigViewModel viewModel);
    void CloseAddIoMappingDialog(HardwareConfigViewModel viewModel);
    bool DeleteSelectedIoMapping(HardwareConfigViewModel viewModel);
    void HandleNewIoMappingPropertyChanged(
        HardwareConfigViewModel viewModel,
        object? sender,
        PropertyChangedEventArgs e);
    void HandleNewInteractionPairPropertyChanged(
        HardwareConfigViewModel viewModel,
        object? sender,
        PropertyChangedEventArgs e);
    void ApplyStandardSignalToDraft(
        HardwareConfigViewModel viewModel,
        IoStandardSignalOptionVm? signal);
    void ApplyStandardInteractionGroupToDraft(
        HardwareConfigViewModel viewModel,
        IoStandardSignalGroupOptionVm? group);
    void RefreshFilteredStandardDataSignals(HardwareConfigViewModel viewModel);
    IReadOnlyCollection<IoMappingVm> BuildMappingsToSave(IEnumerable<IoMappingVm> ioMappings);
}

public sealed class HardwareConfigEditSession : IHardwareConfigEditSession
{
    private readonly IHardwareConfigValidationPresenter _validationPresenter;
    private readonly IHardwareConfigStandardSignalDraftService _standardSignalDraftService;
    private readonly IHardwareConfigMappingSaveBuilder _mappingSaveBuilder;

    public HardwareConfigEditSession(
        IHardwareConfigValidationPresenter validationPresenter,
        IHardwareConfigStandardSignalDraftService standardSignalDraftService,
        IHardwareConfigMappingSaveBuilder mappingSaveBuilder)
    {
        _validationPresenter = validationPresenter;
        _standardSignalDraftService = standardSignalDraftService;
        _mappingSaveBuilder = mappingSaveBuilder;
    }

    public void OpenAddInteractionMappingDialog(HardwareConfigViewModel viewModel)
    {
        if (viewModel.SelectedNetworkDevice is null)
        {
            viewModel.ReportError(viewModel.GetText("Navigation_Hardware_Validation_SelectNetworkDeviceFirst", "请先选择一个 PLC 设备。"));
            return;
        }

        viewModel.IsInteractionPairDialog = true;
        var standardGroup = viewModel.StandardInteractionGroups.FirstOrDefault(static x => x.HasReadAndWrite);
        if (standardGroup is null)
        {
            viewModel.ReportError(viewModel.GetText("Navigation_Hardware_Validation_InteractionGroupRequired", "当前 PLC 没有可添加的插件标准信号交互。"));
            return;
        }

        viewModel.NewInteractionPair = new IoInteractionPairDraftVm();
        viewModel.NewIoMapping = null;
        viewModel.SelectedStandardIoSignal = null;
        viewModel.SelectedStandardInteractionGroup = standardGroup;

        viewModel.IsAddIoMappingDialogOpen = true;
        viewModel.RaiseConfirmAddIoMappingCanExecuteChanged();
    }

    public void OpenAddDataPointMappingDialog(HardwareConfigViewModel viewModel)
    {
        if (viewModel.SelectedNetworkDevice is null)
        {
            viewModel.ReportError(viewModel.GetText("Navigation_Hardware_Validation_SelectNetworkDeviceFirst", "请先选择一个 PLC 设备。"));
            return;
        }

        viewModel.IsInteractionPairDialog = false;
        viewModel.NewIoMapping = new IoMappingDraftVm
        {
            Category = _standardSignalDraftService.FindInitialDataPointCategory(viewModel)
        };
        viewModel.NewInteractionPair = null;
        viewModel.SelectedStandardInteractionGroup = null;
        RefreshFilteredStandardDataSignals(viewModel);
        viewModel.SelectedStandardIoSignal = _standardSignalDraftService.FindStandardDataSignal(viewModel);
        if (viewModel.SelectedStandardIoSignal is null)
        {
            _standardSignalDraftService.ClearStandardSignalDraftForCurrentCategory(viewModel);
        }

        viewModel.IsAddIoMappingDialogOpen = true;
        viewModel.RaiseConfirmAddIoMappingCanExecuteChanged();
    }

    public bool ConfirmAddIoMapping(HardwareConfigViewModel viewModel)
    {
        if (viewModel.IsInteractionPairDialog)
        {
            return ConfirmAddInteractionPair(viewModel);
        }

        return ConfirmAddDataPoint(viewModel);
    }

    public void CloseAddIoMappingDialog(HardwareConfigViewModel viewModel)
    {
        viewModel.IsAddIoMappingDialogOpen = false;
        viewModel.IsInteractionPairDialog = false;
        viewModel.SelectedStandardIoSignal = null;
        viewModel.SelectedStandardInteractionGroup = null;
        viewModel.NewIoMapping = null;
        viewModel.NewInteractionPair = null;
        viewModel.RaiseConfirmAddIoMappingCanExecuteChanged();
    }

    public bool DeleteSelectedIoMapping(HardwareConfigViewModel viewModel)
    {
        var selected = viewModel.SelectedIoMapping;
        if (selected is null)
        {
            return false;
        }

        var removed = false;
        if (_validationPresenter.IsInteractionMapping(selected))
        {
            var interactionKey = _validationPresenter.CreateInteractionGroupKey(selected);
            var removeItems = viewModel.IoMappings
                .Where(x => _validationPresenter.IsInteractionMapping(x)
                    && x.NetworkDeviceId == selected.NetworkDeviceId
                    && string.Equals(_validationPresenter.CreateInteractionGroupKey(x), interactionKey, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var item in removeItems)
            {
                removed |= viewModel.IoMappings.Remove(item);
            }
        }
        else
        {
            removed = viewModel.IoMappings.Remove(selected);
        }

        if (!removed)
        {
            return false;
        }

        viewModel.SelectedIoMapping = null;
        viewModel.RefreshIoMappingGroups();
        viewModel.RaiseDeleteIoMappingCanExecuteChanged();
        return true;
    }

    public void HandleNewIoMappingPropertyChanged(
        HardwareConfigViewModel viewModel,
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (sender is not IoMappingDraftVm)
        {
            return;
        }

        if (e.PropertyName == nameof(IoMappingDraftVm.Category))
        {
            _standardSignalDraftService.SelectNextStandardDataSignal(viewModel);
        }
    }

    public void HandleNewInteractionPairPropertyChanged(
        HardwareConfigViewModel viewModel,
        object? sender,
        PropertyChangedEventArgs e)
    {
    }

    public void ApplyStandardSignalToDraft(
        HardwareConfigViewModel viewModel,
        IoStandardSignalOptionVm? signal)
        => _standardSignalDraftService.ApplyStandardSignalToDraft(viewModel, signal);

    public void ApplyStandardInteractionGroupToDraft(
        HardwareConfigViewModel viewModel,
        IoStandardSignalGroupOptionVm? group)
    {
        if (viewModel.NewInteractionPair is null || group is null)
        {
            return;
        }

        var read = group.ReadSignals.FirstOrDefault();
        var write = group.WriteSignals.FirstOrDefault();
        viewModel.NewInteractionPair.BusinessGroup = group.BusinessGroup;
        viewModel.NewInteractionPair.ReadPlcAddress = read?.PlcAddress ?? string.Empty;
        viewModel.NewInteractionPair.ReadAddressCount = read?.AddressCount ?? 1;
        viewModel.NewInteractionPair.ReadDataType = read?.DataType ?? IoMappingOptionCatalog.DataTypeInt16;
        viewModel.NewInteractionPair.WritePlcAddress = write?.PlcAddress ?? string.Empty;
        viewModel.NewInteractionPair.WriteAddressCount = write?.AddressCount ?? 1;
        viewModel.NewInteractionPair.WriteDataType = write?.DataType ?? IoMappingOptionCatalog.DataTypeInt16;
        viewModel.NewInteractionPair.Remark = read?.Remark ?? write?.Remark;
    }

    public void RefreshFilteredStandardDataSignals(HardwareConfigViewModel viewModel)
        => _standardSignalDraftService.RefreshFilteredStandardDataSignals(viewModel);

    public IReadOnlyCollection<IoMappingVm> BuildMappingsToSave(IEnumerable<IoMappingVm> ioMappings)
        => _mappingSaveBuilder.BuildMappingsToSave(ioMappings);

    private bool ConfirmAddInteractionPair(HardwareConfigViewModel viewModel)
    {
        if (viewModel.SelectedNetworkDevice is null || viewModel.NewInteractionPair is null)
        {
            return false;
        }

        var validationError = _validationPresenter.ValidateInteractionPairDraft(viewModel, viewModel.NewInteractionPair);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            viewModel.ReportError(validationError);
            return false;
        }

        var group = viewModel.SelectedStandardInteractionGroup!;
        var readTemplate = group.ReadSignals.First();
        var writeTemplate = group.WriteSignals.First();
        var readExists = HasIoMapping(viewModel, readTemplate.SignalKey, IoMappingOptionCatalog.DirectionRead);
        var writeExists = HasIoMapping(viewModel, writeTemplate.SignalKey, IoMappingOptionCatalog.DirectionWrite);

        if (readExists || writeExists)
        {
            viewModel.ReportError(viewModel.GetText("Navigation_Hardware_Validation_InteractionGroupExists", "该信号交互已存在映射，新增必须一次生成读写成对点位；请先删除旧映射后再重新新增。"));
            return false;
        }

        viewModel.IoMappings.Add(CreateMappingFromTemplate(
            viewModel,
            readTemplate,
            viewModel.NewInteractionPair.ReadPlcAddress,
            viewModel.NewInteractionPair.ReadAddressCount,
            viewModel.NewInteractionPair.ReadDataType,
            viewModel.NewInteractionPair.Remark));
        viewModel.IoMappings.Add(CreateMappingFromTemplate(
            viewModel,
            writeTemplate,
            viewModel.NewInteractionPair.WritePlcAddress,
            viewModel.NewInteractionPair.WriteAddressCount,
            viewModel.NewInteractionPair.WriteDataType,
            viewModel.NewInteractionPair.Remark));

        viewModel.RefreshIoMappingGroups();
        CloseAddIoMappingDialog(viewModel);
        viewModel.ClearUserFeedback();
        return true;
    }

    private bool ConfirmAddDataPoint(HardwareConfigViewModel viewModel)
    {
        if (viewModel.SelectedNetworkDevice is null || viewModel.NewIoMapping is null)
        {
            return false;
        }

        var validationError = _validationPresenter.ValidateDraft(viewModel, viewModel.NewIoMapping);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            viewModel.ReportError(validationError);
            return false;
        }

        var standardSignal = viewModel.SelectedStandardIoSignal!;
        var category = IoMappingOptionCatalog.NormalizeCategory(viewModel.NewIoMapping.Category, viewModel.NewIoMapping.AddressCount);
        var direction = IoMappingOptionCatalog.GetDirectionForCategory(category) ?? standardSignal.Direction;
        var addressCount = IoMappingOptionCatalog.NormalizeAddressCount(category, viewModel.NewIoMapping.AddressCount);

        viewModel.IoMappings.Add(new IoMappingVm
        {
            NetworkDeviceId = viewModel.SelectedNetworkDevice.Id,
            SignalKey = standardSignal.SignalKey,
            PlcAddress = viewModel.NewIoMapping.PlcAddress.Trim(),
            Category = category,
            AddressCount = addressCount,
            DataType = viewModel.NewIoMapping.DataType.Trim(),
            Direction = direction,
            BusinessGroup = standardSignal.BusinessGroup,
            SortOrder = standardSignal.SortOrder,
            Remark = string.IsNullOrWhiteSpace(viewModel.NewIoMapping.Remark) ? null : viewModel.NewIoMapping.Remark.Trim()
        });

        viewModel.RefreshIoMappingGroups();
        CloseAddIoMappingDialog(viewModel);
        viewModel.ClearUserFeedback();
        return true;
    }

    private static IoMappingVm CreateMappingFromTemplate(
        HardwareConfigViewModel viewModel,
        ModuleIoTemplateEntry template,
        string plcAddress,
        int? addressCount = null,
        string? dataType = null,
        string? remark = null)
    {
        var category = IoMappingOptionCatalog.NormalizeCategory(template.Category, addressCount ?? template.AddressCount);
        var normalizedCount = IoMappingOptionCatalog.NormalizeAddressCount(category, addressCount ?? template.AddressCount);
        var direction = IoMappingOptionCatalog.GetDirectionForCategory(category) ?? template.Direction;

        return new IoMappingVm
        {
            NetworkDeviceId = viewModel.SelectedNetworkDevice?.Id ?? 0,
            SignalKey = template.SignalKey,
            PlcAddress = plcAddress.Trim(),
            Category = category,
            AddressCount = normalizedCount,
            DataType = string.IsNullOrWhiteSpace(dataType) ? template.DataType : dataType.Trim(),
            Direction = direction,
            BusinessGroup = template.BusinessGroup,
            SortOrder = template.SortOrder,
            Remark = string.IsNullOrWhiteSpace(remark) ? template.Remark : remark.Trim()
        };
    }

    private static bool HasIoMapping(HardwareConfigViewModel viewModel, string signalKey, string direction)
        => viewModel.IoMappings.Any(x => string.Equals(x.SignalKey, signalKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Direction, direction, StringComparison.OrdinalIgnoreCase));

}
