namespace IIoT.Edge.Application.Modules.Descriptors;

public sealed class ModuleMenuDescriptor
{
    public string Title { get; set; } = string.Empty;

    public string TitleResourceKey { get; set; } = string.Empty;

    public string ViewId { get; set; } = string.Empty;

    public string Icon { get; set; } = string.Empty;

    public int Order { get; set; }

    public string RequiredPermission { get; set; } = string.Empty;
}
