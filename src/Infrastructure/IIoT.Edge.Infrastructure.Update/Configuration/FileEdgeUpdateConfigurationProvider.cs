using System.Text.Json;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Infrastructure.Update.Configuration;

public sealed class FileEdgeUpdateConfigurationProvider : IEdgeUpdateConfigurationProvider
{
    public const string ReleaseChannelEnvironmentVariable = "IIOT_EDGE_RELEASE_CHANNEL";
    public const string TargetRuntimeEnvironmentVariable = "IIOT_EDGE_TARGET_RUNTIME";

    private const string DefaultChannel = "stable";
    private const string DefaultTargetRuntime = "win-x64";
    private readonly string _baseDirectory;
    private readonly IEdgeProfileCloudSwitchReader _cloudSwitchReader;

    public FileEdgeUpdateConfigurationProvider(
        string baseDirectory,
        IEdgeProfileCloudSwitchReader? cloudSwitchReader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = baseDirectory;
        _cloudSwitchReader = cloudSwitchReader ?? new FileProfileCloudSwitchReader(baseDirectory);
    }

    public EdgeUpdateConfigurationResult Resolve(EdgeUpdateTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!_cloudSwitchReader.IsEnabled(target))
        {
            return EdgeUpdateConfigurationResult.Failed(
                "当前 machine profile 的 Cloud 通信已关闭。");
        }

        var mutable = new MutableCloudApiOptions();
        ApplyConfigurationFile(mutable, Path.Combine(target.HostDirectory, "appsettings.json"));
        ApplyConfigurationFile(mutable, Path.Combine(target.HostDirectory, $"appsettings.{GetEnvironmentName()}.json"));
        ApplyConfigurationFile(mutable, Path.Combine(target.HostDirectory, $"appsettings.machine.{target.MachineProfile}.json"));
        ApplyConfigurationFile(
            mutable,
            EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(target.MachineProfile, target.HostDirectory));
        ApplyEnvironment(mutable);

        var missing = mutable.GetMissingKeys().ToArray();
        if (missing.Length > 0)
        {
            return EdgeUpdateConfigurationResult.Failed(
                $"CloudApi 配置不完整: {string.Join(", ", missing)}");
        }

