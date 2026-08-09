using System.Text.Json.Nodes;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Security;

namespace IIoT.Edge.Installer.UnitTests;

public sealed class EdgeInstallerBindingV3Tests
{
    [Fact]
    public void CanonicalPayload_ShouldParseMaterializeAndRoundTripAllSeventeenRoutes()
    {
        var payload = EdgeInstallerBindingCodec.ParsePayload(CreatePayload().ToJsonString());
        var binding = Assert.Single(payload.Bindings);
        var template = JsonNode.Parse(
            """{"CloudApi":{"BootstrapSecret":"must-disappear","Paths":{"SiteOwned":"keep"}}}""")!
            .AsObject();

        EdgeBindingMaterializer.MaterializeV3(
            template,
            payload,
            binding,
            "plugins/CLIENT-P1",
            "plugins/CLIENT-P1/app");

        var materializedPaths = template["CloudApi"]!["Paths"]!.AsObject();
        Assert.Equal(17, EdgeBindingRouteCatalog.All.Count);
        Assert.All(EdgeBindingRouteCatalog.All, descriptor =>
            Assert.Equal(
                EdgeBindingRouteCatalog.Get(payload.Paths, descriptor.Key),
                materializedPaths[descriptor.MachineConfigKey]!.GetValue<string>()));
        Assert.Equal("keep", materializedPaths["SiteOwned"]!.GetValue<string>());
        Assert.Null(template["CloudApi"]!["BootstrapSecret"]);
        Assert.Null(materializedPaths["PlcSnapshot"]);
        Assert.Null(materializedPaths["PassStationBatch"]);

        var runtimeJson = EdgeInstallerBindingCodec.SerializeRuntime(
            EdgeInstallerBindingCodec.ToRuntime(payload, "S-1-5-21-1000"));
        var runtime = EdgeInstallerBindingCodec.ParseRuntime(runtimeJson);
        Assert.Equal("S-1-5-21-1000", Assert.Single(runtime.Bindings).CredentialOwnerSid);
        Assert.Equal(
            "/api/v1/edge/edge-hosts/plc-runtime-states",
            runtime.Paths.EdgeHostPlcRuntimeStates);
    }

    [Fact]
    public void ParsePayload_WhenRouteSetIsIncompleteUnknownOrUsesLegacyAlias_ShouldFailClosed()
    {
        var missing = CreatePayload();
        Paths(missing).Remove("capacitySummaryRange");
        Assert.Throws<InvalidDataException>(() =>
            EdgeInstallerBindingCodec.ParsePayload(missing.ToJsonString()));

        var unknown = CreatePayload();
        Paths(unknown)["futureRoute"] = "/api/v1/edge/future";
        Assert.Throws<InvalidDataException>(() =>
            EdgeInstallerBindingCodec.ParsePayload(unknown.ToJsonString()));

        var legacyAlias = CreatePayload();
        Paths(legacyAlias).Remove("edgeHostPlcRuntimeStates");
        Paths(legacyAlias)["plcSnapshot"] = "/api/v1/edge/edge-hosts/plc-runtime-states";
        Assert.Throws<InvalidDataException>(() =>
            EdgeInstallerBindingCodec.ParsePayload(legacyAlias.ToJsonString()));
    }

    [Fact]
    public void ParsePayload_WhenRouteIsDuplicated_ShouldFailClosed()
    {
        var json = CreatePayload().ToJsonString();
        const string route = "\"deviceInstance\":\"/api/v1/edge/bootstrap/device-instance\"";
        var duplicated = json.Replace(route, $"{route},{route}", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            EdgeInstallerBindingCodec.ParsePayload(duplicated));
    }

