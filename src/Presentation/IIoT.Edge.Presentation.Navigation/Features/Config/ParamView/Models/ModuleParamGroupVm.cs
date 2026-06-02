using IIoT.Edge.Presentation.Navigation.Common;
using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Navigation.Features.Config.ParamView.Models;

/// <summary>
/// 参数页面中按插件分组展示的一类模块参数。
/// </summary>
public class ModuleParamGroupVm : PresentationObservableModelBase
{
    public string ModuleId { get; set; } = string.Empty;

    public string ModuleDisplayName { get; set; } = string.Empty;

    public ObservableCollection<ModuleParamVm> Params { get; } = new();
}
