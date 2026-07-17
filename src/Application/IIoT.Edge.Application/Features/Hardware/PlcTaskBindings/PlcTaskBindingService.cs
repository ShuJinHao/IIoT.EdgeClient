using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.Extensions.Configuration;

namespace IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;

public sealed class PlcTaskBindingService(
    IConfiguration configuration,
    IStationRuntimeRegistry runtimeRegistry,
    IReadRepository<NetworkDeviceEntity> networkDevices,
    IReadRepository<IoMappingEntity> ioMappings,
    IReadRepository<PlcTaskBindingEntity> bindings,
    IEdgeUnitOfWorkFactory unitOfWorkFactory,
    ILogService logger) : IPlcTaskBindingService
{
    private const string DefaultEnableAllTasksKey = "PlcTaskBinding:DefaultEnableAllTasks";

    public async Task<IReadOnlyList<PlcTaskBindingDeviceDto>> GetModuleDeviceBindingsAsync(
        string moduleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        if (!runtimeRegistry.TryGetFactory(moduleId, out var factory))
        {
            return [];
        }

        var candidates = factory.GetTaskCandidates();
        var devices = await networkDevices.GetListAsync(
            x => x.DeviceType == DeviceType.PLC,
            cancellationToken).ConfigureAwait(false);

        var results = new List<PlcTaskBindingDeviceDto>(devices.Count);
        foreach (var device in devices.OrderBy(static x => x.DeviceName, StringComparer.OrdinalIgnoreCase))
        {
            var rows = await bindings.GetListAsync(
                x => x.NetworkDeviceId == device.Id,
                cancellationToken).ConfigureAwait(false);
            var rowByKey = rows.ToDictionary(x => x.TaskKey, StringComparer.OrdinalIgnoreCase);
            var signalBindings = await LoadSignalBindingsAsync(device.Id, cancellationToken).ConfigureAwait(false);
            var taskItems = candidates
                .Select(candidate => CreateItem(candidate, rowByKey, signalBindings, device.DeviceModel))
                .ToArray();

            results.Add(new PlcTaskBindingDeviceDto(
                device.Id,
                device.DeviceName,
                moduleId,
                device.IsEnabled,
                taskItems));
        }

        return results;
    }

    public async Task<IReadOnlySet<string>> GetEnabledTaskKeysAsync(
        int networkDeviceId,
        IReadOnlyCollection<TaskCandidate> candidates,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
        string? deviceModel = null,
        CancellationToken cancellationToken = default)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException("网络设备 Id 必须大于 0。", nameof(networkDeviceId));
        }

        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(signalBindings);

        var rows = await bindings.GetListAsync(
            x => x.NetworkDeviceId == networkDeviceId,
            cancellationToken).ConfigureAwait(false);
        var rowByKey = rows.ToDictionary(x => x.TaskKey, StringComparer.OrdinalIgnoreCase);
        var enabledTaskKeys = candidates
            .Where(candidate => ResolveEnabled(candidate, rowByKey, signalBindings, deviceModel))
            .Select(static candidate => candidate.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return enabledTaskKeys;
    }

    public async Task SaveDeviceBindingsAsync(
        int networkDeviceId,
        string moduleId,
        IReadOnlyDictionary<string, bool> taskStates,
        CancellationToken cancellationToken = default)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException("网络设备 Id 必须大于 0。", nameof(networkDeviceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(taskStates);

        var device = await networkDevices.GetByIdAsync(networkDeviceId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("未找到要保存任务绑定的 PLC 设备。");

        if (!runtimeRegistry.TryGetFactory(moduleId, out var factory))
        {
            throw new InvalidOperationException("当前模块未注册 PLC 运行时任务工厂。");
        }

        var candidates = factory.GetTaskCandidates();
        var candidateByKey = candidates.ToDictionary(static x => x.Key, StringComparer.OrdinalIgnoreCase);
        var normalizedStates = taskStates
            .Where(x => candidateByKey.ContainsKey(x.Key))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var signalBindings = await LoadSignalBindingsAsync(networkDeviceId, cancellationToken).ConfigureAwait(false);
        var resolvedStates = candidates.ToDictionary(
            static candidate => candidate.Key,
            candidate => normalizedStates.TryGetValue(candidate.Key, out var submittedEnabled)
                ? submittedEnabled
                : EvaluateTaskAvailability(candidate, device.DeviceModel, signalBindings).CanRun && ResolveDefaultEnabled(candidate),
            StringComparer.OrdinalIgnoreCase);
        var invalidEnabledTasks = candidates
            .Select(candidate => new CandidateAvailability(
                candidate,
                EvaluateTaskAvailability(candidate, device.DeviceModel, signalBindings)))
            .Where(x => resolvedStates.TryGetValue(x.Candidate.Key, out var enabled)
                        && enabled
                        && !x.Availability.CanRun)
            .ToArray();
        if (invalidEnabledTasks.Length > 0)
        {
            throw new InvalidOperationException(BuildSaveValidationMessage(device.DeviceName, invalidEnabledTasks));
        }

        var disabledHeartbeatTasks = candidates
            .Where(x => x.IsHeartbeatLike
                        && resolvedStates.TryGetValue(x.Key, out var enabled)
                        && !enabled)
            .Select(static x => x.DisplayName)
            .ToArray();

        var updatedAt = DateTimeOffset.UtcNow;
        var replacements = candidates
            .Select(candidate => PlcTaskBindingEntity.Create(
                networkDeviceId,
                candidate.Key,
                resolvedStates[candidate.Key],
                updatedAt))
            .ToArray();
        await using (var unitOfWork = await unitOfWorkFactory
                         .BeginAsync(cancellationToken)
                         .ConfigureAwait(false))
        {
            var repository = unitOfWork.Repository<PlcTaskBindingEntity>();
            var existing = await repository
                .GetListAsync(x => x.NetworkDeviceId == networkDeviceId, cancellationToken)
                .ConfigureAwait(false);
            foreach (var binding in existing)
            {
                repository.Delete(binding);
            }

            foreach (var replacement in replacements)
            {
                repository.Add(replacement);
            }

            await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (disabledHeartbeatTasks.Length > 0)
        {
            logger.Warn($"PLC“{device.DeviceName}”已关闭心跳类任务：{string.Join("、", disabledHeartbeatTasks)}。");
        }
    }

    public PlcTaskBindingValidationResult ValidateEnabledTasks(
        IReadOnlyCollection<TaskCandidate> candidates,
        IReadOnlySet<string> enabledTaskKeys,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
        string? deviceModel = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(enabledTaskKeys);
        ArgumentNullException.ThrowIfNull(signalBindings);

        var mappedSignals = signalBindings
            .Select(static x => $"{x.SignalKey}\u001f{x.Direction}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issues = new List<PlcTaskBindingValidationIssue>();

        foreach (var candidate in candidates.Where(candidate => enabledTaskKeys.Contains(candidate.Key)))
        {
            if (!candidate.SupportsDeviceModel(deviceModel))
            {
                issues.Add(new PlcTaskBindingValidationIssue(
                    candidate.Key,
                    candidate.DisplayName,
                    RequiredSignal: null,
                    PlcTaskBindingValidationIssueType.UnsupportedDeviceModel,
                    $"任务“{candidate.DisplayName}”不支持当前 PLC 型号“{NormalizeDeviceModel(deviceModel)}”。"));
                continue;
            }

            foreach (var required in candidate.RequiredSignals)
            {
                var key = $"{required.SignalKey}\u001f{required.Direction}";
                if (!mappedSignals.Contains(key))
                {
                    issues.Add(new PlcTaskBindingValidationIssue(
                        candidate.Key,
                        candidate.DisplayName,
                        required,
                        PlcTaskBindingValidationIssueType.MissingRequiredSignal,
                        $"任务“{candidate.DisplayName}”缺少 IO 信号：{required.SignalKey}/{required.Direction}。"));
                }
            }
        }

        return issues.Count == 0
            ? PlcTaskBindingValidationResult.Success()
            : PlcTaskBindingValidationResult.Failure(issues);
    }

    private PlcTaskBindingItemDto CreateItem(
        TaskCandidate candidate,
        IReadOnlyDictionary<string, PlcTaskBindingEntity> rowByKey,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
        string? deviceModel)
    {
        var hasSavedBinding = rowByKey.ContainsKey(candidate.Key);
        var availability = EvaluateTaskAvailability(candidate, deviceModel, signalBindings);
        return new PlcTaskBindingItemDto(
            candidate.Key,
            candidate.DisplayName,
            ResolveEnabled(candidate, rowByKey, signalBindings, deviceModel),
            hasSavedBinding,
            candidate.IsHeartbeatLike,
            candidate.RequiredSignals,
            availability.CanRun,
            availability.UnavailableReason,
            availability.MissingRequiredSignals,
            availability.IsSupportedByCurrentPlc);
    }

    private bool ResolveEnabled(
        TaskCandidate candidate,
        IReadOnlyDictionary<string, PlcTaskBindingEntity> rowByKey,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
        string? deviceModel)
        => rowByKey.TryGetValue(candidate.Key, out var row)
            ? row.Enabled
            : ResolveDefaultEnabled(candidate) && EvaluateTaskAvailability(candidate, deviceModel, signalBindings).CanRun;

    private TaskAvailability EvaluateTaskAvailability(
        TaskCandidate candidate,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings)
    {
        var isSupported = candidate.SupportsDeviceModel(deviceModel);
        var missingSignals = FindMissingRequiredSignals(candidate, signalBindings);

        if (!isSupported)
        {
            return new TaskAvailability(
                CanRun: false,
                UnavailableReason: $"当前 PLC 型号“{NormalizeDeviceModel(deviceModel)}”不支持该任务。",
                MissingRequiredSignals: missingSignals,
                IsSupportedByCurrentPlc: false);
        }

        if (missingSignals.Count > 0)
        {
            var missingText = string.Join("、", missingSignals.Select(static signal => $"{signal.SignalKey}/{signal.Direction}"));
            return new TaskAvailability(
                CanRun: false,
                UnavailableReason: $"缺少 IO：{missingText}",
                MissingRequiredSignals: missingSignals,
                IsSupportedByCurrentPlc: true);
        }

        return new TaskAvailability(
            CanRun: true,
            UnavailableReason: string.Empty,
            MissingRequiredSignals: [],
            IsSupportedByCurrentPlc: true);
    }

    private static IReadOnlyList<TaskRequiredSignal> FindMissingRequiredSignals(
        TaskCandidate candidate,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings)
    {
        var mappedSignals = signalBindings
            .Select(static x => $"{x.SignalKey}\u001f{x.Direction}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return candidate.RequiredSignals
            .Where(required => !mappedSignals.Contains($"{required.SignalKey}\u001f{required.Direction}"))
            .ToArray();
    }

    private async Task<IReadOnlyCollection<ModuleIoSnapshot>> LoadSignalBindingsAsync(
        int networkDeviceId,
        CancellationToken cancellationToken)
    {
        var rows = await ioMappings.GetListAsync(
            x => x.NetworkDeviceId == networkDeviceId,
            cancellationToken).ConfigureAwait(false);

        return rows
            .Select(static row => new ModuleIoSnapshot(
                row.SignalKey,
                row.PlcAddress,
                row.AddressCount,
                row.DataType,
                row.Direction,
                row.SortOrder,
                row.Category,
                row.BusinessGroup))
            .ToArray();
    }

    private static string BuildSaveValidationMessage(
        string deviceName,
        IReadOnlyCollection<CandidateAvailability> invalidEnabledTasks)
    {
        var details = invalidEnabledTasks.Select(x => $"任务“{x.Candidate.DisplayName}”：{x.Availability.UnavailableReason}");

        return $"PLC“{deviceName}”存在不可运行的启用任务，保存失败。{string.Join("；", details)}";
    }

    private static string NormalizeDeviceModel(string? deviceModel)
        => string.IsNullOrWhiteSpace(deviceModel) ? "未配置" : deviceModel.Trim();

    private bool ResolveDefaultEnabled(TaskCandidate candidate)
    {
        if (candidate.DefaultEnabled)
        {
            return true;
        }

        var configured = configuration.GetValue<bool?>(DefaultEnableAllTasksKey);
        if (configured.HasValue)
        {
            return configured.Value;
        }

        return false;
    }

    private sealed record TaskAvailability(
        bool CanRun,
        string UnavailableReason,
        IReadOnlyList<TaskRequiredSignal> MissingRequiredSignals,
        bool IsSupportedByCurrentPlc);

    private sealed record CandidateAvailability(
        TaskCandidate Candidate,
        TaskAvailability Availability);
}
