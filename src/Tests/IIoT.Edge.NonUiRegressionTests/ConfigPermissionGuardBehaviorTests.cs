using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Auth;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Application.Features.Config.ParamView;
using IIoT.Edge.Application.Features.Config.ParamView.Models;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Result;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;

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
            [new GeneralParamVm { Name = "ClientCode", Value = "EDGE-01" }],
            7,
            [new DeviceParamVm { Name = "Temperature", Value = "180" }]);

        Assert.False(result.IsSuccess);
        Assert.Equal("当前用户无参数配置权限。", result.Message);
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
                [new GeneralParamVm { Name = "ClientCode", Value = "EDGE-01" }],
                7,
                [new DeviceParamVm { Name = "Temperature", Value = "180" }]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("当前用户无参数配置权限。", result.Message);
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
            [new NetworkDeviceVm { Id = 1, DeviceName = "PLC-A", DeviceType = DeviceType.PLC }],
            [],
            1,
            []);

        Assert.False(result.IsSuccess);
        Assert.Equal("当前用户无硬件配置权限。", result.Message);
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

        var result = await service.ApplyModuleTemplateAsync(new NetworkDeviceVm
        {
            Id = 1,
            DeviceName = "PLC-A",
            DeviceType = DeviceType.PLC,
            ModuleId = "Injection"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("当前用户无硬件配置权限。", result.Message);
        Assert.Equal(0, sender.SendCount);
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
            CreateMapper(),
            new StubPermissionService { CanEditHardware = true },
            plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [
                    new NetworkDeviceVm
                    {
                        Id = 1,
                        DeviceName = "PLC-A",
                        DeviceType = DeviceType.PLC,
                        ModuleId = "Injection",
                        IsEnabled = true
                    },
                    new NetworkDeviceVm
                    {
                        Id = 2,
                        DeviceName = "PLC-B",
                        DeviceType = DeviceType.PLC,
                        ModuleId = "Injection",
                        IsEnabled = false
                    },
                    new NetworkDeviceVm
                    {
                        Id = 3,
                        DeviceName = "Scanner-A",
                        DeviceType = DeviceType.Scanner,
                        ModuleId = string.Empty,
                        IsEnabled = true
                    }
                ],
                [],
                1,
                [
                    new IoMappingVm
                    {
                        Id = 10,
                        NetworkDeviceId = 1,
                        Label = "Test.Signal",
                        PlcAddress = "DB1.DBW0",
                        AddressCount = 1,
                        DataType = "Int16",
                        Direction = "Read",
                        SortOrder = 1
                    }
                ]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("配置已保存，但以下 PLC 重载失败：", result.Message);
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

    private static AutoMapper.IMapper CreateMapper()
    {
        var configuration = new AutoMapper.MapperConfiguration(
            cfg =>
            {
                cfg.AddProfile<IIoT.Edge.Application.Features.Config.ParamView.Mappings.ParamViewMappingProfile>();
                cfg.AddProfile<IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Mappings.HardwareConfigMappingProfile>();
            },
            NullLoggerFactory.Instance);

        return configuration.CreateMapper();
    }

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

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
