using System.ComponentModel;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Application.Modules.Hardware;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public interface IHardwareConfigEditSession
{
    void OpenAddInteractionMappingDialog(HardwareConfigViewModel viewModel);
    void OpenAddDataPointMappingDialog(HardwareConfigViewModel viewModel);
    void ConfirmAddIoMapping(HardwareConfigViewModel viewModel);
    void CloseAddIoMappingDialog(HardwareConfigViewModel viewModel);
    void DeleteSelectedIoMapping(HardwareConfigViewModel viewModel);
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
    private const int ManualSortOrderBase = 10000;

    private readonly IHardwareConfigValidationPresenter _validationPresenter;

    public HardwareConfigEditSession(IHardwareConfigValidationPresenter validationPresenter)
    {
        _validationPresenter = validationPresenter;
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

        viewModel.NewInteractionPair = new IoInteractionPairDraftVm
        {
            Source = IoMappingOptionCatalog.PointSourceStandardSignal
        };
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
            Source = IoMappingOptionCatalog.PointSourceStandardSignal,
            Category = FindInitialDataPointCategory(viewModel)
        };
        viewModel.NewInteractionPair = null;
        viewModel.SelectedStandardInteractionGroup = null;
        RefreshFilteredStandardDataSignals(viewModel);
        viewModel.SelectedStandardIoSignal = FindStandardDataSignal(viewModel);
        if (viewModel.SelectedStandardIoSignal is null)
        {
            ClearStandardSignalDraftForCurrentCategory(viewModel);
        }

        viewModel.IsAddIoMappingDialogOpen = true;
        viewModel.RaiseConfirmAddIoMappingCanExecuteChanged();
    }

    public void ConfirmAddIoMapping(HardwareConfigViewModel viewModel)
    {
        if (viewModel.IsInteractionPairDialog)
        {
            ConfirmAddInteractionPair(viewModel);
            return;
        }

        ConfirmAddDataPoint(viewModel);
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

    public void DeleteSelectedIoMapping(HardwareConfigViewModel viewModel)
    {
        var selected = viewModel.SelectedIoMapping;
        if (selected is null)
        {
            return;
        }

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
                viewModel.IoMappings.Remove(item);
            }
        }
        else
        {
            viewModel.IoMappings.Remove(selected);
        }

        viewModel.SelectedIoMapping = null;
        viewModel.IoMappingsView.Refresh();
        viewModel.RaiseDeleteIoMappingCanExecuteChanged();
    }

    public void HandleNewIoMappingPropertyChanged(
        HardwareConfigViewModel viewModel,
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (sender is not IoMappingDraftVm draft)
        {
            return;
        }

        if (e.PropertyName == nameof(IoMappingDraftVm.Source))
        {
            if (draft.IsStandardSource)
            {
                SelectNextStandardDataSignal(viewModel);
            }
            else if (draft.IsCustomSource)
            {
                draft.Source = IoMappingOptionCatalog.PointSourceStandardSignal;
            }
        }

        if (e.PropertyName == nameof(IoMappingDraftVm.Category))
        {
            SelectNextStandardDataSignal(viewModel);
        }
    }

    public void HandleNewInteractionPairPropertyChanged(
        HardwareConfigViewModel viewModel,
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (sender is not IoInteractionPairDraftVm draft)
        {
            return;
        }

        if (e.PropertyName == nameof(IoInteractionPairDraftVm.Source))
        {
            if (draft.IsStandardSource)
            {
                viewModel.SelectedStandardInteractionGroup ??= viewModel.StandardInteractionGroups.FirstOrDefault(static x => x.HasReadAndWrite);
                ApplyStandardInteractionGroupToDraft(viewModel, viewModel.SelectedStandardInteractionGroup);
            }
            else if (draft.IsCustomSource)
            {
                draft.Source = IoMappingOptionCatalog.PointSourceStandardSignal;
            }
        }
    }

    public void ApplyStandardSignalToDraft(
        HardwareConfigViewModel viewModel,
        IoStandardSignalOptionVm? signal)
    {
        if (viewModel.NewIoMapping is null || !viewModel.NewIoMapping.IsStandardSource)
        {
            return;
        }

        if (signal is null)
        {
            ClearStandardSignalDraftForCurrentCategory(viewModel);
            return;
        }

        var draftCategory = IoMappingOptionCatalog.NormalizeCategory(viewModel.NewIoMapping.Category, viewModel.NewIoMapping.AddressCount);
        var signalCategory = IoMappingOptionCatalog.NormalizeCategory(signal.Category, signal.AddressCount);
        if (!string.Equals(draftCategory, signalCategory, StringComparison.OrdinalIgnoreCase))
        {
            ClearStandardSignalDraftForCurrentCategory(viewModel);
            return;
        }

        viewModel.NewIoMapping.Category = draftCategory;
        viewModel.NewIoMapping.Direction = IoMappingOptionCatalog.GetDirectionForCategory(draftCategory) ?? signal.Direction;
        viewModel.NewIoMapping.PlcAddress = signal.PlcAddress;
        viewModel.NewIoMapping.AddressCount = signal.AddressCount;
        viewModel.NewIoMapping.DataType = signal.DataType;
        viewModel.NewIoMapping.BusinessGroup = signal.BusinessGroup;
        viewModel.NewIoMapping.SignalName = signal.SignalName;
        viewModel.NewIoMapping.Remark = signal.Remark;
    }

    public void ApplyStandardInteractionGroupToDraft(
        HardwareConfigViewModel viewModel,
        IoStandardSignalGroupOptionVm? group)
    {
        if (viewModel.NewInteractionPair is null || !viewModel.NewInteractionPair.IsStandardSource || group is null)
        {
            return;
        }

        var read = group.ReadSignals.FirstOrDefault();
        var write = group.WriteSignals.FirstOrDefault();
        viewModel.NewInteractionPair.BusinessGroup = group.BusinessGroup;
        viewModel.NewInteractionPair.ReadPlcAddress = read?.PlcAddress ?? string.Empty;
        viewModel.NewInteractionPair.ReadAddressCount = read?.AddressCount ?? 1;
        viewModel.NewInteractionPair.ReadDataType = read?.DataType ?? IoMappingOptionCatalog.DataTypeInt16;
        viewModel.NewInteractionPair.ReadSignalName = read?.SignalName ?? "PLC 触发";
        viewModel.NewInteractionPair.WritePlcAddress = write?.PlcAddress ?? string.Empty;
        viewModel.NewInteractionPair.WriteAddressCount = write?.AddressCount ?? 1;
        viewModel.NewInteractionPair.WriteDataType = write?.DataType ?? IoMappingOptionCatalog.DataTypeInt16;
        viewModel.NewInteractionPair.WriteSignalName = write?.SignalName ?? "上位机应答";
        viewModel.NewInteractionPair.Remark = read?.Remark ?? write?.Remark;
    }

    public void RefreshFilteredStandardDataSignals(HardwareConfigViewModel viewModel)
    {
        var category = viewModel.NewIoMapping?.Category ?? IoMappingOptionCatalog.CategorySingleRead;
        var normalizedCategory = IoMappingOptionCatalog.NormalizeCategory(category, addressCount: 1);
        HardwareConfigViewModel.ReplaceCollection(
            viewModel.FilteredStandardDataSignals,
            viewModel.StandardDataSignals
                .Where(signal => string.Equals(
                    IoMappingOptionCatalog.NormalizeCategory(signal.Category, signal.AddressCount),
                    normalizedCategory,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(static signal => signal.SortOrder)
                .ThenBy(static signal => signal.DisplayText, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyCollection<IoMappingVm> BuildMappingsToSave(IEnumerable<IoMappingVm> ioMappings)
    {
        var mappings = ioMappings.ToArray();
        var result = new List<IoMappingVm>(mappings.Length);

        foreach (var standard in mappings.Where(static x => !IsManualSignal(x)))
        {
            result.Add(CloneIoMapping(standard));
        }

        var manualOrdered = mappings
            .Where(static x => IsManualSignal(x))
            .OrderBy(static x => string.Equals(x.Direction, IoMappingOptionCatalog.DirectionWrite, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(static x => x.SortOrder <= 0 ? int.MaxValue : x.SortOrder)
            .ThenBy(static x => x.SignalName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < manualOrdered.Length; index++)
        {
            var clone = CloneIoMapping(manualOrdered[index]);
            clone.SortOrder = ManualSortOrderBase + index;
            result.Add(clone);
        }

        return result;
    }

    private void ConfirmAddInteractionPair(HardwareConfigViewModel viewModel)
    {
        if (viewModel.SelectedNetworkDevice is null || viewModel.NewInteractionPair is null)
        {
            return;
        }

        var validationError = _validationPresenter.ValidateInteractionPairDraft(viewModel, viewModel.NewInteractionPair);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            viewModel.ReportError(validationError);
            return;
        }

        var group = viewModel.SelectedStandardInteractionGroup!;
        var readTemplate = group.ReadSignals.First();
        var writeTemplate = group.WriteSignals.First();
        var readExists = HasIoMapping(viewModel, readTemplate.SignalKey, IoMappingOptionCatalog.DirectionRead);
        var writeExists = HasIoMapping(viewModel, writeTemplate.SignalKey, IoMappingOptionCatalog.DirectionWrite);

        if (readExists || writeExists)
        {
            viewModel.ReportError(viewModel.GetText("Navigation_Hardware_Validation_InteractionGroupExists", "该信号交互已存在映射，新增必须一次生成读写成对点位；请先删除旧映射后再重新新增。"));
            return;
        }

        viewModel.IoMappings.Add(CreateMappingFromTemplate(
            viewModel,
            readTemplate,
            viewModel.NewInteractionPair.ReadPlcAddress,
            viewModel.NewInteractionPair.ReadAddressCount,
            viewModel.NewInteractionPair.ReadDataType,
            viewModel.NewInteractionPair.ReadSignalName));
        viewModel.IoMappings.Add(CreateMappingFromTemplate(
            viewModel,
            writeTemplate,
            viewModel.NewInteractionPair.WritePlcAddress,
            viewModel.NewInteractionPair.WriteAddressCount,
            viewModel.NewInteractionPair.WriteDataType,
            viewModel.NewInteractionPair.WriteSignalName));

        viewModel.IoMappingsView.Refresh();
        CloseAddIoMappingDialog(viewModel);
        viewModel.ClearUserFeedback();
    }

    private void ConfirmAddDataPoint(HardwareConfigViewModel viewModel)
    {
        if (viewModel.SelectedNetworkDevice is null || viewModel.NewIoMapping is null)
        {
            return;
        }

        var validationError = _validationPresenter.ValidateDraft(viewModel, viewModel.NewIoMapping);
        if (!string.IsNullOrWhiteSpace(validationError))
        {
            viewModel.ReportError(validationError);
            return;
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
            SignalName = viewModel.NewIoMapping.SignalName.Trim(),
            SortOrder = standardSignal.SortOrder,
            Remark = string.IsNullOrWhiteSpace(viewModel.NewIoMapping.Remark) ? null : viewModel.NewIoMapping.Remark.Trim()
        });

        viewModel.IoMappingsView.Refresh();
        CloseAddIoMappingDialog(viewModel);
        viewModel.ClearUserFeedback();
    }

    private static IoStandardSignalOptionVm? FindStandardDataSignal(HardwareConfigViewModel viewModel)
        => viewModel.FilteredStandardDataSignals.FirstOrDefault();

    private static string FindInitialDataPointCategory(HardwareConfigViewModel viewModel)
    {
        var singleRead = viewModel.StandardDataSignals.FirstOrDefault(static signal => string.Equals(
            IoMappingOptionCatalog.NormalizeCategory(signal.Category, signal.AddressCount),
            IoMappingOptionCatalog.CategorySingleRead,
            StringComparison.OrdinalIgnoreCase));
        var firstSignal = singleRead ?? viewModel.StandardDataSignals
            .OrderBy(static signal => signal.SortOrder)
            .ThenBy(static signal => signal.DisplayText, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return firstSignal is null
            ? IoMappingOptionCatalog.CategorySingleRead
            : IoMappingOptionCatalog.NormalizeCategory(firstSignal.Category, firstSignal.AddressCount);
    }

    private void SelectNextStandardDataSignal(HardwareConfigViewModel viewModel)
    {
        RefreshFilteredStandardDataSignals(viewModel);
        var nextSignal = FindStandardDataSignal(viewModel);
        if (!ReferenceEquals(viewModel.SelectedStandardIoSignal, nextSignal))
        {
            viewModel.SelectedStandardIoSignal = nextSignal;
        }
        else
        {
            ApplyStandardSignalToDraft(viewModel, nextSignal);
        }
    }

    private static void ClearStandardSignalDraftForCurrentCategory(HardwareConfigViewModel viewModel)
    {
        if (viewModel.NewIoMapping is null || !viewModel.NewIoMapping.IsStandardSource)
        {
            return;
        }

        var category = IoMappingOptionCatalog.NormalizeCategory(viewModel.NewIoMapping.Category, viewModel.NewIoMapping.AddressCount);
        viewModel.NewIoMapping.Category = category;
        viewModel.NewIoMapping.Direction = IoMappingOptionCatalog.GetDirectionForCategory(category)
                                           ?? IoMappingOptionCatalog.DirectionRead;
        viewModel.NewIoMapping.PlcAddress = string.Empty;
        viewModel.NewIoMapping.AddressCount = IoMappingOptionCatalog.NormalizeAddressCount(category, viewModel.NewIoMapping.AddressCount);
        viewModel.NewIoMapping.DataType = IoMappingOptionCatalog.DataTypeInt16;
        viewModel.NewIoMapping.BusinessGroup = string.Empty;
        viewModel.NewIoMapping.SignalName = string.Empty;
        viewModel.NewIoMapping.Remark = null;
    }

    private static IoMappingVm CreateMappingFromTemplate(
        HardwareConfigViewModel viewModel,
        ModuleIoTemplateEntry template,
        string plcAddress,
        int? addressCount = null,
        string? dataType = null,
        string? signalName = null)
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
            SignalName = string.IsNullOrWhiteSpace(signalName) ? template.SignalName : signalName.Trim(),
            SortOrder = template.SortOrder,
            Remark = template.Remark
        };
    }

    private static bool HasIoMapping(HardwareConfigViewModel viewModel, string signalKey, string direction)
        => viewModel.IoMappings.Any(x => string.Equals(x.SignalKey, signalKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Direction, direction, StringComparison.OrdinalIgnoreCase));

    private static bool IsManualSignal(IoMappingVm mapping)
        => mapping.SignalKey?.StartsWith("Manual.", StringComparison.OrdinalIgnoreCase) ?? false;

    private static IoMappingVm CloneIoMapping(IoMappingVm source)
        => new()
        {
            Id = source.Id,
            NetworkDeviceId = source.NetworkDeviceId,
            SignalKey = source.SignalKey,
            PlcAddress = source.PlcAddress,
            Category = source.Category,
            AddressCount = source.AddressCount,
            DataType = source.DataType,
            Direction = source.Direction,
            BusinessGroup = source.BusinessGroup,
            SignalName = source.SignalName,
            SortOrder = source.SortOrder,
            Remark = source.Remark
        };
}
