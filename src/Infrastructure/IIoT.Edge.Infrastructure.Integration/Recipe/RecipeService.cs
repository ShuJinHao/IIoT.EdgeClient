using IIoT.Edge.Module.Contracts.Cloud;
﻿using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Application.Common.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Recipe;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Module.Contracts.DataPipeline.Recipe;
using System.IO;
using System.Text.Json;

namespace IIoT.Edge.Infrastructure.Integration.Recipe;

public class RecipeService : IRecipeService
{
    private readonly ICloudHttpClient _cloudHttp;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly IDeviceService _deviceService;
    private readonly ILogService _logger;
    private readonly IRecipePersistenceFileSystem _fileSystem;
    private readonly string _recipeDir;

    private RecipeData? _cloudRecipe;
    private RecipeData? _localRecipe;
    private RecipeSource _activeSource = RecipeSource.Cloud;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string CurrentUtcTimestamp()
        => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

    public RecipeService(
        ICloudHttpClient cloudHttp,
        ICloudApiEndpointProvider endpointProvider,
        IDeviceService deviceService,
        ILogService logger,
        string? recipeDirectory = null)
        : this(
            cloudHttp,
            endpointProvider,
            deviceService,
            logger,
            new RecipePersistenceFileSystem(),
            recipeDirectory)
    {
    }

