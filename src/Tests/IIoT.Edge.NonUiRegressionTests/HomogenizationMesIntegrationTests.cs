using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.Enums;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationMesIntegrationTests
{
    [Fact]
    public async Task UploadInboundAsync_ShouldBuildTrayBasedRequest()
    {
        var httpClient = new CapturingMesHttpClient();
        var channel = CreateChannel(httpClient, stationNo: "ST-H-01");

        var result = await channel.UploadInboundAsync(CreateDevice(), "TRAY-001");

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
        var channel = CreateChannel(httpClient, stationNo: "ST-H-02");

        var result = await channel.UploadOutboundAsync(
            CreateDevice(),
            CreateCellData("TRAY-002"));

        Assert.Equal(MesCallOutcome.BusinessRejected, result.Outcome);
        Assert.Equal("/dev/dev/electrode/exit/push", httpClient.LastUrl);
    }

    [Fact]
    public async Task UploadRealtimeAsync_ShouldBuildRealtimeRequest()
    {
        var httpClient = new CapturingMesHttpClient();
        var channel = CreateChannel(httpClient, stationNo: "ST-H-03");

        var result = await channel.UploadRealtimeAsync(
            CreateDevice(),
            new HomogenizationRealtimeSnapshot
            {
                CapturedAt = new DateTime(2026, 4, 29, 8, 1, 2),
                StirringSpeed = 120,
                StirringCurrent = 11,
                DispersionSpeed = 220,
                DispersionCurrent = 12,
                Temperature = 25,
                Vacuum = -9
            });

        Assert.True(result.IsSuccess);
        Assert.Equal("/dev/dev/run/info", httpClient.LastUrl);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(httpClient.LastPayload));
        var root = document.RootElement;
        var device = root.GetProperty("data").GetProperty("devices")[0];
        Assert.Equal("ST-H-03", device.GetProperty("stationNo").GetString());
        Assert.Equal("2026-04-29 08:01:02", device.GetProperty("collectTime").GetString());
        Assert.Equal("rt_stir_speed", device.GetProperty("data")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task UploadRecipeAsync_ShouldBuildRecipeRequest()
    {
        var httpClient = new CapturingMesHttpClient();
        var channel = CreateChannel(httpClient, stationNo: "ST-H-04");

        var result = await channel.UploadRecipeAsync(
            CreateDevice(),
            new HomogenizationRecipeSnapshot
            {
                StirringSpeed = [10],
                DispersionSpeed = [20],
                Ncm = [1.1],
                Sp1 = [2.2],
                Nmp = [3.3],
                GlueSolution = [4.4],
                Cnt = [5.5],
                Vacuum = [true],
                Time = [30],
                Temperature = [45],
                StopStep = [false]
            });

        Assert.True(result.IsSuccess);
        Assert.Equal("/dev/dev/process/param", httpClient.LastUrl);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(httpClient.LastPayload));
        var items = document.RootElement.GetProperty("data").GetProperty("devices");
        Assert.Equal("recipe_stir_speed_01", items[0].GetProperty("code").GetString());
        Assert.Equal("10", items[0].GetProperty("val").GetString());
    }

    [Fact]
    public async Task UploadEquipmentStatusAsync_ShouldBuildStatusRequest()
    {
        var httpClient = new CapturingMesHttpClient();
        var channel = CreateChannel(httpClient, stationNo: "ST-H-05");

        var result = await channel.UploadEquipmentStatusAsync(
            CreateDevice(),
            new HomogenizationEquipmentStatusSnapshot
            {
                StatusCode = 1,
                Messages = ["运行"]
            });

        Assert.True(result.IsSuccess);
        Assert.Equal("/dev/dev/realTime/status", httpClient.LastUrl);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(httpClient.LastPayload));
        var device = document.RootElement.GetProperty("data").GetProperty("devices")[0];
        Assert.Equal("ST-H-05", device.GetProperty("stationNo").GetString());
        Assert.Equal(1, device.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task HomogenizationMesChannel_AsProcessMesUploader_ShouldUploadAllOutboundRecords()
    {
        var httpClient = new CapturingMesHttpClient();
        var uploader = (IProcessMesUploader)CreateChannel(httpClient, stationNo: "ST-H-06");

        var result = await uploader.UploadAsync(
            new ProcessMesUploadContext(CreateDevice()),
            [
                new IIoT.Edge.SharedKernel.DataPipeline.CellCompletedRecord
                {
                    CellData = CreateCellData("TRAY-01")
                },
                new IIoT.Edge.SharedKernel.DataPipeline.CellCompletedRecord
                {
                    CellData = CreateCellData("TRAY-02")
                }
            ]);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, httpClient.Requests.Count);
        Assert.All(httpClient.Requests, request => Assert.Equal("/dev/dev/electrode/exit/push", request.Url));
    }

    private static HomogenizationMesChannel CreateChannel(
        CapturingMesHttpClient httpClient,
        string stationNo)
    {
        var runtimeConfig = new FakeLocalSystemRuntimeConfigService
        {
            Current = SystemRuntimeConfigSnapshot.Default with
            {
                MesBaseUrl = "https://mes.local",
                MesUploadEnabled = true
            }
        };
        var logger = new FakeLogService();
        var executor = new MesRequestExecutor(
            httpClient,
            new FakeMesEndpointProvider(),
            runtimeConfig,
            logger);

        return new HomogenizationMesChannel(
            executor,
            new MutableLocalParameterConfigService(stationNo),
            logger,
            Options.Create(CreateMesOptions()),
            Options.Create(CreateCodeOptions()));
    }

    private static HomogenizationCellData CreateCellData(string trayCode)
        => new()
        {
            TrayCode = trayCode,
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
        };

    private static HomogenizationMesOptions CreateMesOptions()
        => new()
        {
            SignToken = "hdc2023",
            Paths = new HomogenizationMesPathOptions
            {
                Inbound = "/dev/dev/getIn/check",
                Outbound = "/dev/dev/electrode/exit/push",
                Recipe = "/dev/dev/process/param",
                Realtime = "/dev/dev/run/info",
                EquipmentStatus = "/dev/dev/realTime/status"
            }
        };

    private static HomogenizationCodeOptions CreateCodeOptions()
        => new()
        {
            Mes = new HomogenizationMesCodeOptions
            {
                RealtimeItems = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["StirringSpeed"] = Item("rt_stir_speed", "搅拌转速", "short", "RPM"),
                    ["StirringCurrent"] = Item("rt_stir_current", "搅拌电流", "short", "A"),
                    ["DispersionSpeed"] = Item("rt_dispersion_speed", "分散转速", "short", "RPM"),
                    ["DispersionCurrent"] = Item("rt_dispersion_current", "分散电流", "short", "A"),
                    ["Temperature"] = Item("rt_temperature", "温度", "short", "C"),
                    ["Vacuum"] = Item("rt_vacuum", "真空度", "short", "Kpa")
                },
                RecipeItems = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["StirringSpeed"] = Item("recipe_stir_speed", "搅拌转速", "short", "RPM"),
                    ["DispersionSpeed"] = Item("recipe_dispersion_speed", "分散转速", "short", "RPM"),
                    ["Ncm"] = Item("recipe_ncm", "NCM", "decimal", "kg"),
                    ["Sp1"] = Item("recipe_sp1", "SP1", "decimal", "kg"),
                    ["Nmp"] = Item("recipe_nmp", "NMP", "decimal", "kg"),
                    ["GlueSolution"] = Item("recipe_glue_solution", "胶液", "decimal", "kg"),
                    ["Cnt"] = Item("recipe_cnt", "CNT", "decimal", "kg"),
                    ["Vacuum"] = Item("recipe_vacuum", "真空", "bool"),
                    ["Time"] = Item("recipe_time", "时间", "ushort", "min"),
                    ["Temperature"] = Item("recipe_temperature", "温度", "short", "C"),
                    ["StopStep"] = Item("recipe_stop_step", "停止步骤", "bool")
                },
                OutboundProduceItems = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["DeviceCode"] = Item("gluingDeviceCode", "设备编码", "string"),
                    ["DeviceName"] = Item("gluingDeviceName", "设备名称", "string"),
                    ["StartTime"] = Item("gluingStartTime", "开始时间", "datetime"),
                    ["CompleteTime"] = Item("gluingCompleteTime", "完成时间", "datetime"),
                    ["StirringSpeed"] = Item("gluingStirSpeed", "搅拌转速", "short", "RPM"),
                    ["Temperature"] = Item("gluingGlueSolutingTemperature", "温度", "short", "C"),
                    ["Vacuum"] = Item("gluingVacuumDegree", "真空度", "short", "Kpa"),
                    ["CntActual"] = Item("gluingCntActualValue", "CNT 实际值", "decimal", "kg"),
                    ["CntTarget"] = Item("gluingCntTargetValue", "CNT 目标值", "decimal", "kg"),
                    ["CntTankAWeight"] = Item("gluingCntTankAWeight", "CNT A 罐重量", "decimal", "kg"),
                    ["CntTankBWeight"] = Item("gluingCntTankBWeight", "CNT B 罐重量", "decimal", "kg"),
                    ["NmpActual"] = Item("gluingNmpActualValue", "NMP 实际值", "decimal", "kg"),
                    ["NmpTarget"] = Item("gluingNmpTargetValue", "NMP 目标值", "decimal", "kg"),
                    ["GlueActual"] = Item("gluingGlueActualWeight", "胶液实际重量", "decimal", "kg"),
                    ["SetStirringTime"] = Item("gluingSetStirTime", "设定搅拌时间", "ushort", "min"),
                    ["RemainingStirringTime"] = Item("gluingRemainStirTime", "剩余搅拌时间", "ushort", "min"),
                    ["SetDispersionTime"] = Item("gluingSetDispersionTime", "设定分散时间", "ushort", "min"),
                    ["RemainingDispersionTime"] = Item("gluingRemainDispersionTime", "剩余分散时间", "ushort", "min")
                }
            }
        };

    private static HomogenizationMesItemCodeOptions Item(
        string code,
        string name,
        string type,
        string unit = "")
        => new()
        {
            Code = code,
            Name = name,
            Type = type,
            Unit = unit
        };

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
        public List<(string Url, object Payload)> Requests { get; } = [];
        public string? LastUrl => Requests.LastOrDefault().Url;
        public object? LastPayload => Requests.LastOrDefault().Payload;
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
            Requests.Add((url, payload));
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
        public event EventHandler<ParameterConfigChangedEventArgs>? ParameterConfigChanged
        {
            add { }
            remove { }
        }

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
}
