using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Infrastructure.DeviceComm.Plc;
using IIoT.Edge.Module.Sdk.Signals;
using IIoT.Edge.SharedKernel.Repository;

namespace IIoT.Edge.Shell.Core;

public interface IPlcRuntimeTaskBinder
{
    Task BindAsync(CancellationToken cancellationToken = default);

    Task<PlcRuntimeTaskApplyResult> BindDeviceAsync(
        int networkDeviceId,
        bool applyToRunningDevice,
        CancellationToken cancellationToken = default);
}

public sealed class PlcRuntimeTaskBinder : IPlcRuntimeTaskBinder
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IReadRepository<IoMappingEntity> _ioMappings;
    private readonly IStationRuntimeRegistry _runtimeRegistry;
    private readonly IPlcTaskBindingService _taskBindingService;
    private readonly IProductionContextSignalBindingStore _signalBindingStore;
    private readonly PlcRuntimeTaskController _taskController;
    private readonly ILogService _logger;

    public PlcRuntimeTaskBinder(
        IServiceProvider serviceProvider,
        IReadRepository<NetworkDeviceEntity> networkDevices,
        IReadRepository<IoMappingEntity> ioMappings,
        IStationRuntimeRegistry runtimeRegistry,
        IPlcTaskBindingService taskBindingService,
        IProductionContextSignalBindingStore signalBindingStore,
        PlcRuntimeTaskController taskController,
        ILogService logger)
    {
        _serviceProvider = serviceProvider;
        _networkDevices = networkDevices;
        _ioMappings = ioMappings;
        _runtimeRegistry = runtimeRegistry;
        _taskBindingService = taskBindingService;
        _signalBindingStore = signalBindingStore;
        _taskController = taskController;
        _logger = logger;
    }

    public async Task BindAsync(CancellationToken cancellationToken = default)
    {
        var plcDevices = await _networkDevices.GetListAsync(
            x => x.IsEnabled && x.DeviceType == DeviceType.PLC,
            cancellationToken).ConfigureAwait(false);

        foreach (var device in plcDevices)
        {
            if (string.IsNullOrWhiteSpace(device.DeviceName))
            {
                continue;
            }

            var plan = await BuildTaskPlanAsync(device, cancellationToken).ConfigureAwait(false);
            await _taskController
                .RegisterPlanAsync(plan, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<PlcRuntimeTaskApplyResult> BindDeviceAsync(
        int networkDeviceId,
        bool applyToRunningDevice,
        CancellationToken cancellationToken = default)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException("网络设备 Id 必须大于 0。", nameof(networkDeviceId));
        }

        var device = await _networkDevices
            .GetByIdAsync(networkDeviceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("未找到要绑定运行任务的 PLC 设备。");
        if (device.DeviceType != DeviceType.PLC)
        {
            throw new InvalidOperationException($"设备“{device.DeviceName}”不是 PLC。");
        }

        var plan = await BuildTaskPlanAsync(device, cancellationToken).ConfigureAwait(false);
        if (applyToRunningDevice)
        {
            return await _taskController
                .RegisterAndApplyAsync(plan, cancellationToken)
                .ConfigureAwait(false);
        }

        await _taskController
            .RegisterPlanAsync(plan, cancellationToken)
            .ConfigureAwait(false);
        return new PlcRuntimeTaskApplyResult(
            PlcRuntimeTaskApplyState.WaitingForRuntime,
            plan.TaskKeys);
    }

    private async Task<PlcRuntimeTaskPlan> BuildTaskPlanAsync(
        NetworkDeviceEntity device,
        CancellationToken cancellationToken)
    {
        var factory = ResolveActiveRuntimeFactory();
        if (factory is null)
        {
            var factoryCount = _runtimeRegistry.GetRegistrations().Count;
            _logger.Warn(
                $"[PlcCode={device.PlcCode}][TaskKey=未解析][SignalKey=不适用] "
                + $"PLC 业务任务绑定跳过：运行时任务工厂数量={factoryCount}，"
                + "未唯一确定；连接任务仍可独立运行。");
            return PlcRuntimeTaskPlan.Empty(device.Id, device.PlcCode, device.DeviceName);
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
        var candidates = factory.GetTaskCandidates().ToArray();
        var enabledTaskKeys = await _taskBindingService.GetConfiguredEnabledTaskKeysAsync(
            device.Id,
            candidates,
            cancellationToken).ConfigureAwait(false);
        var taskEntries = new List<KeyValuePair<string, PlcRuntimeTaskPlanEntry>>();

        foreach (var taskKey in enabledTaskKeys.OrderBy(
                     static key => key,
                     StringComparer.OrdinalIgnoreCase))
        {
            var matches = candidates
                .Where(candidate => string.Equals(
                    candidate.Key,
                    taskKey,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1)
            {
                _logger.Error(
                    $"[PlcCode={device.PlcCode}][TaskKey={taskKey}][SignalKey=未解析] "
                    + $"业务任务候选数量为 {matches.Length}，已仅隔离该 TaskKey。");
                continue;
            }

            var candidate = matches[0];
            var oneTaskSet = new HashSet<string>(
                [taskKey],
                StringComparer.OrdinalIgnoreCase);
            var validation = _taskBindingService.ValidateEnabledTasks(
                matches,
                oneTaskSet,
                signalBindings,
                device.DeviceModel);
            if (!validation.IsValid)
            {
                _logger.Error(BuildValidationFailureMessage(device.PlcCode, validation));
                continue;
            }

            var capturedTaskKey = taskKey;
            taskEntries.Add(
                new KeyValuePair<string, PlcRuntimeTaskPlanEntry>(
                    capturedTaskKey,
                    new PlcRuntimeTaskPlanEntry(
                        factory.ModuleId,
                        (buffer, context) =>
                        {
                            _signalBindingStore.Set(context, signalBindings);
                            return PlcRuntimeSingleTaskFactory.CreateRequired(
                                capturedTaskKey,
                                enabledKeys => factory.CreateTasks(
                                    _serviceProvider,
                                    buffer,
                                    context,
                                    enabledKeys));
                        },
                        CandidateRequiresPeriodicRead(candidate, signalBindings))));
        }

        return new PlcRuntimeTaskPlan(
            device.Id,
            device.PlcCode,
            device.DeviceName,
            taskEntries);
    }

    private IStationRuntimeFactory? ResolveActiveRuntimeFactory()
    {
        var factories = _runtimeRegistry.GetRegistrations().Values.ToArray();
        return factories.Length == 1 ? factories[0] : null;
    }

    internal static bool CandidateRequiresPeriodicRead(
        TaskCandidate candidate,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(signalBindings);

        var periodicReadSignalKeys = signalBindings
            .Where(static binding =>
                string.Equals(
                    binding.Direction,
                    IoMappingOptionCatalog.DirectionRead,
                    StringComparison.OrdinalIgnoreCase)
                && IoMappingOptionCatalog.IsReadDataCategory(binding.Category))
            .Select(static binding => binding.SignalKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return candidate.RequiredSignals.Any(required =>
            string.Equals(
                required.Direction,
                IoMappingOptionCatalog.DirectionRead,
                StringComparison.OrdinalIgnoreCase)
            && periodicReadSignalKeys.Contains(required.SignalKey));
    }

    private static string BuildValidationFailureMessage(
        string plcCode,
        PlcTaskBindingValidationResult validation)
    {
        var missing = validation.Issues
            .Select(issue =>
                $"[PlcCode={plcCode}]"
                + $"[TaskKey={issue.TaskKey}]"
                + $"[SignalKey={issue.RequiredSignal?.SignalKey ?? "不适用"}] "
                + issue.Message)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return $"PLC 任务绑定校验失败，相关 TaskKey 已单独暂停：{string.Join("；", missing)}。";
    }
}
