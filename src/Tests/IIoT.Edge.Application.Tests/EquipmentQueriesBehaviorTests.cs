using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Production.Equipment;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Result;
using MediatR;

namespace IIoT.Edge.Application.Tests;

public sealed class EquipmentQueriesBehaviorTests
{
    [Fact]
    public async Task GetHardwareStatusHandler_WhenRuntimeIsOffline_ShouldReportDisconnected()
    {
        var device = CreateDevice(1, "PLC-A");
        var handler = new GetHardwareStatusHandler(
            new HardwareSender(device),
            new FakePlcConnectionManager(
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = device.Id,
                    DeviceName = device.DeviceName,
                    IsConnected = false
                }));

        var result = await handler.Handle(new GetHardwareStatusQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.False(result[0].IsConnected);
    }

    [Fact]
    public async Task GetHardwareStatusHandler_WhenRuntimeIsConnected_ShouldReportConnected()
    {
        var device = CreateDevice(2, "PLC-B");
        var handler = new GetHardwareStatusHandler(
            new HardwareSender(device),
            new FakePlcConnectionManager(
                new PlcConnectionRuntimeSnapshot
                {
                    NetworkDeviceId = device.Id,
                    DeviceName = device.DeviceName,
                    IsConnected = true
                }));

        var result = await handler.Handle(new GetHardwareStatusQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.True(result[0].IsConnected);
    }

    private static NetworkDeviceEntity CreateDevice(int id, string deviceName)
        => NetworkDeviceEntity.Create(deviceName, DeviceType.PLC, "127.0.0.1", 102)
            .WithId(id);

    private sealed class HardwareSender(NetworkDeviceEntity device) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetAllNetworkDevicesQuery)
            {
                return Task.FromResult((TResponse)(object)Result.Success(new List<NetworkDeviceEntity> { device }));
            }

            throw new NotSupportedException(request.GetType().Name);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException(request?.GetType().Name);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException(request.GetType().Name);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakePlcConnectionManager(PlcConnectionRuntimeSnapshot? snapshot) : IPlcConnectionManager
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReloadAsync(string deviceName, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default) => Task.CompletedTask;

        public void RegisterTasks(string deviceName, Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
        {
        }

        public IPlcService? GetPlc(int networkDeviceId) => null;

        public ProductionContext? GetContext(string deviceName) => null;

        public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
        {
        }

        public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId)
            => snapshot?.NetworkDeviceId == networkDeviceId ? snapshot : null;

        public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses()
            => snapshot is null ? Array.Empty<PlcConnectionRuntimeSnapshot>() : [snapshot];

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
