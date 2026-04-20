using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Recipe;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.SharedKernel.DataPipeline.Recipe;
using System.IO;
using System.Text.Json;

namespace IIoT.Edge.Infrastructure.Integration.Recipe;

public class RecipeService : IRecipeService
{
    private readonly ICloudHttpClient _cloudHttp;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly IDeviceService _deviceService;
    private readonly ILogService _logger;
    private readonly string _recipeDir;

    private RecipeData? _cloudRecipe;
    private RecipeData? _localRecipe;
    private RecipeSource _activeSource = RecipeSource.Cloud;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RecipeService(
        ICloudHttpClient cloudHttp,
        ICloudApiEndpointProvider endpointProvider,
        IDeviceService deviceService,
        ILogService logger)
    {
        _cloudHttp = cloudHttp;
        _endpointProvider = endpointProvider;
        _deviceService = deviceService;
        _logger = logger;

        _recipeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "recipe");
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
        _logger.Info($"[Recipe] Source switched to: {source}");
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

    public async Task<bool> PullFromCloudAsync()
    {
        var device = _deviceService.CurrentDevice;
        if (device is null)
        {
            _logger.Warn("[Recipe] Device is not identified yet. Cloud pull skipped.");
            return false;
        }

        if (!_deviceService.CanUploadToCloud)
        {
            _logger.Warn(
                $"[Recipe] Cloud pull skipped because upload gate is blocked ({_deviceService.CurrentUploadGate.Reason.ToReasonCode()}).");
            return false;
        }

        var url = _endpointProvider.BuildRecipeByDevicePath(device.DeviceId);
        var result = await _cloudHttp.GetAsync(url);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Payload))
        {
            _logger.Error($"[Recipe] Cloud pull failed. Outcome:{result.Outcome}, Reason:{result.ReasonCode}");
            return false;
        }

        try
        {
            var recipe = ParseCloudResponse(result.Payload);
            if (recipe is null)
            {
                _logger.Warn("[Recipe] Cloud response was empty or invalid.");
                return false;
            }

            _cloudRecipe = recipe;
            SaveSingleFile(_cloudRecipe, GetCloudFilePath());
            _logger.Info($"[Recipe] Cloud recipe loaded: {recipe.RecipeName} {recipe.Version}, Params:{recipe.Parameters.Count}");
            RecipeChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"[Recipe] Parse failed: {ex.Message}");
            return false;
        }
    }

    public void SetLocalParam(string name, double? min, double? max, string unit)
    {
        _localRecipe ??= new RecipeData
        {
            RecipeName = "Local Emergency Recipe",
            Version = "LOCAL",
            UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        _localRecipe.Parameters[name] = new RecipeParam
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Min = min,
            Max = max,
            Unit = unit
        };

        _localRecipe.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        SaveSingleFile(_localRecipe, GetLocalFilePath());
        _logger.Info($"[Recipe] Local parameter updated: {name} [{min} ~ {max}] {unit}");

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

        if (_localRecipe.Parameters.Remove(name))
        {
            _localRecipe.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            SaveSingleFile(_localRecipe, GetLocalFilePath());
            _logger.Info($"[Recipe] Local parameter removed: {name}");

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
        _logger.Info($"[Recipe] Loaded. Cloud params:{cloudCount}, Local params:{localCount}");
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
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RecipeData>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.Error($"[Recipe] Load file failed {path}: {ex.Message}");
            return null;
        }
    }

    private void SaveSingleFile(RecipeData data, string path)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger.Error($"[Recipe] Save file failed {path}: {ex.Message}");
        }
    }

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
            UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
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
            _logger.Error($"[Recipe] Parse parametersJsonb failed: {ex.Message}");
        }

        return result;
    }
}
