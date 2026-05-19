using Avalonia.Media;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Modularity;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

public sealed class NavigationItemViewModel : BaseNotifyPropertyChanged
{
    private const string DefaultModuleIconPath = "M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z M9,10 L15,10 M9,14 L15,14";

    private static readonly IReadOnlyDictionary<string, string> IconPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ChartBar"] = "M5,18 L5,12 L9,12 L9,18 Z M11,18 L11,7 L15,7 L15,18 Z M17,18 L17,10 L21,10 L21,18 Z",
        ["ChartLine"] = "M4,18 L7,18 L9,13 L12,16 L15,8 L18,12 L20,6",
        ["SwapHorizontal"] = "M4,7 L10,7 L10,10 L4,10 Z M14,7 L20,7 L20,10 L14,10 Z M4,14 L10,14 L10,17 L4,17 Z M14,14 L20,14 L20,17 L14,17 Z M10,8.5 L14,8.5 M10,15.5 L14,15.5",
        ["MonitorDashboard"] = "M4,6 L20,6 L20,17 L4,17 Z M8,20 L16,20 M12,17 L12,20 M7,14 L10,11 L13,13 L17,8",
        ["FileDocumentOutline"] = "M6,4 L18,4 L18,20 L6,20 Z M9,8 L15,8 M9,12 L15,12 M9,16 L13,16",
        ["Cog"] = "M7,7 L17,7 M7,12 L17,12 M7,17 L17,17 M9,5 L9,9 M15,10 L15,14 M11,15 L11,19",
        ["ServerNetwork"] = "M6,8 L18,8 L18,16 L6,16 Z M9,5 L9,8 M15,5 L15,8 M9,16 L9,19 M15,16 L15,19 M4,11 L6,11 M18,11 L20,11 M4,13 L6,13 M18,13 L20,13",
        ["Tune"] = "M5,6 L10,6 L10,11 L5,11 Z M14,13 L19,13 L19,18 L14,18 Z M10,8.5 L14,15.5 M11,15.5 L14,15.5 L14,12.5",
        ["Stethoscope"] = "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M12,7 L12,13 M12,16 L12,17"
    };

    private readonly IAppLanguageService _languageService;
    private bool _isSelected;

    public NavigationItemViewModel(
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback,
        string iconPath,
        bool isEnabled)
    {
        _languageService = languageService;
        ViewId = viewId;
        TitleResourceKey = titleResourceKey;
        TitleFallback = titleFallback;
        IconPath = ResolveIconPath(iconPath);
        IconData = Geometry.Parse(IconPath);
        IsEnabled = isEnabled;
    }

    public NavigationItemViewModel(IAppLanguageService languageService, MenuInfo menuInfo)
        : this(
            languageService,
            menuInfo.ViewId,
            menuInfo.TitleResourceKey,
            menuInfo.Title,
            menuInfo.Icon,
            isEnabled: true)
    {
    }

    public string ViewId { get; }

    public string TitleResourceKey { get; }

    public string TitleFallback { get; }

    public string IconPath { get; }

    public Geometry IconData { get; }

    public bool IsEnabled { get; }

    public string Title => _languageService.GetString(TitleResourceKey, TitleFallback);

    public string StatusText => IsEnabled
        ? string.Empty
        : _languageService.GetString("Navigation_Menu_P4Pending", "后续阶段接入");

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusText));
    }

    private static string ResolveIconPath(string icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return DefaultModuleIconPath;
        }

        if (IconPaths.TryGetValue(icon, out var path))
        {
            return path;
        }

        return LooksLikeGeometryPath(icon) ? icon : DefaultModuleIconPath;
    }

    private static bool LooksLikeGeometryPath(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith("M", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("m", StringComparison.OrdinalIgnoreCase);
    }
}
