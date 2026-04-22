namespace IIoT.Edge.Module.Homogenization.Diagnostics;

public static class HomogenizationDiagnosticsFormatter
{
    public static string FormatModuleStatus(bool isEnabled)
        => isEnabled
            ? "Homogenization module is enabled."
            : "Homogenization module is disabled.";
}