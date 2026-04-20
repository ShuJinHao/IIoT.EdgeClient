namespace IIoT.Edge.PackageValidationClient.Modules;

public sealed class PackageValidationModuleOptions
{
    public const string SectionName = "Modules";

    public List<string> Enabled { get; set; } = [];
}

