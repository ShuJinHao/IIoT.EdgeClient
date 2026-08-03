using System.Text.Json;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Infrastructure.Update.Configuration;

public sealed class FileEdgeUpdateConfigurationProvider : IEdgeUpdateConfigurationProvider
{
    private const string DefaultChannel = "stable";
    private const string DefaultTargetRuntime = "win-x64";
    private readonly string _baseDirectory;

    public FileEdgeUpdateConfigurationProvider(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        _baseDirectory = baseDirectory;
    }

    public EdgeUpdateConfigurationResult Resolve(EdgeUpdateTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var mutable = new MutableCloudApiOptions();
        ApplyConfigurationFile(
            mutable,
            EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(target.MachineProfile, target.HostDirectory));

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
        var configPath = EdgeClientProgramDataPaths.ResolveLauncherUpdateConfigPath(_baseDirectory);
        if (LauncherUpdateConfigurationFile.TryReadCurrent(
                configPath,
                out var configuration,
                out _)
            && configuration is not null)
        {
            return new EdgeReleaseOptions(
                configuration.Channel,
                configuration.TargetRuntime);
        }

        return new EdgeReleaseOptions(
            DefaultChannel,
            DefaultTargetRuntime);
    }

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
