using Avalonia;
using Avalonia.Controls;

namespace IIoT.Edge.Presentation.Navigation.Features.Shell;

public partial class PlaceholderPageView : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<PlaceholderPageView, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<PlaceholderPageView, string>(nameof(Description), string.Empty);

    public PlaceholderPageView()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
