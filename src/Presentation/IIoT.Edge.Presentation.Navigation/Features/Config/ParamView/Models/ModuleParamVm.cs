using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Presentation.Navigation.Common;

namespace IIoT.Edge.Presentation.Navigation.Features.Config.ParamView.Models;

/// <summary>
/// 参数页面中的编辑项，可能来自插件参数或宿主级配置白名单。
/// </summary>
public class ModuleParamVm : PresentationObservableModelBase
{
    public string ModuleId { get; set; } = string.Empty;

    public ModuleParamCategory Category { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DisplayNameResourceKey { get; set; } = string.Empty;

    public string DisplayNameFallback { get; set; } = string.Empty;

    public string DescriptionResourceKey { get; set; } = string.Empty;

    public string DescriptionFallback { get; set; } = string.Empty;

    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set
        {
            _displayName = value;
            OnPropertyChanged();
        }
    }

    private string _description = string.Empty;
    public string Description
    {
        get => _description;
        set
        {
            _description = value;
            OnPropertyChanged();
        }
    }

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
