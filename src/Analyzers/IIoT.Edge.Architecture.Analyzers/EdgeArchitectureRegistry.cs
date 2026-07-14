using System.Collections.Immutable;

namespace IIoT.Edge.Architecture.Analyzers;

internal enum EdgeProjectRole
{
    Unknown,
    Analyzer,
    Test,
    TestFixture,
    Domain,
    Application,
    SharedKernel,
    UiShared,
    ModuleSdk,
    ConcretePlugin,
    Infrastructure,
    Presentation,
    VisualTestData,
    Host,
    Tool
}

internal static class EdgeArchitectureRegistry
{
    internal const string EfOwnerAssembly = "IIoT.Edge.Infrastructure.Persistence.EfCore";
    internal const string DapperOwnerAssembly = "IIoT.Edge.Infrastructure.Persistence.Dapper";
    internal const string DeviceCommAssembly = "IIoT.Edge.Infrastructure.DeviceComm";

    internal static readonly ImmutableHashSet<string> ApprovedRepositoryRoots =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "IIoT.Edge.Domain.Hardware.Aggregates.NetworkDeviceEntity",
            "IIoT.Edge.Domain.Hardware.Aggregates.IoMappingEntity",
            "IIoT.Edge.Domain.Hardware.Aggregates.PlcTaskBindingEntity",
            "IIoT.Edge.Domain.Hardware.Aggregates.SerialDeviceEntity",
            "IIoT.Edge.Domain.Config.Aggregates.SystemConfigEntity");

    internal static readonly string ApprovedRootSummary =
        "NetworkDeviceEntity, IoMappingEntity, PlcTaskBindingEntity, SerialDeviceEntity, SystemConfigEntity";

    internal static EdgeProjectRole ClassifyAssembly(string assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            return EdgeProjectRole.Unknown;

        if (assemblyName.Equals("IIoT.Edge.Architecture.Analyzers", StringComparison.Ordinal) ||
            assemblyName.EndsWith(".Analyzers", StringComparison.Ordinal))
            return EdgeProjectRole.Analyzer;

        if (IsTestAssembly(assemblyName))
            return assemblyName.IndexOf("TestPlugin", StringComparison.Ordinal) >= 0
                ? EdgeProjectRole.TestFixture
                : EdgeProjectRole.Test;

        if (assemblyName.Equals("IIoT.Edge.Domain", StringComparison.Ordinal))
            return EdgeProjectRole.Domain;
        if (assemblyName.Equals("IIoT.Edge.Application", StringComparison.Ordinal))
            return EdgeProjectRole.Application;
        if (assemblyName.Equals("IIoT.Edge.SharedKernel", StringComparison.Ordinal))
            return EdgeProjectRole.SharedKernel;
        if (assemblyName.Equals("IIoT.Edge.UI.Shared", StringComparison.Ordinal))
            return EdgeProjectRole.UiShared;
        if (assemblyName.Equals("IIoT.Edge.Module.Sdk", StringComparison.Ordinal))
            return EdgeProjectRole.ModuleSdk;
        if (IsConcretePluginAssembly(assemblyName))
            return EdgeProjectRole.ConcretePlugin;
        if (assemblyName.Equals("IIoT.Edge.Presentation.VisualTestData", StringComparison.Ordinal))
            return EdgeProjectRole.VisualTestData;
        if (assemblyName.StartsWith("IIoT.Edge.Presentation.", StringComparison.Ordinal))
            return EdgeProjectRole.Presentation;
        if (assemblyName.StartsWith("IIoT.Edge.Infrastructure.", StringComparison.Ordinal))
            return EdgeProjectRole.Infrastructure;
        if (assemblyName.Equals("IIoT.Edge.RuntimeLayoutSync", StringComparison.Ordinal))
            return EdgeProjectRole.Tool;
        if (assemblyName.StartsWith("IIoT.Edge.Host.", StringComparison.Ordinal) ||
            assemblyName.Equals("IIoT.Edge.Shell", StringComparison.Ordinal) ||
            assemblyName.Equals("IIoT.Edge.Launcher", StringComparison.Ordinal) ||
            assemblyName.Equals("IIoT.Edge.Installer", StringComparison.Ordinal))
            return EdgeProjectRole.Host;

        return EdgeProjectRole.Unknown;
    }

    internal static bool IsTestAssembly(string assemblyName)
        => assemblyName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase) ||
           assemblyName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
           assemblyName.IndexOf(".Testing", StringComparison.OrdinalIgnoreCase) >= 0 ||
           assemblyName.IndexOf("TestKit", StringComparison.OrdinalIgnoreCase) >= 0 ||
           assemblyName.IndexOf("TestPlugin", StringComparison.OrdinalIgnoreCase) >= 0 ||
           assemblyName.Equals("xunit.core", StringComparison.OrdinalIgnoreCase) ||
           assemblyName.StartsWith("xunit.", StringComparison.OrdinalIgnoreCase) ||
           assemblyName.StartsWith("Microsoft.TestPlatform", StringComparison.OrdinalIgnoreCase) ||
           assemblyName.Equals("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase);

    internal static bool IsConcretePluginAssembly(string assemblyName)
        => assemblyName.StartsWith("IIoT.Edge.Module.", StringComparison.Ordinal) &&
           !assemblyName.Equals("IIoT.Edge.Module.Sdk", StringComparison.Ordinal) &&
           assemblyName.IndexOf(".Tests", StringComparison.OrdinalIgnoreCase) < 0;

    internal static bool IsHostOrCommonRole(EdgeProjectRole role)
        => role is EdgeProjectRole.Domain or
            EdgeProjectRole.Application or
            EdgeProjectRole.SharedKernel or
            EdgeProjectRole.UiShared or
            EdgeProjectRole.Infrastructure or
            EdgeProjectRole.Presentation or
            EdgeProjectRole.VisualTestData or
            EdgeProjectRole.Host or
            EdgeProjectRole.Tool or
            EdgeProjectRole.ModuleSdk;

    internal static bool IsInnerLayer(EdgeProjectRole role)
        => role is EdgeProjectRole.Domain or EdgeProjectRole.Application;

    internal static bool IsPresentationLike(EdgeProjectRole role)
        => role is EdgeProjectRole.Presentation or
            EdgeProjectRole.VisualTestData or
            EdgeProjectRole.Host or
            EdgeProjectRole.Tool or
            EdgeProjectRole.ConcretePlugin or
            EdgeProjectRole.UiShared;
}
