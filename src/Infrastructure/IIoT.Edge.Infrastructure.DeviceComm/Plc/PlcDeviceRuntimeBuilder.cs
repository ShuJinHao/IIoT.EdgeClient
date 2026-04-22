using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Factory;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Signals;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcDeviceRuntimeBuilder
{
    private readonly IRepository<IoMappingEntity> _ioMappings;
    private readonly IPlcDataStore _dataStore;
    private readonly IPlcServiceFactory _plcServiceFactory;
    private readonly IProductionContextStore _contextStore;
    private readonly ILogService _logger;
    private readonly PlcConnectionStatusStore _statusStore;

    public PlcDeviceRuntimeBuilder(
        IRepository<IoMappingEntity> ioMappings,
        IPlcDataStore dataStore,
        IPlcServiceFactory plcServiceFactory,
        IProductionContextStore contextStore,
        ILogService logger,
        PlcConnectionStatusStore statusStore)
    {
        _ioMappings = ioMappings;
        _dataStore = dataStore;
        _plcServiceFactory = plcServiceFactory;
        _contextStore = contextStore;
        _logger = logger;
        _statusStore = statusStore;
    }

    public async Task<PlcDeviceRuntimeHandle> BuildAsync(
        NetworkDeviceEntity device,
        Func<IPlcBuffer, ProductionContext, List<IPlcTask>>? taskFactory,
        CancellationToken ct)
    {
        var mappings = await _ioMappings.GetListAsync(x => x.NetworkDeviceId == device.Id, ct).ConfigureAwait(false);
        var mappingArray = mappings.OrderBy(x => x.SortOrder).ToArray();
        var readCount = mappingArray.Where(x => x.Direction == "Read").Sum(x => x.AddressCount);
        var writeCount = mappingArray.Where(x => x.Direction == "Write").Sum(x => x.AddressCount);

        _dataStore.Register(device.Id, readCount, writeCount);
        var buffer = _dataStore.GetBuffer(device.Id);
        var context = _contextStore.GetOrCreate(device.DeviceName);
        context.DeviceId = device.Id;

        if (!Enum.TryParse<PlcType>(device.DeviceModel, ignoreCase: true, out var plcType))
        {
            throw new InvalidOperationException(
                $"[{device.DeviceName}] Initialization skipped because DeviceModel is invalid: {device.DeviceModel ?? "<empty>"}.");
        }

        _statusStore.EnsureTracked(device.Id, device.DeviceName);
        var plcService = _plcServiceFactory.Create(plcType, device.DeviceName);
        var deviceCts = new CancellationTokenSource();

        var signalInteraction = new SignalInteraction(
            plcService,
            _dataStore,
            device,
            mappingArray,
            _logger,
            _statusStore);
        await signalInteraction.ConnectAsync().ConfigureAwait(false);

        var tasks = new List<IPlcTask> { signalInteraction };
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
}
