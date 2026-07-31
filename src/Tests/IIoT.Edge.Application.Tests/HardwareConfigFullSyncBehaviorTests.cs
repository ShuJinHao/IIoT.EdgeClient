using System.Linq.Expressions;
using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.SharedKernel.Domain;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.SharedKernel.Result;
using IIoT.Edge.SharedKernel.Specification;
using IIoT.Edge.Testing;
using MediatR;

namespace IIoT.Edge.Application.Tests;

public sealed class HardwareConfigFullSyncBehaviorTests
{
    [Fact]
    public async Task SaveNetworkDevicesHandler_WhenPlcModuleMissing_ShouldReturnDomainFailure()
    {
        var repo = new InMemoryRepository<NetworkDeviceEntity>();
        var handler = new SaveNetworkDevicesHandler(new TestEdgeUnitOfWorkFactory(repo));

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
                [
                    new NetworkDeviceDto(
                        0,
                        "PLC-A",
                        DeviceType.PLC,
                        "S7",
                        string.Empty,
                        102,
                        null,
                        null,
                        null,
                        3000,
                        true,
                        null)
                ]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("网络设备地址不能为空。", result.ErrorMessage);
        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task SaveNetworkDevicesHandler_WhenSubmissionInvalid_ShouldNotDeleteExistingRows()
    {
        var repo = new InMemoryRepository<NetworkDeviceEntity>(
            CreateNetworkDevice(id: 1, name: "PLC-A"),
            CreateNetworkDevice(id: 2, name: "PLC-B"));
        var handler = new SaveNetworkDevicesHandler(new TestEdgeUnitOfWorkFactory(repo));

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
                [
                    new NetworkDeviceDto(
                        1,
                        "PLC-A",
                        DeviceType.PLC,
                        "S7",
                        string.Empty,
                        102,
                        null,
                        null,
                        null,
                        3000,
                        true,
                        null)
                ]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal([1, 2], repo.Items.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task SaveNetworkDevicesHandler_WhenDeviceMissingFromSubmission_ShouldDeleteIt()
    {
        var repo = new InMemoryRepository<NetworkDeviceEntity>(
            CreateNetworkDevice(id: 1, name: "PLC-A"),
            CreateNetworkDevice(id: 2, name: "PLC-B"));
        var handler = new SaveNetworkDevicesHandler(new TestEdgeUnitOfWorkFactory(repo));

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
                [
                    new NetworkDeviceDto(
                        1,
                        "PLC-A-UPDATED",
                        DeviceType.PLC,
                        "S7",
                        "192.168.0.11",
                        102,
                        null,
                        null,
                        null,
                        5000,
                        true,
                        "updated")
                ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            repo.Items.OrderBy(x => x.Id),
            device =>
            {
                Assert.Equal(1, device.Id);
                Assert.Equal("PLC-A-UPDATED", device.DeviceName);
                Assert.Equal("192.168.0.11", device.IpAddress);
                Assert.Equal(5000, device.ConnectTimeout);
                Assert.Equal("updated", device.Remark);
                Assert.Equal("PLC-A", device.PlcCode);
            });
    }

    [Fact]
    public async Task SaveNetworkDevicesHandler_WhenExistingDeviceRenamed_ShouldPreservePlcCode()
    {
        var existing = CreateNetworkDevice(id: 1, name: "PLC-A");
        var repo = new InMemoryRepository<NetworkDeviceEntity>(existing);
        var handler = new SaveNetworkDevicesHandler(new TestEdgeUnitOfWorkFactory(repo));

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
                [
                    new NetworkDeviceDto(
                        1,
                        "一号 PLC",
                        DeviceType.PLC,
                        "S7",
                        "192.168.0.11",
                        102,
                        null,
                        null,
                        null,
                        3000,
                        true,
                        null)
                ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var updated = Assert.Single(repo.Items);
        Assert.Equal("一号 PLC", updated.DeviceName);
        Assert.Equal("PLC-A", updated.PlcCode);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenUnchangedPlcRowAdvancesAfterSnapshot_ShouldNotOverwriteIt()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A", port1: 102),
                CreateNetworkDevice(id: 2, name: "PLC-B", port1: 102)
            ]
        };
        var networkRepo = new InMemoryRepository<NetworkDeviceEntity>(
            CreateNetworkDevice(id: 1, name: "PLC-A", port1: 104),
            CreateNetworkDevice(id: 2, name: "PLC-B", port1: 102));
        var plcManager = new FakePlcConnectionManager();
        var handler = new SaveHardwareConfigHandler(
            sender,
            new TestEdgeUnitOfWorkFactory(
                networkRepo,
                new InMemoryRepository<SerialDeviceEntity>(),
                new InMemoryRepository<IoMappingEntity>()),
            new StubPermissionService { CanEditHardware = true },
            plcManager,
            new FakePlcRuntimeApplyService(sender, plcManager),
            new PlcRuntimeConfigurationMutationGate());

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [
                    CreateNetworkDto(id: 1, name: "PLC-A", port1: 102),
                    CreateNetworkDto(id: 2, name: "PLC-B", port1: 103)
                ],
                [],
                2,
                []),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(104, networkRepo.Items.Single(x => x.Id == 1).Port1);
        Assert.Equal(103, networkRepo.Items.Single(x => x.Id == 2).Port1);
        Assert.Equal(["PLC-B"], plcManager.ReloadedDeviceNames);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenUnchangedIoAdvancesAfterSnapshot_ShouldNotOverwriteIt()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A")
            ],
            ExistingIoMappings =
            [
                CreateIoMapping(
                    id: 11,
                    deviceId: 1,
                    signalKey: "Signal.A",
                    plcAddress: "DB1.DBW0")
            ]
        };
        var networkRepo = new InMemoryRepository<NetworkDeviceEntity>(
            CreateNetworkDevice(id: 1, name: "PLC-A"));
        var ioRepo = new InMemoryRepository<IoMappingEntity>(
            CreateIoMapping(
                id: 11,
                deviceId: 1,
                signalKey: "Signal.A",
                plcAddress: "DB1.DBW2"));
        var plcManager = new FakePlcConnectionManager();
        var handler = new SaveHardwareConfigHandler(
            sender,
            new TestEdgeUnitOfWorkFactory(
                networkRepo,
                new InMemoryRepository<SerialDeviceEntity>(),
                ioRepo),
            new StubPermissionService { CanEditHardware = true },
            plcManager,
            new FakePlcRuntimeApplyService(sender, plcManager),
            new PlcRuntimeConfigurationMutationGate());

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkDto(id: 1, name: "PLC-A")],
                [],
                1,
                [
                    CreateIoMappingDto(
                        id: 11,
                        deviceId: 1,
                        signalKey: "Signal.A",
                        plcAddress: "DB1.DBW0")
                ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("DB1.DBW2", Assert.Single(ioRepo.Items).PlcAddress);
        Assert.Empty(plcManager.ReloadedDeviceNames);
    }

    [Fact]
    public async Task SaveNetworkDevicesHandler_WhenNewDeviceNamesCollide_ShouldAssignDistinctPlcCodes()
    {
        var repo = new InMemoryRepository<NetworkDeviceEntity>();
        var handler = new SaveNetworkDevicesHandler(new TestEdgeUnitOfWorkFactory(repo));

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
                [
                    new NetworkDeviceDto(
                        0,
                        "PLC-DUP",
                        DeviceType.PLC,
                        "S7",
                        "192.168.0.11",
                        102,
                        null,
                        null,
                        null,
                        3000,
                        true,
                        null),
                    new NetworkDeviceDto(
                        0,
                        "plc-dup",
                        DeviceType.PLC,
                        "S7",
                        "192.168.0.12",
                        102,
                        null,
                        null,
                        null,
                        3000,
                        true,
                        null)
                ]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, repo.Items.Count);
        Assert.Equal(2, repo.Items.Select(device => device.PlcCode).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(repo.Items, device => device.PlcCode == "PLC-DUP");
        Assert.Contains(repo.Items, device => device.PlcCode.StartsWith("PLC-INTERNAL-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveNetworkDevicesHandler_WhenMcProtocolFrameSubmitted_ShouldPersistNormalizedFrame()
    {
        var repo = new InMemoryRepository<NetworkDeviceEntity>();
        var handler = new SaveNetworkDevicesHandler(new TestEdgeUnitOfWorkFactory(repo));

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
                [
                    new NetworkDeviceDto(
                        0,
                        "PLC-MC",
                        DeviceType.PLC,
                        "Mc",
                        "192.168.0.10",
                        65530,
                        null,
                        null,
                        null,
                        3000,
                        true,
                        null,
                        "e4")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var device = Assert.Single(repo.Items);
        Assert.Equal("E4", device.ProtocolFrame);
    }

    [Fact]
    public async Task SaveNetworkDevicesHandler_WhenMcProtocolFrameInvalid_ShouldRejectSubmission()
    {
        var repo = new InMemoryRepository<NetworkDeviceEntity>();
        var handler = new SaveNetworkDevicesHandler(new TestEdgeUnitOfWorkFactory(repo));

        var result = await handler.Handle(
            new SaveNetworkDevicesCommand(
                [
                    new NetworkDeviceDto(
                        0,
                        "PLC-MC",
                        DeviceType.PLC,
                        "Mc",
                        "192.168.0.10",
                        65530,
                        null,
                        null,
                        null,
                        3000,
                        true,
                        null,
                        "E5")
                ]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("MC PLC 协议帧只支持 E3 或 E4。", result.ErrorMessage);
        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task SaveSerialDevicesHandler_WhenDeviceMissingFromSubmission_ShouldDeleteItAndPreserveExtendedFields()
    {
        var repo = new InMemoryRepository<SerialDeviceEntity>(
            CreateSerialDevice(id: 1, name: "Scanner-A"),
            CreateSerialDevice(id: 2, name: "Scanner-B"));
        var handler = new SaveSerialDevicesHandler(new TestEdgeUnitOfWorkFactory(repo));

        var result = await handler.Handle(
            new SaveSerialDevicesCommand(
                [
                    new SerialDeviceDto(
                        1,
                        "Scanner-A",
                        "Scanner",
                        "COM3",
                        115200,
                        7,
                        "Two",
                        "Odd",
                        "A1",
                        "A2",
                        false,
                        "preserved")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            repo.Items,
            device =>
            {
                Assert.Equal(1, device.Id);
                Assert.Equal(7, device.DataBits);
                Assert.Equal("Two", device.StopBits);
                Assert.Equal("Odd", device.Parity);
                Assert.Equal("A1", device.SendCmd1);
                Assert.Equal("A2", device.SendCmd2);
                Assert.False(device.IsEnabled);
                Assert.Equal("preserved", device.Remark);
            });
    }

    [Fact]
    public async Task SaveIoMappingsHandler_WhenMappingMissingFromSubmission_ShouldDeleteItAndPreserveRemark()
    {
        var repo = new InMemoryRepository<IoMappingEntity>(
            CreateIoMapping(id: 1, deviceId: 9, signalKey: "Signal.A", remark: "keep"),
            CreateIoMapping(id: 2, deviceId: 9, signalKey: "Signal.B", remark: "delete"),
            CreateIoMapping(id: 3, deviceId: 10, signalKey: "Signal.C", remark: "other-device"));
        var handler = new SaveIoMappingsHandler(new TestEdgeUnitOfWorkFactory(repo));

        var result = await handler.Handle(
            new SaveIoMappingsCommand(
                9,
                [
                    new IoMappingDto(
                        1,
                        9,
                        "Signal.A",
                        "DB1.DBW0",
                        2,
                        "Int16",
                        "Read",
                        "单点读数据",
                        string.Empty,
                        1,
                        "updated-remark")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, repo.Items.Count);
        Assert.DoesNotContain(repo.Items, x => x.Id == 2);
        Assert.Contains(repo.Items, x => x.Id == 1 && x.Remark == "updated-remark");
        Assert.Contains(repo.Items, x => x.Id == 3 && x.Remark == "other-device");
    }

    [Fact]
    public async Task SaveIoMappingsHandler_WhenDataTypeAndWordLengthAreInvalid_ShouldRejectWithoutChangingRows()
    {
        var original = CreateIoMapping(id: 1, deviceId: 9, signalKey: "Signal.A", remark: "keep");
        var repo = new InMemoryRepository<IoMappingEntity>(original);
        var handler = new SaveIoMappingsHandler(new TestEdgeUnitOfWorkFactory(repo));

        var result = await handler.Handle(
            new SaveIoMappingsCommand(
                9,
                [
                    new IoMappingDto(
                        1,
                        9,
                        "Signal.A",
                        "D100",
                        1,
                        IoMappingOptionCatalog.DataTypeInt32,
                        IoMappingOptionCatalog.DirectionRead,
                        IoMappingOptionCatalog.CategorySingleRead,
                        string.Empty,
                        1,
                        "must-not-apply")
                ]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        var unchanged = Assert.Single(repo.Items);
        Assert.Equal("keep", unchanged.Remark);
        Assert.Equal(original.DataType, unchanged.DataType);
        Assert.Equal(original.AddressCount, unchanged.AddressCount);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenExistingPlcIsRemoved_ShouldCallStopDeviceAsync()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A"),
                CreateNetworkDevice(id: 2, name: "PLC-B")
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkDto(id: 2, name: "PLC-B")],
                [],
                2,
                []),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal([1], plcManager.StoppedDeviceIds);
        Assert.Empty(plcManager.ReloadedDeviceNames);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenInitialNetworkQueryFails_ShouldNotPersistOrApplyRuntime()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A", port1: 102)
            ]
        };
        sender.FailedNetworkReadCalls.Add(1);
        var networkRepo = new InMemoryRepository<NetworkDeviceEntity>(
            CreateNetworkDevice(id: 1, name: "PLC-A", port1: 102));
        var unitOfWorkFactory = new TestEdgeUnitOfWorkFactory(
            networkRepo,
            new InMemoryRepository<SerialDeviceEntity>(),
            new InMemoryRepository<IoMappingEntity>());
        var plcManager = new FakePlcConnectionManager();
        var handler = new SaveHardwareConfigHandler(
            sender,
            unitOfWorkFactory,
            new StubPermissionService { CanEditHardware = true },
            plcManager,
            new FakePlcRuntimeApplyService(sender, plcManager),
            new PlcRuntimeConfigurationMutationGate());

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkDto(id: 1, name: "PLC-A", port1: 103)],
                [],
                1,
                []),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("读取现有网络设备配置失败", result.Message, StringComparison.Ordinal);
        Assert.Equal(102, Assert.Single(networkRepo.Items).Port1);
        Assert.Equal(0, unitOfWorkFactory.BeginCount);
        Assert.Equal(0, unitOfWorkFactory.CommitCount);
        Assert.Empty(plcManager.StoppedDeviceIds);
        Assert.Empty(plcManager.ReloadedDeviceIds);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenInitialIoQueryFails_ShouldNotPersistOrApplyRuntime()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A")
            ],
            ExistingIoMappings =
            [
                CreateIoMapping(id: 11, deviceId: 1, signalKey: "Signal.A")
            ]
        };
        sender.FailedIoReadCalls.Add(1);
        var networkRepo = new InMemoryRepository<NetworkDeviceEntity>(
            CreateNetworkDevice(id: 1, name: "PLC-A"));
        var ioRepo = new InMemoryRepository<IoMappingEntity>(
            CreateIoMapping(id: 11, deviceId: 1, signalKey: "Signal.A"));
        var unitOfWorkFactory = new TestEdgeUnitOfWorkFactory(
            networkRepo,
            new InMemoryRepository<SerialDeviceEntity>(),
            ioRepo);
        var plcManager = new FakePlcConnectionManager();
        var handler = new SaveHardwareConfigHandler(
            sender,
            unitOfWorkFactory,
            new StubPermissionService { CanEditHardware = true },
            plcManager,
            new FakePlcRuntimeApplyService(sender, plcManager),
            new PlcRuntimeConfigurationMutationGate());

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkDto(id: 1, name: "PLC-A")],
                [],
                1,
                [
                    CreateIoMappingDto(
                        id: 11,
                        deviceId: 1,
                        signalKey: "Signal.A",
                        remark: "changed")
                ]),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("现有 IO 映射失败", result.Message, StringComparison.Ordinal);
        Assert.Null(Assert.Single(ioRepo.Items).Remark);
        Assert.Equal(0, unitOfWorkFactory.BeginCount);
        Assert.Equal(0, unitOfWorkFactory.CommitCount);
        Assert.Empty(plcManager.StoppedDeviceIds);
        Assert.Empty(plcManager.ReloadedDeviceIds);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenAuthoritativeNetworkQueryFails_ShouldNotPersistOrApplyRuntime()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A", port1: 102)
            ]
        };
        sender.FailedNetworkReadCalls.Add(2);
        var networkRepo = new InMemoryRepository<NetworkDeviceEntity>(
            CreateNetworkDevice(id: 1, name: "PLC-A", port1: 102));
        var unitOfWorkFactory = new TestEdgeUnitOfWorkFactory(
            networkRepo,
            new InMemoryRepository<SerialDeviceEntity>(),
            new InMemoryRepository<IoMappingEntity>());
        var plcManager = new FakePlcConnectionManager();
        var handler = new SaveHardwareConfigHandler(
            sender,
            unitOfWorkFactory,
            new StubPermissionService { CanEditHardware = true },
            plcManager,
            new FakePlcRuntimeApplyService(sender, plcManager),
            new PlcRuntimeConfigurationMutationGate());

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkDto(id: 1, name: "PLC-A", port1: 103)],
                [],
                0,
                []),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("读取现有网络设备配置失败", result.Message, StringComparison.Ordinal);
        Assert.Equal(102, Assert.Single(networkRepo.Items).Port1);
        Assert.Equal(0, unitOfWorkFactory.BeginCount);
        Assert.Equal(0, unitOfWorkFactory.CommitCount);
        Assert.Empty(plcManager.StoppedDeviceIds);
        Assert.Empty(plcManager.ReloadedDeviceIds);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenDeletedPlcGateIsHeld_ShouldWaitBeforeAuthoritativeSnapshotAndCommit()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A"),
                CreateNetworkDevice(id: 2, name: "PLC-B")
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        var mutationGate = new PlcRuntimeConfigurationMutationGate();
        using var bindingMutation = await mutationGate.EnterAsync(
            1,
            TestContext.Current.CancellationToken);
        var handler = CreateSaveHandler(sender, plcManager, mutationGate);

        var save = handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkDto(id: 2, name: "PLC-B")],
                [],
                2,
                []),
            TestContext.Current.CancellationToken);

        Assert.False(save.IsCompleted);
        Assert.Single(sender.Requests, static request => request is GetAllNetworkDevicesQuery);
        Assert.Empty(plcManager.StoppedDeviceIds);
        Assert.Empty(plcManager.ReloadedDeviceNames);

        bindingMutation.Dispose();
        var result = await save.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(
            2,
            sender.Requests.Count(static request => request is GetAllNetworkDevicesQuery));
        Assert.Equal([1], plcManager.StoppedDeviceIds);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_ShouldHoldDeletedAndChangedPlcGatesThroughStopAndReload()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A"),
                CreateNetworkDevice(id: 2, name: "PLC-B", port1: 102)
            ]
        };
        var stopEntered = NewCompletionSource();
        var continueStop = NewCompletionSource();
        var reloadEntered = NewCompletionSource();
        var continueReload = NewCompletionSource();
        var plcManager = new FakePlcConnectionManager
        {
            StopBehavior = async (_, ct) =>
            {
                stopEntered.TrySetResult(true);
                await continueStop.Task.WaitAsync(ct);
            },
            ReloadBehavior = async (_, ct) =>
            {
                reloadEntered.TrySetResult(true);
                await continueReload.Task.WaitAsync(ct);
            }
        };
        var mutationGate = new PlcRuntimeConfigurationMutationGate();
        var handler = CreateSaveHandler(sender, plcManager, mutationGate);

        var save = handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkDto(id: 2, name: "PLC-B", port1: 103)],
                [],
                2,
                []),
            TestContext.Current.CancellationToken);

        await stopEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var deletedPlcMutation = mutationGate
            .EnterAsync(1, TestContext.Current.CancellationToken)
            .AsTask();
        var changedPlcMutation = mutationGate
            .EnterAsync(2, TestContext.Current.CancellationToken)
            .AsTask();
        Assert.False(deletedPlcMutation.IsCompleted);
        Assert.False(changedPlcMutation.IsCompleted);

        continueStop.TrySetResult(true);
        await reloadEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(deletedPlcMutation.IsCompleted);
        Assert.False(changedPlcMutation.IsCompleted);

        continueReload.TrySetResult(true);
        var result = await save.WaitAsync(TestContext.Current.CancellationToken);
        using var deletedLease = await deletedPlcMutation.WaitAsync(
            TestContext.Current.CancellationToken);
        using var changedLease = await changedPlcMutation.WaitAsync(
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal([1], plcManager.StoppedDeviceIds);
        Assert.Equal(["PLC-B"], plcManager.ReloadedDeviceNames);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenOnePlcReloads_ShouldNotHoldUnchangedPlcGate()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A", port1: 102),
                CreateNetworkDevice(id: 2, name: "PLC-B", port1: 102)
            ]
        };
        var reloadEntered = NewCompletionSource();
        var continueReload = NewCompletionSource();
        var plcManager = new FakePlcConnectionManager
        {
            ReloadBehavior = async (deviceName, ct) =>
            {
                if (!string.Equals(deviceName, "PLC-A", StringComparison.Ordinal))
                {
                    return;
                }

                reloadEntered.TrySetResult(true);
                await continueReload.Task.WaitAsync(ct);
            }
        };
        var mutationGate = new PlcRuntimeConfigurationMutationGate();
        var handler = CreateSaveHandler(sender, plcManager, mutationGate);

        var save = handler.Handle(
            new SaveHardwareConfigCommand(
                [
                    CreateNetworkDto(id: 1, name: "PLC-A", port1: 103),
                    CreateNetworkDto(id: 2, name: "PLC-B", port1: 102)
                ],
                [],
                1,
                []),
            TestContext.Current.CancellationToken);

        await reloadEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var changedPlcMutation = mutationGate
            .EnterAsync(1, TestContext.Current.CancellationToken)
            .AsTask();
        var unchangedPlcMutation = mutationGate
            .EnterAsync(2, TestContext.Current.CancellationToken)
            .AsTask();

        Assert.False(changedPlcMutation.IsCompleted);
        Assert.True(unchangedPlcMutation.IsCompleted);
        using var unchangedLease = await unchangedPlcMutation;

        continueReload.TrySetResult(true);
        var result = await save.WaitAsync(TestContext.Current.CancellationToken);
        using var changedLease = await changedPlcMutation.WaitAsync(
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(["PLC-A"], plcManager.ReloadedDeviceNames);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenSelectedPlcMappingsChange_ShouldReloadThatPlcOnly()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A"),
                CreateNetworkDevice(id: 2, name: "PLC-B")
            ],
            ExistingIoMappings =
            [
                CreateIoMapping(id: 11, deviceId: 1, signalKey: "Signal.A", plcAddress: "DB1.DBW0", remark: "old")
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkDto(id: 1, name: "PLC-A"), CreateNetworkDto(id: 2, name: "PLC-B")],
                [],
                1,
                [
                    CreateIoMappingDto(id: 11, deviceId: 1, signalKey: "Signal.A", remark: "new")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Empty(plcManager.StoppedDeviceIds);
        Assert.Equal(["PLC-A"], plcManager.ReloadedDeviceNames);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenPlcUnchanged_ShouldNotReloadIt()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A"),
                CreateNetworkDevice(id: 2, name: "PLC-B")
            ],
            ExistingIoMappings =
            [
                CreateIoMapping(id: 11, deviceId: 1, signalKey: "Signal.A", plcAddress: "DB1.DBW0", remark: "same")
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [CreateNetworkDto(id: 1, name: "PLC-A"), CreateNetworkDto(id: 2, name: "PLC-B")],
                [],
                1,
                [
                    CreateIoMappingDto(id: 11, deviceId: 1, signalKey: "Signal.A", remark: "same")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Empty(plcManager.StoppedDeviceIds);
        Assert.Empty(plcManager.ReloadedDeviceNames);
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenTwoPlcsShareTheSameName_ShouldReloadBothChangedTargets()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-DUP", ipAddress: "192.168.0.10", port1: 102),
                CreateNetworkDevice(id: 2, name: "PLC-DUP", ipAddress: "192.168.0.11", port1: 102)
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [
                    CreateNetworkDto(id: 1, name: "PLC-DUP", ipAddress: "192.168.0.10", port1: 103),
                    CreateNetworkDto(id: 2, name: "PLC-DUP", ipAddress: "192.168.0.11", port1: 104)
                ],
                [],
                1,
                []),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal([1, 2], plcManager.ReloadedDeviceIds);
        Assert.Equal(2, plcManager.ReloadedDeviceNames.Count);
        Assert.All(plcManager.ReloadedDeviceNames, x => Assert.Equal("PLC-DUP", x));
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenNewPlcIsSaved_ShouldApplyByGeneratedDeviceId()
    {
        var sender = new HardwareConfigSender();
        var plcManager = new FakePlcConnectionManager();
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [
                    CreateNetworkDto(
                        id: 0,
                        name: "PLC-NEW",
                        ipAddress: "192.168.0.20")
                ],
                [],
                0,
                []),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Collection(
            plcManager.ReloadedDeviceIds,
            networkDeviceId => Assert.True(networkDeviceId > 0));
    }

    [Fact]
    public async Task SaveHardwareConfigHandler_WhenPersistenceSucceedsButStopOrReloadFails_ShouldReturnSavedButNotAppliedMessage()
    {
        var sender = new HardwareConfigSender
        {
            ExistingNetworkDevices =
            [
                CreateNetworkDevice(id: 1, name: "PLC-A"),
                CreateNetworkDevice(id: 2, name: "PLC-B", port1: 102)
            ]
        };
        var plcManager = new FakePlcConnectionManager();
        plcManager.StopFailures[1] = new InvalidOperationException("stop boom");
        plcManager.ReloadFailures["PLC-B"] = new InvalidOperationException("reload boom");
        var handler = CreateSaveHandler(sender, plcManager);

        var result = await handler.Handle(
            new SaveHardwareConfigCommand(
                [
                    CreateNetworkDto(id: 2, name: "PLC-B", port1: 103)
                ],
                [],
                2,
                []),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.StartsWith("配置已保存，但", result.Message);
        Assert.Contains("以下 PLC 已删除停机失败：", result.Message);
        Assert.Contains("以下 PLC 重载失败：", result.Message);
        Assert.Contains("PLC-A", result.Message);
        Assert.Contains("PLC-B", result.Message);
        Assert.Equal([1], plcManager.StoppedDeviceIds);
        Assert.Equal(["PLC-B"], plcManager.ReloadedDeviceNames);
    }

    private static SaveHardwareConfigHandler CreateSaveHandler(
        HardwareConfigSender sender,
        FakePlcConnectionManager plcManager,
        IPlcRuntimeConfigurationMutationGate? runtimeConfigurationMutationGate = null)
        => new(
            sender,
            new TestEdgeUnitOfWorkFactory(
                new InMemoryRepository<NetworkDeviceEntity>([.. sender.ExistingNetworkDevices]),
                new InMemoryRepository<SerialDeviceEntity>(),
                new InMemoryRepository<IoMappingEntity>([.. sender.ExistingIoMappings])),
            new StubPermissionService { CanEditHardware = true },
            plcManager,
            new FakePlcRuntimeApplyService(sender, plcManager),
            runtimeConfigurationMutationGate ?? new PlcRuntimeConfigurationMutationGate());

    private static TaskCompletionSource<bool> NewCompletionSource()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static NetworkDeviceEntity CreateNetworkDevice(
        int id,
        string name,
        string ipAddress = "192.168.0.10",
        int port1 = 102)
    {
        var entity = NetworkDeviceEntity.Create(name, DeviceType.PLC, ipAddress, port1);
        entity.WithId(id);
        entity.UpdateDeviceModel("S7");
        entity.UpdateProtocolFrame(null);
        entity.UpdateEndpoint(ipAddress, port1, null, 3000);
        entity.Enable();
        return entity;
    }

    private static NetworkDeviceDto CreateNetworkDto(
        int id,
        string name,
        string ipAddress = "192.168.0.10",
        int port1 = 102)
        => new(
            id,
            name,
            DeviceType.PLC,
            "S7",
            ipAddress,
            port1,
            null,
            null,
            null,
            3000,
            true,
            null);

    private static IoMappingDto CreateIoMappingDto(
        int id,
        int deviceId,
        string signalKey,
        string plcAddress = "DB1.DBW0",
        string? remark = null)
        => new(
            id,
            deviceId,
            signalKey,
            plcAddress,
            1,
            "Int16",
            "Read",
            "单点读数据",
            string.Empty,
            1,
            remark);

    private static SerialDeviceEntity CreateSerialDevice(int id, string name)
    {
        var entity = SerialDeviceEntity.Create(name, "Scanner", "COM1", 9600);
        entity.WithId(id);
        entity.UpdatePort("COM1", 9600, 8, "One", "None");
        entity.Enable();
        return entity;
    }

    private static IoMappingEntity CreateIoMapping(
        int id,
        int deviceId,
        string signalKey,
        string plcAddress = "DB1.DBW0",
        string? remark = null)
    {
        var entity = IoMappingEntity.Create(deviceId, signalKey, plcAddress, 1, "Int16", "Read");
        entity.WithId(id);
        entity.UpdateSortOrder(1);
        entity.UpdateMetadata(signalKey, "Int16", "Read", "单点读数据", string.Empty, remark);
        return entity;
    }

    private sealed class InMemoryRepository<T>(params T[] seedItems) : IRepository<T>
        where T : class, IEntity<int>, IAggregateRoot
    {
        private readonly List<T> _items = [.. seedItems];
        private int _nextId = seedItems.Length == 0 ? 1 : seedItems.Max(x => x.Id) + 1;

        public IReadOnlyList<T> Items => _items;

        public IQueryable<T> GetQueryable() => _items.AsQueryable();

        public Task<T?> GetByIdAsync<TKey>(TKey id, CancellationToken cancellationToken = default)
            where TKey : notnull
            => Task.FromResult(_items.FirstOrDefault(x => EqualityComparer<TKey>.Default.Equals((TKey)(object)x.Id, id)));

        public Task<T?> GetAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().FirstOrDefault(expression));

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Where(expression).ToList());

        public Task<List<T>> GetListAsync(
            Expression<Func<T, bool>> expression,
            Expression<Func<T, object>>[]? includes = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Where(expression).ToList());

        public Task<List<T>> GetListAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<T?> GetSingleOrDefaultAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> GetCountAsync(
            Expression<Func<T, bool>> expression,
            CancellationToken cancellationToken = default)
            => Task.FromResult(_items.AsQueryable().Count(expression));

        public Task<int> CountAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> AnyAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public T Add(T entity)
        {
            if (entity.Id == 0)
            {
                EntityIdTestHelper.SetId(entity, _nextId++);
            }

            _items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
            var index = _items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0)
            {
                _items[index] = entity;
            }
        }

        public void Delete(T entity)
        {
            var index = _items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0)
            {
                _items.RemoveAt(index);
            }
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> ExecuteDeleteAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            var toDelete = _items.AsQueryable().Where(predicate).ToList();
            foreach (var item in toDelete)
            {
                _items.Remove(item);
            }

            return Task.FromResult(toDelete.Count);
        }

        public async Task<int> ReplaceAsync(
            Expression<Func<T, bool>> predicate,
            IReadOnlyCollection<T> replacements,
            CancellationToken cancellationToken = default)
        {
            var affected = await ExecuteDeleteAsync(predicate, cancellationToken);
            foreach (var replacement in replacements)
            {
                Add(replacement);
                affected++;
            }

            return affected;
        }
    }

    private sealed class HardwareConfigSender : ISender
    {
        public List<NetworkDeviceEntity> ExistingNetworkDevices { get; init; } = [];

        public List<IoMappingEntity> ExistingIoMappings { get; init; } = [];

        public HashSet<int> FailedNetworkReadCalls { get; } = [];

        public HashSet<int> FailedIoReadCalls { get; } = [];

        public List<object> Requests { get; } = [];

        private int _networkReadCount;
        private int _ioReadCount;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            object response = request switch
            {
                GetAllNetworkDevicesQuery => ReadNetworkDevices(),
                GetIoMappingsByDeviceQuery query => ReadIoMappings(query),
                SaveNetworkDevicesCommand => Result.Success(),
                SaveSerialDevicesCommand => Result.Success(),
                SaveIoMappingsCommand => Result.Success(),
                _ => throw new NotSupportedException(request.GetType().FullName)
            };

            return Task.FromResult((TResponse)response);
        }

        private Result<List<NetworkDeviceEntity>> ReadNetworkDevices()
        {
            _networkReadCount++;
            if (FailedNetworkReadCalls.Contains(_networkReadCount))
            {
                return Result.Failure($"network read {_networkReadCount} failed");
            }

            return Result.Success(ExistingNetworkDevices.Select(Clone).ToList());
        }

        private Result<IoMappingPagedDto> ReadIoMappings(
            GetIoMappingsByDeviceQuery query)
        {
            _ioReadCount++;
            if (FailedIoReadCalls.Contains(_ioReadCount))
            {
                return Result.Failure($"io read {_ioReadCount} failed");
            }

            return Result.Success(new IoMappingPagedDto(
                ExistingIoMappings
                    .Where(x => x.NetworkDeviceId == query.NetworkDeviceId)
                    .Select(Clone)
                    .ToList(),
                ExistingIoMappings.Count(x => x.NetworkDeviceId == query.NetworkDeviceId)));
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

        private static NetworkDeviceEntity Clone(NetworkDeviceEntity entity)
        {
            var clone = NetworkDeviceEntity.Create(
                entity.DeviceName,
                entity.DeviceType,
                entity.IpAddress,
                entity.Port1,
                entity.PlcCode);
            clone.WithId(entity.Id);
            clone.UpdateDeviceModel(entity.DeviceModel);
            clone.UpdateProtocolFrame(entity.ProtocolFrame);
            clone.UpdateEndpoint(entity.IpAddress, entity.Port1, entity.Port2, entity.ConnectTimeout);
            clone.UpdateCommands(entity.SendCmd1, entity.SendCmd2);
            clone.SetEnabled(entity.IsEnabled);
            clone.UpdateRemark(entity.Remark);
            return clone;
        }

        private static IoMappingEntity Clone(IoMappingEntity entity)
        {
            var clone = IoMappingEntity.Create(
                entity.NetworkDeviceId,
                entity.SignalKey,
                entity.PlcAddress,
                entity.AddressCount,
                entity.DataType,
                entity.Direction,
                entity.Category,
                entity.BusinessGroup);
            clone.WithId(entity.Id);
            clone.UpdateSortOrder(entity.SortOrder);
            clone.UpdateMetadata(
                entity.SignalKey,
                entity.DataType,
                entity.Direction,
                entity.Category,
                entity.BusinessGroup,
                entity.Remark);
            return clone;
        }
    }

    private sealed class FakePlcConnectionManager : IPlcConnectionManager
    {
        public Dictionary<int, Exception> StopFailures { get; } = [];

        public Dictionary<string, Exception> ReloadFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<int> StoppedDeviceIds { get; } = [];

        public List<int> ReloadedDeviceIds { get; } = [];

        public List<string> ReloadedDeviceNames { get; } = [];

        public Func<int, CancellationToken, Task>? StopBehavior { get; init; }

        public Func<string, CancellationToken, Task>? ReloadBehavior { get; init; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default)
        {
            StoppedDeviceIds.Add(networkDeviceId);
            if (StopFailures.TryGetValue(networkDeviceId, out var exception))
            {
                throw exception;
            }

            if (StopBehavior is not null)
            {
                await StopBehavior(networkDeviceId, ct);
            }
        }

        public async Task ReloadAsync(string deviceName, CancellationToken ct = default)
            => await RecordRuntimeApplyAsync(0, deviceName, ct);

        public async Task RecordRuntimeApplyAsync(
            int networkDeviceId,
            string deviceName,
            CancellationToken ct = default)
        {
            if (networkDeviceId > 0)
            {
                ReloadedDeviceIds.Add(networkDeviceId);
            }

            ReloadedDeviceNames.Add(deviceName);
            if (ReloadFailures.TryGetValue(deviceName, out var exception))
            {
                throw exception;
            }

            if (ReloadBehavior is not null)
            {
                await ReloadBehavior(deviceName, ct);
            }
        }

        public void RegisterTasks(
            string deviceName,
            Func<IPlcBuffer, ProductionContext, List<IIoT.Edge.Module.Contracts.Plc.IPlcTask>> factory)
        {
        }

        public IIoT.Edge.Module.Contracts.Plc.IPlcService? GetPlc(int networkDeviceId) => null;

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

    private sealed class StubPermissionService : IClientPermissionService
    {
        public bool CanEditParams { get; init; }

        public bool CanEditHardware { get; init; }

        public bool IsLocalAdmin { get; init; }

        public event Action? PermissionStateChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) => CanEditHardware;
    }

    private sealed class FakePlcRuntimeApplyService(
        HardwareConfigSender sender,
        FakePlcConnectionManager plcManager) : IPlcRuntimeApplyService
    {
        public Task ApplyDeviceRuntimeAsync(
            int networkDeviceId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            var deviceName = ResolveSubmittedDeviceName(networkDeviceId)
                             ?? sender.ExistingNetworkDevices.FirstOrDefault(x => x.Id == networkDeviceId)?.DeviceName
                             ?? $"DeviceId={networkDeviceId}";
            return plcManager.RecordRuntimeApplyAsync(
                networkDeviceId,
                deviceName,
                cancellationToken);
        }

        public Task ApplyDeviceRuntimeAsync(
            string deviceName,
            string reason,
            CancellationToken cancellationToken = default)
            => Task.FromException(
                new NotSupportedException(
                    "测试禁止按 DeviceName 应用 PLC 运行配置。"));

        private string? ResolveSubmittedDeviceName(int networkDeviceId)
            => sender.Requests
                .OfType<SaveNetworkDevicesCommand>()
                .LastOrDefault()
                ?.Devices
                .FirstOrDefault(x => x.Id == networkDeviceId)
                ?.DeviceName;
    }
}
