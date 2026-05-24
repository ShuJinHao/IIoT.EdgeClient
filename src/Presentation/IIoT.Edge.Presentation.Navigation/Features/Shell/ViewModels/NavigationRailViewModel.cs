using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

public sealed class NavigationRailViewModel : BaseNotifyPropertyChanged
{
    private readonly IAppLanguageService _languageService;
    private NavigationItemViewModel _selectedItem = null!;

    public NavigationRailViewModel(IAppLanguageService languageService)
    {
        _languageService = languageService;
        Items = CreateItems(languageService);
        Items[0].IsSelected = true;
        SelectedItem = Items[0];
        SelectCommand = new BaseCommand(parameter =>
        {
            if (parameter is NavigationItemViewModel item)
            {
                Select(item);
            }
        });
        SwitchLanguageCommand = new BaseCommand(_ => SwitchLanguage());
        _languageService.LanguageChanged += (_, _) => RefreshLanguage();
    }

    public ObservableCollection<NavigationItemViewModel> Items { get; }

    public NavigationItemViewModel SelectedItem
    {
        get => _selectedItem;
        private set
        {
            if (ReferenceEquals(_selectedItem, value))
            {
                return;
            }

            _selectedItem = value;
            OnPropertyChanged();
        }
    }

    public ICommand SelectCommand { get; }

    public ICommand SwitchLanguageCommand { get; }

    public string LanguageButtonText => IsCurrentChinese ? "EN" : "中";

    public string LanguageToolTip => IsCurrentChinese
        ? _languageService.GetString("Shell_Nav_SwitchToEnglish", "切换到 English")
        : _languageService.GetString("Shell_Nav_SwitchToChinese", "Switch to Chinese");

    private bool IsCurrentChinese => string.Equals(_languageService.Current.Name, "zh-CN", StringComparison.OrdinalIgnoreCase);

    private void Select(NavigationItemViewModel item)
    {
        if (!item.IsEnabled)
        {
            return;
        }

        foreach (var navigationItem in Items)
        {
            navigationItem.IsSelected = ReferenceEquals(navigationItem, item);
        }

        SelectedItem = item;
    }

    private void RefreshLanguage()
    {
        foreach (var item in Items)
        {
            item.RefreshLanguage();
        }

        OnPropertyChanged(nameof(LanguageButtonText));
        OnPropertyChanged(nameof(LanguageToolTip));
    }

    private void SwitchLanguage()
    {
        var targetCulture = IsCurrentChinese
            ? CultureInfo.GetCultureInfo("en-US")
            : CultureInfo.GetCultureInfo("zh-CN");

        _languageService.Change(targetCulture);
    }

    private static ObservableCollection<NavigationItemViewModel> CreateItems(IAppLanguageService languageService)
    {
        return
        [
            new(languageService, CoreViewIds.Dashboard, "Shell_Nav_Overview", "总览", "M4.5,11.5 L12,5 L19.5,11.5 M6.5,10.5 L6.5,19 L17.5,19 L17.5,10.5 M10,19 L10,14 L14,14 L14,19", true),
            new(languageService, CoreViewIds.ShellMonitor, "Shell_Nav_Monitor", "监控", "M4.5,18.5 L8.5,14.5 L11.5,16.5 L16.5,8.5 L19.5,11.5 M4.5,20 L19.5,20", true),
            new(languageService, CoreViewIds.ShellOperations, "Shell_Nav_Operations", "运维", "M7,7 L10,7 L10,10 L7,10 Z M14,14 L17,14 L17,17 L14,17 Z M10,10 L14,14 M12,7 L17,7 M7,17 L12,17", true),
            new(languageService, CoreViewIds.ShellConfiguration, "Shell_Nav_Configuration", "配置", "M4,7 L10,7 M14,7 L20,7 M10,5 L14,5 L14,9 L10,9 Z M4,12 L6,12 M10,12 L20,12 M6,10 L10,10 L10,14 L6,14 Z M4,17 L11,17 M15,17 L20,17 M11,15 L15,15 L15,19 L11,19 Z", true)
        ];
    }
}
