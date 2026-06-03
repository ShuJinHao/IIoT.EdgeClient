using IIoT.Edge.Presentation.Navigation.Common;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Navigation.Features.Config.ParamView.Models;

/// <summary>
/// 参数页面中的分组，支持插件参数分组和宿主级配置分组。
/// </summary>
public class ModuleParamGroupVm : PresentationObservableModelBase
{
    public string ModuleId { get; set; } = string.Empty;

    public string ModuleDisplayNameResourceKey { get; set; } = string.Empty;

    public string ModuleDisplayNameFallback { get; set; } = string.Empty;

    private string _moduleDisplayName = string.Empty;
    public string ModuleDisplayName
    {
        get => _moduleDisplayName;
        set
        {
            _moduleDisplayName = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ModuleParamVm> Params { get; } = new();
}
