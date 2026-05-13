namespace IIoT.Edge.UI.Avalonia.Modularity;

public sealed class AvaloniaDockPaneInfo
{
    public required string ViewId { get; init; }

    public required string TitleResourceKey { get; init; }

    public string DockGroup { get; init; } = "documents";

    public bool IsToolPane { get; init; }
}
