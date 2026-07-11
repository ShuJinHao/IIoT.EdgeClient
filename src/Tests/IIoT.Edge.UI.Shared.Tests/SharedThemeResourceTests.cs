using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace IIoT.Edge.UI.Shared.Tests;

public sealed class SharedThemeResourceTests
{
    [AvaloniaFact]
    public void SharedTheme_ExposesTheSingleSpacing2Resource()
    {
        var application = Assert.IsType<SharedUiTestAvaloniaApplication>(Application.Current);

        Assert.True(application.TryFindResource("Edge.Size.Spacing2", out var resource));
        Assert.Equal(2d, Assert.IsType<double>(resource));
    }
}
