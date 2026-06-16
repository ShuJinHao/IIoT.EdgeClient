using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Edge.Launcher.Models;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Launcher.Services;

/// <summary>
/// 首装绑定导入器：客户端首次启动时读取随下载包附带的 <c>iiot-binding.json</c>，
/// 把云端为每个插件分配的设备唯一码（ClientCode）写入对应 profile 的外部机器配置，
/// 实现“下载即配置、现场零操作”。导入完成后归档绑定文件，避免下次启动重复导入；
/// 若用户重装并重新放入新的绑定文件，则会按新文件再次写入（唯一码可复用）。
/// 启动红线：本流程全程非阻断——缺文件、JSON 损坏、无匹配 profile 都只跳过，绝不抛 fatal。
/// </summary>
public interface ILauncherDeviceBindingImporter
{
    void ApplyPendingBindings();
}

public sealed class LauncherDeviceBindingImporter : ILauncherDeviceBindingImporter
{
    public const string BindingFileName = "iiot-binding.json";

    private readonly string _baseDirectory;
    private readonly ILauncherProfileCatalog _profileCatalog;
    private readonly ILauncherProfileModuleConfiguration _moduleConfiguration;

    public LauncherDeviceBindingImporter(
        string baseDirectory,
        ILauncherProfileCatalog profileCatalog,
        ILauncherProfileModuleConfiguration moduleConfiguration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = baseDirectory;
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _moduleConfiguration = moduleConfiguration ?? throw new ArgumentNullException(nameof(moduleConfiguration));
    }

    public void ApplyPendingBindings()
    {
        // 启动红线：导入失败绝不阻断启动，这里兜底吞掉任何异常（包含意外的程序错误）。
        try
        {
            var bindingPath = ResolvePendingBindingPath();
            if (bindingPath is null)
            {
                return;
            }

            var bindings = ParseBindings(bindingPath, out var baseUrl);
            if (bindings.Count == 0)
            {
                // 空/无效绑定也清理掉，避免每次启动重复解析失败的文件。
                FinalizeBindingFile(bindingPath, bindings, baseUrl);
                return;
            }

            var profiles = _profileCatalog.LoadProfiles();
            foreach (var binding in bindings)
            {
                ApplyOneBinding(profiles, binding, baseUrl);
            }

            FinalizeBindingFile(bindingPath, bindings, baseUrl);
        }
        catch (Exception)
        {
            // 非阻断：首启绑定导入失败不得影响客户端启动（客户端规则·启动红线）。
        }
    }

    private string? ResolvePendingBindingPath()
    {
        var dataPath = Path.Combine(
            EdgeClientProgramDataPaths.ResolveLauncherDirectory(_baseDirectory),
            BindingFileName);
        if (File.Exists(dataPath))
        {
            return dataPath;
        }

        var legacyPath = Path.Combine(_baseDirectory, BindingFileName);
        return File.Exists(legacyPath) ? legacyPath : null;
    }

    private void ApplyOneBinding(
        IReadOnlyList<LauncherProfileDefinition> profiles,
        DeviceBinding binding,
        string? baseUrl)
    {
        // moduleId -> profile：匹配“机器配置 Modules.Enabled 含该 module”的 profile（与规则一致）。
        var target = profiles.FirstOrDefault(profile =>
            _moduleConfiguration.ReadEnabledModules(profile)
                .Any(moduleId => string.Equals(moduleId, binding.ModuleId, StringComparison.OrdinalIgnoreCase)));

        if (target is null)
        {
            // 没有匹配 profile：跳过（非阻断），可能是该插件尚未安装。
            return;
        }

        WriteCloudApiIdentity(target, binding.ClientCode, binding.BootstrapSecret, baseUrl);
    }

