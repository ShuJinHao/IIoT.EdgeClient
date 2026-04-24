using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Homogenization;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.Enums;
using System.Text.Json;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationMesIntegrationTests
{
    [Fact]
    public async Task UploadInboundAsync_ShouldBuildTrayBasedRequest()
    {
        var httpClient = new CapturingMesHttpClient
        {
            Response = """{"code":200,"msg":"OK"}"""
        };
        var service = CreateService(httpClient, stationNo: "ST-H-01");

        var result = await service.UploadInboundAsync(CreateDevice(), "TRAY-001");

        Assert.True(result.IsSuccess);
        Assert.Equal("/dev/dev/getIn/check", httpClient.LastUrl);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(httpClient.LastPayload));
        var root = document.RootElement;
        Assert.Equal("CLIENT-H", root.GetProperty("upperComputerNo").GetString());
        Assert.Equal("ST-H-01", root.GetProperty("stationNo").GetString());
        Assert.Equal("TRAY-001", root.GetProperty("data").GetProperty("productNo").GetString());
        Assert.Equal(32, root.GetProperty("sign").GetString()!.Length);
    }

    [Fact]
    public async Task UploadOutboundAsync_WhenMesReturnsNon200_ShouldReturnBusinessRejected()
    {
        var httpClient = new CapturingMesHttpClient
        {
            Response = """{"code":500,"msg":"MES rejected outbound"}"""
        };
        var service = CreateService(httpClient, stationNo: "ST-H-02");

        var result = await service.UploadOutboundAsync(
            CreateDevice(),
            new HomogenizationCellData
            {
                TrayCode = "TRAY-002",
                DeviceCode = "CLIENT-H",
                DeviceName = "PLC-H",
                InboundTime = new DateTime(2026, 4, 23, 8, 0, 0),
                CompletedTime = new DateTime(2026, 4, 23, 8, 30, 0),
                RealtimeSnapshot = new HomogenizationRealtimeSnapshot
                {
                    StirringSpeed = 120,
                    Temperature = 25,
                    Vacuum = -10
                },
                CntActualKg = 15,
                NmpActualKg = 18
            });

        Assert.Equal(MesCallOutcome.BusinessRejected, result.Outcome);
        Assert.Equal("/dev/dev/electrode/exit/push", httpClient.LastUrl);
    }

    [Fact]
    public async Task HomogenizationMesUploader_ShouldUploadAllOutboundRecords()
    {
        var service = new CapturingHomogenizationMesApiService();
        var uploader = new HomogenizationMesUploader(service, new FakeLogService());
        var device = CreateDevice();

        var result = await uploader.UploadAsync(
            new ProcessMesUploadContext(device),
            [
                new IIoT.Edge.SharedKernel.DataPipeline.CellCompletedRecord
                {
                    CellData = new HomogenizationCellData { TrayCode = "TRAY-01" }
                },
                new IIoT.Edge.SharedKernel.DataPipeline.CellCompletedRecord
                {
                    CellData = new HomogenizationCellData { TrayCode = "TRAY-02" }
                }
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, service.OutboundTrayCodes.Count);
        Assert.Contains("TRAY-01", service.OutboundTrayCodes);
        Assert.Contains("TRAY-02", service.OutboundTrayCodes);
    }

    private static HomogenizationMesApiService CreateService(
        CapturingMesHttpClient httpClient,
        string stationNo)
    {
        var moduleConfiguration = HomogenizationModuleConfiguration.Load();
        return new HomogenizationMesApiService(
            httpClient,
            new FakeMesEndpointProvider(),
            new FakeLocalSystemRuntimeConfigService
            {
                Current = SystemRuntimeConfigSnapshot.Default with
                {
                    MesBaseUrl = "https://mes.local",
                    MesUploadEnabled = true
                }
            },
            new MutableLocalParameterConfigService(stationNo),
            new FakeLogService(),
            moduleConfiguration.Mes,
            moduleConfiguration.Codes);
    }

    private static DeviceSession CreateDevice()
        => new()
        {
            DeviceId = Guid.NewGuid(),
            DeviceName = "PLC-H",
            ClientCode = "CLIENT-H",
            ProcessId = Guid.NewGuid()
        };

    private sealed class CapturingMesHttpClient : IMesHttpClient
    {
        public string? LastUrl { get; private set; }
        public object? LastPayload { get; private set; }
        public string Response { get; set; } = """{"code":200,"msg":"OK"}""";

        public Task<bool> PostAsync(
            string url,
            object payload,
            IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<string?> PostWithResponseAsync(
            string url,
            object payload,
            IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            LastUrl = url;
            LastPayload = payload;
            return Task.FromResult<string?>(Response);
        }

        public Task<string?> GetAsync(
            string url,
            IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class FakeMesEndpointProvider : IMesEndpointProvider
    {
        public bool IsConfigured => true;
        public string BuildUrl(string relativeOrAbsoluteUrl) => $"https://mes.local{relativeOrAbsoluteUrl}";
        public IReadOnlyDictionary<string, string> GetDefaultHeaders() => new Dictionary<string, string>();
    }

    private sealed class MutableLocalParameterConfigService(string stationNo) : ILocalParameterConfigService
    {
        public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged;

        public Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LocalSystemConfigSnapshot>>(
            [
                new LocalSystemConfigSnapshot(1, SystemConfigKey.工站编号.ToString(), stationNo, null, 1)
            ]);

        public Task<string?> GetSystemConfigValueAsync(
            SystemConfigKey key,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(key == SystemConfigKey.工站编号 ? stationNo : null);

        public Task<IReadOnlyList<LocalDeviceParameterSnapshot>> GetDeviceParamsAsync(
            int deviceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LocalDeviceParameterSnapshot>>([]);

        public Task<string?> GetDeviceParamValueAsync(
            int deviceId,
            DeviceParamKey key,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class CapturingHomogenizationMesApiService : IHomogenizationMesApiService
    {
        public List<string> OutboundTrayCodes { get; } = [];

        public Task<MesCallResult> UploadInboundAsync(
            DeviceSession? device,
            string trayCode,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult> UploadOutboundAsync(
            DeviceSession? device,
            HomogenizationCellData cellData,
            CancellationToken cancellationToken = default)
        {
            OutboundTrayCodes.Add(cellData.TrayCode);
            return Task.FromResult(MesCallResult.Success());
        }

        public Task<MesCallResult> UploadRealtimeAsync(
            DeviceSession? device,
            HomogenizationRealtimeSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult> UploadRecipeAsync(
            DeviceSession? device,
            HomogenizationRecipeSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());

        public Task<MesCallResult> UploadEquipmentStatusAsync(
            DeviceSession? device,
            HomogenizationEquipmentStatusSnapshot snapshot,
            CancellationToken cancellationToken = default)
            => Task.FromResult(MesCallResult.Success());
    }
}

