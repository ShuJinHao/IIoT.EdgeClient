using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

/// <summary>
/// 首装绑定导入器：客户端首次启动时读取随下载包附带的 <c>iiot-binding.json</c>，
/// 把云端为每个插件分配的设备唯一码（ClientCode）写入对应 profile 的外部机器配置，
/// 实现“下载即配置、现场零操作”。导入完成后归档绑定文件，避免下次启动重复导入；
/// 若用户重装并重新放入新的绑定文件，则会按新文件再次写入（唯一码可复用）。
    /// 启动红线：缺文件、JSON 损坏、无匹配 profile 等可恢复输入问题只保留 pending；
    /// 程序错误不得被无限 catch 后伪装成导入成功。
/// </summary>
public interface ILauncherDeviceBindingImporter
{
    void ApplyPendingBindings();
}

public sealed class LauncherDeviceBindingImporter : ILauncherDeviceBindingImporter
{
    public const string BindingFileName = "iiot-binding.json";
    private const int SupportedBindingSchemaVersion = 2;
    private const string DeviceIdTemplate = "{deviceId}";

    private readonly string _baseDirectory;
    private readonly ILauncherProfileCatalog _profileCatalog;
    private readonly IEdgeProfileModuleConfigurationStore _moduleConfiguration;
    private readonly ILauncherUpdateTargetFactory _targetFactory;
    private readonly ILauncherStartupDiagnosticWriter? _startupDiagnostics;

    public LauncherDeviceBindingImporter(
        string baseDirectory,
        ILauncherProfileCatalog profileCatalog,
        IEdgeProfileModuleConfigurationStore moduleConfiguration,
        ILauncherUpdateTargetFactory targetFactory,
        ILauncherStartupDiagnosticWriter? startupDiagnostics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = baseDirectory;
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _moduleConfiguration = moduleConfiguration ?? throw new ArgumentNullException(nameof(moduleConfiguration));
        _targetFactory = targetFactory ?? throw new ArgumentNullException(nameof(targetFactory));
        _startupDiagnostics = startupDiagnostics;
    }

    public void ApplyPendingBindings()
    {
        try
        {
            var bindingPath = ResolvePendingBindingPath();
            if (bindingPath is null)
            {
                ReplaceBindingDiagnostics([]);
                return;
            }

            if (!TryParseBindingBundle(bindingPath, out var bundle))
            {
                ReplaceBindingDiagnostics(
                [
                    CreateBindingDiagnostic("LAUNCHER_DEVICE_BINDING_INVALID")
                ]);
                return;
            }

            var profiles = _profileCatalog.LoadProfiles();
            var applied = new List<DeviceBinding>();
            var unresolved = new List<DeviceBinding>();
            foreach (var binding in bundle.Bindings)
            {
                if (TryApplyOneBinding(profiles, binding, bundle))
                {
                    applied.Add(binding);
                }
                else
                {
                    unresolved.Add(binding);
                }
            }

            FinalizeAppliedBindings(bindingPath, applied, unresolved, bundle);
            ReplaceBindingDiagnostics(
                unresolved
                    .Select(binding => CreateBindingDiagnostic(
                        "LAUNCHER_DEVICE_BINDING_PENDING",
                        binding.ModuleId))
                    .ToArray());
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            // 非阻断：可恢复的绑定文件或文件系统问题保留原 pending，等待修复后重试。
            ReplaceBindingDiagnostics(
            [
                CreateBindingDiagnostic(
                    "LAUNCHER_DEVICE_BINDING_IMPORT_FAILED",
                    exceptionType: ex.GetType().Name)
            ]);
        }
    }

    private string? ResolvePendingBindingPath()
    {
        var dataPath = Path.Combine(
            EdgeClientProgramDataPaths.ResolveLauncherDirectory(_baseDirectory),
            BindingFileName);
        return File.Exists(dataPath) ? dataPath : null;
    }

