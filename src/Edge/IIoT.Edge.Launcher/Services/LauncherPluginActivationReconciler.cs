using System.Diagnostics;
using System.Security;
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

public sealed class LauncherPluginActivationReconciler : ILauncherPluginActivationReconciler
{
    private readonly HashSet<string> _readyActivations = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _baseDirectory;
    private readonly LauncherHostRuntimeResolver _hostRuntimeResolver;
    private readonly ILauncherPluginActivationSource _activationSource;
    private readonly ILauncherStartupDiagnosticWriter? _diagnostics;

    public LauncherPluginActivationReconciler(
        string baseDirectory,
        ILauncherPluginActivationSource activationSource,
        ILauncherStartupDiagnosticWriter? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = baseDirectory;
        _activationSource = activationSource
            ?? throw new ArgumentNullException(nameof(activationSource));
        _diagnostics = diagnostics;
        _hostRuntimeResolver = new LauncherHostRuntimeResolver(baseDirectory);
    }

    public void Reconcile()
    {
        _readyActivations.Clear();
        var runtimeBindingPath = EdgeClientProgramDataPaths.ResolveRuntimeBindingPath(_baseDirectory);
        if (File.Exists(runtimeBindingPath)
            && EdgeInstallerBindingCodec.ParseRuntime(File.ReadAllText(runtimeBindingPath)).SchemaVersion
            == EdgeInstallerBindingCodec.CurrentSchemaVersion)
        {
            _diagnostics?.ReplaceArea(
                LauncherStartupDiagnosticAreas.PluginActivationMaterialization,
                []);
            return;
        }

        var reconciliationDiagnostics = new List<LauncherStartupDiagnostic>();
        foreach (var activation in _activationSource.LoadActivations())
        {
            try
            {
                ReconcileOne(activation);
                _readyActivations.Add(ActivationKey(activation));
            }
            catch (Exception ex) when (ex is IOException
                                           or UnauthorizedAccessException
                                           or SecurityException
                                           or JsonException
                                           or InvalidOperationException
                                           or ArgumentException
                                           or NotSupportedException)
            {
                Trace.TraceWarning(
                    "插件 activation 配置未应用：{0}/{1} ({2})",
                    activation.ModuleId,
                    activation.ProfileId,
                    ex.GetType().Name);
                reconciliationDiagnostics.Add(new LauncherStartupDiagnostic(
                    LauncherStartupDiagnosticAreas.PluginActivationMaterialization,
                    "LAUNCHER_PLUGIN_ACTIVATION_APPLY_FAILED",
                    LauncherStartupDiagnosticRepairTargets.PluginActivation,
                    $"{activation.ModuleId}/{activation.ProfileId}",
                    ex.GetType().Name));
            }
        }

        _diagnostics?.ReplaceArea(
            LauncherStartupDiagnosticAreas.PluginActivationMaterialization,
            reconciliationDiagnostics);
    }

    public bool IsReady(LauncherPluginActivation activation)
        => _readyActivations.Contains(ActivationKey(activation));

    private void ReconcileOne(LauncherPluginActivation activation)
    {
        LauncherPluginActivationSource.ValidateActivationFiles(activation);
        var template = ReadObject(activation.MachineConfigPath);

        var hostDirectory = _hostRuntimeResolver.Resolve().HostDirectory;
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
