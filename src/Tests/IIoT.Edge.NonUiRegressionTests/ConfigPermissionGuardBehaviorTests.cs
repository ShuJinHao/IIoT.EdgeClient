using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Auth;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Result;
using MediatR;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class ConfigPermissionGuardBehaviorTests
{
    [Fact]
    public void ClientPermissionService_WhenAuthStateChanges_ShouldRefreshPermissionFlags()
    {
        var authService = new FakeAuthService();
        var permissionService = new ClientPermissionService(authService);
        var eventCount = 0;
        permissionService.PermissionStateChanged += () => eventCount++;

        authService.SetSession(new UserSession
        {
            DisplayName = "E1001",
            EmployeeNo = "E1001",
            Permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Permissions.ParamConfig
            }
        });

        Assert.True(permissionService.CanEditParams);
        Assert.False(permissionService.CanEditHardware);
        Assert.False(permissionService.IsLocalAdmin);
        Assert.Equal(1, eventCount);

        authService.SetSession(new UserSession
        {
            DisplayName = "Local Admin",
            EmployeeNo = "LOCAL_ADMIN",
            IsLocalAdmin = true
        });

        Assert.True(permissionService.CanEditParams);
        Assert.True(permissionService.CanEditHardware);
        Assert.True(permissionService.IsLocalAdmin);
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public async Task ParamViewCrudService_SaveAsync_WhenNoParamPermission_ShouldFailWithoutSending()
    {
        var sender = new CountingSender();
        var service = new ParamViewCrudService(
            sender,
            new StubPermissionService { CanEditParams = false });

        var result = await service.SaveAsync(
            [new ParamViewValueDto("Module:Homogenization:Business:启用托盘码重码验证", "true")]);

        Assert.False(result.IsSuccess);
        Assert.Contains("参数配置权限", result.Message);
        Assert.Equal(0, sender.SendCount);
    }

    [Fact]
    public async Task SaveParamViewHandler_WhenNoParamPermission_ShouldFailWithoutSaving()
    {
        var sender = new CountingSender();
        var handler = new SaveParamViewHandler(
            sender,
            new StubPermissionService { CanEditParams = false });

        var result = await handler.Handle(
            new SaveParamViewCommand(
                [new ParamViewValueDto("Module:Homogenization:Business:启用托盘码重码验证", "true")]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("参数配置权限", result.Message);
        Assert.Equal(0, sender.SendCount);
    }

    [Fact]
    public async Task HardwareConfigCrudService_SaveAsync_WhenNoHardwarePermission_ShouldFailWithoutSending()
    {
        var sender = new CountingSender();
        var service = new HardwareConfigCrudService(
            sender,
            [],
            new StubPermissionService { CanEditHardware = false });

        var result = await service.SaveAsync(
            [CreateNetworkDeviceDto(1, "PLC-A")],
            [],
            1,
            []);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, sender.SendCount);
    }

    [Fact]
    public async Task HardwareConfigCrudService_ApplyModuleTemplateAsync_WhenNoHardwarePermission_ShouldFailWithoutSending()
    {
        var sender = new CountingSender();
        var service = new HardwareConfigCrudService(
            sender,
            [],
            new StubPermissionService { CanEditHardware = false });

        var result = await service.ApplyModuleTemplateAsync(CreateNetworkDeviceDto(1, "PLC-A"));

        Assert.False(result.IsSuccess);
        Assert.Equal(0, sender.SendCount);
    }

    [Fact]
    public async Task HardwareConfigCrudService_ApplyModuleTemplateAsync_WhenAllowed_ShouldResetCurrentPlcMappings()
    {
        SaveIoMappingsCommand? savedCommand = null;
        var sender = new CountingSender(request => request switch
        {
            SaveIoMappingsCommand command => Capture(command),
            _ => throw new NotSupportedException(request.GetType().FullName)
        });
        var service = new HardwareConfigCrudService(
            sender,
            [new ResetTemplateProfile()],
            new StubPermissionService { CanEditHardware = true });

        var result = await service.ApplyModuleTemplateAsync(CreateNetworkDeviceDto(7, "PLC-A"));

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(savedCommand);
        Assert.Equal(3, savedCommand!.Mappings.Count);
        Assert.DoesNotContain(savedCommand.Mappings, x => x.SignalKey == "TEST");
        Assert.Contains(savedCommand.Mappings, x => x.SignalKey == "Test.Interaction.Outbound" && x.Direction == "Read");
        Assert.Contains(savedCommand.Mappings, x => x.SignalKey == "Test.Interaction.Outbound" && x.Direction == "Write");
        Assert.Contains(savedCommand.Mappings, x => x.SignalKey == "Test.Pending" && x.PlcAddress == string.Empty);

        Result Capture(SaveIoMappingsCommand command)
        {
            savedCommand = command;
            return Result.Success();
        }
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenReloadFails_ShouldReturnFailureAfterSaving()
    {
        var sender = new CountingSender(request => request switch
        {
            GetAllNetworkDevicesQuery => Result.Success(new List<NetworkDeviceEntity>()),
            GetIoMappingsByDeviceQuery => Result.Success(new IoMappingPagedDto(new List<IoMappingEntity>(), 0)),
            SaveNetworkDevicesCommand => Result.Success(),
            SaveSerialDevicesCommand => Result.Success(),
            SaveIoMappingsCommand => Result.Success(),
            _ => throw new NotSupportedException(request.GetType().FullName)
        });
        var plcManager = new FakePlcConnectionManager();
        plcManager.ReloadFailures["PLC-B"] = new InvalidOperationException("reload boom");

        var handler = new SaveHardwareConfigHandler(
            sender,
            new StubPermissionService { CanEditHardware = true },
            plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [
                    CreateNetworkDeviceDto(1, "PLC-A"),
                    CreateNetworkDeviceDto(2, "PLC-B", isEnabled: false),
                    CreateNetworkDeviceDto(3, "Scanner-A", DeviceType.Scanner)
                ],
                [],
                1,
                [
                    CreateIoMappingDto(10, 1, "Test.Signal")
                ]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("PLC-B", result.Message);
        Assert.Equal(
            ["PLC-A", "PLC-B"],
            plcManager.ReloadedDeviceNames);
        Assert.Equal(
            [
                typeof(GetAllNetworkDevicesQuery),
                typeof(GetIoMappingsByDeviceQuery),
                typeof(SaveNetworkDevicesCommand),
                typeof(SaveSerialDevicesCommand),
                typeof(SaveIoMappingsCommand)
            ],
            sender.Requests.Select(x => x.GetType()).ToArray());
    }

    private static NetworkDeviceDto CreateNetworkDeviceDto(
        int id,
        string name,
        DeviceType deviceType = DeviceType.PLC,
        bool isEnabled = true)
        => new(
            id,
            name,
            deviceType,
            deviceType == DeviceType.PLC ? "S7" : null,
            "192.168.0.10",
            102,
            null,
            null,
            null,
            3000,
            isEnabled,
            null);

    private static IoMappingDto CreateIoMappingDto(
        int id,
        int deviceId,
        string signalKey)
        => new(
            id,
            deviceId,
            signalKey,
            "DB1.DBW0",
            1,
            "Int16",
            "Read",
            "单点读数据",
            string.Empty,
            1,
            null);

    private sealed class FakeAuthService : IAuthService
    {
        private UserSession? _currentUser;

        public UserSession? CurrentUser => _currentUser;

        public bool IsAuthenticated => _currentUser is not null;

        public event Action<UserSession?>? AuthStateChanged;

        public bool HasPermission(string permission)
        {
            if (_currentUser is null)
            {
                return false;
            }

            if (_currentUser.IsLocalAdmin)
            {
                return true;
            }

            return _currentUser.Permissions.Contains(permission);
        }

        public Task<bool> EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(IsAuthenticated);

        public Task<AuthResult> LoginLocalAsync(string password) => throw new NotSupportedException();

        public Task<AuthResult> LoginCloudAsync(string employeeNo, string password, Guid deviceId) => throw new NotSupportedException();

        public void Logout() => SetSession(null);

        public void SetSession(UserSession? session)
        {
            _currentUser = session;
            AuthStateChanged?.Invoke(session);
        }
    }

    private sealed class StubPermissionService : IClientPermissionService
    {
        public bool CanEditParams { get; init; }

        public bool CanEditHardware { get; init; }

        public bool IsLocalAdmin { get; init; }

        public event Action? PermissionStateChanged;

        public bool HasPermission(string permission)
            => permission switch
            {
                _ when IsLocalAdmin => true,
                var value when string.Equals(value, Permissions.ParamConfig, StringComparison.OrdinalIgnoreCase) => CanEditParams,
                var value when string.Equals(value, Permissions.HardwareConfig, StringComparison.OrdinalIgnoreCase) => CanEditHardware,
                _ => false
            };

        public void RaisePermissionStateChanged() => PermissionStateChanged?.Invoke();
    }

    private sealed class CountingSender(Func<object, object?>? responseFactory = null) : ISender
    {
        public int SendCount { get; private set; }

        public List<object> Requests { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            SendCount++;
            Requests.Add(request);

            if (responseFactory is null)
            {
                throw new NotSupportedException(request.GetType().FullName);
            }

            return Task.FromResult((TResponse)responseFactory(request)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException(request?.GetType().FullName);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().FullName);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().FullName);

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().FullName);
    }

    private sealed class ResetTemplateProfile : IModuleHardwareProfileProvider
    {
        public string ModuleId => "TestModule";

        public ModulePlcDefaults GetDefaultPlcSettings()
            => new("Mc", 3000, 6000);

        public PlcIoRuntimePolicy GetIoRuntimePolicy()
            => PlcIoRuntimePolicy.Default;

        public IReadOnlyList<ModuleIoTemplateEntry> GetDefaultIoTemplate()
            =>
            [
                new(
                    "Test.Interaction.Outbound",
                    "D702",
                    1,
                    "Int16",
                    "Read",
                    1,
                    "测试交互读点",
                    "信号交互",
                    "出料上传"),
                new(
                    "Test.Interaction.Outbound",
                    "D602",
                    1,
                    "Int16",
                    "Write",
                    101,
                    "测试交互写点",
                    "信号交互",
                    "出料上传")
            ];

        public IReadOnlyList<ModuleIoTemplateEntry> GetIoMappingCandidates()
            =>
            [
                .. GetDefaultIoTemplate(),
                new(
                    "Test.Pending",
                    "",
                    1,
                    "Int16",
                    "Read",
                    999,
                    "待配置点位",
                    "单点读数据",
                    "调试")
            ];

        public ModuleHardwareValidationResult ValidatePlcConfiguration(
            string deviceName,
            string? deviceModel,
            IReadOnlyCollection<ModuleIoSnapshot> mappings)
            => ModuleHardwareValidationResult.Success();
    }

    private sealed class FakePlcConnectionManager : IPlcConnectionManager
    {
        public Dictionary<string, Exception> ReloadFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> ReloadedDeviceNames { get; } = [];

        public List<int> StoppedDeviceIds { get; } = [];

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default)
        {
            StoppedDeviceIds.Add(networkDeviceId);
            return Task.CompletedTask;
        }

        public Task ReloadAsync(string deviceName, CancellationToken ct = default)
        {
            ReloadedDeviceNames.Add(deviceName);
            if (ReloadFailures.TryGetValue(deviceName, out var exception))
            {
                throw exception;
            }

            return Task.CompletedTask;
        }

        public void RegisterTasks(
            string deviceName,
            Func<IPlcBuffer, ProductionContext, List<IIoT.Edge.Application.Abstractions.Plc.IPlcTask>> factory)
        {
        }

        public IIoT.Edge.Application.Abstractions.Plc.IPlcService? GetPlc(int networkDeviceId) => null;

        public ProductionContext? GetContext(string deviceName) => null;

        public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
        {
        }

        public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId) => null;

        public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses()
            => Array.Empty<PlcConnectionRuntimeSnapshot>();

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
