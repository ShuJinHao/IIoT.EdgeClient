using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using IIoT.Edge.Presentation.Navigation.Features.Formula.RecipeView;
using Xunit;

namespace IIoT.Edge.Shell.UiTests;

public sealed class RecipeViewPageHeadlessTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void EmergencyEditor_ShouldFollowLocalAdminVisibility(
        bool isLocalAdmin,
        bool expectedVisibility)
    {
        var view = new RecipeViewPage
        {
            DataContext = new RecipeAccessState(isLocalAdmin)
        };
        var window = new Window
        {
            Content = view,
            Width = 1200,
            Height = 800
        };

        try
        {
            window.Show();

            var emergencyEditCard = view.FindControl<Control>("EmergencyEditCard");
            Assert.NotNull(emergencyEditCard);
            Assert.Equal(expectedVisibility, emergencyEditCard.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed record RecipeAccessState(bool IsLocalAdmin);
}
