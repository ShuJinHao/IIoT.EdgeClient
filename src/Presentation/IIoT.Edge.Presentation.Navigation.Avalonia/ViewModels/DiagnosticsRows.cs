namespace IIoT.Edge.Presentation.Navigation.Avalonia.ViewModels;

public sealed record RuntimeRegistrationRow(string ModuleId, int TaskCount, string TaskNames);

public sealed record DiagnosticsModuleRegistrationRow(
    string ModuleId,
    string ProcessType,
    string AssemblyName,
    string EnabledText,
    string CellDataText,
    string RuntimeFactoryText,
    string CloudUploaderText,
    string MesUploaderText,
    string HardwareProfileText);

public sealed record DiagnosticsPersistenceRow(string Scope, string Status, string Message);

public sealed record DiagnosticsPluginStateRow(
    string ModuleId,
    string DisplayName,
    string ProcessType,
    string Version,
    string State,
    string Message);

public sealed record DiagnosticsIssueRow(string Code, string ModuleId, string DeviceName, string Message);

public sealed record DiagnosticsIoWriteGateRow(
    string Time,
    string DeviceName,
    string BusinessGroup,
    string Message,
    string Value);

public sealed record DiagnosticsPlcWriteTraceRow(
    string Time,
    string DeviceName,
    string Kind,
    string StartAddress,
    string WordCount,
    string Message);

public sealed record DiagnosticsFieldAcceptanceSummaryRow(
    string Scope,
    string Status,
    string Message);
