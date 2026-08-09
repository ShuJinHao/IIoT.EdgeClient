namespace IIoT.Edge.Host.Bootstrap.Modules;

public sealed record ModulePluginDescriptor(
    string ModuleId,
    string ProcessType,
    string DisplayName,
    string Version,
    string HostApiVersion,
    string MinHostVersion,
    string MaxHostVersion,
    IReadOnlyList<string> Dependencies,
    string AssemblyName,
    string EntryTypeName,
    string PluginDirectory,
    string ManifestPath,
    string EntryAssemblyPath,
    ModulePluginConfigurationContract? ConfigurationContract = null,
    ModulePluginPrivateDatabaseContract? PrivateDatabaseContract = null);

public sealed record ModulePluginConfigurationContract(
    string SchemaRelativePath,
    string SchemaPath,
    int SeedSchemaVersion,
    int CurrentSeedVersion,
    IReadOnlyList<string> SupportedEnvironments,
    bool RequiresProductionPlan);

public sealed record ModulePluginPrivateDatabaseContract(
    int SchemaVersion,
    string LifecycleContractVersion,
    string ConfigurationContractVersion,
    string MigrationAssembly,
    string EntryPoint,
    bool RequiresProductionPlan);
