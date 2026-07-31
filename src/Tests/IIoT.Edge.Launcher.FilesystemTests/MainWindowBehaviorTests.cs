using Xunit;

namespace IIoT.Edge.Launcher.FilesystemTests;

public sealed class MainWindowBehaviorTests
{
    [Fact]
    public void MainWindow_HeroFlowChips_ShouldUseSharedResourceLabels()
    {
        var axaml = File.ReadAllText(ResolveLauncherAxamlPath("MainWindow.axaml"));

        Assert.Contains("Text=\"{DynamicResource Launcher_Hero_FlowLogin}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{DynamicResource Launcher_Hero_FlowProfile}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{DynamicResource Launcher_Hero_FlowShell}\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_ShouldKeepPasswordInputOnlyOnLoginPage()
    {
        var axaml = File.ReadAllText(ResolveLauncherAxamlPath("MainWindow.axaml"));

        Assert.Contains("x:Name=\"PasswordInput\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"NewPasswordInput\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_ShouldKeepLauncherTextStylesInSharedClasses()
    {
        var axaml = File.ReadAllText(ResolveLauncherAxamlPath("MainWindow.axaml"));

        AssertDoesNotDeclareInlineTextStyle(axaml);
        Assert.Contains("Text=\"{Binding WelcomeText}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ProfileSummaryText}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding StatusMessage}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding ErrorMessage}\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_ShouldKeepOperatorProcessNamesClean()
    {
        var axaml = File.ReadAllText(ResolveLauncherAxamlPath("MainWindow.axaml"));

        Assert.DoesNotContain("Text=\"{Binding ModuleId}\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding MachineProfile}\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding PluginDisplayPath}\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding DataDisplayPath}\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_UpdateCenter_ShouldUseSharedProgressAndResponsiveTableLayout()
    {
        var axaml = File.ReadAllText(ResolveLauncherAxamlPath("MainWindow.axaml"));

        Assert.Contains("x:Name=\"ClientReleaseProgressBar\"", axaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeProgressBar", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ProgressBar", axaml, StringComparison.Ordinal);
        Assert.Contains("Icon=\"{StaticResource Edge.Icon.Refresh}\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight=\"260\"", axaml, StringComparison.Ordinal);
        Assert.Contains("ViewportMaxHeight=\"150\"", axaml, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,Auto,Auto,*\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding StartupDiagnosticsText}\"", axaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeActionColumn", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RowDefinitions=\"Auto,2*,3*\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_ProfileLaunchButton_ShouldUseLauncherLocalBrandAccentTokens()
    {
        var appAxaml = File.ReadAllText(ResolveLauncherAxamlPath("App.axaml"));
        var windowAxaml = File.ReadAllText(ResolveLauncherAxamlPath("MainWindow.axaml"));

        Assert.Contains("Classes=\"launcher-brand-action\"", windowAxaml, StringComparison.Ordinal);
        Assert.Contains(
            "Value=\"{DynamicResource Edge.Brush.Accent.Primary}\"",
            appAxaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Value=\"{DynamicResource Edge.Brush.Text.OnAccent}\"",
            appAxaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("#", appAxaml, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionHistoryWindow_ShouldUseSharedProgressBar()
    {
        var axaml = File.ReadAllText(ResolveLauncherAxamlPath("VersionHistoryWindow.axaml"));

        Assert.Contains("x:Name=\"VersionHistoryProgressBar\"", axaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeProgressBar", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ProgressBar", axaml, StringComparison.Ordinal);
        Assert.Contains("ViewportMaxHeight=\"0\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangePasswordWindow_ShouldDeclareMaskedPasswordFields()
    {
        var axaml = File.ReadAllText(ResolveLauncherAxamlPath("ChangePasswordWindow.axaml"));

        Assert.Contains("x:Name=\"UserNameInput\"", axaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OldPasswordInput\"", axaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"NewPasswordInput\"", axaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConfirmPasswordInput\"", axaml, StringComparison.Ordinal);
        Assert.Contains("PasswordChar=\"●\"", axaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ConfirmButton\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"ConfirmButton_Click\"", axaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangePasswordWindow_ShouldKeepLauncherTextStylesInSharedClasses()
    {
        var axaml = File.ReadAllText(ResolveLauncherAxamlPath("ChangePasswordWindow.axaml"));

        AssertDoesNotDeclareInlineTextStyle(axaml);
    }

    private static void AssertDoesNotDeclareInlineTextStyle(string axaml)
    {
        Assert.DoesNotContain("FontSize=", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FontFamily=", axaml, StringComparison.Ordinal);
    }

    private static string ResolveLauncherAxamlPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Edge",
                "IIoT.Edge.Launcher",
                fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"未找到 Launcher {fileName}。");
    }
}
