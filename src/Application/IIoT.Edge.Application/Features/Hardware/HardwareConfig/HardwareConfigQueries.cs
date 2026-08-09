using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Features.Hardware.UseCases.IoMapping.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.NetworkDevice.Commands;
using IIoT.Edge.Application.Features.Hardware.UseCases.SerialDevice.Commands;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Auth;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plugins;
using IIoT.Edge.Module.Sdk.Hardware;
using MediatR;

namespace IIoT.Edge.Application.Features.Hardware.HardwareConfigView;

public record HardwareConfigInitResult(
    List<NetworkDeviceDto> NetworkDevices,
    List<SerialDeviceDto> SerialDevices);

public record IoMappingPageResult(
    List<IoMappingDto> Items,
    int TotalCount);

public record ModuleTemplateInfoResult(
    bool IsAvailable,
    IReadOnlyList<ModuleIoTemplateEntry> DefaultSignals,
    IReadOnlyList<ModuleIoTemplateEntry> CandidateSignals,
    string Message);

public record LoadHardwareConfigQuery : IRequest<HardwareConfigInitResult>;

public record LoadIoMappingsQuery(int NetworkDeviceId)
    : IRequest<IoMappingPageResult>;

public record SaveHardwareConfigCommand(
    List<NetworkDeviceDto> NetworkDevices,
    List<SerialDeviceDto> SerialDevices,
    int SelectedNetworkDeviceId,
    List<IoMappingDto> IoMappings) : IRequest<CrudOperationResult>;

public sealed class LoadHardwareConfigHandler(
    IDevicePluginConfigurationSnapshotAccessor snapshots)
    : IRequestHandler<LoadHardwareConfigQuery, HardwareConfigInitResult>
{
    public Task<HardwareConfigInitResult> Handle(
        LoadHardwareConfigQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var networks = snapshots.GetPlcs()
            .Select(static item => new NetworkDeviceDto(
                item.Id,
                item.DeviceName,
                item.DeviceType,
                item.DeviceModel,
                item.IpAddress,
                item.Port1,
                item.Port2,
                item.SendCmd1,
                item.SendCmd2,
                item.ConnectTimeout,
                item.IsEnabled,
                item.Remark,
                item.ProtocolFrame,
                item.PlcCode))
            .ToList();
        return Task.FromResult(new HardwareConfigInitResult(networks, []));
    }
}

public sealed class LoadIoMappingsHandler(
    IDevicePluginConfigurationSnapshotAccessor snapshots)
    : IRequestHandler<LoadIoMappingsQuery, IoMappingPageResult>
{
    public Task<IoMappingPageResult> Handle(
        LoadIoMappingsQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = snapshots.GetIoPoints()
            .Where(item => item.NetworkDeviceId == request.NetworkDeviceId)
            .OrderBy(static item => item.SortOrder)
            .Select(static item => new IoMappingDto(
                item.Id,
                item.NetworkDeviceId,
                item.SignalKey,
                item.PlcAddress,
                item.AddressCount,
                item.DataType,
                item.Direction,
                item.Category,
                item.BusinessGroup,
                item.SortOrder,
                item.Remark))
            .ToList();
        return Task.FromResult(new IoMappingPageResult(items, items.Count));
    }
}

