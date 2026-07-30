using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using IIoT.Edge.Module.Contracts.Identity;
using System.Text.Json;

namespace IIoT.Edge.Host.DataPipeline.Context;

public class ProductionContextStore : IProductionContextStore, IPlcProductionContextStore
{
    private const string PersistFileName = "production_context.json";

    private readonly Dictionary<string, ProductionContext> _contexts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProductionContext> _compatibilityContexts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProductionContext> _pendingMigrationContexts = [];
    private readonly List<PlcProductionContextBlockDiagnostic> _identityBlocks = [];
    private readonly IReadOnlyDictionary<string, IProductionContextFactory> _contextFactories;
    private readonly IProductionContextPersistenceFileSystem _fileSystem;
    private readonly IProductionContextCorruptFileQuarantine _corruptFileQuarantine;
    private readonly IProductionContextRuntimeStateCopier _stateCopier;
    private readonly ILogService _logger;
    private readonly string _persistPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lock = new();
    private ProductionContextPersistenceDiagnostics _persistenceDiagnostics = new(0, null);

    public ProductionContextStore(
        ILogService logger,
        ICellDataTypeRegistry cellDataTypeRegistry,
        string? persistDirectory = null)
        : this(logger, Array.Empty<IProductionContextFactory>(), cellDataTypeRegistry, new ProductionContextPersistenceFileSystem(), persistDirectory)
    {
    }

    public ProductionContextStore(
        ILogService logger,
        IEnumerable<IProductionContextFactory> contextFactories,
        ICellDataTypeRegistry cellDataTypeRegistry,
        string? persistDirectory = null)
        : this(logger, contextFactories, cellDataTypeRegistry, new ProductionContextPersistenceFileSystem(), persistDirectory)
    {
    }

    internal ProductionContextStore(
        ILogService logger,
        ICellDataTypeRegistry cellDataTypeRegistry,
        IProductionContextPersistenceFileSystem fileSystem,
        string? persistDirectory = null)
        : this(logger, Array.Empty<IProductionContextFactory>(), cellDataTypeRegistry, fileSystem, persistDirectory)
    {
    }

    internal ProductionContextStore(
        ILogService logger,
        IEnumerable<IProductionContextFactory> contextFactories,
        ICellDataTypeRegistry cellDataTypeRegistry,
        IProductionContextPersistenceFileSystem fileSystem,
        string? persistDirectory = null,
        IProductionContextCorruptFileQuarantine? corruptFileQuarantine = null,
        IProductionContextRuntimeStateCopier? stateCopier = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(cellDataTypeRegistry);

        _logger = logger;
        _fileSystem = fileSystem;
        _corruptFileQuarantine = corruptFileQuarantine ?? new ProductionContextCorruptFileQuarantine(logger);
        _stateCopier = stateCopier ?? new ProductionContextRuntimeStateCopier();
        _jsonOptions = CreateJsonOptions(cellDataTypeRegistry);
        _contextFactories = (contextFactories ?? Array.Empty<IProductionContextFactory>())
            .Where(static x => !string.IsNullOrWhiteSpace(x.ModuleId))
            .GroupBy(static x => x.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last(),
                StringComparer.OrdinalIgnoreCase);

        var dir = persistDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IIoT.Edge");

        Directory.CreateDirectory(dir);
        _persistPath = Path.Combine(dir, PersistFileName);
    }

