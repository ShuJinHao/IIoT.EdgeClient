namespace IIoT.Edge.Application.Abstractions.Time;

/// <summary>
/// 生产业务时间入口。
/// 技术过期时间可以继续使用 UTC；对外业务时间必须通过本接口按配置时区转换。
/// </summary>
public interface IProductionTimeProvider
{
    TimeZoneInfo BusinessTimeZone { get; }

    DateTime UtcNow { get; }

    DateTime BusinessNow { get; }

    DateTime ToUtc(DateTime value);

    DateTime ToBusinessTime(DateTime value);

    string FormatBusinessTimestamp(DateTime value);
}
