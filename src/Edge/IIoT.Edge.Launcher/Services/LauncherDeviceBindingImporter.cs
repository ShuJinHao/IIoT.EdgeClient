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
    private readonly IEdgeProfileModuleConfigurationStore _moduleConfiguration;
    private readonly ILauncherUpdateTargetFactory _targetFactory;

    public LauncherDeviceBindingImporter(
        string baseDirectory,
        ILauncherProfileCatalog profileCatalog,
        IEdgeProfileModuleConfigurationStore moduleConfiguration,
        ILauncherUpdateTargetFactory targetFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = baseDirectory;
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _moduleConfiguration = moduleConfiguration ?? throw new ArgumentNullException(nameof(moduleConfiguration));
        _targetFactory = targetFactory ?? throw new ArgumentNullException(nameof(targetFactory));
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
                return;
            }

            var profiles = _profileCatalog.LoadProfiles();
            var applied = new List<DeviceBinding>();
            var unresolved = new List<DeviceBinding>();
            foreach (var binding in bindings)
            {
                if (TryApplyOneBinding(profiles, binding, baseUrl))
                {
                    applied.Add(binding);
                }
                else
                {
                    unresolved.Add(binding);
                }
            }

            FinalizeAppliedBindings(bindingPath, applied, unresolved, baseUrl);
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
        return File.Exists(dataPath) ? dataPath : null;
    }

    private bool TryApplyOneBinding(
        IReadOnlyList<LauncherProfileDefinition> profiles,
        DeviceBinding binding,
        string? baseUrl)
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
                baseUrl);
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
        string? bootstrapSecret,
        string? baseUrl)
    {
        var targetPath = EnsureExternalMachineProfile(target);

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

    // 与 Infrastructure.Update 的外部机器配置约定保持一致：外部不存在则从打包配置拷贝。
    private static string EnsureExternalMachineProfile(EdgeUpdateTarget target)
    {
        var targetPath = EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(target.MachineProfile, target.HostDirectory);
        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        var packagedPath = Path.Combine(target.HostDirectory, $"appsettings.machine.{target.MachineProfile}.json");
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

    // 只消费已经成功写入机器配置的绑定：摘要永不包含启动密钥；未匹配插件的原始绑定
    // 保留在 pending 文件中，供插件安装后的下一次启动继续处理。
    private void FinalizeAppliedBindings(
        string bindingPath,
        IReadOnlyList<DeviceBinding> applied,
        IReadOnlyList<DeviceBinding> unresolved,
        string? baseUrl)
    {
        if (applied.Count == 0)
        {
            return;
        }

        try
        {
            var launcherDirectory = EdgeClientProgramDataPaths.ResolveLauncherDirectory(_baseDirectory);
            Directory.CreateDirectory(launcherDirectory);

            var summary = new JsonObject
            {
                ["appliedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["baseUrl"] = baseUrl,
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
                ["schemaVersion"] = 1,
                ["baseUrl"] = baseUrl,
                ["bindings"] = new JsonArray(
                    unresolved.Select(binding => (JsonNode?)new JsonObject
                    {
                        ["moduleId"] = binding.ModuleId,
                        ["clientCode"] = binding.ClientCode,
                        ["bootstrapSecret"] = binding.BootstrapSecret,
                    }).ToArray()),
            };
            WritePendingBindingsAtomically(bindingPath, pending);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WritePendingBindingsAtomically(string bindingPath, JsonObject pending)
    {
        var directory = Path.GetDirectoryName(bindingPath)
            ?? throw new InvalidOperationException("绑定文件缺少目录。");
        var temporaryPath = Path.Combine(directory, $".{BindingFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(
                temporaryPath,
                pending.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, bindingPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct DeviceBinding(string ModuleId, string ClientCode, string BootstrapSecret);
}
