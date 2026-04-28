namespace IIoT.Edge.Application.Modules.Hardware;

public sealed record ModulePlcDefaults(
    string? DeviceModel,
    int? ConnectTimeout,
    int? Port1 = null);

public sealed record ModuleIoTemplateEntry(
    string Label,
    string PlcAddress,
    int AddressCount,
    string DataType,
    string Direction,
    int SortOrder,
    string? Remark = null,
    string Category = "单点读数据",
    string GroupName = "",
    string DisplayRole = "");

public sealed record ModuleIoSnapshot(
    string Label,
    string PlcAddress,
    int AddressCount,
    string DataType,
    string Direction,
    int SortOrder,
    string Category = "单点读数据",
    string GroupName = "",
    string DisplayRole = "");

public sealed record ModuleHardwareValidationIssue(string Message);

public sealed class ModuleHardwareValidationResult
{
    private ModuleHardwareValidationResult(IReadOnlyList<ModuleHardwareValidationIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<ModuleHardwareValidationIssue> Issues { get; }

    public bool IsValid => Issues.Count == 0;

    public static ModuleHardwareValidationResult Success()
        => new([]);

    public static ModuleHardwareValidationResult Failure(IEnumerable<ModuleHardwareValidationIssue> issues)
        => new(issues.ToList().AsReadOnly());
}
