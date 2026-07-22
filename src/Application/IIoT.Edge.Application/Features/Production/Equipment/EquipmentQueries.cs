using IIoT.Edge.Module.Contracts.Context;
using IIoT.Edge.Module.Contracts.Plc;
using IIoT.Edge.Module.Contracts.Recipe;
using IIoT.Edge.Application.Features.Hardware.Queries;
using MediatR;

namespace IIoT.Edge.Application.Features.Production.Equipment;

public record HardwareSnapshot(
    string Name,
    string Address,
    string DeviceType,
    bool IsConnected);

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
    string RecentHourLabel);

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
                    isConnected);
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

public class GetCapacitySnapshotHandler(IProductionContextStore contextStore)
    : IRequestHandler<GetCapacitySnapshotQuery, CapacitySnapshot>
{
    public Task<CapacitySnapshot> Handle(
        GetCapacitySnapshotQuery request,
        CancellationToken cancellationToken)
    {
        var ok = 0;
        var ng = 0;
        var recentHourOk = 0;
        var recentHourNg = 0;
        var currentBatch = "--";
        var recentHourSlots = ResolveRecentHourSlots(DateTime.Now);

        foreach (var context in contextStore.GetAll())
        {
            ok += context.TodayCapacity.OkAll;
            ng += context.TodayCapacity.NgAll;
            foreach (var bucket in context.TodayCapacity.HalfHourly.Where(bucket => recentHourSlots.Contains(bucket.SlotIndex)))
            {
                recentHourOk += bucket.OkCount;
                recentHourNg += bucket.NgCount;
            }

            if (context.DeviceBag.TryGetValue("CurrentBatch", out var batchValue) &&
                batchValue is string batch)
            {
                currentBatch = batch;
            }
        }

        var total = ok + ng;
        var yield = total > 0 ? $"{ok * 100.0 / total:F1}%" : "0.0%";
        var recentHourTotal = recentHourOk + recentHourNg;

        return Task.FromResult(new CapacitySnapshot(
            total,
            ok,
            ng,
            yield,
            currentBatch,
            recentHourTotal,
            recentHourOk,
            recentHourNg,
            BuildRecentHourLabel(DateTime.Now)));
    }

    private static HashSet<int> ResolveRecentHourSlots(DateTime now)
    {
        var currentSlot = now.Hour * 2 + (now.Minute >= 30 ? 1 : 0);
        return
        [
            currentSlot,
            (currentSlot + 47) % 48
        ];
    }

    private static string BuildRecentHourLabel(DateTime now)
    {
        var start = now.AddHours(-1);
        return $"{start:HH:mm}-{now:HH:mm}";
    }
}