    private bool TryApplyOneBinding(
        IReadOnlyList<LauncherProfileDefinition> profiles,
        DeviceBinding binding,
        DeviceBindingBundle bundle)
    {
        try
        {
            // moduleId -> profile：匹配“机器配置 Modules.Enabled 含该 module”的 profile（与规则一致）。
            var target = profiles.FirstOrDefault(profile =>
                _moduleConfiguration.ReadEnabledModules(_targetFactory.Create(profile))
                    .Any(moduleId => string.Equals(moduleId, binding.ModuleId, StringComparison.OrdinalIgnoreCase)));

            if (target is null)
            {
                // 没有匹配 profile：保留待处理绑定，等待对应插件安装。
                return false;
            }

            WriteCloudApiIdentity(
                _targetFactory.Create(target),
                binding.ClientCode,
                binding.BootstrapSecret,
                bundle.BaseUrl,
                bundle.Paths);
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or JsonException
                                       or InvalidOperationException
                                       or ArgumentException)
        {
            return false;
        }
    }

    private static void WriteCloudApiIdentity(
        EdgeUpdateTarget target,
        string clientCode,
        string bootstrapSecret,
        string baseUrl,
        BindingPaths paths)
    {
        var targetPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
            target.MachineProfile,
            target.HostDirectory);
        var sourcePath = File.Exists(targetPath)
            ? targetPath
            : Path.Combine(target.HostDirectory, $"appsettings.machine.{target.MachineProfile}.json");
        var root = File.Exists(sourcePath)
            ? JsonNode.Parse(File.ReadAllText(sourcePath))?.AsObject()
              ?? throw new JsonException("机器配置根节点不能为空。")
            : new JsonObject();

        if (root["CloudApi"] is not JsonObject cloudApi)
        {
            cloudApi = new JsonObject();
            root["CloudApi"] = cloudApi;
        }

        // 只有 Cloud 下载包同时提供完整地址、设备寻址码和轮换后的启动密钥时才启用。
        cloudApi["Enabled"] = true;
        cloudApi["ClientCode"] = clientCode;
        cloudApi["BootstrapSecret"] = bootstrapSecret;
        cloudApi["BaseUrl"] = baseUrl;

        if (cloudApi["Paths"] is not JsonObject cloudPaths)
        {
            cloudPaths = new JsonObject();
            cloudApi["Paths"] = cloudPaths;
        }

