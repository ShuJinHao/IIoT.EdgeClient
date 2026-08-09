using IIoT.Edge.Application.Common.DataPipeline;
using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Sdk.DataPipeline;

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
            Scenario: ResolveScenario(recordList))
        {
            PlcCode = ResolveSingle(recordList.Select(record => record.ResolvePlcCode()))
        };
    }

    public static MesUploadDiagnosticsContext CreateMesContext(CellCompletedRecord record)
        => new(
            DeviceName: record.ResolveDeviceName(),
            ModuleId: record.ModuleId,
            TaskKey: record.TaskKey,
            Scenario: ResolveScenario(record))
        {
            PlcCode = record.ResolvePlcCode()
        };

    public static string ResolveLogPlcCode(IEnumerable<CellCompletedRecord> records)
    {
        var plcCodes = records
            .Select(record => record.ResolvePlcCode())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(static code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return plcCodes.Count == 1 ? plcCodes[0] : "多PLC";
    }

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
        => new(
            (record.ClientCode ?? string.Empty).Trim(),
            (record.TypeKey ?? string.Empty).Trim(),
            record.ResolvePlcCode().Trim());

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

internal readonly record struct UploadRecordSourceKey(
    string ClientCode,
    string TypeKey,
    string PlcCode);
