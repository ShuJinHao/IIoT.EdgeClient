using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.IoMappings;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

public interface IHardwareConfigValidationPresenter
{
    Task<IReadOnlyCollection<ValidationIssue>> ValidateSaveAsync(HardwareConfigViewModel viewModel);

    CrudOperationResult CreateValidationResult(
        HardwareConfigViewModel viewModel,
        IEnumerable<ValidationIssue> issues);

    string? ValidateDraft(HardwareConfigViewModel viewModel, IoMappingDraftVm draft);

    string? ValidateInteractionPairDraft(
        HardwareConfigViewModel viewModel,
        IoInteractionPairDraftVm draft);

    bool IsInteractionMapping(IoMappingVm mapping);

    string CreateInteractionGroupKey(IoMappingVm mapping);
}

public sealed class HardwareConfigValidationPresenter : IHardwareConfigValidationPresenter
{
    public async Task<IReadOnlyCollection<ValidationIssue>> ValidateSaveAsync(HardwareConfigViewModel viewModel)
    {
        var issues = new List<ValidationIssue>();
        issues.AddRange(await ValidateItemsAsync(
            viewModel.NetworkDevices,
            new NetworkDeviceValidator(viewModel.GetText, viewModel.FormatText)));
        issues.AddRange(await ValidateItemsAsync(
            viewModel.SerialDevices,
            new SerialDeviceValidator(viewModel.GetText, viewModel.FormatText)));
        issues.AddRange(await ValidateItemsAsync(
            viewModel.IoMappings,
            new IoMappingValidator(viewModel.GetText, viewModel.FormatText)));
        issues.AddRange(ValidateInteractionPairs(viewModel.IoMappings));

        return issues;
    }

    public CrudOperationResult CreateValidationResult(
        HardwareConfigViewModel viewModel,
        IEnumerable<ValidationIssue> issues)
    {
        var validationIssues = issues
            .Where(issue => !string.IsNullOrWhiteSpace(issue.Message))
            .Distinct()
            .ToArray();

        return validationIssues.Length == 0
            ? CrudOperationResult.Success()
            : CrudOperationResult.ValidationFailure(
                validationIssues,
                viewModel.GetText("Navigation_Validation_FixInvalidFields", "请先修正无效表单字段。"));
    }

    public string? ValidateDraft(HardwareConfigViewModel viewModel, IoMappingDraftVm draft)
    {
        if (!IoMappingOptionCatalog.IsKnownPointSource(draft.Source))
        {
            return viewModel.GetText("Navigation_Hardware_Validation_IoSourceRequired", "请选择点位来源。");
        }

        if (draft.IsStandardSource)
        {
            if (viewModel.SelectedStandardIoSignal is null)
            {
                return viewModel.GetText("Navigation_Hardware_Validation_NoEnumSignalForCategory", "当前分类暂无插件枚举信号。");
            }

            var draftCategory = IoMappingOptionCatalog.NormalizeCategory(draft.Category, draft.AddressCount);
            var standardCategory = IoMappingOptionCatalog.NormalizeCategory(
                viewModel.SelectedStandardIoSignal.Category,
                viewModel.SelectedStandardIoSignal.AddressCount);

            if (!IoMappingOptionCatalog.IsDataPointCategory(draftCategory))
            {
                return viewModel.GetText("Navigation_Hardware_Validation_DataPointOnly", "新增数据点不能选择信号交互点位。");
            }

            if (!string.Equals(draftCategory, standardCategory, StringComparison.OrdinalIgnoreCase))
            {
                return viewModel.GetText("Navigation_Hardware_Validation_DataCategoryMismatch", "请选择当前 IO 分类下的插件标准数据点。");
            }

            if (viewModel.IoMappings.Any(x => string.Equals(
                    x.SignalKey,
                    viewModel.SelectedStandardIoSignal.SignalKey,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Direction, IoMappingOptionCatalog.GetDirectionForCategory(draftCategory), StringComparison.OrdinalIgnoreCase)))
            {
                return viewModel.GetText("Navigation_Hardware_Validation_StandardSignalExists", "该插件标准信号已存在，不能重复添加。");
            }

            if (string.IsNullOrWhiteSpace(draft.PlcAddress))
            {
                return viewModel.GetText("Navigation_Hardware_Validation_IoAddressRequired", "PLC 地址不能为空。");
            }

            if (IoMappingOptionCatalog.IsFixedAddressCountCategory(draftCategory) && draft.AddressCount != 1)
            {
                return viewModel.GetText("Navigation_Hardware_Validation_FixedCountMustBeOne", "信号交互、单点读数据和单点写数据的数量必须为 1。");
            }

            if (draft.AddressCount <= 0)
            {
                return viewModel.GetText("Navigation_Hardware_Validation_IoAddressCountPositive", "地址数量必须大于 0。");
            }

            if (!IoMappingOptionCatalog.IsKnownDataType(draft.DataType))
            {
                return viewModel.GetText("Navigation_Hardware_Validation_IoDataTypeRequired", "请选择 IO 数据类型。");
            }

            if (string.IsNullOrWhiteSpace(draft.SignalName))
            {
                return viewModel.GetText("Navigation_Hardware_Validation_IoSignalNameRequired", "信号名称不能为空。");
            }

            return null;
        }

        return viewModel.GetText("Navigation_Hardware_Validation_DataPointOnly", "新增数据点只能选择插件枚举定义的单点读数据、连续读数据、单点写数据或连续写数据。");
    }

