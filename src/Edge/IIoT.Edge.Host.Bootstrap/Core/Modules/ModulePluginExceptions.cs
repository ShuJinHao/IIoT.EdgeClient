namespace IIoT.Edge.Host.Bootstrap.Modules;

public sealed class ModulePluginManifestException : InvalidOperationException
{
    public ModulePluginManifestException(string message)
        : base(message)
    {
    }

    public ModulePluginManifestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ModulePluginLoadException : InvalidOperationException
{
    public ModulePluginLoadException(string message)
        : base(message)
    {
    }

    public ModulePluginLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
