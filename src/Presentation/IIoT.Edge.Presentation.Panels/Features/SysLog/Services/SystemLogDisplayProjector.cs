using System.Text.RegularExpressions;
using IIoT.Edge.Application.Abstractions.Logging;

namespace IIoT.Edge.Presentation.Panels.Features.SysLog;

public interface ISystemLogDisplayProjector
{
    IReadOnlyList<LogEntry> BuildAggregatedEntries(IEnumerable<LogEntry> entries, int limit = 200);

    IReadOnlyList<LogEntry> BuildDeviceEntries(IEnumerable<LogEntry> entries, string deviceName, int limit = 200);

    IReadOnlyList<string> ExtractDeviceNames(IEnumerable<LogEntry> entries);
}

public sealed class SystemLogDisplayProjector : ISystemLogDisplayProjector
{
    private static readonly Regex DeviceLogPattern = new(
        @"^\[(?<device>[^\]]+)\]\s*(?<body>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SignalPattern = new(
        @"(?:Read|读取)\s*(?<signal>[A-Za-z]+[0-9]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public IReadOnlyList<LogEntry> BuildAggregatedEntries(IEnumerable<LogEntry> entries, int limit = 200)
    {
        var groups = new Dictionary<string, PlcFailureAggregation>(StringComparer.OrdinalIgnoreCase);
        var regularEntries = new List<LogEntry>();

        foreach (var entry in entries)
        {
            if (TryCreatePlcFailureItem(entry, out var failure))
            {
                var key = $"{failure.LineName}|{failure.Kind}";
                if (!groups.TryGetValue(key, out var aggregation))
                {
                    aggregation = new PlcFailureAggregation(failure.LineName, failure.Kind);
                    groups[key] = aggregation;
                }

                aggregation.Add(failure, entry);
                continue;
            }

            regularEntries.Add(entry);
        }

        return groups.Values
            .Select(static group => group.ToLogEntry())
            .Concat(regularEntries)
            .OrderByDescending(static entry => entry.Time)
            .Take(limit)
            .ToArray();
    }

    public IReadOnlyList<LogEntry> BuildDeviceEntries(IEnumerable<LogEntry> entries, string deviceName, int limit = 200)
        => entries
            .Where(entry => IsDeviceEntry(entry, deviceName))
            .Take(limit)
            .ToArray();

    public IReadOnlyList<string> ExtractDeviceNames(IEnumerable<LogEntry> entries)
        => entries
            .Select(static entry => TryExtractDeviceName(entry.Message, out var deviceName) ? deviceName : null)
            .Where(static deviceName => !string.IsNullOrWhiteSpace(deviceName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static deviceName => deviceName, StringComparer.OrdinalIgnoreCase)
            .Select(static deviceName => deviceName!)
            .ToArray();

    private static bool IsDeviceEntry(LogEntry entry, string deviceName)
        => TryExtractDeviceName(entry.Message, out var actual)
           && string.Equals(actual, deviceName, StringComparison.OrdinalIgnoreCase);

    private static bool TryCreatePlcFailureItem(LogEntry entry, out PlcFailureItem item)
    {
        item = default;
        var match = DeviceLogPattern.Match(entry.Message);
        if (!match.Success)
        {
            return false;
        }

        var deviceName = match.Groups["device"].Value.Trim();
        var body = match.Groups["body"].Value.Trim();
        if (!IsPlcFailureBody(body))
        {
            return false;
        }

        item = new PlcFailureItem(
            deviceName,
            ResolveLineName(deviceName),
            ResolveFailureKind(body),
            ResolveSignal(body),
            ResolveRecentError(body),
            ResolveSeverity(entry.Level));
        return true;
    }

    private static bool TryExtractDeviceName(string message, out string deviceName)
    {
        var match = DeviceLogPattern.Match(message ?? string.Empty);
        deviceName = match.Success ? match.Groups["device"].Value.Trim() : string.Empty;
        return !string.IsNullOrWhiteSpace(deviceName);
    }

    private static bool IsPlcFailureBody(string body)
    {
        var mentionsPlc = body.Contains("PLC", StringComparison.OrdinalIgnoreCase);
        var mentionsReadFailure = body.Contains("读取", StringComparison.OrdinalIgnoreCase)
                                  && body.Contains("失败", StringComparison.OrdinalIgnoreCase);
        var mentionsEnglishReadFailure = body.Contains("Read", StringComparison.OrdinalIgnoreCase)
                                         && body.Contains("failed", StringComparison.OrdinalIgnoreCase);
        var mentionsFailure = body.Contains("失败", StringComparison.OrdinalIgnoreCase)
                              || body.Contains("异常", StringComparison.OrdinalIgnoreCase)
                              || body.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                              || body.Contains("timed out", StringComparison.OrdinalIgnoreCase);

        return (mentionsPlc && mentionsFailure)
               || mentionsReadFailure
               || mentionsEnglishReadFailure;
    }

    private static string ResolveLineName(string deviceName)
    {
        if (deviceName.StartsWith("P1-AP", StringComparison.OrdinalIgnoreCase))
        {
            return "负极模切";
        }

        if (deviceName.StartsWith("P2-CP", StringComparison.OrdinalIgnoreCase))
        {
            return "正极模切";
        }

        return "PLC";
    }

    private static string ResolveFailureKind(string body)
    {
        if (body.Contains("连接", StringComparison.OrdinalIgnoreCase)
            || body.Contains("connect", StringComparison.OrdinalIgnoreCase))
        {
            return "Connection";
        }

        return "Read";
    }

    private static string ResolveSignal(string body)
    {
        var match = SignalPattern.Match(body);
        return match.Success ? match.Groups["signal"].Value.ToUpperInvariant() : string.Empty;
    }

    private static string ResolveRecentError(string body)
    {
        var separators = new[] { '：', ':' };
        var index = body.LastIndexOfAny(separators);
        if (index >= 0 && index < body.Length - 1)
        {
            return body[(index + 1)..].Trim();
        }

        return body;
    }

    private static int ResolveSeverity(string level)
        => level.ToUpperInvariant() switch
        {
            "FATAL" => 4,
            "ERROR" => 3,
            "WARN" => 2,
            "INFO" => 1,
            _ => 0
        };

    private readonly record struct PlcFailureItem(
        string DeviceName,
        string LineName,
        string Kind,
        string Signal,
        string RecentError,
        int Severity);

    private sealed class PlcFailureAggregation
    {
        private readonly HashSet<string> _devices = new(StringComparer.OrdinalIgnoreCase);

        public PlcFailureAggregation(string lineName, string kind)
        {
            LineName = lineName;
            Kind = kind;
        }

        public string LineName { get; }

        public string Kind { get; }

        public DateTime LatestTime { get; private set; } = DateTime.MinValue;

        public string Signal { get; private set; } = string.Empty;

        public string RecentError { get; private set; } = string.Empty;

        public int Severity { get; private set; }

        public void Add(PlcFailureItem item, LogEntry source)
        {
            _devices.Add(item.DeviceName);
            if (source.Time >= LatestTime)
            {
                LatestTime = source.Time;
                Signal = item.Signal;
                RecentError = item.RecentError;
            }

            Severity = Math.Max(Severity, item.Severity);
        }

        public LogEntry ToLogEntry()
        {
            var signalPart = string.IsNullOrWhiteSpace(Signal)
                ? string.Empty
                : $"，失败信号 {Signal}";
            var action = Kind == "Connection" ? "未连接" : "读取失败";
            var error = string.IsNullOrWhiteSpace(RecentError) ? action : RecentError;

            return new LogEntry
            {
                Time = LatestTime == DateTime.MinValue ? DateTime.Now : LatestTime,
                Level = Severity >= 3 ? "ERROR" : "WARN",
                Message = $"{LineName}采样异常：{_devices.Count} 台 PLC {action}{signalPart}，最近错误 {error}，MES 上传已暂停。"
            };
        }
    }
}
