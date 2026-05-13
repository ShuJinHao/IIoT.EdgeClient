namespace IIoT.Edge.Application.Abstractions.Config;

public sealed record EdgeRuntimePaths(
    string BaseDirectory,
    string ProfileName,
    string RuntimeDataRoot,
    string DatabaseDirectory,
    string ContextDirectory,
    string RecipeDirectory,
    string ExcelDirectory,
    string DiagnosticsDirectory,
    string LogDirectory,
    string DeviceCacheFilePath,
    string PrimaryCrashLogPath,
    string FallbackCrashLogPath);
