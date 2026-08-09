using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Production;
using IIoT.Edge.Module.Contracts.Recipe;
using IIoT.Edge.Module.Contracts.Time;
using IIoT.Edge.Module.Contracts.UI;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Common.Identity;
using MediatR;

namespace IIoT.Edge.Application.Features.Production.Equipment;

public record HardwareSnapshot(
    string Name,
    string Address,
    string DeviceType,
    bool IsConnected)
{
    public string PlcCode { get; init; } = string.Empty;
}

public record RecipeSnapshot(
    string RecipeName,
    string RecipeVersion,
    string ProcessName,
    bool IsRecipeActive,
    List<RecipeParamSnapshot> Parameters);

public record RecipeParamSnapshot(
    string ParamName,
    string CurrentValue,
    string MinValue,
    string MaxValue,
    string Unit,
    string WarnLow,
    string WarnHigh);

public record CapacitySnapshot(
    int TodayOutput,
    int OkCount,
    int NgCount,
    string TodayYield,
    string CurrentBatch,
    int RecentHourOutput,
    int RecentHourOk,
    int RecentHourNg,
    string RecentHourLabel)
{
    public bool IsAvailable { get; init; } = true;

    public string? UnavailableReason { get; init; }
}

public record GetHardwareStatusQuery() : IRequest<List<HardwareSnapshot>>;

public record GetRecipeSnapshotQuery() : IRequest<RecipeSnapshot?>;

public record GetCapacitySnapshotQuery() : IRequest<CapacitySnapshot>;

public class GetHardwareStatusHandler(
    ISender sender,
    IPlcConnectionManager plcManager)
    : IRequestHandler<GetHardwareStatusQuery, List<HardwareSnapshot>>
{
    public async Task<List<HardwareSnapshot>> Handle(
        GetHardwareStatusQuery request,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetAllNetworkDevicesQuery(), cancellationToken);
        if (!result.IsSuccess || result.Value is null)
            return new List<HardwareSnapshot>();

        return result.Value
            .Select(device =>
            {
                var isConnected = plcManager.GetRuntimeStatus(device.Id)?.IsConnected == true;

                return new HardwareSnapshot(
                    device.DeviceName,
                    device.IpAddress,
                    device.DeviceType.ToString(),
                    isConnected)
                {
                    PlcCode = device.PlcCode
                };
            })
            .ToList();
    }
}

public class GetRecipeSnapshotHandler(IRecipeService recipeService)
    : IRequestHandler<GetRecipeSnapshotQuery, RecipeSnapshot?>
{
    public Task<RecipeSnapshot?> Handle(
        GetRecipeSnapshotQuery request,
        CancellationToken cancellationToken)
    {
        var recipe =
            recipeService.CloudRecipe ??
            recipeService.LocalRecipe ??
            recipeService.ActiveRecipe;

        if (recipe is null)
            return Task.FromResult<RecipeSnapshot?>(null);

        var tag = recipe == recipeService.CloudRecipe
            ? "Cloud"
            : recipe == recipeService.LocalRecipe
                ? "Local"
                : string.Empty;

        var displayName = string.IsNullOrEmpty(tag)
            ? recipe.RecipeName
            : $"{recipe.RecipeName} ({tag})";

        var parameters = recipe.Parameters.Values
            .Select(parameter => new RecipeParamSnapshot(
                parameter.Name,
                parameter.CustomValue ?? "--",
                parameter.Min?.ToString("G4") ?? "--",
                parameter.Max?.ToString("G4") ?? "--",
                parameter.Unit,
                parameter.Min.HasValue ? (parameter.Min * 1.05)?.ToString("G4") ?? "--" : "--",
                parameter.Max.HasValue ? (parameter.Max * 0.95)?.ToString("G4") ?? "--" : "--"))
            .ToList();

        return Task.FromResult<RecipeSnapshot?>(
            new RecipeSnapshot(
                displayName,
                recipe.Version,
                recipe.ProcessName,
                recipe.Status == "Active",
                parameters));
    }
}

public class GetCapacitySnapshotHandler(
    IEnumerable<IModuleProductionRecordSummarySource> summarySources,
    IProductionTimeProvider productionTime,
    IPlcDeviceSelectionContext deviceSelectionContext)
    : IRequestHandler<GetCapacitySnapshotQuery, CapacitySnapshot>
{
    public async Task<CapacitySnapshot> Handle(
        GetCapacitySnapshotQuery request,
        CancellationToken cancellationToken)
    {
        var utcNow = EnsureUtc(productionTime.UtcNow);
        var businessNow = productionTime.ToBusinessTime(utcNow);
        var businessDayStart = DateTime.SpecifyKind(businessNow.Date, DateTimeKind.Unspecified);
        var rangeStartUtc = EnsureUtc(productionTime.ToUtc(businessDayStart));
        var rangeEndUtc = EnsureUtc(productionTime.ToUtc(businessDayStart.AddDays(1)));
        var recentWindowStartUtc = utcNow.AddHours(-1);
        var selectedIdentity = string.IsNullOrWhiteSpace(deviceSelectionContext.SelectedPlcCode)
            ? deviceSelectionContext.SelectedDeviceKey
            : deviceSelectionContext.SelectedPlcCode;
        var query = new ProductionRecordSummaryQuery(
            rangeStartUtc,
            rangeEndUtc,
            recentWindowStartUtc,
            selectedIdentity);
        var summaries = new List<ModuleProductionRecordSummary>();

        foreach (var source in summarySources.OrderBy(
                     static source => source.ModuleId,
                     StringComparer.OrdinalIgnoreCase))
        {
            summaries.Add(await source.QueryAsync(query, cancellationToken).ConfigureAwait(false));
        }

        if (summaries.Count == 0)
        {
            return new CapacitySnapshot(
                0,
                0,
                0,
                "--",
                "--",
                0,
                0,
                0,
                BuildRecentHourLabel(businessNow))
            {
                IsAvailable = false,
                UnavailableReason = "cloud_history_only"
            };
        }

        var ok = summaries.Sum(static summary => summary.TodayOk);
        var ng = summaries.Sum(static summary => summary.TodayNg);
        var recentHourOk = summaries.Sum(static summary => summary.RecentOk);
        var recentHourNg = summaries.Sum(static summary => summary.RecentNg);
        var currentBatch = "--";
        foreach (var summary in summaries)
        {
            if (!string.IsNullOrWhiteSpace(summary.CurrentBatch))
            {
                currentBatch = summary.CurrentBatch;
                break;
            }
        }

        var total = ok + ng;
        var yield = total > 0 ? $"{ok * 100.0 / total:F1}%" : "0.0%";
        var recentHourTotal = recentHourOk + recentHourNg;

        return new CapacitySnapshot(
            ToDisplayCount(total),
            ToDisplayCount(ok),
            ToDisplayCount(ng),
            yield,
            currentBatch,
            ToDisplayCount(recentHourTotal),
            ToDisplayCount(recentHourOk),
            ToDisplayCount(recentHourNg),
            BuildRecentHourLabel(businessNow));
    }

    private static string BuildRecentHourLabel(DateTime now)
    {
        var start = now.AddHours(-1);
        return $"{start:HH:mm}-{now:HH:mm}";
    }

    private static int ToDisplayCount(long value)
        => (int)Math.Clamp(value, 0L, int.MaxValue);

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
