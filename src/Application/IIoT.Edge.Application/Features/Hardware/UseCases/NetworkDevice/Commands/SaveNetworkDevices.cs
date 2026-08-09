using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Plugins;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Result;

namespace IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;

public record NetworkDeviceDto(
    int Id,
    string DeviceName,
    DeviceType DeviceType,
    string? DeviceModel,
    string IpAddress,
    int Port1,
    int? Port2,
    string? SendCmd1,
    string? SendCmd2,
    int ConnectTimeout,
    bool IsEnabled,
    string? Remark,
    string? ProtocolFrame = null,
    string PlcCode = "");

public record SaveNetworkDevicesCommand(List<NetworkDeviceDto> Devices) : ICommand<Result>;

/// <summary>正式 v3 网络设备保存端口；Host 不再取得插件 DbContext、Entity 或 UoW。</summary>
public sealed class SaveNetworkDevicesHandler(
    IDevicePluginConfigurationSnapshotAccessor snapshots,
    IEnumerable<IDevicePluginConfigurationStoreV1> stores)
    : ICommandHandler<SaveNetworkDevicesCommand, Result>
{
    private readonly IDevicePluginConfigurationStoreV1[] _stores = stores.ToArray();

    public async Task<Result> Handle(
        SaveNetworkDevicesCommand request,
        CancellationToken cancellationToken)
    {
        if (_stores.Length != 1)
        {
            return Result.Failure("PLUGIN_DATABASE_PORT_CARDINALITY_INVALID");
        }

        var configurations = new List<DevicePluginPlcConfiguration>(request.Devices.Count);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Devices)
        {
            if (item.DeviceType != DeviceType.PLC
                || string.IsNullOrWhiteSpace(item.PlcCode)
                || string.IsNullOrWhiteSpace(item.DeviceName)
                || string.IsNullOrWhiteSpace(item.IpAddress)
                || item.Port1 is < 1 or > 65535
                || item.Port2 is < 1 or > 65535
                || item.ConnectTimeout <= 0
                || !codes.Add(item.PlcCode.Trim()))
            {
                return Result.Failure("PLUGIN_PLC_CONFIGURATION_INVALID");
            }

            configurations.Add(new DevicePluginPlcConfiguration(
                item.PlcCode.Trim().ToUpperInvariant(),
                item.DeviceName.Trim(),
                item.DeviceType.ToString(),
                Normalize(item.DeviceModel),
                Normalize(item.ProtocolFrame)?.ToUpperInvariant(),
                item.IpAddress.Trim(),
                item.Port1,
                item.Port2,
                item.ConnectTimeout,
                item.IsEnabled,
                Normalize(item.Remark)));
        }

        var snapshot = snapshots.GetRequiredSnapshot();
        var expectedVersion = snapshot.ConfigurationVersion;
        var incomingByCode = configurations.ToDictionary(
            static item => item.PlcCode,
            StringComparer.OrdinalIgnoreCase);
        foreach (var existing in snapshot.Plcs.Where(item => !incomingByCode.ContainsKey(item.PlcCode)))
        {
            var result = await _stores[0]
                .DeletePlcAsync(existing.PlcCode, expectedVersion, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return Result.Failure(result.FailureReasonCode ?? "PLUGIN_PLC_DELETE_REJECTED");
            }

            expectedVersion = result.ConfigurationVersion;
        }

        foreach (var configuration in configurations)
        {
            var existing = snapshot.Plcs.SingleOrDefault(item => string.Equals(
                item.PlcCode,
                configuration.PlcCode,
                StringComparison.OrdinalIgnoreCase));
            if (existing == configuration)
            {
                continue;
            }

            var result = await _stores[0]
                .UpsertPlcAsync(configuration, expectedVersion, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return Result.Failure(result.FailureReasonCode ?? "PLUGIN_PLC_UPSERT_REJECTED");
            }

            expectedVersion = result.ConfigurationVersion;
        }

        await snapshots.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
