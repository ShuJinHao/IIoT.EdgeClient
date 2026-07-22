using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Runtime;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;
using System.Text.Json;

namespace IIoT.Edge.Host.DataPipeline.Context;

public class ProductionContextStore : IProductionContextStore
{
    private const string PersistFileName = "production_context.json";

    private readonly Dictionary<string, ProductionContext> _contexts = new();
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
        lock (_lock)
        {
            if (_contexts.TryGetValue(deviceName, out var ctx))
            {
                if (TryGetContextFactory(moduleId, out var factory)
                    && !factory.ContextType.IsInstanceOfType(ctx))
                {
                    var upgraded = CreateContext(factory, deviceName);
                    _stateCopier.Copy(ctx, upgraded);
                    _contexts[deviceName] = upgraded;
                    return upgraded;
                }

                return ctx;
            }

            ctx = TryGetContextFactory(moduleId, out var contextFactory)
                ? CreateContext(contextFactory, deviceName)
                : new ProductionContext
                {
                    DeviceName = deviceName
                };
            _contexts[deviceName] = ctx;
            return ctx;
        }
    }

    public IReadOnlyCollection<ProductionContext> GetAll()
    {
        lock (_lock)
        {
            return _contexts.Values.ToList().AsReadOnly();
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
                    _contexts[ctx.DeviceName] = ctx;
                }
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
                        $"  [{ctx.DeviceName}] 电芯数：{cellCount}，步骤：{(string.IsNullOrEmpty(stepInfo) ? "无" : stepInfo)}，白班：{capacity.DayShift.Total}，夜班：{capacity.NightShift.Total}");
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
                contexts = _contexts.Values.ToList();
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
            _persistenceDiagnostics = diagnostics;
        }
    }

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
