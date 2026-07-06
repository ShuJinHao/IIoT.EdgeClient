using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Infrastructure.Integration;

internal static class UploadDiagnosticsContextFactory
{
    public static CloudUploadDiagnosticsContext CreateCloudContext(IEnumerable<CellCompletedRecord> records)
    {
        var recordList = records.ToList();
        return new CloudUploadDiagnosticsContext(
            DeviceName: ResolveLogDeviceName(recordList),
            ModuleId: ResolveSingle(recordList.Select(record => record.ModuleId)),
            TaskKey: ResolveSingle(recordList.Select(record => record.TaskKey)),
            Scenario: ResolveScenario(recordList));
    }

    public static MesUploadDiagnosticsContext CreateMesContext(CellCompletedRecord record)
        => new(
            DeviceName: record.ResolveDeviceName(),
            ModuleId: record.ModuleId,
            TaskKey: record.TaskKey,
            Scenario: ResolveScenario(record));

    public static string ResolveLogDeviceName(IEnumerable<CellCompletedRecord> records)
    {
        var deviceNames = records
            .Select(record => record.ResolveDeviceName())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return deviceNames.Count == 1 ? deviceNames[0] : "多PLC";
    }

    public static UploadRecordSourceKey CreateSourceKey(CellCompletedRecord record)
        => new(record.ResolveNetworkDeviceId(), record.ResolveDeviceName().Trim());

    public static bool IsDeviceStatusRecord(CellCompletedRecord record)
    {
        var recordKind = DataPipelineUploadScenarioResolver.TryReadRecordKind(record.CellData);
        return DataPipelineUploadScenarioResolver.IsDeviceStatus(record.TaskKey, recordKind, record.CellData.ProcessType);
    }

    private static string? ResolveSingle(IEnumerable<string?> values)
    {
        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinct.Count == 1 ? distinct[0] : null;
    }

    private static string? ResolveScenario(IReadOnlyList<CellCompletedRecord> records)
    {
        var scenarios = records
            .Select(ResolveScenario)
            .Where(scenario => !string.IsNullOrWhiteSpace(scenario))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return scenarios.Count == 1 ? scenarios[0] : null;
    }

    private static string? ResolveScenario(CellCompletedRecord record)
        => DataPipelineUploadScenarioResolver.Resolve(
            record.TaskKey,
            DataPipelineUploadScenarioResolver.TryReadRecordKind(record.CellData),
            record.CellData.ProcessType);
}

internal readonly record struct UploadRecordSourceKey(int? NetworkDeviceId, string DeviceName);
