namespace IIoT.Edge.Plugin.Shared.Modules;

public sealed class EdgeMenuInfo
{
    public string Title { get; set; } = string.Empty;

    public string ViewId { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;

    public int Order { get; set; }

    public string RequiredPermission { get; set; } = string.Empty;
}
