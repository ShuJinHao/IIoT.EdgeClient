namespace IIoT.Edge.Application.Abstractions.Modules;

public enum EdgeAnchorablePosition
{
    Left = 0,
    Right = 1,
    Bottom = 2,
    Main = 3
}

public sealed class EdgeAnchorableInfo
{
    public string Title { get; set; } = string.Empty;

    public string TitleResourceKey { get; set; } = string.Empty;

    public string ContentId { get; set; } = string.Empty;

    public EdgeAnchorablePosition InitialPosition { get; set; } = EdgeAnchorablePosition.Main;

    public bool IsVisible { get; set; } = true;
}