public sealed class SaveHardwareConfigHandler(
    IDevicePluginConfigurationSnapshotAccessor snapshots,
    IEnumerable<IDevicePluginConfigurationStoreV1> stores,
    IClientPermissionService permissionService,
    IPlcConnectionManager plcConnectionManager,
    IPlcRuntimeApplyService plcRuntimeApplyService,
    IPlcRuntimeConfigurationMutationGate runtimeConfigurationMutationGate,
    IPlcConfigurationSnapshotInvalidator? plcConfigurationSnapshotInvalidator = null)
    : IRequestHandler<SaveHardwareConfigCommand, CrudOperationResult>
{
    private readonly IDevicePluginConfigurationStoreV1[] _stores = stores.ToArray();

    public async Task<CrudOperationResult> Handle(
        SaveHardwareConfigCommand request,
        CancellationToken cancellationToken)
    {
        if (!permissionService.CanEditHardware)
        {
            return CrudOperationResult.Failure("当前用户没有硬件配置权限。");
        }

        if (_stores.Length != 1)
        {
            return CrudOperationResult.Failure("PLUGIN_DATABASE_PORT_CARDINALITY_INVALID");
        }

        if (request.SerialDevices.Count > 0)
        {
            return CrudOperationResult.Failure("PLUGIN_SERIAL_DEVICE_NOT_SUPPORTED");
        }

        var validationError = ValidateSubmission(request);
        if (validationError is not null)
        {
            return CrudOperationResult.Failure(validationError);
        }

        var initial = snapshots.GetRequiredSnapshot();
        var existingPlcs = snapshots.GetPlcs();
        var existingIoPoints = snapshots.GetIoPoints();
        var affectedIds = FindAffectedPlcDeviceIds(existingPlcs, existingIoPoints, request);
        using var mutationScope = await EnterMutationGatesAsync(affectedIds, cancellationToken)
            .ConfigureAwait(false);

        if (snapshots.GetRequiredSnapshot().ConfigurationVersion != initial.ConfigurationVersion)
        {
            return CrudOperationResult.Failure("PLUGIN_CONFIGURATION_VERSION_CONFLICT");
        }

        var expectedVersion = initial.ConfigurationVersion;
        var existingByCode = existingPlcs.ToDictionary(
            static item => item.PlcCode,
            StringComparer.OrdinalIgnoreCase);
        var submitted = request.NetworkDevices
            .Select(ToPluginConfiguration)
            .ToArray();
        var submittedByCode = submitted.ToDictionary(
            static item => item.PlcCode,
            StringComparer.OrdinalIgnoreCase);

        foreach (var removed in existingByCode.Values
                     .Where(item => !submittedByCode.ContainsKey(item.PlcCode))
                     .OrderBy(static item => item.PlcCode, StringComparer.OrdinalIgnoreCase))
        {
            var result = await _stores[0]
                .DeletePlcAsync(removed.PlcCode, expectedVersion, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return WriteFailure(result, "PLUGIN_PLC_DELETE_REJECTED");
            }

            expectedVersion = result.ConfigurationVersion;
        }

        foreach (var configuration in submitted.OrderBy(
                     static item => item.PlcCode,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (existingByCode.TryGetValue(configuration.PlcCode, out var existing)
                && existing.Configuration == configuration)
            {
                continue;
            }

            var result = await _stores[0]
                .UpsertPlcAsync(configuration, expectedVersion, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return WriteFailure(result, "PLUGIN_PLC_UPSERT_REJECTED");
            }

            expectedVersion = result.ConfigurationVersion;
        }

        var selected = request.NetworkDevices.SingleOrDefault(
            item => item.Id == request.SelectedNetworkDeviceId);
        if (selected is not null)
        {
            var plcCode = NormalizeRequired(selected.PlcCode);
            var existingSelected = existingIoPoints
                .Where(item => string.Equals(item.PlcCode, plcCode, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(static item => item.SignalKey, StringComparer.OrdinalIgnoreCase);
            var incomingSelected = request.IoMappings
                .Select(item => ToPluginConfiguration(plcCode, item))
                .ToDictionary(static item => item.SignalKey, StringComparer.OrdinalIgnoreCase);

            foreach (var removed in existingSelected.Values
                         .Where(item => !incomingSelected.ContainsKey(item.SignalKey))
                         .OrderBy(static item => item.SignalKey, StringComparer.OrdinalIgnoreCase))
            {
                var result = await _stores[0]
                    .DeleteIoPointAsync(plcCode, removed.SignalKey, expectedVersion, cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return WriteFailure(result, "PLUGIN_IO_DELETE_REJECTED");
                }

                expectedVersion = result.ConfigurationVersion;
            }

            foreach (var configuration in incomingSelected.Values.OrderBy(
                         static item => item.SortOrder))
            {
                if (existingSelected.TryGetValue(configuration.SignalKey, out var existing)
                    && existing.Configuration == configuration)
                {
                    continue;
                }

                var result = await _stores[0]
                    .UpsertIoPointAsync(configuration, expectedVersion, cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    return WriteFailure(result, "PLUGIN_IO_UPSERT_REJECTED");
                }

                expectedVersion = result.ConfigurationVersion;
            }
        }

        await snapshots.RefreshAsync(cancellationToken).ConfigureAwait(false);
        plcConfigurationSnapshotInvalidator?.Invalidate();

        var runtimeIssues = await ApplyRuntimeChangesAsync(
            existingPlcs,
            request,
            affectedIds,
            cancellationToken).ConfigureAwait(false);
        if (plcConfigurationSnapshotInvalidator is not null)
        {
            try
            {
                await plcConfigurationSnapshotInvalidator.WarmAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                runtimeIssues.Add($"PLC 权威配置缓存重建失败（{exception.Message}）");
            }
        }

        return runtimeIssues.Count == 0
            ? CrudOperationResult.Success("硬件配置已保存。")
            : CrudOperationResult.Failure($"配置已保存，但 {string.Join("；", runtimeIssues)}");
    }

    private async Task<List<string>> ApplyRuntimeChangesAsync(
        IReadOnlyCollection<DevicePluginPlcSnapshot> existingPlcs,
        SaveHardwareConfigCommand request,
        IReadOnlyCollection<int> affectedIds,
        CancellationToken cancellationToken)
    {
        var issues = new List<string>();
        var submittedIds = request.NetworkDevices
            .Select(static item => item.Id > 0
                ? item.Id
                : DevicePluginProjectionIds.Plc(item.PlcCode))
            .ToHashSet();
        foreach (var removed in existingPlcs.Where(item => !submittedIds.Contains(item.Id)))
        {
            try
            {
                await plcConnectionManager.StopDeviceAsync(removed.Id, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                issues.Add($"{removed.DeviceName} 停机失败（{exception.Message}）");
            }
        }

        var currentById = snapshots.GetPlcs().ToDictionary(static item => item.Id);
        foreach (var deviceId in affectedIds.Where(currentById.ContainsKey).OrderBy(static item => item))
        {
            try
            {
                await plcRuntimeApplyService.ApplyDeviceRuntimeAsync(
                    deviceId,
                    PlcRuntimeApplyReasons.HardwareOrIoMappingSave,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                issues.Add($"{currentById[deviceId].DeviceName} 重载失败（{exception.Message}）");
            }
        }

        return issues;
    }

    private async ValueTask<IDisposable> EnterMutationGatesAsync(
        IReadOnlyCollection<int> networkDeviceIds,
        CancellationToken cancellationToken)
    {
        var leases = new List<IDisposable>(networkDeviceIds.Count);
        try
        {
            foreach (var networkDeviceId in networkDeviceIds.OrderBy(static item => item))
            {
                leases.Add(await runtimeConfigurationMutationGate
                    .EnterAsync(networkDeviceId, cancellationToken)
                    .ConfigureAwait(false));
            }

            return new CompositeMutationLease(leases);
        }
        catch
        {
            DisposeLeasesInReverseOrder(leases);
            throw;
        }
    }

    private static int[] FindAffectedPlcDeviceIds(
        IReadOnlyCollection<DevicePluginPlcSnapshot> existingPlcs,
        IReadOnlyCollection<DevicePluginIoPointSnapshot> existingIoPoints,
        SaveHardwareConfigCommand request)
    {
        var affected = new HashSet<int>();
        var existingById = existingPlcs.ToDictionary(static item => item.Id);
        var submittedById = request.NetworkDevices.ToDictionary(
            static item => item.Id > 0 ? item.Id : DevicePluginProjectionIds.Plc(item.PlcCode));
        foreach (var existing in existingPlcs)
        {
            if (!submittedById.TryGetValue(existing.Id, out var incoming)
                || existing.Configuration != ToPluginConfiguration(incoming))
            {
                affected.Add(existing.Id);
            }
        }

        foreach (var incoming in submittedById)
        {
            if (!existingById.ContainsKey(incoming.Key))
            {
                affected.Add(incoming.Key);
            }
        }

        if (request.SelectedNetworkDeviceId > 0
            && submittedById.TryGetValue(request.SelectedNetworkDeviceId, out var selected))
        {
            var plcCode = NormalizeRequired(selected.PlcCode);
            var existing = existingIoPoints
                .Where(item => string.Equals(item.PlcCode, plcCode, StringComparison.OrdinalIgnoreCase))
                .Select(static item => item.Configuration)
                .OrderBy(static item => item.SignalKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var incoming = request.IoMappings
                .Select(item => ToPluginConfiguration(plcCode, item))
                .OrderBy(static item => item.SignalKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (!existing.SequenceEqual(incoming))
            {
                affected.Add(request.SelectedNetworkDeviceId);
            }
        }

        return affected.OrderBy(static item => item).ToArray();
    }

    private static string? ValidateSubmission(SaveHardwareConfigCommand request)
    {
        if (request.NetworkDevices.Any(static item => item.DeviceType != DeviceType.PLC))
        {
            return "PLUGIN_NETWORK_DEVICE_TYPE_NOT_SUPPORTED";
        }

        var plcCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.NetworkDevices)
        {
            if (string.IsNullOrWhiteSpace(item.PlcCode)
                || string.IsNullOrWhiteSpace(item.DeviceName)
                || string.IsNullOrWhiteSpace(item.IpAddress)
                || item.Port1 is < 1 or > 65535
                || item.Port2 is < 1 or > 65535
                || item.ConnectTimeout <= 0
                || !plcCodes.Add(item.PlcCode.Trim()))
            {
                return "PLUGIN_PLC_CONFIGURATION_INVALID";
            }

            if (string.Equals(item.DeviceModel?.Trim(), PlcType.Mc.ToString(), StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.ProtocolFrame)
                && !string.Equals(item.ProtocolFrame.Trim(), nameof(McPlcFrameType.E3), StringComparison.OrdinalIgnoreCase)
                && !string.Equals(item.ProtocolFrame.Trim(), nameof(McPlcFrameType.E4), StringComparison.OrdinalIgnoreCase))
            {
                return "PLUGIN_PLC_PROTOCOL_FRAME_INVALID";
            }
        }

        var signalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.IoMappings)
        {
            var typeWordLength = PlcIoTypeWordLengthValidator.Validate(item.DataType, item.AddressCount);
            if (string.IsNullOrWhiteSpace(item.SignalKey)
                || string.IsNullOrWhiteSpace(item.PlcAddress)
                || string.IsNullOrWhiteSpace(item.DataType)
                || string.IsNullOrWhiteSpace(item.Direction)
                || item.SortOrder < 0
                || !typeWordLength.IsValid
                || !signalKeys.Add(item.SignalKey.Trim()))
            {
                return typeWordLength.IsValid
                    ? "PLUGIN_IO_CONFIGURATION_INVALID"
                    : typeWordLength.FailureCode;
            }
        }

        return null;
    }

    private static DevicePluginPlcConfiguration ToPluginConfiguration(NetworkDeviceDto item)
        => new(
            NormalizeRequired(item.PlcCode),
            item.DeviceName.Trim(),
            item.DeviceType.ToString(),
            NormalizeOptional(item.DeviceModel),
            NormalizeOptional(item.ProtocolFrame)?.ToUpperInvariant(),
            item.IpAddress.Trim(),
            item.Port1,
            item.Port2,
            item.ConnectTimeout,
            item.IsEnabled,
            NormalizeOptional(item.Remark));

    private static DevicePluginIoPointConfiguration ToPluginConfiguration(
        string plcCode,
        IoMappingDto item)
        => new(
            plcCode,
            item.SignalKey.Trim(),
            item.PlcAddress.Trim(),
            item.AddressCount,
            item.DataType.Trim(),
            item.Direction.Trim(),
            string.IsNullOrWhiteSpace(item.Category) ? "单点读数据" : item.Category.Trim(),
            item.BusinessGroup?.Trim() ?? string.Empty,
            item.SortOrder,
            NormalizeOptional(item.Remark));

    private static CrudOperationResult WriteFailure(
        DevicePluginConfigurationWriteResult result,
        string fallback)
        => CrudOperationResult.Failure(result.FailureReasonCode ?? fallback);

    private static string NormalizeRequired(string value) => value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void DisposeLeasesInReverseOrder(IReadOnlyList<IDisposable> leases)
    {
        for (var index = leases.Count - 1; index >= 0; index--)
        {
            leases[index].Dispose();
        }
    }

    private sealed class CompositeMutationLease(IReadOnlyList<IDisposable> leases) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                DisposeLeasesInReverseOrder(leases);
            }
        }
    }
}
