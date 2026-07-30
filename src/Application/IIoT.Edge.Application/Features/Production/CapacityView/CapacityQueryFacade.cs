using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Device;
using MediatR;

namespace IIoT.Edge.Application.Features.Production.CapacityView;

/// <summary>
/// 产能查询 facade 契约。
/// 提供联网状态和产能查询能力。
/// </summary>
public interface ICapacityQueryFacade
{
    event Action<EdgeUploadGateSnapshot>? UploadGateChanged;

    bool IsOnline { get; }

    Task<CapacityViewResult> LoadTodayAsync(string plcName, CancellationToken cancellationToken = default);

    Task<CapacityViewResult> LoadTodayAsync(
        CapacityPlcQueryScope scope,
        CancellationToken cancellationToken = default)
        => LoadTodayAsync(scope.GetQueryKeys().FirstOrDefault() ?? string.Empty, cancellationToken);

    Task<CapacityViewResult> QueryHistoryAsync(string queryMode, DateTime queryDate, string plcName, CancellationToken cancellationToken = default);

    Task<CapacityViewResult> QueryHistoryAsync(
        string queryMode,
        DateTime queryDate,
        CapacityPlcQueryScope scope,
        CancellationToken cancellationToken = default)
        => QueryHistoryAsync(
            queryMode,
            queryDate,
            scope.GetQueryKeys().FirstOrDefault() ?? string.Empty,
            cancellationToken);
}

public sealed record CapacityPlcQueryScope(
    bool IsAggregate,
    string PlcCode,
    IReadOnlyList<string> DeviceNameAliases)
{
    public static CapacityPlcQueryScope Aggregate { get; } = new(true, string.Empty, []);

    public static CapacityPlcQueryScope ForPlc(
        string? plcCode,
        IEnumerable<string>? deviceNameAliases = null)
        => new(
            false,
            plcCode?.Trim() ?? string.Empty,
            (deviceNameAliases ?? [])
            .Select(static name => name?.Trim() ?? string.Empty)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());

    public IReadOnlyList<string> GetQueryKeys()
    {
        if (IsAggregate)
        {
            return [string.Empty];
        }

        if (string.IsNullOrWhiteSpace(PlcCode))
        {
            return [];
        }

        return new[] { PlcCode.Trim() }
            .Concat(DeviceNameAliases)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>
/// 产能查询 facade。
/// 负责衔接设备上下文、联网状态与产能查询用例。
/// </summary>
public sealed class CapacityQueryFacade(
    ISender sender,
    IDeviceService deviceService) : ICapacityQueryFacade
{
    public event Action<EdgeUploadGateSnapshot>? UploadGateChanged
    {
        add => deviceService.UploadGateChanged += value;
        remove => deviceService.UploadGateChanged -= value;
    }

    public bool IsOnline => deviceService.CanUploadToCloud;

    public async Task<CapacityViewResult> LoadTodayAsync(string plcName, CancellationToken cancellationToken = default)
        => await LoadTodayCoreAsync([plcName], cancellationToken).ConfigureAwait(false);

    public async Task<CapacityViewResult> LoadTodayAsync(
        CapacityPlcQueryScope scope,
        CancellationToken cancellationToken = default)
        => await LoadTodayCoreAsync(scope.GetQueryKeys(), cancellationToken).ConfigureAwait(false);

    private async Task<CapacityViewResult> LoadTodayCoreAsync(
        IReadOnlyList<string> queryKeys,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!deviceService.CanUploadToCloud)
        {
            return CapacityViewResult.Unavailable(CapacityQueryReasonCodes.CloudGateNotReady);
        }

        var deviceId = deviceService.CurrentDevice?.DeviceId;
        if (deviceId is null)
        {
            return CapacityViewResult.Unavailable(CapacityQueryReasonCodes.DeviceNotIdentified);
        }

        if (queryKeys.Count == 0)
        {
            return CapacityViewResult.Unavailable(CapacityQueryReasonCodes.PlcIdentityUnresolved);
        }

        var results = new List<CapacityViewResult>(queryKeys.Count);
        foreach (var queryKey in queryKeys)
        {
            results.Add(await sender.Send(
                    new LoadTodayCapacityQuery(deviceId.Value, DateTime.Now, queryKey),
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return MergeAliasResults(results, divisor: 1);
    }

    public async Task<CapacityViewResult> QueryHistoryAsync(
        string queryMode,
        DateTime queryDate,
        string plcName,
        CancellationToken cancellationToken = default)
        => await QueryHistoryCoreAsync(
                queryMode,
                queryDate,
                [plcName],
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<CapacityViewResult> QueryHistoryAsync(
        string queryMode,
        DateTime queryDate,
        CapacityPlcQueryScope scope,
        CancellationToken cancellationToken = default)
        => await QueryHistoryCoreAsync(
                queryMode,
                queryDate,
                scope.GetQueryKeys(),
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<CapacityViewResult> QueryHistoryCoreAsync(
        string queryMode,
        DateTime queryDate,
        IReadOnlyList<string> queryKeys,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!deviceService.CanUploadToCloud)
        {
            return CapacityViewResult.Unavailable(CapacityQueryReasonCodes.CloudGateNotReady);
        }

        var deviceId = deviceService.CurrentDevice?.DeviceId;
        if (deviceId is null)
        {
            return CapacityViewResult.Unavailable(CapacityQueryReasonCodes.DeviceNotIdentified);
        }

        if (queryKeys.Count == 0)
        {
            return CapacityViewResult.Unavailable(CapacityQueryReasonCodes.PlcIdentityUnresolved);
        }

        var results = new List<CapacityViewResult>(queryKeys.Count);
        foreach (var queryKey in queryKeys)
        {
            results.Add(await sender.Send(
                    new QueryCapacityHistoryQuery(
                        deviceId.Value,
                        queryMode,
                        queryDate,
                        queryKey),
                    cancellationToken)
                .ConfigureAwait(false));
        }

        var divisor = string.Equals(queryMode, CapacityQueryModes.Year, StringComparison.Ordinal)
            ? 12
            : 0;
        return MergeAliasResults(results, divisor);
    }

    private static CapacityViewResult MergeAliasResults(
        IReadOnlyList<CapacityViewResult> results,
        int divisor)
    {
        var failure = results.FirstOrDefault(result =>
            result.State is CapacityQueryState.Unavailable or CapacityQueryState.InvalidPayload);
        if (failure is not null)
        {
            return failure;
        }

        var mergedRows = results
            .SelectMany((result, queryPriority) => result.Rows.Select(row => new
            {
                Row = row,
                QueryPriority = queryPriority
            }))
            .GroupBy(item => (
                item.Row.Date,
                item.Row.DateFull,
                item.Row.DayOfWeek))
            .Select(group => group
                .OrderByDescending(static item => item.Row.Total)
                .ThenBy(static item => item.QueryPriority)
                .First()
                .Row)
            .OrderBy(static row => row.DateFull, StringComparer.Ordinal)
            .ToArray();

        if (mergedRows.Length == 0)
        {
            return CapacityViewResult.Empty();
        }

        return CapacityQueryHelper.ToResult(
            CapacityQueryResult<IReadOnlyList<DailyCapacitySnapshot>>.Success(mergedRows),
            divisor > 0 ? divisor : mergedRows.Length);
    }
}
