using System.Globalization;
using IIoT.Edge.Module.Contracts.Time;

namespace IIoT.Edge.Application.Common.Time;

/// <summary>
/// 按配置时区统一处理生产业务时间。
/// </summary>
public sealed class ProductionTimeProvider : IProductionTimeProvider
{
    private static readonly IReadOnlyDictionary<string, string> TimeZoneAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Asia/Shanghai"] = "China Standard Time",
            ["China Standard Time"] = "Asia/Shanghai"
        };

    public ProductionTimeProvider(ProductionTimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        BusinessTimeZone = ResolveTimeZone(options.TimeZoneId);
    }

    public TimeZoneInfo BusinessTimeZone { get; }

    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime BusinessNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, BusinessTimeZone);

    public DateTime ToUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => TimeZoneInfo.ConvertTimeToUtc(value, BusinessTimeZone)
        };
    }

    public DateTime ToBusinessTime(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => TimeZoneInfo.ConvertTimeToUtc(value, BusinessTimeZone)
        };

        return TimeZoneInfo.ConvertTimeFromUtc(utc, BusinessTimeZone);
    }

    public string FormatBusinessTimestamp(DateTime value)
        => ToBusinessTime(value).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static bool IsTimeZoneAvailable(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return false;

        try
        {
            _ = ResolveTimeZone(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException) when (TimeZoneAliases.TryGetValue(timeZoneId, out var alias))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(alias);
        }
        catch (InvalidTimeZoneException) when (TimeZoneAliases.TryGetValue(timeZoneId, out var alias))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(alias);
        }
    }
}
