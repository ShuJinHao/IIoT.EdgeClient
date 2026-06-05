using Xunit;

namespace IIoT.Edge.Launcher.Tests;

public sealed class MainWindowBehaviorTests
{
    [Fact]
    public void MainWindow_ShouldNotHardcodeProcessChipsInHero()
    {
        var axaml = File.ReadAllText(ResolveLauncherAxamlPath("MainWindow.axaml"));

        Assert.DoesNotContain("Text=\"叠片\"", axaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"匀浆\"", axaml, StringComparison.Ordinal);
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
