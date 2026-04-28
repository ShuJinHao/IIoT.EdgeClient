namespace IIoT.Edge.Application.Modules.Descriptors;

public enum ModulePanelPosition
{
    Left = 0,
    Right = 1,
    Bottom = 2,
    Main = 3
}

public sealed class ModulePanelDescriptor
{
    public string Title { get; set; } = string.Empty;

    public string TitleResourceKey { get; set; } = string.Empty;

    public string ContentId { get; set; } = string.Empty;

    public ModulePanelPosition InitialPosition { get; set; } = ModulePanelPosition.Main;

    public bool IsVisible { get; set; } = true;
}
