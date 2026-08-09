using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Module.Contracts.Plugins;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Result;

namespace IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;

public record IoMappingDto(
    int Id,
    int NetworkDeviceId,
    string SignalKey,
    string PlcAddress,
    int AddressCount,
    string DataType,
    string Direction,
    string Category,
    string BusinessGroup,
    int SortOrder,
    string? Remark);

public record SaveIoMappingsCommand(
    int NetworkDeviceId,
    List<IoMappingDto> Mappings) : ICommand<Result>;

/// <summary>正式 v3 IO 保存端口；一次写入只经当前插件拥有的版本化配置 Store。</summary>
public sealed class SaveIoMappingsHandler(
    IDevicePluginConfigurationSnapshotAccessor snapshots,
    IEnumerable<IDevicePluginConfigurationStoreV1> stores)
    : ICommandHandler<SaveIoMappingsCommand, Result>
{
    private readonly IDevicePluginConfigurationStoreV1[] _stores = stores.ToArray();

    public async Task<Result> Handle(
        SaveIoMappingsCommand request,
        CancellationToken cancellationToken)
    {
        if (_stores.Length != 1)
        {
            return Result.Failure("PLUGIN_DATABASE_PORT_CARDINALITY_INVALID");
        }

        var plc = snapshots.GetPlcs().SingleOrDefault(item => item.Id == request.NetworkDeviceId);
        if (plc is null)
        {
            return Result.Failure("PLUGIN_PLC_NOT_FOUND");
        }

        var configurations = new List<DevicePluginIoPointConfiguration>(request.Mappings.Count);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Mappings)
        {
            var typeWordLength = PlcIoTypeWordLengthValidator.Validate(item.DataType, item.AddressCount);
            if (!typeWordLength.IsValid
                || string.IsNullOrWhiteSpace(item.SignalKey)
                || string.IsNullOrWhiteSpace(item.PlcAddress)
                || string.IsNullOrWhiteSpace(item.DataType)
                || string.IsNullOrWhiteSpace(item.Direction)
                || item.SortOrder < 0
                || !keys.Add(item.SignalKey.Trim()))
            {
                return Result.Failure(
                    typeWordLength.IsValid
                        ? "PLUGIN_IO_CONFIGURATION_INVALID"
                        : typeWordLength.FailureCode);
            }

            configurations.Add(new DevicePluginIoPointConfiguration(
                plc.PlcCode,
                item.SignalKey.Trim(),
                item.PlcAddress.Trim(),
                item.AddressCount,
                item.DataType.Trim(),
                item.Direction.Trim(),
                string.IsNullOrWhiteSpace(item.Category) ? "单点读数据" : item.Category.Trim(),
                item.BusinessGroup?.Trim() ?? string.Empty,
                item.SortOrder,
                string.IsNullOrWhiteSpace(item.Remark) ? null : item.Remark.Trim()));
        }

        var snapshot = snapshots.GetRequiredSnapshot();
        var expectedVersion = snapshot.ConfigurationVersion;
        var existing = snapshot.IoPoints
            .Where(item => string.Equals(item.PlcCode, plc.PlcCode, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(static item => item.SignalKey, StringComparer.OrdinalIgnoreCase);
        var incoming = configurations.ToDictionary(
            static item => item.SignalKey,
            StringComparer.OrdinalIgnoreCase);
        foreach (var removed in existing.Values.Where(item => !incoming.ContainsKey(item.SignalKey)))
        {
            var result = await _stores[0]
                .DeleteIoPointAsync(plc.PlcCode, removed.SignalKey, expectedVersion, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return Result.Failure(result.FailureReasonCode ?? "PLUGIN_IO_DELETE_REJECTED");
            }

            expectedVersion = result.ConfigurationVersion;
        }

        foreach (var configuration in configurations)
        {
            if (existing.TryGetValue(configuration.SignalKey, out var current)
                && current == configuration)
            {
                continue;
            }

            var result = await _stores[0]
                .UpsertIoPointAsync(configuration, expectedVersion, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return Result.Failure(result.FailureReasonCode ?? "PLUGIN_IO_UPSERT_REJECTED");
            }

            expectedVersion = result.ConfigurationVersion;
        }

        await snapshots.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
