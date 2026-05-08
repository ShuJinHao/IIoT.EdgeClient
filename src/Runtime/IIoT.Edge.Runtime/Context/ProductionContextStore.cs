using IIoT.Edge.Application.Context;
using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IIoT.Edge.Runtime.Context;

public class ProductionContextStore : IProductionContextStore
{
    private const string PersistFileName = "production_context.json";
    private static readonly Regex CorruptFileTimestampPattern = new(
        @"^production_context\.corrupt-(\d{17})(?:-\d+)?\.json$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dictionary<string, ProductionContext> _contexts = new();
    private readonly IReadOnlyDictionary<string, IProductionContextFactory> _contextFactories;
    private readonly IProductionContextPersistenceFileSystem _fileSystem;
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
        string? persistDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(cellDataTypeRegistry);

        _logger = logger;
        _fileSystem = fileSystem;
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
                    CopyRuntimeState(ctx, upgraded);
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
            _logger.Info("[ContextStore] 未找到持久化文件，使用空运行状态。");
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

            _logger.Info($"[ContextStore] 已恢复 {list.Count} 个设备运行上下文。");

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
            _logger.Error($"[ContextStore] 加载运行状态失败：{ex.Message}");
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
                _logger.Error(
                    $"[ContextStore] 写入临时文件 {Path.GetFileName(tempPath)} 失败：{ex.Message}。{CleanupTempFile(tempPath)}");
                return;
            }

            try
            {
                _fileSystem.ReplaceFile(tempPath, _persistPath);
            }
            catch (Exception ex)
            {
                _logger.Error(
                    $"[ContextStore] 替换持久化文件 {Path.GetFileName(_persistPath)} 失败：{ex.Message}。{CleanupTempFile(tempPath)}");
                return;
            }

            _logger.Info($"[ContextStore] 已保存 {contexts.Count} 个设备运行上下文。");
        }
        catch (Exception ex)
        {
            _logger.Error($"[ContextStore] 保存运行状态失败：{ex.Message}");
        }
    }

    public async Task StartAutoSaveAsync(CancellationToken ct, int intervalSeconds = 30)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct);
                SaveToFile();
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

        var quarantinedPath = TryQuarantineCorruptFile();
        if (quarantinedPath is not null)
        {
            _logger.Error(
                $"[ContextStore] 持久化运行状态已损坏，已隔离到 {Path.GetFileName(quarantinedPath)}。{ex.Message}");
        }
        else
        {
            _logger.Error($"[ContextStore] 持久化运行状态已损坏，且无法完成隔离：{ex.Message}");
        }

        _logger.Warn("[ContextStore] 持久化文件无法恢复，已使用空运行状态启动。");
        RefreshPersistenceDiagnostics();
    }

    private string? TryQuarantineCorruptFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(_persistPath) ?? ".";
            var baseName = Path.GetFileNameWithoutExtension(PersistFileName);
            var extension = Path.GetExtension(PersistFileName);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var candidatePath = Path.Combine(directory, $"{baseName}.corrupt-{timestamp}{extension}");
            var suffix = 0;

            while (File.Exists(candidatePath))
            {
                suffix++;
                candidatePath = Path.Combine(directory, $"{baseName}.corrupt-{timestamp}-{suffix}{extension}");
            }

            File.Move(_persistPath, candidatePath);
            return candidatePath;
        }
        catch (Exception moveEx)
        {
            _logger.Error($"[ContextStore] 隔离损坏运行状态失败：{moveEx.Message}");
            return null;
        }
    }

    private void RefreshPersistenceDiagnostics()
    {
        try
        {
            var directory = Path.GetDirectoryName(_persistPath) ?? ".";
            if (!Directory.Exists(directory))
            {
                UpdatePersistenceDiagnostics(new ProductionContextPersistenceDiagnostics(0, null));
                return;
            }

            var files = Directory.GetFiles(directory, "production_context.corrupt-*.json");
            var lastCorruptDetectedAt = files
                .Select(ParseCorruptTimestamp)
                .Where(x => x.HasValue)
                .Max();

            UpdatePersistenceDiagnostics(new ProductionContextPersistenceDiagnostics(
                CorruptFileCount: files.Length,
                LastCorruptDetectedAt: lastCorruptDetectedAt));
        }
        catch (Exception ex)
        {
            _logger.Warn($"[ContextStore] 刷新持久化诊断失败：{ex.Message}");
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

    private static void CopyRuntimeState(ProductionContext source, ProductionContext target)
    {
        target.DeviceName = source.DeviceName;
        target.NetworkDeviceId = source.NetworkDeviceId;
        target.TodayCapacity = source.TodayCapacity;

        foreach (var entry in source.StepStateEntries)
        {
            target.StepStateEntries[entry.Key] = entry.Value;
        }

        foreach (var entry in source.DeviceBagEntries)
        {
            target.DeviceBagEntries[entry.Key] = entry.Value;
        }

        foreach (var entry in source.CurrentCellEntries)
        {
            target.CurrentCellEntries[entry.Key] = entry.Value;
        }
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

    private static DateTime? ParseCorruptTimestamp(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var match = CorruptFileTimestampPattern.Match(fileName);
        if (match.Success
            && DateTime.TryParseExact(
                match.Groups[1].Value,
                "yyyyMMddHHmmssfff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestampUtc))
        {
            return timestampUtc;
        }

        return File.Exists(path)
            ? File.GetLastWriteTimeUtc(path)
            : null;
    }
}

internal class CellDataBaseConverter : JsonConverter<CellDataBase>
{
    private readonly ICellDataTypeRegistry _cellDataTypeRegistry;

    public CellDataBaseConverter(ICellDataTypeRegistry cellDataTypeRegistry)
    {
        _cellDataTypeRegistry = cellDataTypeRegistry;
    }

    public override CellDataBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var processType = root.TryGetProperty("processType", out var prop)
            ? prop.GetString()
            : null;

        if (processType is null)
                throw new JsonException("CellData 缺少 processType 属性。");

        var json = root.GetRawText();
        var result = _cellDataTypeRegistry.Deserialize(processType, json, options);

        return result ?? throw new JsonException($"Unknown process type: {processType}");
    }

    public override void Write(Utf8JsonWriter writer, CellDataBase value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}

public class ObjectToInferredTypesConverter : JsonConverter<object>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number when reader.TryGetInt32(out var i) => i,
            JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
            JsonTokenType.Number => reader.GetDouble(),
            JsonTokenType.String when reader.TryGetDateTime(out var dt) => dt,
            JsonTokenType.String => reader.GetString()!,
            _ => JsonDocument.ParseValue(ref reader).RootElement.Clone()
        };
    }

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
