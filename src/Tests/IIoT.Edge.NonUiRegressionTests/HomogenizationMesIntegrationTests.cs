using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Features.Production.Planning;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Application.Modules.Mes;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Parameters;
using IIoT.Edge.Module.Homogenization.Integration;
using IIoT.Edge.Module.Homogenization.Payload;
using Microsoft.Extensions.Options;
using System.Text.Json;
using HomogenizationMesScenarioChannel = IIoT.Edge.Application.Modules.Mes.IMesScenarioChannel<
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationCellData,
    string,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRealtimeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationRecipeSnapshot,
    IIoT.Edge.Module.Homogenization.Payload.HomogenizationEquipmentStatusSnapshot,
    IIoT.Edge.Module.Homogenization.Integration.HomogenizationMainPlanRequest,
    IIoT.Edge.Module.Homogenization.Integration.HomogenizationMainPlan,
    IIoT.Edge.Module.Homogenization.Integration.HomogenizationTraceBatchRequest,
    IIoT.Edge.Module.Homogenization.Integration.HomogenizationTraceBatchResult>;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationMesIntegrationTests
{
    [Fact]
    public void HomogenizationMesChannel_ShouldExposeGenericScenarioContractAndProcessUploader()
    {
        var channel = CreateChannel(new CapturingMesHttpClient(), stationNo: "ST-H-00");

        Assert.IsAssignableFrom<HomogenizationMesScenarioChannel>(channel);
        Assert.IsAssignableFrom<IProcessMesUploader>(channel);
    }

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
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("timestamp").GetString()));
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
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(httpClient.LastPayload));
        var root = document.RootElement;
        Assert.Equal("ST-H-02", root.GetProperty("stationNo").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("timestamp").GetString()));
        Assert.Equal(32, root.GetProperty("sign").GetString()!.Length);
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
        Assert.Equal("ST-H-03", root.GetProperty("stationNo").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("timestamp").GetString()));
        Assert.Equal(32, root.GetProperty("sign").GetString()!.Length);
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
        var root = document.RootElement;
        Assert.Equal("ST-H-04", root.GetProperty("stationNo").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("timestamp").GetString()));
        Assert.Equal(32, root.GetProperty("sign").GetString()!.Length);
        var items = root.GetProperty("data").GetProperty("devices");
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
        var root = document.RootElement;
        Assert.Equal("ST-H-05", root.GetProperty("stationNo").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("timestamp").GetString()));
        Assert.Equal(32, root.GetProperty("sign").GetString()!.Length);
        var device = root.GetProperty("data").GetProperty("devices")[0];
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

    [Fact]
    public async Task UploadInboundAsync_WhenPathParameterOverridesDefault_ShouldUseConfiguredPath()
    {
        var httpClient = new CapturingMesHttpClient();
        var channel = CreateChannel(
            httpClient,
            stationNo: "ST-H-07",
            new Dictionary<HomogenizationParams.Mes, string>
            {
                [HomogenizationParams.Mes.InboundPath] = "/configured/inbound"
            });

        var result = await channel.UploadInboundAsync(CreateDevice(), "TRAY-007");

        Assert.True(result.IsSuccess);
        Assert.Equal("/configured/inbound", httpClient.LastUrl);
    }

    [Fact]
    public async Task GetMainPlanAsync_ShouldUseOrderPathAndParseOrders()
    {
        var httpClient = new CapturingMesHttpClient
        {
            Response = """
            {"code":200,"msg":"success","data":{"orders":[[{"code":"orderNo","name":"工单号","val":"MO-001"},{"code":"planStatus","name":"状态","val":"下发"}]]}}
            """
        };
        var channel = CreateChannel(
            httpClient,
            stationNo: "ST-H-08",
            new Dictionary<HomogenizationParams.Mes, string>
            {
                [HomogenizationParams.Mes.OrderPath] = "/configured/order"
            });

        var result = await channel.GetMainPlanAsync(
            new HomogenizationMainPlanRequest("A1-STUC", new DateTime(2026, 4, 24, 12, 10, 11)));

        Assert.True(result.IsSuccess);
        Assert.Equal("/configured/order?upperComputerNo=A1-STUC&timestamp=2026-04-24%2012%3A10%3A11", httpClient.LastGetUrl);
        var order = Assert.Single(result.Data!.Orders);
        Assert.Equal("orderNo", order[0].Code);
        Assert.Equal("MO-001", order[0].Value);
    }

    [Fact]
    public async Task GenerateTraceBatchNumberAsync_ShouldUseBatchNumberPathAndJsonPayload()
    {
        var httpClient = new CapturingMesHttpClient
        {
            Response = """{"code":200,"msg":"success","data":"ASG5NAB-CG0003"}"""
        };
        var channel = CreateChannel(
            httpClient,
            stationNo: "ST-H-09",
            new Dictionary<HomogenizationParams.Mes, string>
            {
                [HomogenizationParams.Mes.BatchNumberPath] = "/configured/batch-number"
            });

        var result = await channel.GenerateTraceBatchNumberAsync(
            new HomogenizationTraceBatchRequest("PLAN-001", "CG"));

        Assert.True(result.IsSuccess);
        Assert.Equal("/configured/batch-number", httpClient.LastUrl);
        Assert.Equal("ASG5NAB-CG0003", result.Data!.BatchNumber);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(httpClient.LastPayload));
        var root = document.RootElement;
        Assert.Equal("PLAN-001", root.GetProperty("masterPlanCode").GetString());
        Assert.Equal("CG", root.GetProperty("operationCode").GetString());
    }

    [Fact]
    public async Task ProductionPlanSelectionService_WhenPlanSelected_ShouldGenerateTraceBatchNumber()
    {
        var httpClient = new CapturingMesHttpClient
        {
            Response = """{"code":200,"msg":"success","data":"TRACE-001"}"""
        };
        var channel = CreateChannel(
            httpClient,
            stationNo: "ST-H-13",
            new Dictionary<HomogenizationParams.Mes, string>
            {
                [HomogenizationParams.Mes.BatchNumberPath] = "/configured/batch-number"
            });
        var service = new HomogenizationProductionPlanSelectionService(
            channel,
            new FakeModuleParamRoleProvider("ST-H-13", operationCode: "CG"),
            new FakeProductionTimeProvider());
        var option = new ProductionPlanOption(
            Id: "1",
            MainPlanCode: "PLAN-001",
            WorkOrderCode: "WO-001",
            ErpOrderCode: string.Empty,
            ProductCode: "P-001",
            ProductName: "正极极片",
            PlanStatus: "下发",
            ProcessCode: "CG",
            ProcessName: "正极制胶",
            LineCode: string.Empty,
            LineName: string.Empty,
            PlannedQuantity: "10",
            CompletedQuantity: string.Empty,
            Unit: string.Empty,
            ProductModel: string.Empty,
            StartTime: string.Empty,
            EndTime: string.Empty,
            Fields: new Dictionary<string, string>());

        await service.SelectPlanAsync(option);

        var state = await service.GetStateAsync();
        Assert.Same(option, state.CurrentPlan);
        Assert.Equal("TRACE-001", state.TraceBatchNumber);
        Assert.True(state.HasTraceBatchNumber);
        Assert.Equal("/configured/batch-number", httpClient.LastUrl);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(httpClient.LastPayload));
        var root = document.RootElement;
        Assert.Equal("PLAN-001", root.GetProperty("masterPlanCode").GetString());
        Assert.Equal("CG", root.GetProperty("operationCode").GetString());
    }

    [Fact]
    public async Task ProductionPlanSelectionService_WhenTraceBatchTimesOut_ShouldKeepPlanAndExposeTimeout()
    {
        var httpClient = new CapturingMesHttpClient
        {
            PostWithResponseException = new TaskCanceledException("The MES request timed out.")
        };
        var channel = CreateChannel(httpClient, stationNo: "ST-H-15");
        var service = new HomogenizationProductionPlanSelectionService(
            channel,
            new FakeModuleParamRoleProvider("ST-H-15", operationCode: "CG"),
            new FakeProductionTimeProvider());
        var option = new ProductionPlanOption(
            Id: "1",
            MainPlanCode: "PLAN-001",
            WorkOrderCode: "WO-001",
            ErpOrderCode: string.Empty,
            ProductCode: string.Empty,
            ProductName: "P-001",
            PlanStatus: string.Empty,
            ProcessCode: string.Empty,
            ProcessName: string.Empty,
            LineCode: string.Empty,
            LineName: string.Empty,
            PlannedQuantity: string.Empty,
            CompletedQuantity: string.Empty,
            Unit: string.Empty,
            ProductModel: string.Empty,
            StartTime: string.Empty,
            EndTime: string.Empty,
            Fields: new Dictionary<string, string>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SelectPlanAsync(option));

        Assert.Equal(ProductionPlanSelectionErrorCodes.TraceBatchTimeout, error.Message);
        var state = await service.GetStateAsync();
        Assert.Same(option, state.CurrentPlan);
        Assert.False(state.HasTraceBatchNumber);
        Assert.Equal(ProductionPlanSelectionErrorCodes.TraceBatchTimeout, state.TraceBatchError);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(httpClient.LastPayload));
        var root = document.RootElement;
        Assert.Equal("PLAN-001", root.GetProperty("masterPlanCode").GetString());
        Assert.Equal("CG", root.GetProperty("operationCode").GetString());
    }

    [Fact]
    public async Task ProductionPlanSelectionService_WhenOperationCodeMissing_ShouldRejectSelection()
    {
        var service = new HomogenizationProductionPlanSelectionService(
            CreateChannel(new CapturingMesHttpClient(), stationNo: "ST-H-14"),
            new FakeModuleParamRoleProvider("ST-H-14", operationCode: null),
            new FakeProductionTimeProvider());
        var option = new ProductionPlanOption(
            Id: "1",
            MainPlanCode: "PLAN-001",
            WorkOrderCode: string.Empty,
            ErpOrderCode: string.Empty,
            ProductCode: string.Empty,
            ProductName: string.Empty,
            PlanStatus: string.Empty,
            ProcessCode: string.Empty,
            ProcessName: string.Empty,
            LineCode: string.Empty,
            LineName: string.Empty,
            PlannedQuantity: string.Empty,
            CompletedQuantity: string.Empty,
            Unit: string.Empty,
            ProductModel: string.Empty,
            StartTime: string.Empty,
            EndTime: string.Empty,
            Fields: new Dictionary<string, string>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SelectPlanAsync(option));

        Assert.Equal(ProductionPlanSelectionErrorCodes.MissingOperationCode, error.Message);
        var state = await service.GetStateAsync();
        Assert.Same(option, state.CurrentPlan);
        Assert.False(state.HasTraceBatchNumber);
        Assert.Equal(ProductionPlanSelectionErrorCodes.MissingOperationCode, state.TraceBatchError);
    }

    [Fact]
    public async Task ExecuteGetAsync_WhenMesRejects_ShouldReturnBusinessRejected()
    {
        var httpClient = new CapturingMesHttpClient
        {
            Response = """{"code":500,"msg":"业务拒绝","data":null}"""
        };
        var executor = CreateExecutor(httpClient, "ST-H-10");

        var result = await executor.ExecuteGetAsync(
            "Homogenization",
            "/reject",
            new Dictionary<string, string?>(),
            static data => data.GetRawText());

        Assert.Equal(MesCallOutcome.BusinessRejected, result.Outcome);
        Assert.Equal("业务拒绝", result.Message);
    }

    [Fact]
    public async Task ExecuteGetAsync_WhenMesReturnsEmptyResponse_ShouldReturnTransportFailure()
    {
        var httpClient = new CapturingMesHttpClient
        {
            Response = string.Empty
        };
        var executor = CreateExecutor(httpClient, "ST-H-11");

        var result = await executor.ExecuteGetAsync(
            "Homogenization",
            "/empty",
            new Dictionary<string, string?>(),
            static data => data.GetRawText());

        Assert.Equal(MesCallOutcome.TransportFailure, result.Outcome);
    }

    [Fact]
    public async Task ExecutePostAsync_WhenDataParserThrows_ShouldReturnTransportFailure()
    {
        var httpClient = new CapturingMesHttpClient
        {
            Response = """{"code":200,"msg":"success","data":{"value":"abc"}}"""
        };
        var executor = CreateExecutor(httpClient, "ST-H-12");

        var result = await executor.ExecutePostAsync<int>(
            "Homogenization",
            "/parse-failure",
            new { value = 1 },
            static data => data.GetProperty("value").GetInt32());

        Assert.Equal(MesCallOutcome.TransportFailure, result.Outcome);
    }

    private static HomogenizationMesChannel CreateChannel(
        CapturingMesHttpClient httpClient,
        string stationNo,
        IReadOnlyDictionary<HomogenizationParams.Mes, string>? mesValues = null)
    {
        var logger = new FakeLogService();
        var roleProvider = new FakeModuleParamRoleProvider(stationNo);
        var parameters = new FakeModuleParamProvider(mesValues);
        var executor = CreateExecutor(httpClient, stationNo, logger, roleProvider);

        return new HomogenizationMesChannel(
            executor,
            roleProvider,
            parameters,
            logger,
            new FakeProductionTimeProvider(),
            Options.Create(CreateMesOptions()),
            Options.Create(CreateCodeOptions()));
    }

    private static MesRequestExecutor CreateExecutor(
        CapturingMesHttpClient httpClient,
        string stationNo,
        FakeLogService? logger = null,
        FakeModuleParamRoleProvider? roleProvider = null)
        => new(
            httpClient,
            new FakeMesEndpointProvider(),
            roleProvider ?? new FakeModuleParamRoleProvider(stationNo),
            logger ?? new FakeLogService());

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
                Inbound = "/legacy/inbound",
                Outbound = "/legacy/outbound",
                Recipe = "/legacy/recipe",
                Realtime = "/legacy/realtime",
                EquipmentStatus = "/legacy/equipment-status"
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
        public List<string> GetUrls { get; } = [];
        public string? LastUrl => Requests.LastOrDefault().Url;
        public object? LastPayload => Requests.LastOrDefault().Payload;
        public string? LastGetUrl => GetUrls.LastOrDefault();
        public string Response { get; set; } = """{"code":200,"msg":"OK"}""";
        public Exception? PostWithResponseException { get; set; }

        public Task<bool> PostAsync(
            string processType,
            string url,
            object payload,
            IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<string?> PostWithResponseAsync(
            string processType,
            string url,
            object payload,
            IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((url, payload));
            if (PostWithResponseException is not null)
            {
                return Task.FromException<string?>(PostWithResponseException);
            }

            return Task.FromResult<string?>(Response);
        }

        public Task<string?> GetAsync(
            string processType,
            string url,
            IReadOnlyDictionary<string, string>? headers = null,
            CancellationToken cancellationToken = default)
        {
            GetUrls.Add(url);
            return Task.FromResult<string?>(Response);
        }
    }

    private sealed class FakeMesEndpointProvider : IMesEndpointProvider
    {
        public Task<bool> IsConfiguredAsync(string processType, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<string> BuildUrlAsync(
            string processType,
            string relativeOrAbsoluteUrl,
            CancellationToken cancellationToken = default)
            => Task.FromResult($"https://mes.local{relativeOrAbsoluteUrl}");

        public Task<string?> TryBuildFirstConfiguredUrlAsync(
            IReadOnlyCollection<string> processTypes,
            string relativeOrAbsoluteUrl,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>($"https://mes.local{relativeOrAbsoluteUrl}");

        public IReadOnlyDictionary<string, string> GetDefaultHeaders() => new Dictionary<string, string>();
    }

    private sealed class FakeModuleParamProvider(
        IReadOnlyDictionary<HomogenizationParams.Mes, string>? mesValues)
        : IModuleParamProvider<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>
    {
        public Task<ModuleParamSnapshot<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>> GetAsync(
            CancellationToken cancellationToken = default)
        {
            var mesDefaults = new Dictionary<HomogenizationParams.Mes, string?>
            {
                [HomogenizationParams.Mes.启用] = "true",
                [HomogenizationParams.Mes.服务地址] = "https://mes.local",
                [HomogenizationParams.Mes.工站编号] = "ST-H-00",
                [HomogenizationParams.Mes.MesHealthPath] = "/heath",
                [HomogenizationParams.Mes.InboundPath] = "/dev/dev/getIn/check",
                [HomogenizationParams.Mes.OutboundPath] = "/dev/dev/electrode/exit/push",
                [HomogenizationParams.Mes.RecipePath] = "/dev/dev/process/param",
                [HomogenizationParams.Mes.RealtimePath] = "/dev/dev/run/info",
                [HomogenizationParams.Mes.EquipmentStatusPath] = "/dev/dev/realTime/status",
                [HomogenizationParams.Mes.OrderPath] = "/dev/dev/get/order",
                [HomogenizationParams.Mes.BatchNumberPath] = "/dev/dev/get/batchNumber",
                [HomogenizationParams.Mes.签名令牌] = "hdc2023"
            };
            var mesKinds = Enum.GetValues<HomogenizationParams.Mes>()
                .ToDictionary(static key => key, static key => ParamValueKind.String);
            mesKinds[HomogenizationParams.Mes.启用] = ParamValueKind.Bool;

            var mes = new ModuleParamGroup<HomogenizationParams.Mes>(
                "Homogenization",
                ModuleParamCategory.Mes,
                mesValues ?? new Dictionary<HomogenizationParams.Mes, string>(),
                mesDefaults,
                mesKinds,
                warn: null);

            var cloud = new ModuleParamGroup<HomogenizationParams.Cloud>(
                "Homogenization",
                ModuleParamCategory.Cloud,
                new Dictionary<HomogenizationParams.Cloud, string>(),
                new Dictionary<HomogenizationParams.Cloud, string?>
                {
                    [HomogenizationParams.Cloud.启用] = "false"
                },
                new Dictionary<HomogenizationParams.Cloud, ParamValueKind>
                {
                    [HomogenizationParams.Cloud.启用] = ParamValueKind.Bool
                },
                warn: null);

            var business = new ModuleParamGroup<HomogenizationParams.Business>(
                "Homogenization",
                ModuleParamCategory.Business,
                new Dictionary<HomogenizationParams.Business, string>(),
                new Dictionary<HomogenizationParams.Business, string?>
                {
                    [HomogenizationParams.Business.启用托盘码重码验证] = "false"
                },
                new Dictionary<HomogenizationParams.Business, ParamValueKind>
                {
                    [HomogenizationParams.Business.启用托盘码重码验证] = ParamValueKind.Bool
                },
                warn: null);

            var snapshot = new ModuleParamSnapshot<HomogenizationParams.Mes, HomogenizationParams.Cloud, HomogenizationParams.Business>(
                "Homogenization",
                mes,
                cloud,
                business);
            return Task.FromResult(snapshot);
        }
    }

    private sealed class FakeModuleParamRoleProvider(
        string stationNo,
        string upperComputerNo = "A1-STUC",
        string? operationCode = "CG") : IModuleParamRoleProvider
    {
        public Task<ModuleParamRoleValue?> GetAsync(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateValue(moduleId, category, role));

        public Task<IReadOnlyList<ModuleParamRoleValue>> GetAllAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            CancellationToken cancellationToken = default)
        {
            var values = (moduleIds ?? ["Homogenization"])
                .Select(moduleId => CreateValue(moduleId, category, role))
                .Where(static value => value is not null)
                .Cast<ModuleParamRoleValue>()
                .ToList();
            return Task.FromResult<IReadOnlyList<ModuleParamRoleValue>>(values);
        }

        public Task<string?> GetStringAsync(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role,
            string? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateValue(moduleId, category, role)?.Value ?? defaultValue);

        public Task<string?> FirstStringAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(CreateValue((moduleIds ?? ["Homogenization"]).First(), category, role)?.Value);

        public Task<bool> GetBoolAsync(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role,
            bool defaultValue = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(role == ModuleParamRole.MesEnabled || defaultValue);

        public Task<bool> AnyBoolAsync(
            ModuleParamCategory category,
            ModuleParamRole role,
            IReadOnlyCollection<string>? moduleIds = null,
            bool defaultValue = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(role == ModuleParamRole.MesEnabled || defaultValue);

        private ModuleParamRoleValue? CreateValue(
            string moduleId,
            ModuleParamCategory category,
            ModuleParamRole role)
        {
            if (category != ModuleParamCategory.Mes)
            {
                return null;
            }

            return role switch
            {
                ModuleParamRole.MesEnabled => Build("启用", "true", ParamValueKind.Bool),
                ModuleParamRole.MesBaseUrl => Build("服务地址", "https://mes.local", ParamValueKind.String),
                ModuleParamRole.StationNo => Build("工站编号", stationNo, ParamValueKind.String),
                ModuleParamRole.MesUpperComputerNo => Build("UpperComputerNo", upperComputerNo, ParamValueKind.String),
                ModuleParamRole.MesOperationCode when operationCode is not null => Build("OperationCode", operationCode, ParamValueKind.String),
                _ => null
            };

            ModuleParamRoleValue Build(string name, string value, ParamValueKind kind)
                => new(
                    moduleId,
                    category,
                    role,
                    kind,
                    name,
                    $"Module:{moduleId}:Mes:{name}",
                    value,
                    value);
        }

        public Task<IReadOnlyList<LocalSystemConfigSnapshot>> GetSystemConfigsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LocalSystemConfigSnapshot>>(
            [
                new LocalSystemConfigSnapshot(1, "Module:Homogenization:Mes:工站编号", stationNo, null, 1)
            ]);

        public Task<string?> LegacyGetSystemConfigValueAsync(
            string key,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(key == "工站编号" ? stationNo : null);

    }
}
