using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Sdk.Signals;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Shell.Core;

public interface IPlcRuntimeTaskBinder
{
    Task BindAsync(CancellationToken cancellationToken = default);
}

public sealed class PlcRuntimeTaskBinder : IPlcRuntimeTaskBinder
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IReadRepository<IoMappingEntity> _ioMappings;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly IStationRuntimeRegistry _runtimeRegistry;
    private readonly IPlcTaskBindingService _taskBindingService;
    private readonly IProductionContextSignalBindingStore _signalBindingStore;
    private readonly ILogService _logger;

    public PlcRuntimeTaskBinder(
        IServiceProvider serviceProvider,
        IReadRepository<NetworkDeviceEntity> networkDevices,
        IReadRepository<IoMappingEntity> ioMappings,
        IPlcConnectionManager plcConnectionManager,
        IStationRuntimeRegistry runtimeRegistry,
        IPlcTaskBindingService taskBindingService,
        IProductionContextSignalBindingStore signalBindingStore,
        ILogService logger)
    {
        _serviceProvider = serviceProvider;
        _networkDevices = networkDevices;
        _ioMappings = ioMappings;
        _plcConnectionManager = plcConnectionManager;
        _runtimeRegistry = runtimeRegistry;
        _taskBindingService = taskBindingService;
        _signalBindingStore = signalBindingStore;
        _logger = logger;
    }

    public async Task BindAsync(CancellationToken cancellationToken = default)
    {
        var factory = ResolveActiveRuntimeFactory();
        if (factory is null)
        {
            _logger.Warn("PLC 运行任务绑定跳过：当前插件库未唯一确定运行时任务工厂。");
            return;
        }

        var plcDevices = await _networkDevices.GetListAsync(
            x => x.IsEnabled && x.DeviceType == DeviceType.PLC,
            cancellationToken).ConfigureAwait(false);

        foreach (var device in plcDevices)
        {
            if (string.IsNullOrWhiteSpace(device.DeviceName))
            {
                continue;
            }

            var mappings = await _ioMappings.GetListAsync(
                x => x.NetworkDeviceId == device.Id,
                cancellationToken).ConfigureAwait(false);
            var signalBindings = mappings
                .Select(static mapping => new ModuleIoSnapshot(
                    mapping.SignalKey,
                    mapping.PlcAddress,
                    mapping.AddressCount,
                    mapping.DataType,
                    mapping.Direction,
                    mapping.SortOrder,
                    mapping.Category,
                    mapping.BusinessGroup))
                .ToArray();
            var candidates = factory.GetTaskCandidates();
            var enabledTaskKeys = await _taskBindingService.GetEnabledTaskKeysAsync(
                device.Id,
                candidates,
                signalBindings,
                device.DeviceModel,
                cancellationToken).ConfigureAwait(false);
            var validation = _taskBindingService.ValidateEnabledTasks(candidates, enabledTaskKeys, signalBindings, device.DeviceModel);
            if (!validation.IsValid)
            {
                var message = BuildValidationFailureMessage(device.DeviceName, validation);
                _plcConnectionManager.MarkRuntimeFault(device.Id, device.DeviceName, message);
                _logger.Error(message);
                continue;
            }

            _plcConnectionManager.RegisterTasks(
                device.DeviceName,
                (buffer, context) =>
                {
                    _signalBindingStore.Set(context, signalBindings);
                    return factory.CreateTasks(_serviceProvider, buffer, context, enabledTaskKeys);
                });
        }
    }

    private IStationRuntimeFactory? ResolveActiveRuntimeFactory()
    {
        var factories = _runtimeRegistry.GetRegistrations().Values.ToArray();
        return factories.Length == 1 ? factories[0] : null;
    }

    private static string BuildValidationFailureMessage(
        string deviceName,
        PlcTaskBindingValidationResult validation)
    {
        var missing = validation.Issues
            .Select(static issue => $"{issue.TaskDisplayName}({issue.TaskKey}) {issue.Message}")
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return $"PLC“{deviceName}”任务绑定校验失败：{string.Join("；", missing)}。";
    }
}