    [Fact]
    public void ParsePayload_WhenPlaceholdersOrFixedRoutesDrift_ShouldFailClosed()
    {
        var wrongPassStation = CreatePayload();
        Paths(wrongPassStation)["passStationBatchTemplate"] =
            "/api/v1/edge/pass-stations/{deviceId}/batch";
        Assert.Throws<InvalidDataException>(() =>
            EdgeInstallerBindingCodec.ParsePayload(wrongPassStation.ToJsonString()));

        var duplicateDeviceId = CreatePayload();
        Paths(duplicateDeviceId)["recipeByDeviceTemplate"] =
            "/api/v1/edge/recipes/{deviceId}/device/{deviceId}";
        Assert.Throws<InvalidDataException>(() =>
            EdgeInstallerBindingCodec.ParsePayload(duplicateDeviceId.ToJsonString()));

        var wrongPlc = CreatePayload();
        Paths(wrongPlc)["edgeHostPlcRuntimeStates"] =
            "/api/v1/edge-hosts/plc-runtime-states";
        Assert.Throws<InvalidDataException>(() =>
            EdgeInstallerBindingCodec.ParsePayload(wrongPlc.ToJsonString()));
    }

    private static JsonObject CreatePayload()
    {
        const string generationId = "GEN-ROUTES";
        const string clientCode = "CLIENT-P1";
        var now = DateTimeOffset.UtcNow;
        return new JsonObject
        {
            ["schemaVersion"] = 3,
            ["generationId"] = generationId,
            ["generatedAtUtc"] = now.ToString("O"),
            ["expiresAtUtc"] = now.AddMinutes(30).ToString("O"),
            ["baseUrl"] = "https://cloud.example.test",
            ["paths"] = CanonicalPaths(),
            ["bindings"] = new JsonArray
            {
                new JsonObject
                {
                    ["clientCode"] = clientCode,
                    ["deviceName"] = "P1",
                    ["processId"] = "11111111-1111-1111-1111-111111111111",
                    ["processType"] = "DieCutting",
                    ["moduleId"] = "P1",
                    ["pluginVersion"] = "2.0.21",
                    ["packageSha256"] = new string('a', 64),
                    ["pluginDirectory"] = $"plugins/{clientCode}/app",
                    ["configDirectory"] = $"plugins/{clientCode}/config",
                    ["dbDirectory"] = $"plugins/{clientCode}/db",
                    ["dataDirectory"] = $"plugins/{clientCode}/data",
                    ["logsDirectory"] = $"plugins/{clientCode}/logs",
                    ["cacheDirectory"] = $"plugins/{clientCode}/cache",
                    ["contextDirectory"] = $"plugins/{clientCode}/context",
                    ["buffersDirectory"] = $"plugins/{clientCode}/buffers",
                    ["pendingCredential"] = new JsonObject
                    {
                        ["name"] = WindowsCredentialManagerStore.CreatePendingReference(
                            generationId,
                            clientCode),
                        ["secret"] = "pending-secret"
                    }
                }
            }
        };
    }

    private static JsonObject Paths(JsonObject payload) => payload["paths"]!.AsObject();

    private static JsonObject CanonicalPaths() => new()
    {
        ["deviceInstance"] = "/api/v1/edge/bootstrap/device-instance",
        ["bootstrapRefresh"] = "/api/v1/edge/bootstrap/edge-refresh",
        ["activateDevice"] = "/api/v1/edge/bootstrap/device-activate",
        ["activateDeviceConfirm"] = "/api/v1/edge/bootstrap/device-activation-confirm",
        ["identityDeviceLogin"] = "/api/v1/human/identity/edge-login",
        ["humanIdentityRefresh"] = "/api/v1/human/identity/refresh",
        ["humanSessionValidation"] = "/api/v1/human/identity/session",
        ["deviceLog"] = "/api/v1/edge/device-logs",
        ["passStationBatchTemplate"] = "/api/v1/edge/pass-stations/{typeKey}/batch",
        ["capacityHourly"] = "/api/v1/edge/capacity/hourly",
        ["capacitySummary"] = "/api/v1/edge/capacity/summary",
        ["capacitySummaryRange"] = "/api/v1/edge/capacity/summary/range",
        ["recipeByDeviceTemplate"] = "/api/v1/edge/recipes/device/{deviceId}",
        ["clientReleaseCatalogTemplate"] = "/api/v1/edge/client-releases/device/{deviceId}/catalog",
        ["clientVersionReport"] = "/api/v1/edge/client-releases/version-reports",
        ["runtimeHeartbeat"] = "/api/v1/edge/runtime-heartbeats",
        ["edgeHostPlcRuntimeStates"] = "/api/v1/edge/edge-hosts/plc-runtime-states"
    };
}
