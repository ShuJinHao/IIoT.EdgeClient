namespace IIoT.Edge.UI.Avalonia.Modularity;

public sealed class AvaloniaMenuInfo
{
    public required string ViewId { get; init; }

    public required string TitleResourceKey { get; init; }

    public int Order { get; init; }
}
