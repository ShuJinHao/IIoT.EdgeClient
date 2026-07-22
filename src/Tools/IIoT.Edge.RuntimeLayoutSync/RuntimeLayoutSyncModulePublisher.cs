internal sealed class RuntimeLayoutSyncModulePublisher(IRuntimeLayoutSyncFileSystem fileSystem) : IRuntimeLayoutSyncModulePublisher
{
    public void PublishModulesToPluginsRoot(
        IReadOnlyList<string> moduleIds,
        string targetPluginsRoot)
    {
        var uniqueModuleIds = moduleIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (uniqueModuleIds.Length != 0)
        {
            throw new InvalidOperationException(
                $"Host runtime layout sync does not compile plugin source. " +
                $"Profiles requested external modules: {string.Join(", ", uniqueModuleIds)}. " +
                "Compose packaged plugin artifacts through the separately authorized plugin composition workflow.");
        }

        fileSystem.RemoveDirectoryIfExists(targetPluginsRoot);
        fileSystem.CreateDirectory(targetPluginsRoot);
    }
}
