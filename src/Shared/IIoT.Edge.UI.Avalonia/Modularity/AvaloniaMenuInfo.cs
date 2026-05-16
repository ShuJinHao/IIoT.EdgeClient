namespace IIoT.Edge.UI.Avalonia.Modularity;

public sealed class AvaloniaMenuInfo
{
    public required string ViewId { get; init; }

    public string Title { get; init; } = string.Empty;

    public required string TitleResourceKey { get; init; }

    public string Icon { get; init; } = string.Empty;

    public int Order { get; init; }

    public string RequiredPermission { get; init; } = string.Empty;
}