    public string? ValidateInteractionPairDraft(
        HardwareConfigViewModel viewModel,
        IoInteractionPairDraftVm draft)
    {
        if (!IoMappingOptionCatalog.IsKnownPointSource(draft.Source))
        {
            return viewModel.GetText("Navigation_Hardware_Validation_IoSourceRequired", "请选择点位来源。");
        }

        if (!draft.IsStandardSource)
        {
            return viewModel.GetText("Navigation_Hardware_Validation_InteractionGroupRequired", "信号交互只能选择插件定义的标准业务动作。");
        }

        if (viewModel.SelectedStandardInteractionGroup is null)
        {
            return viewModel.GetText("Navigation_Hardware_Validation_InteractionGroupRequired", "请选择插件标准信号交互组。");
        }

        if (!viewModel.SelectedStandardInteractionGroup.HasReadAndWrite)
        {
            return viewModel.GetText("Navigation_Hardware_Validation_InteractionGroupIncomplete", "信号交互组必须同时包含 PLC→PC 读点和 PC→PLC 写点。");
        }

        if (string.IsNullOrWhiteSpace(draft.ReadPlcAddress) || string.IsNullOrWhiteSpace(draft.WritePlcAddress))
        {
            return viewModel.GetText("Navigation_Hardware_Validation_InteractionAddressRequired", "信号交互必须同时填写读地址和写地址。");
        }

        if (draft.ReadAddressCount <= 0 || draft.WriteAddressCount <= 0)
        {
            return viewModel.GetText("Navigation_Hardware_Validation_IoAddressCountPositive", "地址数量必须大于 0。");
        }

        if (draft.ReadAddressCount != 1 || draft.WriteAddressCount != 1)
        {
            return viewModel.GetText("Navigation_Hardware_Validation_InteractionCountMustBeOne", "信号交互读点和写点数量必须固定为 1。");
        }

        if (!IoMappingOptionCatalog.IsKnownDataType(draft.ReadDataType)
            || !IoMappingOptionCatalog.IsKnownDataType(draft.WriteDataType))
        {
            return viewModel.GetText("Navigation_Hardware_Validation_IoDataTypeRequired", "请选择 IO 数据类型。");
        }

        if (string.IsNullOrWhiteSpace(draft.ReadSignalName) || string.IsNullOrWhiteSpace(draft.WriteSignalName))
        {
            return viewModel.GetText("Navigation_Hardware_Validation_IoSignalNameRequired", "信号名称不能为空。");
        }

        return null;
    }

    public bool IsInteractionMapping(IoMappingVm mapping)
        => string.Equals(
            IoMappingOptionCatalog.NormalizeCategory(mapping.Category, mapping.AddressCount),
            IoMappingOptionCatalog.CategoryInteraction,
            StringComparison.OrdinalIgnoreCase);

    public string CreateInteractionGroupKey(IoMappingVm mapping)
    {
        if (IsStandardInteractionSignalKey(mapping.SignalKey))
        {
            return $"SIGNAL:{mapping.SignalKey.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(mapping.BusinessGroup))
        {
            return $"GROUP:{mapping.BusinessGroup.Trim()}";
        }

        return $"SIGNAL:{mapping.SignalKey?.Trim() ?? string.Empty}";
    }

    private IReadOnlyCollection<ValidationIssue> ValidateInteractionPairs(IEnumerable<IoMappingVm> ioMappings)
        => ioMappings
            .Where(IsInteractionMapping)
            .GroupBy(CreateInteractionGroupKey, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var issues = new List<ValidationIssue>();
                var displayName = CreateInteractionDisplayName(group.First());
                var readCount = group.Count(x => string.Equals(x.Direction, IoMappingOptionCatalog.DirectionRead, StringComparison.OrdinalIgnoreCase));
                var writeCount = group.Count(x => string.Equals(x.Direction, IoMappingOptionCatalog.DirectionWrite, StringComparison.OrdinalIgnoreCase));
                if (readCount == 0 || writeCount == 0)
                {
                    issues.Add(new ValidationIssue($"信号交互“{displayName}”必须同时包含 PLC→PC 读点和 PC→PLC 写点。", nameof(IoMappingVm.BusinessGroup)));
                }

                if (readCount > 1)
                {
                    issues.Add(new ValidationIssue($"信号交互“{displayName}”存在重复 PLC→PC 读点。", nameof(IoMappingVm.BusinessGroup)));
                }

                if (writeCount > 1)
                {
                    issues.Add(new ValidationIssue($"信号交互“{displayName}”存在重复 PC→PLC 写点。", nameof(IoMappingVm.BusinessGroup)));
                }

                return issues;
            })
            .ToArray();

    private static async Task<IReadOnlyCollection<ValidationIssue>> ValidateItemsAsync<TModel>(
        IEnumerable<TModel> models,
        IEditorValidator<TModel> validator)
    {
        var issues = new List<ValidationIssue>();
        foreach (var model in models)
        {
            issues.AddRange(await validator.ValidateAsync(model));
        }

        return issues;
    }

    private static bool IsStandardInteractionSignalKey(string? signalKey)
        => signalKey?.StartsWith("Homogenization.Interaction.", StringComparison.OrdinalIgnoreCase) ?? false;

    private static string CreateInteractionDisplayName(IoMappingVm mapping)
    {
        if (!string.IsNullOrWhiteSpace(mapping.BusinessGroup))
        {
            return mapping.BusinessGroup.Trim();
        }

        return string.IsNullOrWhiteSpace(mapping.SignalKey) ? "未命名" : mapping.SignalKey.Trim();
    }
}