    private static void WriteCloudApiIdentity(
        LauncherProfileDefinition profile,
        string clientCode,
        string? bootstrapSecret,
        string? baseUrl)
    {
        var hostDirectory = LauncherCloudApiConfigurationResolver.ResolveHostDirectory(profile);
        var targetPath = EnsureExternalMachineProfile(profile, hostDirectory);

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(targetPath))?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        if (root["CloudApi"] is not JsonObject cloudApi)
        {
            cloudApi = new JsonObject();
            root["CloudApi"] = cloudApi;
        }

        // 写设备寻址码 + 启动密钥（由云端在下载时轮换生成，客户端不自行生成）+ 可选云端地址。
        cloudApi["ClientCode"] = clientCode;
        if (!string.IsNullOrWhiteSpace(bootstrapSecret))
        {
            cloudApi["BootstrapSecret"] = bootstrapSecret;
        }
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            cloudApi["BaseUrl"] = baseUrl.Trim();
        }

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            targetPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // 与 LauncherProfileModuleConfiguration 保持同一外部机器配置约定：外部不存在则从打包配置拷贝。
    private static string EnsureExternalMachineProfile(LauncherProfileDefinition profile, string hostDirectory)
    {
        var targetPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(profile.MachineProfile, hostDirectory);
        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        var packagedPath = Path.Combine(hostDirectory, $"appsettings.machine.{profile.MachineProfile}.json");
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(packagedPath))
        {
            File.Copy(packagedPath, targetPath, overwrite: false);
        }
        else
        {
            File.WriteAllText(targetPath, "{}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return targetPath;
    }

    private static List<DeviceBinding> ParseBindings(string bindingPath, out string? baseUrl)
    {
        baseUrl = null;
        var result = new List<DeviceBinding>();

        using var document = JsonDocument.Parse(File.ReadAllText(bindingPath));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        if (root.TryGetProperty("baseUrl", out var baseUrlElement)
            && baseUrlElement.ValueKind == JsonValueKind.String)
        {
            var value = baseUrlElement.GetString();
            baseUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        if (!root.TryGetProperty("bindings", out var bindingsElement)
            || bindingsElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in bindingsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var moduleId = ReadString(item, "moduleId");
            var clientCode = ReadString(item, "clientCode");
            if (string.IsNullOrWhiteSpace(moduleId) || string.IsNullOrWhiteSpace(clientCode))
            {
                continue;
            }

            var bootstrapSecret = ReadString(item, "bootstrapSecret");
            result.Add(new DeviceBinding(
                moduleId.Trim(),
                clientCode.Trim(),
                bootstrapSecret?.Trim() ?? string.Empty));
        }

        return result;
    }

    // 导入完成后：写一份脱敏摘要（不含启动密钥），再删除含明文密钥的原始绑定文件，
    // 避免磁盘上多留一份明文密钥（机器配置里已有必需的密钥，无需再保留第二份）。
    private void FinalizeBindingFile(
        string bindingPath,
        IReadOnlyList<DeviceBinding> bindings,
        string? baseUrl)
    {
        try
        {
            if (bindings.Count > 0)
            {
                var launcherDirectory = EdgeClientProgramDataPaths.ResolveLauncherDirectory(_baseDirectory);
                Directory.CreateDirectory(launcherDirectory);

                var summary = new JsonObject
                {
                    ["appliedAtUtc"] = DateTime.UtcNow.ToString("O"),
                    ["baseUrl"] = baseUrl,
                    ["bindings"] = new JsonArray(
                        bindings.Select(binding => (JsonNode?)new JsonObject
                        {
                            ["moduleId"] = binding.ModuleId,
                            ["clientCode"] = binding.ClientCode,
                            // 脱敏：不写 BootstrapSecret
                        }).ToArray()),
                };

                var summaryPath = Path.Combine(
                    launcherDirectory,
                    $"iiot-binding.applied.{DateTime.UtcNow:yyyyMMddHHmmss}.json");
                File.WriteAllText(
                    summaryPath,
                    summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }

            File.Delete(bindingPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct DeviceBinding(string ModuleId, string ClientCode, string BootstrapSecret);
}
