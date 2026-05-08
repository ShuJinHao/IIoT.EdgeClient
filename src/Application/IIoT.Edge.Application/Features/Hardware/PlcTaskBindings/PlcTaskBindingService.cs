using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace IIoT.Edge.Application.Features.Hardware.PlcTaskBindings;

public sealed class PlcTaskBindingService(
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    IStationRuntimeRegistry runtimeRegistry,
    IReadRepository<NetworkDeviceEntity> networkDevices,
    IRepository<PlcTaskBindingEntity> bindings,
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
            x => x.DeviceType == DeviceType.PLC && x.ModuleId == moduleId,
            cancellationToken).ConfigureAwait(false);

        var results = new List<PlcTaskBindingDeviceDto>(devices.Count);
        foreach (var device in devices.OrderBy(static x => x.DeviceName, StringComparer.OrdinalIgnoreCase))
        {
            var rows = await bindings.GetListAsync(
                x => x.NetworkDeviceId == device.Id,
                cancellationToken).ConfigureAwait(false);
            var rowByKey = rows.ToDictionary(x => x.TaskKey, StringComparer.OrdinalIgnoreCase);
            var taskItems = candidates
                .Select(candidate => CreateItem(candidate, rowByKey))
                .ToArray();

            results.Add(new PlcTaskBindingDeviceDto(
                device.Id,
                device.DeviceName,
                device.ModuleId,
                device.IsEnabled,
                taskItems));
        }

        return results;
    }

    public async Task<IReadOnlySet<string>> GetEnabledTaskKeysAsync(
        int networkDeviceId,
        IReadOnlyCollection<TaskCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        if (networkDeviceId <= 0)
        {
            throw new ArgumentException("网络设备 Id 必须大于 0。", nameof(networkDeviceId));
        }

        var rows = await bindings.GetListAsync(
            x => x.NetworkDeviceId == networkDeviceId,
            cancellationToken).ConfigureAwait(false);
        var rowByKey = rows.ToDictionary(x => x.TaskKey, StringComparer.OrdinalIgnoreCase);
        var enabledTaskKeys = candidates
            .Where(candidate => ResolveEnabled(candidate.Key, rowByKey))
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
        if (!string.Equals(device.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("PLC 设备所属模块与当前任务绑定页面不一致。");
        }

        if (!runtimeRegistry.TryGetFactory(moduleId, out var factory))
        {
            throw new InvalidOperationException("当前模块未注册 PLC 运行时任务工厂。");
        }

        var candidates = factory.GetTaskCandidates();
        var candidateByKey = candidates.ToDictionary(static x => x.Key, StringComparer.OrdinalIgnoreCase);
        var normalizedStates = taskStates
            .Where(x => candidateByKey.ContainsKey(x.Key))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var disabledHeartbeatTasks = candidates
            .Where(x => x.IsHeartbeatLike
                        && normalizedStates.TryGetValue(x.Key, out var enabled)
                        && !enabled)
            .Select(static x => x.DisplayName)
            .ToArray();

        await bindings.ExecuteDeleteAsync(
            x => x.NetworkDeviceId == networkDeviceId,
            cancellationToken).ConfigureAwait(false);

        var updatedAt = DateTimeOffset.UtcNow;
        foreach (var candidate in candidates)
        {
            var enabled = normalizedStates.TryGetValue(candidate.Key, out var submittedEnabled)
                ? submittedEnabled
                : ResolveDefaultEnabled();
            bindings.Add(PlcTaskBindingEntity.Create(networkDeviceId, candidate.Key, enabled, updatedAt));
        }

        await bindings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (disabledHeartbeatTasks.Length > 0)
        {
            logger.Warn($"PLC“{device.DeviceName}”已关闭心跳类任务：{string.Join("、", disabledHeartbeatTasks)}。");
        }
    }

    public PlcTaskBindingValidationResult ValidateEnabledTasks(
        IReadOnlyCollection<TaskCandidate> candidates,
        IReadOnlySet<string> enabledTaskKeys,
        IReadOnlyCollection<ModuleIoSnapshot> signalBindings)
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
            foreach (var required in candidate.RequiredSignals)
            {
                var key = $"{required.SignalKey}\u001f{required.Direction}";
                if (!mappedSignals.Contains(key))
                {
                    issues.Add(new PlcTaskBindingValidationIssue(candidate.Key, candidate.DisplayName, required));
                }
            }
        }

        return issues.Count == 0
            ? PlcTaskBindingValidationResult.Success()
            : PlcTaskBindingValidationResult.Failure(issues);
    }

    private PlcTaskBindingItemDto CreateItem(
        TaskCandidate candidate,
        IReadOnlyDictionary<string, PlcTaskBindingEntity> rowByKey)
    {
        var hasSavedBinding = rowByKey.ContainsKey(candidate.Key);
        return new PlcTaskBindingItemDto(
            candidate.Key,
            candidate.DisplayName,
            ResolveEnabled(candidate.Key, rowByKey),
            hasSavedBinding,
            candidate.IsHeartbeatLike,
            candidate.RequiredSignals);
    }

    private bool ResolveEnabled(
        string taskKey,
        IReadOnlyDictionary<string, PlcTaskBindingEntity> rowByKey)
        => rowByKey.TryGetValue(taskKey, out var row)
            ? row.Enabled
            : ResolveDefaultEnabled();

    private bool ResolveDefaultEnabled()
    {
        var configured = configuration.GetValue<bool?>(DefaultEnableAllTasksKey);
        if (configured.HasValue)
        {
            return configured.Value;
        }

        return hostEnvironment.IsProduction();
    }
}
