using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Stacking.Config.Hardware;
using IIoT.Edge.Module.Stacking.Runtime;
using IIoT.Edge.SharedKernel.Enums;
using MediatR;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class StackingHardwareProfileBehaviorTests
{
    [Fact]
    public void StackingHardwareProfileProvider_ShouldExposeStableDefaultTemplate()
    {
        var provider = new StackingHardwareProfileProvider(new StackingPlcSignalProfile());

        var defaults = provider.GetDefaultPlcSettings();
        var template = provider.GetDefaultIoTemplate();

        Assert.Equal("S7", defaults.DeviceModel);
        Assert.Equal(3000, defaults.ConnectTimeout);
        Assert.Equal(4, template.Count);
        Assert.Equal(
            ["Stacking.Sequence", "Stacking.LayerCount", "Stacking.ResultCode", "Stacking.Ack"],
            template.OrderBy(x => x.SortOrder).Select(x => x.SignalKey).ToArray());
        Assert.Equal(
            ["DB1.DBW0", "DB1.DBW2", "DB1.DBW4", "DB1.DBW6"],
            template.OrderBy(x => x.SortOrder).Select(x => x.PlcAddress).ToArray());
    }

    [Fact]
    public void StackingHardwareProfileProvider_ShouldRejectOutOfSequenceMappings()
    {
        var provider = new StackingHardwareProfileProvider(new StackingPlcSignalProfile());
        var mappings = CreateValidSnapshots(provider)
            .Select(static mapping => mapping.SignalKey switch
            {
                "Stacking.Sequence" => mapping with { SortOrder = 2 },
                "Stacking.LayerCount" => mapping with { SortOrder = 1 },
                _ => mapping
            })
            .ToArray();

        var validation = provider.ValidatePlcConfiguration("Stacking-PLC", "S7", mappings);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public async Task HardwareConfigCrudService_WhenApplyingTemplateTwice_ShouldOnlyFillMissingMappings()
    {
        var provider = new StackingHardwareProfileProvider(new StackingPlcSignalProfile());
        var sender = new FakeSender(
        [
            new FakeIoMappingEntity(9, "Stacking.Sequence", "DB1.DBW0", 1, "Int16", "Read", 1)
        ]);
        var service = new HardwareConfigCrudService(
            sender,
            [provider],
            new StubPermissionService { CanEditHardware = true });
        var device = new NetworkDeviceVm
        {
            Id = 9,
            DeviceName = "PLC-STACKING-DEV",
            DeviceType = DeviceType.PLC,
            ModuleId = "Stacking"
        };

        var firstApply = await service.ApplyModuleTemplateAsync(device);
        var secondApply = await service.ApplyModuleTemplateAsync(device);

        Assert.True(firstApply.IsSuccess, firstApply.Message);
        Assert.True(secondApply.IsSuccess, secondApply.Message);
        Assert.Single(sender.SaveCommands);
        Assert.Equal(4, sender.CurrentMappings.Count);
        Assert.Contains(sender.CurrentMappings, x => x.SignalKey == "Stacking.Ack" && x.Direction == "Write");
        Assert.Equal("默认点位已存在，无需补充映射。", secondApply.Message);
    }

    [Fact]
    public async Task HardwareConfigCrudService_GetModuleTemplateInfoAsync_ShouldExposeStandardSignalsForDialog()
    {
        var provider = new StackingHardwareProfileProvider(new StackingPlcSignalProfile());
        var service = new HardwareConfigCrudService(
            new FakeSender([]),
            [provider],
            new StubPermissionService { CanEditHardware = true });
        var device = new NetworkDeviceVm
        {
            Id = 9,
            DeviceName = "PLC-STACKING-DEV",
            DeviceType = DeviceType.PLC,
            ModuleId = "Stacking"
        };

        var info = await service.GetModuleTemplateInfoAsync(device);

        Assert.True(info.IsAvailable);
        Assert.Equal("Stacking", info.ModuleId);
        Assert.Equal("只补齐当前 PLC 缺失的插件默认点位，不覆盖已维护地址。", info.Message);
        Assert.Contains(info.DefaultSignals, x =>
            x.SignalKey == "Stacking.Ack"
            && x.Direction == "Write"
            && x.SignalName == "采集应答");
    }

    [Fact]
    public async Task HardwareConfigCrudService_WhenApplyingTemplate_ShouldOnlyAffectSelectedPlc()
    {
        var provider = new StackingHardwareProfileProvider(new StackingPlcSignalProfile());
        var sender = new FakeSender(
        [
            new FakeIoMappingEntity(10, "Stacking.Sequence", "DB10.DBW0", 1, "Int16", "Read", 1),
            new FakeIoMappingEntity(11, "Stacking.Sequence", "DB11.DBW0", 1, "Int16", "Read", 1)
        ]);
        var service = new HardwareConfigCrudService(
            sender,
            [provider],
            new StubPermissionService { CanEditHardware = true });
        var selectedPlc = new NetworkDeviceVm
        {
            Id = 10,
            DeviceName = "PLC-STACKING-A",
            DeviceType = DeviceType.PLC,
            ModuleId = "Stacking"
        };

        var result = await service.ApplyModuleTemplateAsync(selectedPlc);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Single(sender.SaveCommands);
        Assert.Equal(10, sender.SaveCommands.Single().NetworkDeviceId);
        Assert.Contains(sender.CurrentMappings, x => x.NetworkDeviceId == 10 && x.SignalKey == "Stacking.Ack" && x.Direction == "Write");
        Assert.Contains(sender.CurrentMappings, x => x.NetworkDeviceId == 11 && x.SignalKey == "Stacking.Sequence" && x.PlcAddress == "DB11.DBW0");
        Assert.DoesNotContain(sender.CurrentMappings, x => x.NetworkDeviceId == 11 && x.SignalKey == "Stacking.Ack");
    }

    private static ModuleIoSnapshot[] CreateValidSnapshots(StackingHardwareProfileProvider provider)
        => provider.GetDefaultIoTemplate()
            .Select(static template => new ModuleIoSnapshot(
                template.SignalKey,
                template.PlcAddress,
                template.AddressCount,
                template.DataType,
                template.Direction,
                template.SortOrder,
                template.Category,
                template.BusinessGroup,
                template.SignalName))
            .ToArray();

    private sealed class FakeSender(List<FakeIoMappingEntity> mappings) : ISender
    {
        public List<FakeIoMappingEntity> CurrentMappings { get; } = mappings;

        public List<SaveIoMappingsCommand> SaveCommands { get; } = [];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            return request switch
            {
                GetIoMappingsByDeviceQuery query => Task.FromResult((TResponse)(object)IIoT.Edge.SharedKernel.Result.Result.Success(
                    new IoMappingPagedDto(
                        CurrentMappings
                            .Where(x => x.NetworkDeviceId == query.NetworkDeviceId)
                            .Select(x => x.ToEntity())
                            .ToList(),
                        CurrentMappings.Count(x => x.NetworkDeviceId == query.NetworkDeviceId)))),
                SaveIoMappingsCommand command => HandleSave(command).ContinueWith(_ => (TResponse)(object)IIoT.Edge.SharedKernel.Result.Result.Success()),
                _ => throw new NotSupportedException(request.GetType().FullName)
            };
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

        private Task HandleSave(SaveIoMappingsCommand command)
        {
            SaveCommands.Add(command);
            CurrentMappings.RemoveAll(x => x.NetworkDeviceId == command.NetworkDeviceId);
            CurrentMappings.AddRange(command.Mappings.Select(x => new FakeIoMappingEntity(
                x.NetworkDeviceId,
                x.SignalKey,
                x.PlcAddress,
                x.AddressCount,
                x.DataType,
                x.Direction,
                x.SortOrder,
                x.Remark)));
            return Task.CompletedTask;
        }
    }

    private sealed record FakeIoMappingEntity(
        int NetworkDeviceId,
        string SignalKey,
        string PlcAddress,
        int AddressCount,
        string DataType,
        string Direction,
        int SortOrder,
        string? Remark = null)
    {
        public IIoT.Edge.Domain.Hardware.Aggregates.IoMappingEntity ToEntity()
        {
            var entity = IIoT.Edge.Domain.Hardware.Aggregates.IoMappingEntity.Create(
                NetworkDeviceId,
                SignalKey,
                PlcAddress,
                AddressCount,
                DataType,
                Direction);
            entity.UpdateSortOrder(SortOrder);
            entity.UpdateMetadata(SignalKey, DataType, Direction, "单点读数据", string.Empty, string.Empty, Remark);
            return entity;
        }
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

        public bool HasPermission(string permission)
            => permission switch
            {
                _ when IsLocalAdmin => true,
                var value when string.Equals(value, Permissions.HardwareConfig, StringComparison.OrdinalIgnoreCase) => CanEditHardware,
                var value when string.Equals(value, Permissions.ParamConfig, StringComparison.OrdinalIgnoreCase) => CanEditParams,
                _ => false
            };
    }
}