    private static JsonSerializerOptions CreateJsonOptions(ICellDataTypeRegistry cellDataTypeRegistry)
        => new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters =
            {
                new ObjectToInferredTypesConverter(),
                new CellDataBaseConverter(cellDataTypeRegistry)
            }
        };

    public ProductionContext GetOrCreate(string deviceName)
        => GetOrCreate(deviceName, moduleId: null);

    public ProductionContext GetOrCreate(string deviceName, string? moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);

        lock (_lock)
        {
            var normalizedDeviceName = deviceName.Trim();
            if (_compatibilityContexts.TryGetValue(normalizedDeviceName, out var ctx))
            {
                if (TryGetContextFactory(moduleId, out var factory)
                    && !factory.ContextType.IsInstanceOfType(ctx))
                {
                    var upgraded = CreateContext(factory, normalizedDeviceName);
                    _stateCopier.Copy(ctx, upgraded);
                    _compatibilityContexts[normalizedDeviceName] = upgraded;
                    return upgraded;
                }

                return ctx;
            }

            ctx = TryGetContextFactory(moduleId, out var contextFactory)
                ? CreateContext(contextFactory, normalizedDeviceName)
                : new ProductionContext
                {
                    DeviceName = normalizedDeviceName
                };
            _compatibilityContexts[normalizedDeviceName] = ctx;
            return ctx;
        }
    }

    public PlcProductionContextResolution GetOrCreate(
        PlcIdentity identity,
        string? moduleId = null)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var plcCode = identity.PlcCode?.Trim() ?? string.Empty;
        var deviceName = identity.DeviceName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(plcCode)
            || identity.NetworkDeviceId <= 0
            || string.IsNullOrWhiteSpace(deviceName))
        {
            return Block(
                PlcProductionContextResolutionOutcome.InvalidIdentity,
                plcCode,
                identity.NetworkDeviceId > 0 ? identity.NetworkDeviceId : null,
                deviceName,
                "plc_identity_invalid",
                "PLC 生产上下文要求有效 PlcCode、正数 NetworkDeviceId 和当前 DeviceName。");
        }

        lock (_lock)
        {
            var conflictingResolvedContext = _contexts.Values.FirstOrDefault(context =>
                context.NetworkDeviceId == identity.NetworkDeviceId
                && !string.Equals(context.PlcCode, plcCode, StringComparison.OrdinalIgnoreCase));
            if (conflictingResolvedContext is not null)
            {
                return BlockLocked(
                    PlcProductionContextResolutionOutcome.IdentityConflict,
                    plcCode,
                    identity.NetworkDeviceId,
                    deviceName,
                    "plc_identity_network_device_conflict",
                    $"NetworkDeviceId={identity.NetworkDeviceId} 已归属 PlcCode={conflictingResolvedContext.PlcCode}。");
            }

            var candidates = _pendingMigrationContexts
                .Where(context => IsMigrationCandidate(context, identity))
                .ToArray();
            if (_contexts.TryGetValue(plcCode, out var existing))
            {
                if (candidates.Length > 0)
                {
                    return BlockLocked(
                        PlcProductionContextResolutionOutcome.IdentityConflict,
                        plcCode,
                        identity.NetworkDeviceId,
                        deviceName,
                        "production_context_duplicate_plc_code",
                        $"PlcCode={plcCode} 同时匹配到已解析上下文和 {candidates.Length} 条历史上下文，已失败关闭。");
                }

                existing = UpgradeContextIfRequired(existing, moduleId, plcCode);
                existing.PlcCode = plcCode;
                existing.NetworkDeviceId = identity.NetworkDeviceId;
                existing.DeviceName = deviceName;
                _contexts[plcCode] = existing;
                RemoveIdentityBlockLocked(plcCode, identity.NetworkDeviceId);
                RefreshPersistenceDiagnosticsLocked();
                return PlcProductionContextResolution.Success(existing);
            }

            if (candidates.Length > 1)
            {
                return BlockLocked(
                    PlcProductionContextResolutionOutcome.IdentityConflict,
                    plcCode,
                    identity.NetworkDeviceId,
                    deviceName,
                    "production_context_migration_ambiguous",
                    $"发现 {candidates.Length} 条可匹配的历史运行上下文，禁止按 DeviceName 猜测归属。");
            }

            if (candidates.Length == 1)
            {
                var candidate = candidates[0];
                var embeddedCodes = ResolveEmbeddedPlcCodes(candidate);
                if (embeddedCodes.Count > 0
                    && !embeddedCodes.All(code =>
                        string.Equals(code, plcCode, StringComparison.OrdinalIgnoreCase)))
                {
                    return BlockLocked(
                        PlcProductionContextResolutionOutcome.MigrationBlocked,
                        plcCode,
                        identity.NetworkDeviceId,
                        deviceName,
                        "production_context_plc_code_conflict",
                        $"历史上下文 CellData.DeviceCode 与权威 PlcCode={plcCode} 不一致。");
                }

                _pendingMigrationContexts.Remove(candidate);
                RemoveCompatibilityContextReferencesLocked(candidate);
                candidate = UpgradeContextIfRequired(candidate, moduleId, plcCode);
                candidate.PlcCode = plcCode;
                candidate.NetworkDeviceId = identity.NetworkDeviceId;
                candidate.DeviceName = deviceName;
                _contexts[plcCode] = candidate;
                RemoveIdentityBlockLocked(plcCode, identity.NetworkDeviceId);
                RefreshPersistenceDiagnosticsLocked();
                _logger.Info(
                    $"[运行上下文][PlcCode={plcCode}] 历史上下文已按稳定身份完成迁移，NetworkDeviceId={identity.NetworkDeviceId}。");
                return PlcProductionContextResolution.Success(candidate);
            }

            var context = TryGetContextFactory(moduleId, out var factory)
                ? CreateContext(factory, deviceName)
                : new ProductionContext();
            context.PlcCode = plcCode;
            context.NetworkDeviceId = identity.NetworkDeviceId;
            context.DeviceName = deviceName;
            _contexts[plcCode] = context;
            RemoveIdentityBlockLocked(plcCode, identity.NetworkDeviceId);
            RefreshPersistenceDiagnosticsLocked();
            return PlcProductionContextResolution.Success(context);
        }
    }

    public IReadOnlyCollection<ProductionContext> GetAll()
    {
        lock (_lock)
        {
            var blockedPlcCodes = _identityBlocks
                .Select(static diagnostic => diagnostic.PlcCode?.Trim() ?? string.Empty)
                .Where(static plcCode => !string.IsNullOrWhiteSpace(plcCode))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            return _contexts.Values
                .Where(context => !blockedPlcCodes.Contains(context.PlcCode))
                .Concat(_compatibilityContexts.Values.Where(context =>
                    !_pendingMigrationContexts.Contains(context, ReferenceEqualityComparer.Instance)
                    && !blockedPlcCodes.Contains(context.PlcCode)))
                .Distinct<ProductionContext>(ReferenceEqualityComparer.Instance)
                .ToList()
                .AsReadOnly();
        }
    }

    public ProductionContextPersistenceDiagnostics GetPersistenceDiagnostics()
    {
        lock (_lock)
        {
            return _persistenceDiagnostics;
        }
    }

    public void LoadFromFile()
    {
        if (!File.Exists(_persistPath))
        {
            _logger.Info("[运行上下文] 未找到持久化文件，使用空运行状态。");
            RefreshPersistenceDiagnostics();
            return;
        }

        try
        {
            var json = File.ReadAllText(_persistPath);
            var list = JsonSerializer.Deserialize<List<ProductionContext>>(json, _jsonOptions);
            if (list is null)
            {
                return;
            }

            lock (_lock)
            {
                foreach (var ctx in list)
                {
                    LoadContextLocked(ctx);
                }

                RefreshPersistenceDiagnosticsLocked();
            }

            _logger.Info($"[运行上下文] 已恢复 {list.Count} 个设备运行上下文。");

            lock (_lock)
            {
                foreach (var ctx in _contexts.Values)
                {
                    var cellCount = ctx.CurrentCells.Count;
                    var stepInfo = string.Join(", ", ctx.StepStates.Select(kv => $"{kv.Key}={kv.Value}"));
                    var capacity = ctx.TodayCapacity;
                    _logger.Info(
                        $"  [PlcCode={ctx.PlcCode}][{ctx.DeviceName}] 电芯数：{cellCount}，步骤：{(string.IsNullOrEmpty(stepInfo) ? "无" : stepInfo)}，白班：{capacity.DayShift.Total}，夜班：{capacity.NightShift.Total}");
                }
            }
        }
        catch (JsonException ex)
        {
            HandleCorruptPersistedFile(ex);
        }
        catch (InvalidOperationException ex)
        {
            HandleCorruptPersistedFile(ex);
        }
        catch (NotSupportedException ex)
        {
            HandleCorruptPersistedFile(ex);
        }
        catch (Exception ex)
        {
            _logger.Error($"[运行上下文] 加载运行状态失败：{ex.Message}");
        }
        finally
        {
            RefreshPersistenceDiagnostics();
        }
    }

    public void SaveToFile()
    {
        var tempPath = _persistPath + ".tmp";

        try
        {
            List<ProductionContext> contexts;
            lock (_lock)
            {
                contexts = _contexts.Values
                    .Concat(_pendingMigrationContexts)
                    .Concat(_compatibilityContexts.Values)
                    .Distinct<ProductionContext>(ReferenceEqualityComparer.Instance)
                    .ToList();
            }

            var json = JsonSerializer.Serialize(contexts, _jsonOptions);

            try
            {
                _fileSystem.WriteAllText(tempPath, json);
            }
            catch (Exception ex)
            {
                var message =
                    $"[运行上下文] 写入临时文件 {Path.GetFileName(tempPath)} 失败：{ex.Message}。{CleanupTempFile(tempPath)}";
                _logger.Error(message);
                throw new IOException(message, ex);
            }

            try
            {
                _fileSystem.ReplaceFile(tempPath, _persistPath);
            }
            catch (Exception ex)
            {
                var message =
                    $"[运行上下文] 替换持久化文件 {Path.GetFileName(_persistPath)} 失败：{ex.Message}。{CleanupTempFile(tempPath)}";
                _logger.Error(message);
                throw new IOException(message, ex);
            }

            _logger.Info($"[运行上下文] 已保存 {contexts.Count} 个设备运行上下文。");
        }
        catch (Exception ex)
        {
            _logger.Error($"[运行上下文] 保存运行状态失败：{ex.Message}");
            throw;
        }
    }

    public async Task StartAutoSaveAsync(CancellationToken ct, int intervalSeconds = 30)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct);
                try
                {
                    SaveToFile();
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[运行上下文] 自动保存失败，将在下一周期重试：{ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private void HandleCorruptPersistedFile(Exception ex)
    {
        lock (_lock)
        {
            _contexts.Clear();
            _compatibilityContexts.Clear();
            _pendingMigrationContexts.Clear();
            _identityBlocks.Clear();
        }

        var quarantinedPath = _corruptFileQuarantine.TryQuarantine(_persistPath, PersistFileName);
        if (quarantinedPath is not null)
        {
            _logger.Error(
                $"[运行上下文] 持久化运行状态已损坏，已隔离到 {Path.GetFileName(quarantinedPath)}。{ex.Message}");
        }
        else
        {
            _logger.Error($"[运行上下文] 持久化运行状态已损坏，且无法完成隔离：{ex.Message}");
        }

        _logger.Warn("[运行上下文] 持久化文件无法恢复，已使用空运行状态启动。");
        RefreshPersistenceDiagnostics();
    }

    private void RefreshPersistenceDiagnostics()
    {
        try
        {
            UpdatePersistenceDiagnostics(_corruptFileQuarantine.BuildDiagnostics(_persistPath));
        }
        catch (Exception ex)
        {
            _logger.Warn($"[运行上下文] 刷新持久化诊断失败：{ex.Message}");
        }
    }

    private void UpdatePersistenceDiagnostics(ProductionContextPersistenceDiagnostics diagnostics)
    {
        lock (_lock)
        {
            _persistenceDiagnostics = diagnostics with
            {
                IdentityBlocks = _identityBlocks.ToArray()
            };
        }
    }

    private void LoadContextLocked(ProductionContext context)
    {
        var plcCode = context.PlcCode?.Trim() ?? string.Empty;
        var embeddedCodes = ResolveEmbeddedPlcCodes(context);
        if (!string.IsNullOrWhiteSpace(plcCode)
            && embeddedCodes.All(code =>
                string.Equals(code, plcCode, StringComparison.OrdinalIgnoreCase))
            && !_contexts.ContainsKey(plcCode))
        {
            context.PlcCode = plcCode;
            _contexts[plcCode] = context;
            return;
        }

        _pendingMigrationContexts.Add(context);
        if (!string.IsNullOrWhiteSpace(context.DeviceName))
        {
            var deviceName = context.DeviceName.Trim();
            if (_compatibilityContexts.TryGetValue(deviceName, out var existing)
                && !ReferenceEquals(existing, context))
            {
                _compatibilityContexts.Remove(deviceName);
            }
            else
            {
                _compatibilityContexts[deviceName] = context;
            }
        }

        var code = !string.IsNullOrWhiteSpace(plcCode)
            ? "production_context_duplicate_plc_code"
            : embeddedCodes.Count > 1
                ? "production_context_multiple_cell_plc_codes"
                : "production_context_identity_unresolved";
        var message = !string.IsNullOrWhiteSpace(plcCode)
            ? $"历史运行上下文 PlcCode={plcCode} 重复或与 CellData.DeviceCode 冲突。"
            : embeddedCodes.Count > 1
                ? $"历史运行上下文包含多个 CellData.DeviceCode：{string.Join(",", embeddedCodes)}。"
                : "历史运行上下文缺少 PlcCode，等待按正数 NetworkDeviceId 或唯一 CellData.DeviceCode 迁移。";
        var diagnosticPlcCode = !string.IsNullOrWhiteSpace(plcCode)
            ? plcCode
            : embeddedCodes.Count == 1
                ? embeddedCodes[0]
                : string.Empty;
        AddIdentityBlockLocked(new PlcProductionContextBlockDiagnostic(
            diagnosticPlcCode,
            context.NetworkDeviceId > 0 ? context.NetworkDeviceId : null,
            context.DeviceName,
            code,
            message));
    }

    private ProductionContext UpgradeContextIfRequired(
        ProductionContext context,
        string? moduleId,
        string plcCode)
    {
        if (!TryGetContextFactory(moduleId, out var factory)
            || factory.ContextType.IsInstanceOfType(context))
        {
            return context;
        }

        var upgraded = CreateContext(factory, context.DeviceName);
        _stateCopier.Copy(context, upgraded);
        upgraded.PlcCode = plcCode;
        return upgraded;
    }

    private static bool IsMigrationCandidate(
        ProductionContext context,
        PlcIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(context.PlcCode)
            && string.Equals(context.PlcCode, identity.PlcCode, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (context.NetworkDeviceId > 0
            && context.NetworkDeviceId == identity.NetworkDeviceId)
        {
            return true;
        }

        var embeddedCodes = ResolveEmbeddedPlcCodes(context);
        return embeddedCodes.Count == 1
               && string.Equals(embeddedCodes[0], identity.PlcCode, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ResolveEmbeddedPlcCodes(ProductionContext context)
        => context.CurrentCells.Values
            .Select(static cell => cell.DeviceCode?.Trim() ?? string.Empty)
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static code => code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private PlcProductionContextResolution Block(
        PlcProductionContextResolutionOutcome outcome,
        string plcCode,
        int? networkDeviceId,
        string? deviceName,
        string diagnosticCode,
        string diagnosticMessage)
    {
        lock (_lock)
        {
            return BlockLocked(
                outcome,
                plcCode,
                networkDeviceId,
                deviceName,
                diagnosticCode,
                diagnosticMessage);
        }
    }

    private PlcProductionContextResolution BlockLocked(
        PlcProductionContextResolutionOutcome outcome,
        string plcCode,
        int? networkDeviceId,
        string? deviceName,
        string diagnosticCode,
        string diagnosticMessage)
    {
        AddIdentityBlockLocked(new PlcProductionContextBlockDiagnostic(
            plcCode,
            networkDeviceId,
            deviceName,
            diagnosticCode,
            diagnosticMessage));
        RefreshPersistenceDiagnosticsLocked();
        _logger.Error(
            $"[运行上下文][PlcCode={FormatIdentity(plcCode)}] 稳定身份解析已阻断：{diagnosticMessage}");
        return PlcProductionContextResolution.Blocked(
            outcome,
            plcCode,
            diagnosticCode,
            diagnosticMessage);
    }

    private void AddIdentityBlockLocked(PlcProductionContextBlockDiagnostic diagnostic)
    {
        if (_identityBlocks.Any(existing =>
            string.Equals(existing.PlcCode, diagnostic.PlcCode, StringComparison.OrdinalIgnoreCase)
            && existing.NetworkDeviceId == diagnostic.NetworkDeviceId
            && string.Equals(existing.DiagnosticCode, diagnostic.DiagnosticCode, StringComparison.Ordinal)))
        {
            return;
        }

        _identityBlocks.Add(diagnostic);
    }

    private void RemoveIdentityBlockLocked(string plcCode, int networkDeviceId)
        => _identityBlocks.RemoveAll(diagnostic =>
            string.Equals(diagnostic.PlcCode, plcCode, StringComparison.OrdinalIgnoreCase)
            || diagnostic.NetworkDeviceId == networkDeviceId);

    private void RemoveCompatibilityContextReferencesLocked(ProductionContext context)
    {
        foreach (var deviceName in _compatibilityContexts
                     .Where(pair => ReferenceEquals(pair.Value, context))
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            _compatibilityContexts.Remove(deviceName);
        }
    }

    private void RefreshPersistenceDiagnosticsLocked()
        => _persistenceDiagnostics = _persistenceDiagnostics with
        {
            IdentityBlocks = _identityBlocks.ToArray()
        };

    private static string FormatIdentity(string? value)
        => string.IsNullOrWhiteSpace(value) ? "未知" : value;

    private bool TryGetContextFactory(string? moduleId, out IProductionContextFactory factory)
    {
        if (!string.IsNullOrWhiteSpace(moduleId)
            && _contextFactories.TryGetValue(moduleId, out factory!))
        {
            return true;
        }

        factory = default!;
        return false;
    }

    private static ProductionContext CreateContext(IProductionContextFactory factory, string deviceName)
    {
        var context = factory.Create(deviceName);
        if (string.IsNullOrWhiteSpace(context.DeviceName))
        {
            context.DeviceName = deviceName;
        }

        return context;
    }

    private string CleanupTempFile(string tempPath)
    {
        try
        {
            if (!_fileSystem.FileExists(tempPath))
            {
                return "临时文件清理：未发现残留 .tmp 文件。";
            }

            _fileSystem.DeleteFile(tempPath);
            return "临时文件清理：已删除残留 .tmp 文件。";
        }
        catch (Exception cleanupEx)
        {
            return $"临时文件清理：删除残留 .tmp 文件失败：{cleanupEx.Message}";
        }
    }
}