    internal RecipeService(
        ICloudHttpClient cloudHttp,
        ICloudApiEndpointProvider endpointProvider,
        IDeviceService deviceService,
        ILogService logger,
        IRecipePersistenceFileSystem fileSystem,
        string? recipeDirectory = null)
    {
        _cloudHttp = cloudHttp;
        _endpointProvider = endpointProvider;
        _deviceService = deviceService;
        _logger = logger;
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

        _recipeDir = recipeDirectory
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "recipe");
        Directory.CreateDirectory(_recipeDir);
    }

    public RecipeSource ActiveSource => _activeSource;

    public void SwitchSource(RecipeSource source)
    {
        if (_activeSource == source)
        {
            return;
        }

        _activeSource = source;
        _logger.Info($"[配方] 配方来源已切换为：{source}");
        RecipeChanged?.Invoke();
    }

    public RecipeParam? GetParam(string name)
    {
        var recipe = ActiveRecipe;
        if (recipe is null)
        {
            return null;
        }

        return recipe.Parameters.TryGetValue(name, out var param) ? param : null;
    }

    public IReadOnlyDictionary<string, RecipeParam> GetAllParams()
    {
        var recipe = ActiveRecipe;
        return recipe?.Parameters ?? new Dictionary<string, RecipeParam>();
    }

    public RecipeData? ActiveRecipe => _activeSource == RecipeSource.Cloud ? _cloudRecipe : _localRecipe;
    public RecipeData? CloudRecipe => _cloudRecipe;
    public RecipeData? LocalRecipe => _localRecipe;

    public async Task<bool> PullFromCloudAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = _deviceService.CurrentDevice;
        if (device is null)
        {
            _logger.Warn("[配方] 设备尚未识别，已跳过云端配方拉取。");
            return false;
        }

        if (!_deviceService.CanUploadToCloud)
        {
            _logger.Warn(
                $"[配方] 上传门控已阻塞，已跳过云端配方拉取（{_deviceService.CurrentUploadGate.Reason.ToReasonCode()}）。");
            return false;
        }

        var url = _endpointProvider.BuildRecipeByDevicePath(device.DeviceId);
        var result = await _cloudHttp.GetAsync(
                url,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Payload))
        {
            _logger.Error($"[配方] 云端配方拉取失败。结果：{result.Outcome}，原因：{result.ReasonCode}");
            return false;
        }

        try
        {
            var recipe = ParseCloudResponse(result.Payload);
            if (recipe is null)
            {
                _logger.Warn("[配方] 云端配方响应为空或格式无效。");
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            SaveSingleFile(recipe, GetCloudFilePath());
            _cloudRecipe = recipe;
            _logger.Info($"[配方] 云端配方已加载：{recipe.RecipeName} {recipe.Version}，参数数：{recipe.Parameters.Count}");
            RecipeChanged?.Invoke();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"[配方] 配方解析失败：{ex.Message}");
            return false;
        }
    }

    public void SetLocalParam(string name, double? min, double? max, string unit)
    {
        var candidate = _localRecipe is null
            ? new RecipeData
            {
                RecipeName = "Local Emergency Recipe",
                Version = "LOCAL",
                UpdatedAt = CurrentUtcTimestamp()
            }
            : CloneRecipe(_localRecipe);

        candidate.Parameters[name] = new RecipeParam
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Min = min,
            Max = max,
            Unit = unit
        };

        candidate.UpdatedAt = CurrentUtcTimestamp();
        SaveSingleFile(candidate, GetLocalFilePath());
        _localRecipe = candidate;
        _logger.Info($"[配方] 本地参数已更新：{name} [{min} ~ {max}] {unit}");

        if (_activeSource == RecipeSource.Local)
        {
            RecipeChanged?.Invoke();
        }
    }

    public void RemoveLocalParam(string name)
    {
        if (_localRecipe is null)
        {
            return;
        }

        var candidate = CloneRecipe(_localRecipe);
        if (candidate.Parameters.Remove(name))
        {
            candidate.UpdatedAt = CurrentUtcTimestamp();
            SaveSingleFile(candidate, GetLocalFilePath());
            _localRecipe = candidate;
            _logger.Info($"[配方] 本地参数已删除：{name}");

            if (_activeSource == RecipeSource.Local)
            {
                RecipeChanged?.Invoke();
            }
        }
    }

    public void LoadFromFile()
    {
        _cloudRecipe = LoadSingleFile(GetCloudFilePath());
        _localRecipe = LoadSingleFile(GetLocalFilePath());

        var cloudCount = _cloudRecipe?.Parameters.Count ?? 0;
        var localCount = _localRecipe?.Parameters.Count ?? 0;
        _logger.Info($"[配方] 配方已加载。云端参数数：{cloudCount}，本地参数数：{localCount}");
    }

    public void SaveToFile()
    {
        if (_cloudRecipe is not null)
        {
            SaveSingleFile(_cloudRecipe, GetCloudFilePath());
        }

        if (_localRecipe is not null)
        {
            SaveSingleFile(_localRecipe, GetLocalFilePath());
        }
    }

    public event Action? RecipeChanged;

    private string GetCloudFilePath() => Path.Combine(_recipeDir, "cloud_recipe.json");
    private string GetLocalFilePath() => Path.Combine(_recipeDir, "local_recipe.json");

    private RecipeData? LoadSingleFile(string path)
    {
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        try
        {
            var json = _fileSystem.ReadAllText(path);
            return JsonSerializer.Deserialize<RecipeData>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.Error($"[配方] 读取配方文件失败 {path}：{ex.Message}");
            return null;
        }
    }

    private void SaveSingleFile(RecipeData data, string path)
    {
        var tempPath = path + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            _fileSystem.WriteAllText(tempPath, json);
            _fileSystem.ReplaceFile(tempPath, path);
        }
        catch (Exception ex)
        {
            var cleanup = CleanupTempFile(tempPath);
            var message = $"[配方] 原子保存配方文件失败 {Path.GetFileName(path)}：{ex.Message}。{cleanup}";
            _logger.Error(message);
            throw new RecipePersistenceException(message, ex);
        }
    }

    private string CleanupTempFile(string tempPath)
    {
        try
        {
            if (!_fileSystem.FileExists(tempPath))
                return "临时文件清理：无残留 .tmp 文件。";

            _fileSystem.DeleteFile(tempPath);
            return "临时文件清理：已删除残留 .tmp 文件。";
        }
        catch (Exception ex)
        {
            return $"临时文件清理：删除失败（{ex.Message}）。";
        }
    }

    private static RecipeData CloneRecipe(RecipeData source)
        => new()
        {
            RecipeId = source.RecipeId,
            RecipeName = source.RecipeName,
            Version = source.Version,
            ProcessName = source.ProcessName,
            Status = source.Status,
            UpdatedAt = source.UpdatedAt,
            Parameters = source.Parameters.ToDictionary(
                static pair => pair.Key,
                static pair => new RecipeParam
                {
                    Id = pair.Value.Id,
                    Name = pair.Value.Name,
                    Min = pair.Value.Min,
                    Max = pair.Value.Max,
                    Unit = pair.Value.Unit,
                    CustomValue = pair.Value.CustomValue
                },
                source.Parameters.Comparer)
        };

    private RecipeData? ParseCloudResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        JsonElement recipeArray;
        if (root.ValueKind == JsonValueKind.Array)
        {
            recipeArray = root;
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("value", out var valEl) && valEl.ValueKind == JsonValueKind.Array)
        {
            recipeArray = valEl;
        }
        else
        {
            return null;
        }

        if (recipeArray.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement? activeElement = null;
        foreach (var item in recipeArray.EnumerateArray())
        {
            if (item.TryGetProperty("status", out var statusEl) && statusEl.GetString() == "Active")
            {
                activeElement = item;
                break;
            }
        }

        var recipeEl = activeElement ?? recipeArray[0];
        var recipe = new RecipeData
        {
            UpdatedAt = CurrentUtcTimestamp()
        };

        if (recipeEl.TryGetProperty("id", out var idEl)) recipe.RecipeId = idEl.GetString() ?? string.Empty;
        if (recipeEl.TryGetProperty("recipeName", out var nameEl)) recipe.RecipeName = nameEl.GetString() ?? string.Empty;
        if (recipeEl.TryGetProperty("version", out var verEl)) recipe.Version = verEl.GetString() ?? string.Empty;
        if (recipeEl.TryGetProperty("status", out var statEl)) recipe.Status = statEl.GetString() ?? string.Empty;

        if (recipeEl.TryGetProperty("parametersJsonb", out var paramsEl))
        {
            var paramsJson = paramsEl.GetString();
            if (!string.IsNullOrEmpty(paramsJson))
            {
                recipe.Parameters = ParseParametersJsonb(paramsJson);
            }
        }

        return recipe;
    }

    private Dictionary<string, RecipeParam> ParseParametersJsonb(string json)
    {
        var result = new Dictionary<string, RecipeParam>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var param = new RecipeParam();
                if (item.TryGetProperty("id", out var idEl)) param.Id = idEl.GetString() ?? string.Empty;
                if (item.TryGetProperty("name", out var nameEl)) param.Name = nameEl.GetString() ?? string.Empty;
                if (item.TryGetProperty("min", out var minEl) && minEl.ValueKind == JsonValueKind.Number) param.Min = minEl.GetDouble();
                if (item.TryGetProperty("max", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number) param.Max = maxEl.GetDouble();
                if (item.TryGetProperty("unit", out var unitEl)) param.Unit = unitEl.GetString() ?? string.Empty;

                if (!string.IsNullOrEmpty(param.Name))
                {
                    result[param.Name] = param;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[配方] 解析 parametersJsonb 失败：{ex.Message}");
        }

        return result;
    }
}
