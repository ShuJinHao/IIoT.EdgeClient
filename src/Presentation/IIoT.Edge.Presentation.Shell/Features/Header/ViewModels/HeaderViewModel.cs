using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace IIoT.Edge.Presentation.Shell.Features.Header;

public class HeaderViewModel : ViewModelBase
{
    private readonly IAppLanguageService _languageService;
    private string _currentUser = "Admin";
    private string _maxRestoreIcon = "WindowMaximize";

    public HeaderViewModel(IAppLanguageService languageService)
    {
        _languageService = languageService;

        LayoutRow = 0;
        LayoutColumn = 0;
        ColumnSpan = 12;

        WindowControlCommand = new BaseCommand(ExecuteWindowControl);
        WindowDragCommand = new BaseCommand(ExecuteWindowDrag);
        _languageService.LanguageChanged += (_, _) => OnPropertyChanged(nameof(SelectedLanguage));
    }

    public override string ViewId => "Core.SystemHeader";
    public override string ViewTitle => _languageService.GetString("Shell_ViewTitle_Header", "Header");

    public string CurrentUser
    {
        get => _currentUser;
        set { _currentUser = value; OnPropertyChanged(); }
    }

    public string MaxRestoreIcon
    {
        get => _maxRestoreIcon;
        set { _maxRestoreIcon = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<LanguageOption> SupportedLanguages => _languageService.SupportedLanguages;

    public LanguageOption SelectedLanguage
    {
        get => _languageService.CurrentOption;
        set
        {
            if (value is not null)
            {
                _languageService.Change(value.Culture);
            }
        }
    }

    public ICommand WindowControlCommand { get; }
    public ICommand WindowDragCommand { get; }

    private void ExecuteWindowControl(object? parameter)
    {
        if (parameter is null)
        {
            return;
        }

        var win = System.Windows.Application.Current.Windows
                      .OfType<Window>()
                      .FirstOrDefault(w => w.IsActive)
                  ?? System.Windows.Application.Current.MainWindow;

        if (win is null)
        {
            return;
        }

        switch (parameter.ToString())
        {
            case "Min":
                win.WindowState = WindowState.Minimized;
                break;
            case "Max":
                win.WindowState = win.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
                MaxRestoreIcon = win.WindowState == WindowState.Maximized
                    ? "WindowRestore"
                    : "WindowMaximize";
                break;
            case "Close":
                System.Windows.Application.Current.Shutdown();
                break;
        }
    }

    private void ExecuteWindowDrag(object? parameter)
    {
        var win = System.Windows.Application.Current.Windows
                      .OfType<Window>()
                      .FirstOrDefault(w => w.IsActive)
                  ?? System.Windows.Application.Current.MainWindow;

        if (win?.WindowState == WindowState.Maximized)
        {
            win.WindowState = WindowState.Normal;
            MaxRestoreIcon = "WindowMaximize";
        }

        win?.DragMove();
    }
}
