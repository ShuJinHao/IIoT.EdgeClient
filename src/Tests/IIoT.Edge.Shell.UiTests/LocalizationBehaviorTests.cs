using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using IIoT.Edge.Presentation.Navigation.Features.Shell;
using IIoT.Edge.Presentation.Shell.Localization;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using Xunit;

namespace IIoT.Edge.Shell.UiTests;

public sealed class LocalizationBehaviorTests
{
    [Fact]
    public void AppLanguageService_Change_UpdatesCultureAndPersistsState()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var tempFile = CreateLanguageStateFilePath();

        try
        {
            var service = new AppLanguageService(tempFile);
            service.Initialize();

            service.Change(CultureInfo.GetCultureInfo("en-US"));

            Assert.Equal("en-US", service.Current.Name);
            Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
            Assert.True(File.Exists(tempFile));

            var reloaded = new AppLanguageService(tempFile);
            Assert.Equal("en-US", reloaded.Current.Name);
        }
        finally
        {
            RestoreCulture(originalCulture, originalUiCulture);
            TryDeleteDirectory(Path.GetDirectoryName(tempFile));
        }
    }

    [Fact]
    public void AppLanguageService_Change_LoadsVisibleUiResourceDictionaries()
    {
        AssertDictionaryString(
            "IIoT.Edge.Presentation.Shell",
            "zh-CN",
            "Shell_Footer_Executing",
            "\u6B63\u5728\u6267\u884C");
        AssertDictionaryString(
            "IIoT.Edge.Presentation.Shell",
            "zh-CN",
            "Shell_Footer_RunMinutesFormat",
            "{0} \u5206\u949F");
        AssertDictionaryString(
            "IIoT.Edge.Presentation.Navigation",
            "zh-CN",
            "Navigation_DashboardPreview_Title",
            "\u4ECA\u65E5\u4EA7\u7EBF\u603B\u89C8");
        AssertDictionaryString(
            "IIoT.Edge.Presentation.Panels",
            "zh-CN",
            "Panels_Title_SystemLog",
            "\u7CFB\u7EDF\u65E5\u5FD7");

        AssertDictionaryString(
            "IIoT.Edge.Presentation.Shell",
            "en-US",
            "Shell_Footer_Executing",
            "Executing");
        AssertDictionaryString(
            "IIoT.Edge.Presentation.Shell",
            "en-US",
            "Shell_Footer_RunMinutesFormat",
            "{0} min");
        AssertDictionaryString(
            "IIoT.Edge.Presentation.Navigation",
            "en-US",
            "Navigation_DashboardPreview_Title",
            "Production Overview");
        AssertDictionaryString(
            "IIoT.Edge.Presentation.Panels",
            "en-US",
            "Panels_Title_SystemLog",
            "System Log");
    }

    [Fact]
    public void ShellFooterView_ShouldNotRenderHardcodedCloudOrMesPrefixes()
    {
        var root = FindRepositoryRoot();
        var axaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Shell",
            "Views",
            "ShellFooterView.axaml"));

        Assert.DoesNotContain("Cloud: ", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MES: ", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{DynamicResource Shell_Footer_Executing}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{DynamicResource Shell_Footer_RunTime}\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationRail_LanguageCommand_TogglesCultureAndButtonText()
    {
        var languageService = new TestAppLanguageService();
        var viewModel = CreateNavigationRailViewModelForLanguageCommand(languageService);

        Assert.Equal("zh-CN", languageService.Current.Name);
        Assert.Equal("EN", viewModel.LanguageButtonText);
        Assert.True(viewModel.SwitchLanguageCommand.CanExecute(null));

        viewModel.SwitchLanguageCommand.Execute(null);

        Assert.Equal("en-US", languageService.Current.Name);
        Assert.Equal("中", viewModel.LanguageButtonText);

        viewModel.SwitchLanguageCommand.Execute(null);

        Assert.Equal("zh-CN", languageService.Current.Name);
        Assert.Equal("EN", viewModel.LanguageButtonText);
    }

    private static NavigationRailViewModel CreateNavigationRailViewModelForLanguageCommand(TestAppLanguageService languageService)
    {
        var type = typeof(NavigationRailViewModel);
        var viewModel = (NavigationRailViewModel)RuntimeHelpers.GetUninitializedObject(type);
        type.GetField("_languageService", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(viewModel, languageService);

        var switchLanguage = type.GetMethod("SwitchLanguage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var command = new BaseCommand(_ => switchLanguage.Invoke(viewModel, null));
        type.GetField("<SwitchLanguageCommand>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(viewModel, command);

        return viewModel;
    }

    private static string CreateLanguageStateFilePath()
        => Path.Combine(Path.GetTempPath(), "edge-language-tests", Guid.NewGuid().ToString("N"), "language.json");

    private static void AssertDictionaryString(string assemblyName, string cultureName, string key, string expected)
    {
        var root = FindRepositoryRoot();
        var projectDirectory = assemblyName switch
        {
            "IIoT.Edge.Presentation.Shell" => Path.Combine(root, "src", "Presentation", "IIoT.Edge.Presentation.Shell"),
            "IIoT.Edge.Presentation.Navigation" => Path.Combine(root, "src", "Presentation", "IIoT.Edge.Presentation.Navigation"),
            "IIoT.Edge.Presentation.Panels" => Path.Combine(root, "src", "Presentation", "IIoT.Edge.Presentation.Panels"),
            _ => throw new ArgumentOutOfRangeException(nameof(assemblyName), assemblyName, null)
        };
        var path = Path.Combine(projectDirectory, "Resources", "Languages", $"{cultureName}.axaml");
        var document = XDocument.Load(path);
        var keyName = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
        var value = document
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Attribute(keyName)?.Value, key, StringComparison.Ordinal))
            ?.Value;

        Assert.Equal(expected, value);
    }

    private static void RestoreCulture(CultureInfo originalCulture, CultureInfo originalUiCulture)
    {
        CultureInfo.CurrentCulture = originalCulture;
        CultureInfo.CurrentUICulture = originalUiCulture;
        CultureInfo.DefaultThreadCurrentCulture = originalCulture;
        CultureInfo.DefaultThreadCurrentUICulture = originalUiCulture;
        Thread.CurrentThread.CurrentCulture = originalCulture;
        Thread.CurrentThread.CurrentUICulture = originalUiCulture;
    }

    private static void TryDeleteDirectory(string? directory)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 IIoT.EdgeClient 仓库根目录。");
    }

    private sealed class TestAppLanguageService : IAppLanguageService
    {
        public CultureInfo Current { get; private set; } = CultureInfo.GetCultureInfo("zh-CN");

        public LanguageOption CurrentOption => SupportedLanguages.First(x => x.Culture.Name == Current.Name);

        public IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
        [
            new(CultureInfo.GetCultureInfo("zh-CN"), "中文"),
            new(CultureInfo.GetCultureInfo("en-US"), "English")
        ];

        public event EventHandler? LanguageChanged;

        public void Initialize()
        {
        }

        public void Change(CultureInfo culture)
        {
            Current = culture;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetString(string key, string fallback = "") => fallback;

        public string Format(string key, string fallback, params object[] args)
            => string.Format(CultureInfo.CurrentCulture, fallback, args);
    }

}
