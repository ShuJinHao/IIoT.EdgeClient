using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Factory;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Signals;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.SharedKernel.Repository;
using System.Globalization;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcDeviceRuntimeBuilder
{
    private readonly IReadRepository<IoMappingEntity> _ioMappings;
    private readonly IPlcDataStore _dataStore;
    private readonly IPlcServiceFactory _plcServiceFactory;
    private readonly IProductionContextStore _contextStore;
    private readonly ILogService _logger;
    private readonly PlcConnectionStatusStore _statusStore;
    private readonly IPlcSignalBlockPlanner _signalBlockPlanner;
    private readonly IPlcEndpointResolver _endpointResolver;
    private readonly ModuleHardwareProfileResolver _hardwareProfileResolver;
    private readonly IModuleParamRoleProvider? _moduleParamRoleProvider;

    public PlcDeviceRuntimeBuilder(
        IReadRepository<IoMappingEntity> ioMappings,
        IPlcDataStore dataStore,
        IPlcServiceFactory plcServiceFactory,
        IProductionContextStore contextStore,
        ILogService logger,
        PlcConnectionStatusStore statusStore,
        IPlcSignalBlockPlanner signalBlockPlanner,
        IPlcEndpointResolver endpointResolver,
        ModuleHardwareProfileResolver hardwareProfileResolver,
        IModuleParamRoleProvider? moduleParamRoleProvider = null)
    {
        _ioMappings = ioMappings;
        _dataStore = dataStore;
        _plcServiceFactory = plcServiceFactory;
        _contextStore = contextStore;
        _logger = logger;
        _statusStore = statusStore;
        _signalBlockPlanner = signalBlockPlanner;
        _endpointResolver = endpointResolver;
        _hardwareProfileResolver = hardwareProfileResolver;
        _moduleParamRoleProvider = moduleParamRoleProvider;
    }

    public async Task<PlcDeviceRuntimeHandle> BuildAsync(
        NetworkDeviceEntity device,
        Func<IPlcBuffer, ProductionContext, List<IPlcTask>>? taskFactory,
        CancellationToken ct)
    {
        var mappings = await _ioMappings.GetListAsync(x => x.NetworkDeviceId == device.Id, ct).ConfigureAwait(false);
        var mappingArray = mappings
            .Where(static x => !string.IsNullOrWhiteSpace(x.PlcAddress))
            .OrderBy(x => x.SortOrder)
            .ToArray();
        var readCount = mappingArray.Where(x => x.Direction == "Read").Sum(x => x.AddressCount);
        var writeCount = mappingArray.Where(x => x.Direction == "Write").Sum(x => x.AddressCount);
        var signalBindings = BuildSignalBindings(mappingArray);

        _dataStore.Register(device.Id, readCount, writeCount, signalBindings);
        var buffer = _dataStore.GetBuffer(device.Id);
        var hardwareProfile = _hardwareProfileResolver.Resolve();
        var context = _contextStore.GetOrCreate(device.DeviceName, hardwareProfile?.ModuleId ?? string.Empty);
        context.NetworkDeviceId = device.Id;

        if (!Enum.TryParse<PlcType>(device.DeviceModel, ignoreCase: true, out var plcType))
        {
            throw new InvalidOperationException(
                $"[{device.DeviceName}] Initialization skipped because DeviceModel is invalid: {device.DeviceModel ?? "<empty>"}.");
        }

        _statusStore.EnsureTracked(device.Id, device.DeviceName);
        var plcService = _plcServiceFactory.Create(plcType, device.DeviceName);
        var endpoint = await _endpointResolver.ResolveAsync(device, plcType, ct).ConfigureAwait(false);
        var deviceCts = new CancellationTokenSource();
        var runtimePolicy = hardwareProfile?.GetIoRuntimePolicy() ?? PlcIoRuntimePolicy.Default;

        var ioScanTask = new PlcIoScanTask(
            plcService,
            _dataStore,
            device,
            mappingArray,
            _logger,
            _signalBlockPlanner,
            _statusStore,
            runtimePolicy,
            endpoint);
        var dataReadScanTask = new PlcDataReadScanTask(
            plcService,
            _dataStore,
            device,
            mappingArray,
            _logger,
            _signalBlockPlanner,
            _statusStore,
            runtimePolicy,
            token => ResolveDataReadLoopIntervalAsync(hardwareProfile?.ModuleId, runtimePolicy, token));

        var tasks = new List<IPlcTask> { ioScanTask, dataReadScanTask };
        if (buffer is not null && taskFactory is not null)
        {
            tasks.AddRange(taskFactory(buffer, context));
        }

        return new PlcDeviceRuntimeHandle
        {
            DeviceId = device.Id,
            DeviceName = device.DeviceName,
            PlcService = plcService,
            CancellationTokenSource = deviceCts,
            Tasks = tasks
        };
    }

    private async Task<int> ResolveDataReadLoopIntervalAsync(
        string? moduleId,
        PlcIoRuntimePolicy runtimePolicy,
        CancellationToken cancellationToken)
    {
        var policyValue = runtimePolicy.NormalizeDataReadLoopInterval();
        if (string.IsNullOrWhiteSpace(moduleId) || _moduleParamRoleProvider is null)
        {
            return policyValue;
        }

        var configuredValue = await _moduleParamRoleProvider
            .GetStringAsync(
                moduleId,
                ModuleParamCategory.Business,
                ModuleParamRole.DataReadLoopIntervalMs,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return int.TryParse(configuredValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval)
               && interval > 0
            ? interval
            : policyValue;
    }

    private static IReadOnlyCollection<PlcBufferSignalBinding> BuildSignalBindings(
        IReadOnlyCollection<IoMappingEntity> mappings)
    {
        var bindings = new List<PlcBufferSignalBinding>(mappings.Count);
        var readOffset = 0;
        var writeOffset = 0;

        foreach (var mapping in mappings.Where(static x => x.Direction == "Read").OrderBy(static x => x.SortOrder))
        {
            bindings.Add(new PlcBufferSignalBinding(
                mapping.SignalKey,
                mapping.Direction,
                readOffset,
                Math.Max(1, mapping.AddressCount)));
            readOffset += Math.Max(1, mapping.AddressCount);
        }

        foreach (var mapping in mappings.Where(static x => x.Direction == "Write").OrderBy(static x => x.SortOrder))
        {
            bindings.Add(new PlcBufferSignalBinding(
                mapping.SignalKey,
                mapping.Direction,
                writeOffset,
                Math.Max(1, mapping.AddressCount)));
            writeOffset += Math.Max(1, mapping.AddressCount);
        }

        return bindings;
    }
}
