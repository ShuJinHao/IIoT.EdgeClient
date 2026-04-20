using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView;
using IIoT.Edge.Application.Features.Hardware.HardwareConfigView.Models;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Module.Stacking.Runtime;
using IIoT.Edge.SharedKernel.Enums;
using MediatR;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class StackingHardwareProfileBehaviorTests
{
    [Fact]
    public void StackingHardwareProfileProvider_ShouldExposeStableDefaultTemplate()
    {
        var provider = new StackingHardwareProfileProvider();

        var defaults = provider.GetDefaultPlcSettings();
        var template = provider.GetDefaultIoTemplate();

        Assert.Equal("S7", defaults.DeviceModel);
        Assert.Equal(3000, defaults.ConnectTimeout);
        Assert.Equal(4, template.Count);
        Assert.Equal(
            ["Stacking.Sequence", "Stacking.LayerCount", "Stacking.ResultCode", "Stacking.Ack"],
            template.OrderBy(x => x.SortOrder).Select(x => x.Label).ToArray());
        Assert.Equal(
            ["DB1.DBW0", "DB1.DBW2", "DB1.DBW4", "DB1.DBW6"],
            template.OrderBy(x => x.SortOrder).Select(x => x.PlcAddress).ToArray());
    }

    [Fact]
    public async Task HardwareConfigCrudService_WhenApplyingTemplateTwice_ShouldOnlyFillMissingMappings()
    {
        var provider = new StackingHardwareProfileProvider();
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
        Assert.Contains(sender.CurrentMappings, x => x.Label == "Stacking.Ack" && x.Direction == "Write");
        Assert.Equal("模块模板已存在，无需补充映射。", secondApply.Message);
    }

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
            CurrentMappings.Clear();
            CurrentMappings.AddRange(command.Mappings.Select(x => new FakeIoMappingEntity(
                x.NetworkDeviceId,
                x.Label,
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
        string Label,
        string PlcAddress,
        int AddressCount,
        string DataType,
        string Direction,
        int SortOrder,
        string? Remark = null)
    {
        public IIoT.Edge.Domain.Hardware.Aggregates.IoMappingEntity ToEntity()
            => new(NetworkDeviceId, Label, PlcAddress, AddressCount, DataType, Direction)
            {
                SortOrder = SortOrder,
                Remark = Remark
            };
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