        return EdgeUpdateConfigurationResult.Succeeded(new EdgeUpdateCloudApiOptions(
            mutable.BaseUrl!,
            mutable.TimeoutSeconds ?? 10,
            mutable.ClientCode!,
            mutable.BootstrapSecret!,
            mutable.DeviceInstancePath!,
            mutable.ClientReleaseCatalogTemplate!,
            mutable.ClientVersionReportPath!,
            mutable.RuntimeHeartbeatPath!));
    }

    public EdgeReleaseOptions ResolveReleaseOptions()
    {
        var channel = Environment.GetEnvironmentVariable(ReleaseChannelEnvironmentVariable)?.Trim();
        var targetRuntime = Environment.GetEnvironmentVariable(TargetRuntimeEnvironmentVariable)?.Trim();
        var configPath = EdgeClientProgramDataPaths.ResolveLauncherUpdateConfigPath(_baseDirectory);

        if (File.Exists(configPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(configPath));
                var root = document.RootElement;
                channel = FirstNotWhiteSpace(channel, ReadString(root, "Channel") ?? ReadString(root, "channel"));
                targetRuntime = FirstNotWhiteSpace(targetRuntime, ReadString(root, "TargetRuntime") ?? ReadString(root, "targetRuntime"));
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return new EdgeReleaseOptions(
            FirstNotWhiteSpace(channel, DefaultChannel)!,
            FirstNotWhiteSpace(targetRuntime, DefaultTargetRuntime)!);
    }

    private static string GetEnvironmentName()
        => Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

    private static void ApplyConfigurationFile(MutableCloudApiOptions target, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("CloudApi", out var cloudApi))
            {
                return;
            }

            target.BaseUrl = FirstNotWhiteSpace(ReadString(cloudApi, "BaseUrl"), target.BaseUrl);
            target.ClientCode = FirstNotWhiteSpace(ReadString(cloudApi, "ClientCode"), target.ClientCode);
            target.BootstrapSecret = FirstNotWhiteSpace(ReadString(cloudApi, "BootstrapSecret"), target.BootstrapSecret);
            var timeout = ReadInt32(cloudApi, "TimeoutSecs");
            target.TimeoutSeconds = timeout ?? target.TimeoutSeconds;

            if (!cloudApi.TryGetProperty("Paths", out var paths))
            {
                return;
            }

            target.DeviceInstancePath = FirstNotWhiteSpace(
                ReadString(paths, "DeviceInstance"),
                target.DeviceInstancePath);
            target.ClientReleaseCatalogTemplate = FirstNotWhiteSpace(
                ReadString(paths, "ClientReleaseCatalogTemplate"),
                target.ClientReleaseCatalogTemplate);
            target.ClientVersionReportPath = FirstNotWhiteSpace(
                ReadString(paths, "ClientVersionReport"),
                target.ClientVersionReportPath);
            target.RuntimeHeartbeatPath = FirstNotWhiteSpace(
                ReadString(paths, "RuntimeHeartbeat"),
                target.RuntimeHeartbeatPath);
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void ApplyEnvironment(MutableCloudApiOptions target)
    {
        target.BaseUrl = FirstNotWhiteSpace(Environment.GetEnvironmentVariable("CloudApi__BaseUrl"), target.BaseUrl);
        target.ClientCode = FirstNotWhiteSpace(Environment.GetEnvironmentVariable("CloudApi__ClientCode"), target.ClientCode);
        target.BootstrapSecret = FirstNotWhiteSpace(
            Environment.GetEnvironmentVariable("CloudApi__BootstrapSecret"),
            target.BootstrapSecret);
        target.DeviceInstancePath = FirstNotWhiteSpace(
            Environment.GetEnvironmentVariable("CloudApi__Paths__DeviceInstance"),
            target.DeviceInstancePath);
        target.ClientReleaseCatalogTemplate = FirstNotWhiteSpace(
            Environment.GetEnvironmentVariable("CloudApi__Paths__ClientReleaseCatalogTemplate"),
            target.ClientReleaseCatalogTemplate);
        target.ClientVersionReportPath = FirstNotWhiteSpace(
            Environment.GetEnvironmentVariable("CloudApi__Paths__ClientVersionReport"),
            target.ClientVersionReportPath);
        target.RuntimeHeartbeatPath = FirstNotWhiteSpace(
            Environment.GetEnvironmentVariable("CloudApi__Paths__RuntimeHeartbeat"),
            target.RuntimeHeartbeatPath);

        if (int.TryParse(Environment.GetEnvironmentVariable("CloudApi__TimeoutSecs"), out var timeout)
            && timeout > 0)
        {
            target.TimeoutSeconds = timeout;
        }
    }

    private static string? FirstNotWhiteSpace(string? first, string? fallback)
        => string.IsNullOrWhiteSpace(first) ? fallback : first.Trim();

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private static int? ReadInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number > 0)
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out number)
            && number > 0)
        {
            return number;
        }

        return null;
    }

    private sealed class MutableCloudApiOptions
    {
        public string? BaseUrl { get; set; }
        public int? TimeoutSeconds { get; set; }
        public string? ClientCode { get; set; }
        public string? BootstrapSecret { get; set; }
        public string? DeviceInstancePath { get; set; }
        public string? ClientReleaseCatalogTemplate { get; set; }
        public string? ClientVersionReportPath { get; set; }
        public string? RuntimeHeartbeatPath { get; set; }

        public IEnumerable<string> GetMissingKeys()
        {
            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                yield return "CloudApi:BaseUrl";
            }

            if (string.IsNullOrWhiteSpace(ClientCode))
            {
                yield return "CloudApi:ClientCode";
            }

            if (string.IsNullOrWhiteSpace(BootstrapSecret))
            {
                yield return "CloudApi:BootstrapSecret";
            }

            if (string.IsNullOrWhiteSpace(DeviceInstancePath))
            {
                yield return "CloudApi:Paths:DeviceInstance";
            }

            if (string.IsNullOrWhiteSpace(ClientReleaseCatalogTemplate))
            {
                yield return "CloudApi:Paths:ClientReleaseCatalogTemplate";
            }

            if (string.IsNullOrWhiteSpace(ClientVersionReportPath))
            {
                yield return "CloudApi:Paths:ClientVersionReport";
            }

            if (string.IsNullOrWhiteSpace(RuntimeHeartbeatPath))
            {
                yield return "CloudApi:Paths:RuntimeHeartbeat";
            }
        }
    }
}
