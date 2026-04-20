namespace IIoT.Edge.Shell.Modules;

public sealed class ShellModuleOptions
{
    public const string SectionName = "Modules";

    public List<string> Enabled { get; set; } = [];
}
