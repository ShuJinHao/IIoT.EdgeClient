using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using IIoT.Edge.Module.Sdk.Cloud;
using IIoT.Edge.Infrastructure.Integration.PassStation;

namespace IIoT.Edge.Cloud.ContractFilesystemTests;

public sealed partial class PassStationContractSnapshotTests
{
    [Fact]
    public void UploadDto_ShouldExposeRequiredCloudFieldTypes()
    {
        var itemType = typeof(PassStationUploadItem);
        var nullability = new NullabilityInfoContext();

        Assert.Equal(typeof(string), itemType.GetProperty(nameof(PassStationUploadItem.Barcode))!.PropertyType);
        Assert.Equal(
            NullabilityState.NotNull,
            nullability.Create(itemType.GetProperty(nameof(PassStationUploadItem.Barcode))!).ReadState);
        Assert.Equal(typeof(string), itemType.GetProperty(nameof(PassStationUploadItem.CellResult))!.PropertyType);
        Assert.Equal(
            NullabilityState.NotNull,
            nullability.Create(itemType.GetProperty(nameof(PassStationUploadItem.CellResult))!).ReadState);
        Assert.Equal(typeof(DateTime), itemType.GetProperty(nameof(PassStationUploadItem.CompletedTime))!.PropertyType);
        Assert.Equal(typeof(JsonElement), itemType.GetProperty(nameof(PassStationUploadItem.Payload))!.PropertyType);
    }

    [Fact]
    public void CanonicalSnapshot_ShouldMatchUploaderContractConstantsAndConfiguredRoute()
    {
        using var snapshot = LoadSnapshot();
        var root = snapshot.RootElement;
        var fields = root.GetProperty("request").GetProperty("fields");
        var item = root.GetProperty("item").GetProperty("fields");

        Assert.Equal(PassStationCloudContract.SchemaVersion, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(PassStationCloudContract.MinItems, fields.GetProperty("items").GetProperty("minItems").GetInt32());
        Assert.Equal(PassStationCloudContract.MaxItems, fields.GetProperty("items").GetProperty("maxItems").GetInt32());
        Assert.Equal(PassStationCloudContract.MaxRequestIdLength, fields.GetProperty("requestId").GetProperty("maxLength").GetInt32());
        Assert.Equal(PassStationCloudContract.MaxProcessTypeLength, fields.GetProperty("processType").GetProperty("maxLength").GetInt32());
        Assert.Equal(PassStationCloudContract.MaxBarcodeLength, item.GetProperty("barcode").GetProperty("maxLength").GetInt32());
        Assert.Equal(PassStationCloudContract.MaxCellResultLength, item.GetProperty("cellResult").GetProperty("maxLength").GetInt32());
        Assert.Equal(PassStationCloudContract.MaxPayloadProperties, item.GetProperty("payload").GetProperty("maxProperties").GetInt32());
        Assert.Equal(
            PassStationCloudContract.MinimumCompletedTimeUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            item.GetProperty("completedTime").GetProperty("minimumUtc").GetString());
        Assert.Equal(
            $"utc-now-plus-{PassStationCloudContract.MaximumCompletedTimeOffsetDays}-day",
            item.GetProperty("completedTime").GetProperty("maximumUtc").GetString());
        Assert.Equal(
            [PassStationCloudContract.EmittedOk, PassStationCloudContract.EmittedNg],
            item.GetProperty("cellResult").GetProperty("edgeEmittedValues")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray());

        using var appSettings = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Edge",
            "IIoT.Edge.Shell",
            "appsettings.json")));
        var configuredRoute = appSettings.RootElement
            .GetProperty("CloudApi")
            .GetProperty("Paths")
            .GetProperty("PassStationBatchTemplate")
            .GetString();
        Assert.Equal(root.GetProperty("http").GetProperty("routeTemplate").GetString(), configuredRoute);
    }

    [Fact]
    public void CanonicalSnapshot_RequestIdDescription_ShouldMatchRealHashBuilder()
    {
        using var snapshot = LoadSnapshot();
        var requestId = snapshot.RootElement
            .GetProperty("request")
            .GetProperty("fields")
            .GetProperty("requestId");
        var actual = CloudIdempotencyKeyBuilder.ForPayload("test", "contract", "{}");

        Assert.Equal(requestId.GetProperty("edgeLength").GetInt32(), actual.Length);
        Assert.Equal("sha256-uppercase-hex", requestId.GetProperty("edgeFormat").GetString());
        Assert.Matches(UppercaseSha256Pattern(), actual);
    }

    [GeneratedRegex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex UppercaseSha256Pattern();

    private static JsonDocument LoadSnapshot() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory,
        "ContractSnapshots",
        "pass-station-batch-v1.json")));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the IIoT.EdgeClient repository root.");
    }
}
