using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

public interface ILauncherPluginActivationReconciler
{
    void Reconcile();

    bool IsReady(LauncherPluginActivation activation);
}

public sealed class LauncherPluginActivationReconciler(
    string baseDirectory,
    ILauncherPluginActivationSource activationSource) : ILauncherPluginActivationReconciler
{
    private readonly HashSet<string> _readyActivations = new(StringComparer.OrdinalIgnoreCase);

    public void Reconcile()
    {
        _readyActivations.Clear();
        foreach (var activation in activationSource.LoadActivations())
        {
            try
            {
                ReconcileOne(activation);
                _readyActivations.Add(ActivationKey(activation));
            }
            catch (Exception ex) when (ex is IOException
                                           or UnauthorizedAccessException
                                           or JsonException
                                           or InvalidOperationException
                                           or ArgumentException)
            {
                Trace.TraceWarning(
                    "插件 activation 配置未应用：{0}/{1} ({2})",
                    activation.ModuleId,
                    activation.ProfileId,
                    ex.GetType().Name);
            }
        }
    }

    public bool IsReady(LauncherPluginActivation activation)
        => _readyActivations.Contains(ActivationKey(activation));

    private void ReconcileOne(LauncherPluginActivation activation)
    {
        LauncherPluginActivationSource.ValidateActivationFiles(activation);
        var template = ReadObject(activation.MachineConfigPath);

        var hostDirectory = Path.GetFullPath(Path.Combine(baseDirectory, "..", "host"));
        var targetPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
            activation.ProfileId,
            hostDirectory);
        JsonObject target;
        if (File.Exists(targetPath))
        {
            target = ReadObject(targetPath);
            MergeMissing(target, template);
        }
        else
        {
            target = template.DeepClone().AsObject();
        }

        EnsureOwningModuleEnabled(target, activation.ModuleId);
        WriteObjectAtomically(targetPath, target);
    }

    private static JsonObject ReadObject(string path)
        => JsonNode.Parse(File.ReadAllText(path)) as JsonObject
           ?? throw new InvalidOperationException($"JSON 根节点必须是对象：{path}");

    private static void MergeMissing(JsonObject target, JsonObject template)
    {
        foreach (var property in template)
        {
            if (!target.TryGetPropertyValue(property.Key, out var current) || current is null)
            {
                target[property.Key] = property.Value?.DeepClone();
                continue;
            }

            if (current is JsonObject currentObject && property.Value is JsonObject templateObject)
            {
                MergeMissing(currentObject, templateObject);
            }
        }
    }

    private static void EnsureOwningModuleEnabled(JsonObject target, string moduleId)
    {
        if (target["Modules"] is not JsonObject modules)
        {
            modules = new JsonObject();
            target["Modules"] = modules;
        }

        if (modules["Enabled"] is not JsonArray enabled)
        {
            enabled = [];
            modules["Enabled"] = enabled;
        }

        var values = enabled
            .Where(static node => node is JsonValue)
            .Select(static node => node!.GetValue<string>()?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        values.Add(moduleId);
        modules["Enabled"] = new JsonArray(
            values
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .Select(static value => JsonValue.Create(value))
                .Cast<JsonNode?>()
                .ToArray());
    }

    private static void WriteObjectAtomically(string path, JsonObject root)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("外部机器配置缺少目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string ActivationKey(LauncherPluginActivation activation)
        => $"{activation.ModuleId}\0{activation.ProfileId}";
}
