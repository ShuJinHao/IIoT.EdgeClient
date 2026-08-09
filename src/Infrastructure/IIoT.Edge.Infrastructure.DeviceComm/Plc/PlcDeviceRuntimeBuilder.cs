using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Plc.Factory;
using IIoT.Edge.Module.Contracts.Plc.Store;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Application.Common.Plugins;
using IIoT.Edge.Infrastructure.DeviceComm.Signals;
using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Identity;
using System.Globalization;
using IIoT.Edge.Application.Common.Plc;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcDeviceRuntimeBuilder
{
    private readonly IDevicePluginConfigurationSnapshotAccessor _snapshots;
    private readonly IPlcDataStore _dataStore;
    private readonly IPlcServiceFactory _plcServiceFactory;
    private readonly IPlcProductionContextStore _contextStore;
    private readonly ILogService _logger;
    private readonly PlcConnectionStatusStore _statusStore;
    private readonly IPlcSignalBlockPlanner _signalBlockPlanner;
    private readonly IPlcEndpointResolver _endpointResolver;
    private readonly ModuleHardwareProfileResolver _hardwareProfileResolver;
    private readonly IModuleParamRoleProvider? _moduleParamRoleProvider;
    private readonly IPlcTaskRuntimeStatusWriter? _taskStatusWriter;

    public PlcDeviceRuntimeBuilder(
        IDevicePluginConfigurationSnapshotAccessor snapshots,
        IPlcDataStore dataStore,
        IPlcServiceFactory plcServiceFactory,
        IPlcProductionContextStore contextStore,
        ILogService logger,
        PlcConnectionStatusStore statusStore,
        IPlcSignalBlockPlanner signalBlockPlanner,
        IPlcEndpointResolver endpointResolver,
        ModuleHardwareProfileResolver hardwareProfileResolver,
        IModuleParamRoleProvider? moduleParamRoleProvider = null,
        IPlcTaskRuntimeStatusWriter? taskStatusWriter = null)
    {
        _snapshots = snapshots;
        _dataStore = dataStore;
        _plcServiceFactory = plcServiceFactory;
        _contextStore = contextStore;
        _logger = logger;
        _statusStore = statusStore;
        _signalBlockPlanner = signalBlockPlanner;
        _endpointResolver = endpointResolver;
        _hardwareProfileResolver = hardwareProfileResolver;
        _moduleParamRoleProvider = moduleParamRoleProvider;
        _taskStatusWriter = taskStatusWriter;
    }

    public async Task<PlcDeviceRuntimeHandle> BuildAsync(
        DevicePluginPlcSnapshot device,
        PlcRuntimeTaskPlan taskPlan,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(taskPlan);
        if (taskPlan.NetworkDeviceId != device.Id
            || !string.Equals(taskPlan.PlcCode, device.PlcCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"任务计划 PLC“{taskPlan.PlcCode}”(NetworkDeviceId={taskPlan.NetworkDeviceId}, DeviceName={taskPlan.DeviceName})"
                + $"与待构建 PLC“{device.PlcCode}”(NetworkDeviceId={device.Id}, DeviceName={device.DeviceName}) 不一致。");
        }

        ct.ThrowIfCancellationRequested();
        var mappings = _snapshots.GetIoPoints()
            .Where(item => item.NetworkDeviceId == device.Id)
            .ToArray();
        var mappingArray = mappings
            .Where(static x => !string.IsNullOrWhiteSpace(x.PlcAddress))
            .OrderBy(x => x.SortOrder)
            .ToArray();
        var runtimeMappingArray = mappingArray
            .Where(mapping => IsValidRuntimeMapping(device.PlcCode, mapping))
            .ToArray();
        var readCount = runtimeMappingArray.Where(x => x.Direction == "Read").Sum(x => x.AddressCount);
        var writeCount = runtimeMappingArray.Where(x => x.Direction == "Write").Sum(x => x.AddressCount);
        var signalBindings = BuildSignalBindings(runtimeMappingArray);

        _dataStore.Register(device.Id, readCount, writeCount, signalBindings);
        var buffer = _dataStore.GetBuffer(device.Id);
        var hardwareProfile = _hardwareProfileResolver.Resolve();
        var identity = new PlcIdentity(device.PlcCode, device.Id, device.DeviceName);
        var contextResolution = _contextStore.GetOrCreate(
            identity,
            hardwareProfile?.ModuleId ?? string.Empty);
        var effectiveTaskPlan = taskPlan;
        ProductionContext context;
        if (contextResolution.IsSuccess)
        {
            context = contextResolution.Context!;
        }
        else
        {
            context = new ProductionContext
            {
                PlcCode = device.PlcCode,
                NetworkDeviceId = device.Id,
                DeviceName = device.DeviceName
            };
            effectiveTaskPlan = PlcRuntimeTaskPlan.Empty(
                device.Id,
                device.PlcCode,
                device.DeviceName,
                taskPlan.BusinessOnDemandReadSignalKeys,
                taskPlan.PeriodicReadExcludedSignalKeys);
            _logger.Error(
                $"[PlcCode={device.PlcCode}] 生产上下文稳定身份解析失败，已暂停该 PLC 全部业务 TaskKey，基础连接继续："
                + $"{contextResolution.DiagnosticCode}/{contextResolution.DiagnosticMessage}");
        }

        if (!Enum.TryParse<PlcType>(device.DeviceModel, ignoreCase: true, out var plcType))
        {
            throw new InvalidOperationException(
                $"[PlcCode={device.PlcCode}] Initialization skipped because DeviceModel is invalid: {device.DeviceModel ?? "<empty>"}.");
        }

        _statusStore.EnsureTracked(device.Id, device.PlcCode, device.DeviceName);
        var plcService = _plcServiceFactory.Create(plcType, device.PlcCode);
        var endpoint = await _endpointResolver.ResolveAsync(device, plcType, ct).ConfigureAwait(false);
        var deviceCts = new CancellationTokenSource();
        var runtimePolicy = hardwareProfile?.GetIoRuntimePolicy() ?? PlcIoRuntimePolicy.Default;
        var connectionSignal = new PlcRuntimeConnectionSignal();
        var scanIntervalResolver = new Func<CancellationToken, Task<int>>(
            token => ResolveDataReadLoopIntervalAsync(hardwareProfile?.ModuleId, runtimePolicy, token));

        if (buffer is null)
        {
            throw new InvalidOperationException(
                $"[PlcCode={device.PlcCode}] PLC Buffer 注册后仍不可用，拒绝创建 runtime。");
        }

        if (effectiveTaskPlan.BusinessOnDemandReadSignalKeys.Count > 0)
        {
            if (buffer is not PlcBuffer hostBuffer)
            {
                throw new InvalidOperationException(
                    $"[PlcCode={device.PlcCode}] PLC Buffer 不支持业务按需原子读取，拒绝启动相关 TaskKey。");
            }

            hostBuffer.SetOnDemandReadCoordinator(
                new PlcBusinessOnDemandReadCoordinator(
                    plcService,
                    hostBuffer,
                    runtimeMappingArray.Select(static mapping => new PlcIoScanMapping(
                        mapping.SignalKey,
                        mapping.PlcAddress,
                        mapping.AddressCount,
                        mapping.DataType,
                        mapping.Direction,
                        mapping.Category,
                        mapping.SortOrder)).ToArray(),
                    effectiveTaskPlan.BusinessOnDemandReadSignalKeys,
                    _logger,
                    _statusStore,
                    connectionSignal.Report,
                    _signalBlockPlanner,
                    runtimePolicy,
                    scanIntervalResolver,
                    deviceCts.Token,
                    device.Id,
                    device.PlcCode,
                    device.DeviceName));
        }

        var signalInteractionTask = new PlcSignalInteractionTask(
            plcService,
            _dataStore,
            device,
            runtimeMappingArray,
            _logger,
            _signalBlockPlanner,
            _statusStore,
            runtimePolicy,
            endpoint,
            connectionSignal.Report);
        var periodicBatchReadTask = new PlcPeriodicBatchReadTask(
            plcService,
            _dataStore,
            device,
            runtimeMappingArray,
            _logger,
            _signalBlockPlanner,
            _statusStore,
            runtimePolicy,
            scanIntervalResolver,
            connectionSignal.Report,
            effectiveTaskPlan.PeriodicReadExcludedSignalKeys);

        var runtime = new PlcDeviceRuntimeHandle
        {
            DeviceId = device.Id,
            PlcCode = device.PlcCode,
            DeviceName = device.DeviceName,
            PlcService = plcService,
            Buffer = buffer,
            Context = context,
            ConnectionTask = signalInteractionTask,
            PeriodicReadTask = periodicBatchReadTask,
            ConnectionSignal = connectionSignal,
            Logger = _logger,
            StatusStore = _statusStore,
            TaskStatusWriter = _taskStatusWriter,
            CancellationTokenSource = deviceCts,
        };
        await runtime.ApplyTaskPlanAsync(effectiveTaskPlan, ct).ConfigureAwait(false);
        if (!contextResolution.IsSuccess)
        {
            foreach (var taskKey in taskPlan.TaskKeys)
            {
                _taskStatusWriter?.SetState(
                    device.PlcCode,
                    taskKey,
                    PlcTaskRuntimeState.Faulted,
                    PlcTaskRuntimeErrorCodes.ConfigurationInvalid);
            }
        }

        return runtime;
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
        IReadOnlyCollection<DevicePluginIoPointSnapshot> mappings)
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

    private bool IsValidRuntimeMapping(string plcCode, DevicePluginIoPointSnapshot mapping)
    {
        var validation = PlcIoTypeWordLengthValidator.Validate(
            mapping.DataType,
            mapping.AddressCount);
        if (validation.IsValid)
        {
            return true;
        }

        _logger.Error(
            $"[PlcCode={plcCode}][TaskKey=未解析][SignalKey={mapping.SignalKey}] "
            + $"IO 数据类型与 word 长度无效，运行时已跳过该映射：{validation.FailureCode}。");
        return false;
    }
}
