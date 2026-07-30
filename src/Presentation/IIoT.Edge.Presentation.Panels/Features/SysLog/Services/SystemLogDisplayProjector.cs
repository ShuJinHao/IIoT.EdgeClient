using System.Text.RegularExpressions;
using IIoT.Edge.Module.Contracts.Logging;

namespace IIoT.Edge.Presentation.Panels.Features.SysLog;

public interface ISystemLogDisplayProjector
{
    IReadOnlyList<LogEntry> BuildAggregatedEntries(IEnumerable<LogEntry> entries, int limit = 200);

    IReadOnlyList<LogEntry> BuildDeviceEntries(IEnumerable<LogEntry> entries, string plcCode, int limit = 200);

    IReadOnlyList<string> ExtractDeviceNames(IEnumerable<LogEntry> entries);
}

public sealed class SystemLogDisplayProjector : ISystemLogDisplayProjector
{
    private static readonly Regex StablePlcLogPattern = new(
        @"^\[PlcCode=(?<device>[^\]]+)\]\s*(?<body>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex LegacyPlcLogPattern = new(
        @"^\[(?<device>PLC-[^\]]+)\]\s*(?<body>.+)$",
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

    public IReadOnlyList<LogEntry> BuildDeviceEntries(IEnumerable<LogEntry> entries, string plcCode, int limit = 200)
        => entries
            .Where(entry => IsDeviceEntry(entry, plcCode))
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

    private static bool IsDeviceEntry(LogEntry entry, string plcCode)
        => TryExtractDeviceName(entry.Message, out var actual)
           && string.Equals(actual, plcCode, StringComparison.OrdinalIgnoreCase);

    private static bool TryCreatePlcFailureItem(LogEntry entry, out PlcFailureItem item)
    {
        item = default;
        if (!TryMatchPlcLog(entry.Message, out var plcCode, out var body))
        {
            return false;
        }

        if (!IsPlcFailureBody(body))
        {
            return false;
        }

        item = new PlcFailureItem(
            plcCode,
            ResolveLineName(plcCode),
            ResolveFailureKind(body),
            ResolveSignal(body),
            ResolveRecentError(body),
            ResolveSeverity(entry.Level));
        return true;
    }

    private static bool TryExtractDeviceName(string message, out string deviceName)
    {
        var matched = TryMatchPlcLog(message, out var plcCode, out _);
        deviceName = plcCode;
        return matched;
    }

    private static bool TryMatchPlcLog(
        string? message,
        out string plcCode,
        out string body)
    {
        var text = message ?? string.Empty;
        var match = StablePlcLogPattern.Match(text);
        if (!match.Success)
        {
            match = LegacyPlcLogPattern.Match(text);
        }

        plcCode = match.Success ? match.Groups["device"].Value.Trim() : string.Empty;
        body = match.Success ? match.Groups["body"].Value.Trim() : string.Empty;
        return !string.IsNullOrWhiteSpace(plcCode);
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

    private static string ResolveLineName(string plcCode)
    {
        _ = plcCode;
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
