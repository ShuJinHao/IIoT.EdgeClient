namespace IIoT.Edge.Presentation.VisualTestData;

internal static class VisualTestScenario
{
    public const string SecondaryDeviceName = "PLC-Homogenization-02";
    public const string RecipeName = "匀浆 A 线试产配方";
    public const string RecipeVersion = "V2.4";
    public const string ProcessName = "匀浆";
    public const string MainPlanCode = "MES-HG-MAIN-20260604-A";

    public static string ResolveBatchCode(VisualTestDataOptions options)
        => string.IsNullOrWhiteSpace(options.BatchCode)
            ? "VT-HG-20260604-01"
            : options.BatchCode.Trim();

    public static VisualTestCapacityMetrics CreateCapacityMetrics(
        VisualTestDataOptions options,
        DateTimeOffset now)
    {
        var minuteOffset = now.Minute % 12;
        var ok = 16840 + minuteOffset * 9;
        var ng = 18 + minuteOffset % 4;
        var total = ok + ng;
        var recentHourOk = 326 + minuteOffset * 3;
        var recentHourNg = minuteOffset % 3;
        var recentHourTotal = recentHourOk + recentHourNg;

        return new VisualTestCapacityMetrics(
            Total: total,
            Ok: ok,
            Ng: ng,
            Yield: FormatYield(ok, total),
            BatchCode: ResolveBatchCode(options),
            RecentHourTotal: recentHourTotal,
            RecentHourOk: recentHourOk,
            RecentHourNg: recentHourNg,
            RecentHourLabel: $"{now.AddHours(-1):HH:mm}-{now:HH:mm}");
    }

    public static string FormatYield(int ok, int total)
        => total > 0 ? $"{ok * 100.0 / total:F1}%" : "0.0%";
}

internal sealed record VisualTestCapacityMetrics(
    int Total,
    int Ok,
    int Ng,
    string Yield,
    string BatchCode,
    int RecentHourTotal,
    int RecentHourOk,
    int RecentHourNg,
    string RecentHourLabel);
