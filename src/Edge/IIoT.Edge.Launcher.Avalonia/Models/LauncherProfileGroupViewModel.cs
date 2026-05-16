namespace IIoT.Edge.Launcher.Models;

public sealed class LauncherProfileGroupViewModel
{
    public LauncherProfileGroupViewModel(
        string displayName,
        string description,
        string machineProfile,
        LauncherProfileDefinition primaryProfile,
        IReadOnlyList<LauncherProfileDefinition> variants)
    {
        DisplayName = displayName;
        Description = description;
        MachineProfile = machineProfile;
        PrimaryProfile = primaryProfile;
        Variants = variants;
    }

    public string DisplayName { get; }

    public string Description { get; }

    public string MachineProfile { get; }

    public LauncherProfileDefinition PrimaryProfile { get; }

    public IReadOnlyList<LauncherProfileDefinition> Variants { get; }

    public bool HasVariants => Variants.Count > 1;
}
