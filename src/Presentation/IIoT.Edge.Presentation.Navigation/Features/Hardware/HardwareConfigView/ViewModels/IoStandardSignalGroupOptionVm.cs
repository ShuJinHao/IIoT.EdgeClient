using IIoT.Edge.Application.Features.Hardware.IoMappings;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.HardwareConfigView;

/// <summary>
/// 插件标准信号交互组，一组内必须同时具备读信号和写信号。
/// </summary>
public sealed class IoStandardSignalGroupOptionVm
{
    public IoStandardSignalGroupOptionVm(string businessGroup, IReadOnlyList<ModuleIoTemplateEntry> signals)
    {
        BusinessGroup = string.IsNullOrWhiteSpace(businessGroup) ? "未分组信号交互" : businessGroup.Trim();
        Signals = signals
            .OrderBy(static x => x.SortOrder)
            .ToArray();
        DisplayText = $"{BusinessGroup}（读 {ReadSignals.Count} / 写 {WriteSignals.Count}）";
    }

    public string BusinessGroup { get; }

    public IReadOnlyList<ModuleIoTemplateEntry> Signals { get; }

    public IReadOnlyList<ModuleIoTemplateEntry> ReadSignals
        => Signals
            .Where(static x => string.Equals(x.Direction, IoMappingOptionCatalog.DirectionRead, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public IReadOnlyList<ModuleIoTemplateEntry> WriteSignals
        => Signals
            .Where(static x => string.Equals(x.Direction, IoMappingOptionCatalog.DirectionWrite, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public bool HasReadAndWrite => ReadSignals.Count > 0 && WriteSignals.Count > 0;

    public string DisplayText { get; }
}
