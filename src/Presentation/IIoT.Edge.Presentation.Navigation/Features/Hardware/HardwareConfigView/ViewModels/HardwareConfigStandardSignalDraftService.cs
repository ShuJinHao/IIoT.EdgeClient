using IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public interface IHardwareConfigStandardSignalDraftService
{
    string FindInitialDataPointCategory(HardwareConfigViewModel viewModel);

    IoStandardSignalOptionVm? FindStandardDataSignal(HardwareConfigViewModel viewModel);

    void SelectNextStandardDataSignal(HardwareConfigViewModel viewModel);

    void ApplyStandardSignalToDraft(HardwareConfigViewModel viewModel, IoStandardSignalOptionVm? signal);

    void ClearStandardSignalDraftForCurrentCategory(HardwareConfigViewModel viewModel);

    void RefreshFilteredStandardDataSignals(HardwareConfigViewModel viewModel);
}

public sealed class HardwareConfigStandardSignalDraftService : IHardwareConfigStandardSignalDraftService
{
    public string FindInitialDataPointCategory(HardwareConfigViewModel viewModel)
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

    public IoStandardSignalOptionVm? FindStandardDataSignal(HardwareConfigViewModel viewModel)
        => viewModel.FilteredStandardDataSignals.FirstOrDefault();

    public void SelectNextStandardDataSignal(HardwareConfigViewModel viewModel)
    {
        RefreshFilteredStandardDataSignals(viewModel);
        var nextSignal = FindStandardDataSignal(viewModel);
        if (!ReferenceEquals(viewModel.SelectedStandardIoSignal, nextSignal))
        {
            viewModel.SelectedStandardIoSignal = nextSignal;
            return;
        }

        ApplyStandardSignalToDraft(viewModel, nextSignal);
    }

    public void ApplyStandardSignalToDraft(HardwareConfigViewModel viewModel, IoStandardSignalOptionVm? signal)
    {
        if (viewModel.NewIoMapping is null)
        {
            return;
        }

        if (signal is null)
        {
            ClearStandardSignalDraftForCurrentCategory(viewModel);
            return;
        }

        var draftCategory = IoMappingOptionCatalog.NormalizeCategory(
            viewModel.NewIoMapping.Category,
            viewModel.NewIoMapping.AddressCount);
        var signalCategory = IoMappingOptionCatalog.NormalizeCategory(signal.Category, signal.AddressCount);
        if (!string.Equals(draftCategory, signalCategory, StringComparison.OrdinalIgnoreCase))
        {
            ClearStandardSignalDraftForCurrentCategory(viewModel);
            return;
        }

        viewModel.NewIoMapping.Category = draftCategory;
        viewModel.NewIoMapping.Direction = IoMappingOptionCatalog.GetDirectionForCategory(draftCategory)
                                           ?? signal.Direction;
        viewModel.NewIoMapping.PlcAddress = signal.PlcAddress;
        viewModel.NewIoMapping.AddressCount = signal.AddressCount;
        viewModel.NewIoMapping.DataType = signal.DataType;
        viewModel.NewIoMapping.BusinessGroup = signal.BusinessGroup;
        viewModel.NewIoMapping.Remark = signal.Remark;
    }

    public void ClearStandardSignalDraftForCurrentCategory(HardwareConfigViewModel viewModel)
    {
        if (viewModel.NewIoMapping is null)
        {
            return;
        }

        var category = IoMappingOptionCatalog.NormalizeCategory(
            viewModel.NewIoMapping.Category,
            viewModel.NewIoMapping.AddressCount);
        viewModel.NewIoMapping.Category = category;
        viewModel.NewIoMapping.Direction = IoMappingOptionCatalog.GetDirectionForCategory(category)
                                           ?? IoMappingOptionCatalog.DirectionRead;
        viewModel.NewIoMapping.PlcAddress = string.Empty;
        viewModel.NewIoMapping.AddressCount = IoMappingOptionCatalog.NormalizeAddressCount(
            category,
            viewModel.NewIoMapping.AddressCount);
        viewModel.NewIoMapping.DataType = IoMappingOptionCatalog.DataTypeInt16;
        viewModel.NewIoMapping.BusinessGroup = string.Empty;
        viewModel.NewIoMapping.Remark = null;
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
}