        cloudPaths["DeviceInstance"] = paths.DeviceInstance;
        cloudPaths["ClientReleaseCatalogTemplate"] =
            paths.ClientReleaseCatalogTemplate;
        cloudPaths["ClientVersionReport"] = paths.ClientVersionReport;
        cloudPaths["RuntimeHeartbeat"] = paths.RuntimeHeartbeat;

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        WriteJsonAtomically(targetPath, root);
    }

    private static bool TryParseBindingBundle(
        string bindingPath,
        out DeviceBindingBundle bundle)
    {
        bundle = default;

        using var document = JsonDocument.Parse(File.ReadAllText(bindingPath));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out var schemaVersion)
            || schemaVersion.ValueKind != JsonValueKind.Number
            || !schemaVersion.TryGetInt32(out var parsedSchemaVersion)
            || parsedSchemaVersion != SupportedBindingSchemaVersion
            || !TryNormalizeHttpBaseUrl(
                ReadString(root, "baseUrl"),
                out var baseUrl)
            || !TryReadPaths(root, out var paths)
            || !TryParseGeneratedAtUtc(root, out var generatedAtUtc)
            || !root.TryGetProperty("bindings", out var bindingsElement)
            || bindingsElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var bindings = new List<DeviceBinding>();
        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in bindingsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var moduleId = ReadString(item, "moduleId")?.Trim();
            var clientCode = ReadString(item, "clientCode")?.Trim();
            var bootstrapSecret = ReadString(item, "bootstrapSecret")?.Trim();
            var deviceName = ReadString(item, "deviceName")?.Trim();
            if (string.IsNullOrWhiteSpace(moduleId)
                || !moduleIds.Add(moduleId)
                || string.IsNullOrWhiteSpace(clientCode)
                || string.IsNullOrWhiteSpace(bootstrapSecret)
                || string.IsNullOrWhiteSpace(deviceName)
                || !item.TryGetProperty("processId", out var processIdElement)
                || processIdElement.ValueKind != JsonValueKind.String
                || !Guid.TryParse(processIdElement.GetString(), out var processId)
                || processId == Guid.Empty)
            {
                return false;
            }

            bindings.Add(new DeviceBinding(
                moduleId,
                clientCode,
                bootstrapSecret,
                deviceName,
                processId));
        }

        if (bindings.Count == 0)
        {
            return false;
        }

        bundle = new DeviceBindingBundle(
            baseUrl,
            paths,
            generatedAtUtc.ToUniversalTime(),
            bindings);
        return true;
    }

    // 只消费已经成功写入机器配置的绑定：摘要永不包含启动密钥；未匹配插件的原始绑定
    // 保留在 pending 文件中，供插件安装后的下一次启动继续处理。
    private void FinalizeAppliedBindings(
        string bindingPath,
        IReadOnlyList<DeviceBinding> applied,
        IReadOnlyList<DeviceBinding> unresolved,
        DeviceBindingBundle bundle)
    {
        if (applied.Count == 0)
        {
            return;
        }

        var launcherDirectory = EdgeClientProgramDataPaths.ResolveLauncherDirectory(_baseDirectory);
        Directory.CreateDirectory(launcherDirectory);

        var summary = new JsonObject
        {
            ["schemaVersion"] = SupportedBindingSchemaVersion,
            ["appliedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["baseUrl"] = bundle.BaseUrl,
            ["paths"] = CreatePathsJson(bundle.Paths),
            ["bindings"] = new JsonArray(
                applied.Select(binding => (JsonNode?)new JsonObject
                {
                    ["moduleId"] = binding.ModuleId,
                    ["clientCode"] = binding.ClientCode,
                }).ToArray()),
        };

        var summaryPath = Path.Combine(
            launcherDirectory,
            $"iiot-binding.applied.{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
        File.WriteAllText(
            summaryPath,
            summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (unresolved.Count == 0)
        {
            File.Delete(bindingPath);
            return;
        }

        var pending = new JsonObject
        {
            ["schemaVersion"] = SupportedBindingSchemaVersion,
            ["baseUrl"] = bundle.BaseUrl,
            ["paths"] = CreatePathsJson(bundle.Paths),
            ["generatedAtUtc"] = bundle.GeneratedAtUtc.ToString(
                "O",
                CultureInfo.InvariantCulture),
            ["bindings"] = new JsonArray(
                unresolved.Select(binding => (JsonNode?)new JsonObject
                {
                    ["moduleId"] = binding.ModuleId,
                    ["clientCode"] = binding.ClientCode,
                    ["bootstrapSecret"] = binding.BootstrapSecret,
                    ["deviceName"] = binding.DeviceName,
                    ["processId"] = binding.ProcessId.ToString("D"),
                }).ToArray()),
        };
        WritePendingBindingsAtomically(bindingPath, pending);
    }

    private void ReplaceBindingDiagnostics(
        IReadOnlyCollection<LauncherStartupDiagnostic> values)
        => _startupDiagnostics?.ReplaceArea(
            LauncherStartupDiagnosticAreas.DeviceBinding,
            values);

    private static LauncherStartupDiagnostic CreateBindingDiagnostic(
        string reasonCode,
        string? subject = null,
        string? exceptionType = null)
        => new(
            LauncherStartupDiagnosticAreas.DeviceBinding,
            reasonCode,
            LauncherStartupDiagnosticRepairTargets.DeviceBinding,
            subject,
            exceptionType);

    private static void WritePendingBindingsAtomically(string bindingPath, JsonObject pending)
        => WriteJsonAtomically(bindingPath, pending);

    private static void WriteJsonAtomically(string targetPath, JsonObject payload)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("目标文件缺少目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool TryNormalizeHttpBaseUrl(string? rawValue, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue)
            || !Uri.TryCreate(rawValue.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private static bool TryReadPaths(
        JsonElement root,
        out BindingPaths paths)
    {
        paths = default;
        if (!root.TryGetProperty("paths", out var pathsElement)
            || pathsElement.ValueKind != JsonValueKind.Object
            || !TryNormalizeRelativePath(
                ReadString(pathsElement, "deviceInstance"),
                requiresDeviceIdTemplate: false,
                out var deviceInstance)
            || !TryNormalizeRelativePath(
                ReadString(pathsElement, "clientReleaseCatalogTemplate"),
                requiresDeviceIdTemplate: true,
                out var catalogTemplate)
            || !TryNormalizeRelativePath(
                ReadString(pathsElement, "clientVersionReport"),
                requiresDeviceIdTemplate: false,
                out var versionReport)
            || !TryNormalizeRelativePath(
                ReadString(pathsElement, "runtimeHeartbeat"),
                requiresDeviceIdTemplate: false,
                out var runtimeHeartbeat))
        {
            return false;
        }

        paths = new BindingPaths(
            deviceInstance,
            catalogTemplate,
            versionReport,
            runtimeHeartbeat);
        return true;
    }

    private static bool TryNormalizeRelativePath(
        string? rawValue,
        bool requiresDeviceIdTemplate,
        out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var value = rawValue.Trim();
        if (!value.StartsWith("/", StringComparison.Ordinal)
            || value.StartsWith("//", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Contains('?')
            || value.Contains('#')
            || value.Any(char.IsControl))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (decoded.StartsWith("//", StringComparison.Ordinal)
            || decoded.Contains('\\')
            || decoded.Contains('?')
            || decoded.Contains('#')
            || decoded.Any(char.IsControl)
            || decoded.Split('/').Any(segment => segment is "." or ".."))
        {
            return false;
        }

        var tokenCount = CountOccurrences(value, DeviceIdTemplate);
        var withoutDeviceId = value.Replace(
            DeviceIdTemplate,
            string.Empty,
            StringComparison.Ordinal);
        if ((requiresDeviceIdTemplate && tokenCount != 1)
            || (!requiresDeviceIdTemplate && tokenCount != 0)
            || withoutDeviceId.Contains('{')
            || withoutDeviceId.Contains('}'))
        {
            return false;
        }

        normalized = value;
        return true;
    }

    private static bool TryParseGeneratedAtUtc(
        JsonElement root,
        out DateTimeOffset generatedAtUtc)
    {
        generatedAtUtc = default;
        if (!root.TryGetProperty("generatedAtUtc", out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var rawValue = element.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var timeSeparator = rawValue.IndexOf('T');
        var hasExplicitOffset = rawValue.EndsWith('Z')
            || rawValue.EndsWith('z')
            || (timeSeparator >= 0
                && (rawValue.IndexOf('+', timeSeparator) >= 0
                    || rawValue.IndexOf('-', timeSeparator) >= 0));
        return hasExplicitOffset
            && DateTimeOffset.TryParse(
                rawValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out generatedAtUtc);
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private static JsonObject CreatePathsJson(BindingPaths paths)
        => new()
        {
            ["deviceInstance"] = paths.DeviceInstance,
            ["clientReleaseCatalogTemplate"] =
                paths.ClientReleaseCatalogTemplate,
            ["clientVersionReport"] = paths.ClientVersionReport,
            ["runtimeHeartbeat"] = paths.RuntimeHeartbeat,
        };

    private static bool IsRecoverable(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidOperationException
            or ArgumentException;

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct DeviceBinding(
        string ModuleId,
        string ClientCode,
        string BootstrapSecret,
        string DeviceName,
        Guid ProcessId);

    private readonly record struct BindingPaths(
        string DeviceInstance,
        string ClientReleaseCatalogTemplate,
        string ClientVersionReport,
        string RuntimeHeartbeat);

    private readonly record struct DeviceBindingBundle(
        string BaseUrl,
        BindingPaths Paths,
        DateTimeOffset GeneratedAtUtc,
        IReadOnlyList<DeviceBinding> Bindings);
}
