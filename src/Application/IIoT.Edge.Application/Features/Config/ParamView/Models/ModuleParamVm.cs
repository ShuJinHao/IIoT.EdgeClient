using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Common.Models;

namespace IIoT.Edge.Application.Features.Config.ParamView.Models;

/// <summary>
/// 插件枚举参数在参数页面中的编辑项。
/// </summary>
public class ModuleParamVm : ObservableModelBase
{
    public string ModuleId { get; set; } = string.Empty;

    public ModuleParamCategory Category { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ParamValueKind ValueKind { get; set; }

    public string DefaultValue { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string Min { get; set; } = string.Empty;

    public string Max { get; set; } = string.Empty;

    private string _value = string.Empty;
    public string Value
    {
        get => _value;
        set
        {
            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BoolValue));
        }
    }

    public bool IsBool => ValueKind == ParamValueKind.Bool;

    public bool BoolValue
    {
        get => bool.TryParse(Value, out var parsed) && parsed;
        set => Value = value.ToString();
    }
}
