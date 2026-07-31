using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.Application.Common.Plc;
using IIoT.Edge.Module.Contracts.Plc.Checkpoints;

namespace IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;

public sealed class PlcTaskBindingService(
    IStationRuntimeRegistry runtimeRegistry,
    IReadRepository<NetworkDeviceEntity> networkDevices,
    IReadRepository<IoMappingEntity> ioMappings,
    IReadRepository<PlcTaskBindingEntity> bindings,
    IEdgeUnitOfWorkFactory unitOfWorkFactory,
    IPlcTaskRuntimeStatusReader? runtimeStatuses = null,
    IPlcTaskRecoveryApplicationService? taskRecovery = null)
    : IPlcTaskBindingService, IPlcTaskBindingPersistenceTransaction
{
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
        var observedAtUtc = DateTimeOffset.UtcNow;
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
            var recoveryByTaskKey = new Dictionary<string, PlcTaskRecoverySnapshot?>(
                StringComparer.OrdinalIgnoreCase);
            if (taskRecovery is not null && !string.IsNullOrWhiteSpace(device.PlcCode))
            {
                foreach (var candidate in candidates)
                {
                    recoveryByTaskKey[candidate.Key] = await taskRecovery
                        .QueryAsync(
                            moduleId,
                            device.PlcCode,
                            candidate.Key,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var taskItems = candidates
                .Select(candidate => CreateItem(
                    candidate,
                    rowByKey,
                    signalBindings,
                    device.DeviceModel,
                    device.PlcCode,
                    device.IsEnabled,
                    observedAtUtc,
                    recoveryByTaskKey.GetValueOrDefault(candidate.Key)))
                .ToArray();

            results.Add(new PlcTaskBindingDeviceDto(
                device.Id,
                device.DeviceName,
                moduleId,
                device.IsEnabled,
                taskItems)
            {
                PlcCode = device.PlcCode
            });
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

    public async Task<IReadOnlySet<string>> GetConfiguredEnabledTaskKeysAsync(
        int networkDeviceId,
        IReadOnlyCollection<TaskCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException("网络设备 Id 必须大于 0。", nameof(networkDeviceId));
        }

        ArgumentNullException.ThrowIfNull(candidates);
        var candidateKeys = candidates
            .Select(static candidate => candidate.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = await bindings.GetListAsync(
            x => x.NetworkDeviceId == networkDeviceId,
            cancellationToken).ConfigureAwait(false);
        return rows
            .Where(row => row.Enabled && candidateKeys.Contains(row.TaskKey))
            .Select(static row => row.TaskKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<PlcTaskBindingSavePreparation> PrepareAsync(
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
        if (device.DeviceType != DeviceType.PLC)
        {
            throw new InvalidOperationException($"设备“{device.DeviceName}”不是 PLC，已禁止保存 PLC 任务绑定。");
        }

        if (!runtimeRegistry.TryGetFactory(moduleId, out var factory))
        {
            throw new InvalidOperationException("当前模块未注册 PLC 运行时任务工厂。");
        }

        var candidates = factory.GetTaskCandidates().ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException("当前模块没有可绑定的 PLC 运行时任务候选，已禁止保存。");
        }

        var candidateByKey = candidates.ToDictionary(static x => x.Key, StringComparer.OrdinalIgnoreCase);
        var normalizedStates = taskStates.ToDictionary(
            static x => x.Key,
            static x => x.Value,
            StringComparer.OrdinalIgnoreCase);
        var unknownTaskKeys = normalizedStates.Keys
            .Where(key => !candidateByKey.ContainsKey(key))
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknownTaskKeys.Length > 0)
        {
            throw new InvalidOperationException(
                $"提交内容包含当前模块未声明的 TaskKey：{string.Join("、", unknownTaskKeys)}。已禁止部分保存。");
        }

        var savedRows = await bindings.GetListAsync(
            x => x.NetworkDeviceId == networkDeviceId,
            cancellationToken).ConfigureAwait(false);
        var savedByKey = savedRows.ToDictionary(static row => row.TaskKey, StringComparer.OrdinalIgnoreCase);
        var signalBindings = await LoadSignalBindingsAsync(networkDeviceId, cancellationToken).ConfigureAwait(false);
        var resolvedStates = candidates.ToDictionary(
            static candidate => candidate.Key,
            candidate => normalizedStates.TryGetValue(candidate.Key, out var submittedEnabled)
                ? submittedEnabled
                : savedByKey.TryGetValue(candidate.Key, out var saved)
                    && saved.Enabled,
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
        var candidateTaskKeys = candidates
            .Select(static candidate => candidate.Key)
            .ToArray();
        var candidateTaskKeySet = candidateTaskKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var originalRows = savedRows
            .Where(row => candidateTaskKeySet.Contains(row.TaskKey))
            .Select(static row => new PlcTaskBindingRowSnapshot(
                row.Id,
                row.TaskKey,
                row.Enabled,
                row.UpdatedAt))
            .OrderBy(static row => row.TaskKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PlcTaskBindingSavePreparation(
            networkDeviceId,
            device.PlcCode,
            device.DeviceName,
            moduleId,
            candidateTaskKeys,
            resolvedStates,
            originalRows,
            updatedAt,
            disabledHeartbeatTasks);
    }

    public Task CommitAsync(
        PlcTaskBindingSavePreparation preparation,
        CancellationToken cancellationToken = default)
        => ApplyPreparedRowsAsync(preparation, restoreOriginal: false, cancellationToken);

    public Task RestoreAsync(
        PlcTaskBindingSavePreparation preparation,
        CancellationToken cancellationToken = default)
        => ApplyPreparedRowsAsync(preparation, restoreOriginal: true, cancellationToken);

    private async Task ApplyPreparedRowsAsync(
        PlcTaskBindingSavePreparation preparation,
        bool restoreOriginal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var candidateTaskKeys = preparation.CandidateTaskKeys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (candidateTaskKeys.Count != preparation.CandidateTaskKeys.Count
            || candidateTaskKeys.Count != preparation.ResolvedStates.Count)
        {
            throw new InvalidOperationException("PLC 任务绑定事务快照包含重复或不完整的 TaskKey。");
        }

        await using var unitOfWork = await unitOfWorkFactory
            .BeginAsync(cancellationToken)
            .ConfigureAwait(false);
        var repository = unitOfWork.Repository<PlcTaskBindingEntity>();
        var deviceRows = await repository
            .GetListAsync(
                x => x.NetworkDeviceId == preparation.NetworkDeviceId,
                cancellationToken)
            .ConfigureAwait(false);
        var currentCandidateRows = deviceRows
            .Where(row => candidateTaskKeys.Contains(row.TaskKey))
            .ToArray();
        var currentByKey = currentCandidateRows.ToDictionary(
            static row => row.TaskKey,
            StringComparer.OrdinalIgnoreCase);

        if (restoreOriginal)
        {
            var originalByKey = preparation.OriginalRows.ToDictionary(
                static row => row.TaskKey,
                StringComparer.OrdinalIgnoreCase);
            foreach (var current in currentCandidateRows)
            {
                if (!originalByKey.TryGetValue(current.TaskKey, out var original))
                {
                    repository.Delete(current);
                    continue;
                }

                current.UpdateEnabled(original.Enabled, original.UpdatedAt);
                repository.Update(current);
            }

            foreach (var original in preparation.OriginalRows)
            {
                if (currentByKey.ContainsKey(original.TaskKey))
                {
                    continue;
                }

                repository.Add(PlcTaskBindingEntity.Create(
                    preparation.NetworkDeviceId,
                    original.TaskKey,
                    original.Enabled,
                    original.UpdatedAt));
            }
        }
        else
        {
            EnsureOriginalRowsUnchanged(preparation, currentCandidateRows);
            foreach (var taskKey in preparation.CandidateTaskKeys)
            {
                var enabled = preparation.ResolvedStates[taskKey];
                if (currentByKey.TryGetValue(taskKey, out var current))
                {
                    current.UpdateEnabled(enabled, preparation.UpdatedAt);
                    repository.Update(current);
                    continue;
                }

                repository.Add(PlcTaskBindingEntity.Create(
                    preparation.NetworkDeviceId,
                    taskKey,
                    enabled,
                    preparation.UpdatedAt));
            }
        }

        await unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
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
                var matches = signalBindings
                    .Where(binding => string.Equals(
                                          binding.SignalKey,
                                          required.SignalKey,
                                          StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(
                                          binding.Direction,
                                          required.Direction,
                                          StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (matches.Length == 0)
                {
                    issues.Add(new PlcTaskBindingValidationIssue(
                        candidate.Key,
                        candidate.DisplayName,
                        required,
                        PlcTaskBindingValidationIssueType.MissingRequiredSignal,
                        $"任务“{candidate.DisplayName}”缺少 IO 信号：{required.SignalKey}/{required.Direction}。"));
                    continue;
                }

                foreach (var mapping in matches)
                {
                    var typeWordLength = PlcIoTypeWordLengthValidator.Validate(
                        mapping.DataType,
                        mapping.AddressCount);
                    if (typeWordLength.IsValid)
                    {
                        continue;
                    }

                    issues.Add(new PlcTaskBindingValidationIssue(
                        candidate.Key,
                        candidate.DisplayName,
                        required,
                        PlcTaskBindingValidationIssueType.InvalidIoTypeWordLength,
                        $"任务“{candidate.DisplayName}”的 IO 信号 {required.SignalKey}/{required.Direction} "
                        + $"数据类型与 word 长度无效：{typeWordLength.FailureCode}。"));
                }
            }
        }

        return issues.Count == 0
            ? PlcTaskBindingValidationResult.Success()
            : PlcTaskBindingValidationResult.Failure(issues);
    }

    public Task<PlcTaskRecoveryConfirmationResult> ConfirmRecoveryAsync(
        string moduleId,
        string plcCode,
        string taskKey,
        long expectedRevision,
        PlcTaskRecoveryConfirmationAction action,
        CancellationToken cancellationToken = default)
        => taskRecovery?.ConfirmAsync(
               moduleId,
               plcCode,
               taskKey,
               expectedRevision,
               action,
               cancellationToken)
           ?? Task.FromResult(PlcTaskRecoveryConfirmationResult.Rejected(
               PlcTaskRecoveryConfirmationOutcome.NotFound,
               PlcTaskRecoveryDiagnosticCodes.ProviderUnavailable));

    private PlcTaskBindingItemDto CreateItem(
        TaskCandidate candidate,
        IReadOnlyDictionary<string, PlcTaskBindingEntity> rowByKey,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
        string? deviceModel,
        string plcCode,
        bool isDeviceEnabled,
        DateTimeOffset observedAtUtc,
        PlcTaskRecoverySnapshot? recovery)
    {
        var hasSavedBinding = rowByKey.ContainsKey(candidate.Key);
        var configuredEnabled = rowByKey.TryGetValue(candidate.Key, out var configuredRow)
                                && configuredRow.Enabled;
        var availability = EvaluateTaskAvailability(candidate, deviceModel, signalBindings);
        var runtime = string.IsNullOrWhiteSpace(plcCode)
            ? null
            : runtimeStatuses?.GetSnapshot(plcCode, candidate.Key);
        return new PlcTaskBindingItemDto(
            candidate.Key,
            candidate.DisplayName,
            configuredEnabled,
            hasSavedBinding,
            candidate.IsHeartbeatLike,
            candidate.RequiredSignals,
            availability.CanRun,
            availability.UnavailableReason,
            availability.MissingRequiredSignals,
            availability.IsSupportedByCurrentPlc,
            ResolveConfigurationStateChangedAt(
                hasSavedBinding,
                isDeviceEnabled,
                configuredEnabled,
                configuredRow,
                observedAtUtc),
            runtime?.State,
            runtime?.StateChangedAtUtc,
            runtime?.LastSuccessfulAtUtc,
            runtime?.ErrorCode,
            runtime?.ExceptionType,
            recovery?.State ?? PlcTaskRecoveryState.None,
            recovery?.Revision ?? 0,
            recovery?.CheckpointMagazineCode,
            recovery?.ObservedMagazineCode,
            recovery?.CheckpointSavedAtUtc,
            recovery?.ObservedAtUtc,
            recovery?.DiagnosticCode);
    }

    private static DateTimeOffset ResolveConfigurationStateChangedAt(
        bool hasSavedBinding,
        bool isDeviceEnabled,
        bool configuredEnabled,
        PlcTaskBindingEntity? configuredRow,
        DateTimeOffset observedAtUtc)
    {
        if (hasSavedBinding
            && isDeviceEnabled
            && !configuredEnabled
            && configuredRow is not null)
        {
            return configuredRow.UpdatedAt;
        }

        return observedAtUtc;
    }

    private static bool ResolveEnabled(
        TaskCandidate candidate,
        IReadOnlyDictionary<string, PlcTaskBindingEntity> rowByKey,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings,
        string? deviceModel)
        => rowByKey.TryGetValue(candidate.Key, out var row)
            && row.Enabled
            && EvaluateTaskAvailability(candidate, deviceModel, signalBindings).CanRun;

    private static TaskAvailability EvaluateTaskAvailability(
        TaskCandidate candidate,
        string? deviceModel,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings)
    {
        var isSupported = candidate.SupportsDeviceModel(deviceModel);
        var missingSignals = FindMissingRequiredSignals(candidate, signalBindings);
        var invalidTypeWordLengths = FindInvalidRequiredSignalTypeWordLengths(
            candidate,
            signalBindings);

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

        if (invalidTypeWordLengths.Count > 0)
        {
            return new TaskAvailability(
                CanRun: false,
                UnavailableReason: $"IO 类型/word 长度无效：{string.Join("、", invalidTypeWordLengths)}",
                MissingRequiredSignals: [],
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

    private static IReadOnlyList<string> FindInvalidRequiredSignalTypeWordLengths(
        TaskCandidate candidate,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings)
    {
        var invalid = new List<string>();
        foreach (var required in candidate.RequiredSignals)
        {
            foreach (var mapping in signalBindings.Where(binding =>
                         string.Equals(
                             binding.SignalKey,
                             required.SignalKey,
                             StringComparison.OrdinalIgnoreCase)
                         && string.Equals(
                             binding.Direction,
                             required.Direction,
                             StringComparison.OrdinalIgnoreCase)))
            {
                var validation = PlcIoTypeWordLengthValidator.Validate(
                    mapping.DataType,
                    mapping.AddressCount);
                if (!validation.IsValid)
                {
                    invalid.Add(
                        $"{required.SignalKey}/{required.Direction}/{validation.FailureCode}");
                }
            }
        }

        return invalid
            .Distinct(StringComparer.OrdinalIgnoreCase)
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

    private static void EnsureOriginalRowsUnchanged(
        PlcTaskBindingSavePreparation preparation,
        IReadOnlyCollection<PlcTaskBindingEntity> currentRows)
    {
        var originals = preparation.OriginalRows.ToDictionary(
            static row => row.TaskKey,
            StringComparer.OrdinalIgnoreCase);
        if (originals.Count != currentRows.Count)
        {
            throw new InvalidOperationException(
                "PLC 任务绑定在校验与提交之间已变化，已拒绝覆盖并要求重新加载。");
        }

        foreach (var current in currentRows)
        {
            if (!originals.TryGetValue(current.TaskKey, out var original)
                || original.Id != current.Id
                || original.Enabled != current.Enabled
                || original.UpdatedAt != current.UpdatedAt)
            {
                throw new InvalidOperationException(
                    "PLC 任务绑定在校验与提交之间已变化，已拒绝覆盖并要求重新加载。");
            }
        }
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
