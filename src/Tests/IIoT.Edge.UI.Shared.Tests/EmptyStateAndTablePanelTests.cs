using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using IIoT.Edge.UI.Shared.Avalonia.Views;
using Xunit;

namespace IIoT.Edge.UI.Shared.Tests;

public sealed class EmptyStateAndTablePanelTests
{
    [AvaloniaFact]
    public void EmptyStateView_DefaultsShouldBeLanguageNeutral()
    {
        var view = new EmptyStateView();

        Assert.Null(view.Title);
        Assert.Null(view.Message);
    }

    [AvaloniaFact]
    public void EdgeTablePanel_ErrorStateShouldSeparateTitleAndMessage()
    {
        const string title = "Query unavailable";
        const string message = "The real query failure detail remains visible.";
        var panel = new EdgeTablePanel
        {
            HasError = true,
            ErrorTitle = title,
            ErrorMessage = message
        };
        var window = new Window { Content = panel };

        try
        {
            window.Show();

            var errorState = panel
                .GetVisualDescendants()
                .OfType<EmptyStateView>()
                .Single(view => view.Classes.Contains("edge-table-error"));
            Assert.Equal(title, errorState.Title);
            Assert.Equal(message, errorState.Message);
        }
        finally
        {
            window.Close();
        }
    }
}
