using IIoT.Edge.Shell.Core;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class ShellRuntimePathResolverBehaviorTests
{
    [Fact]
    public void Resolve_WhenRuntimeDataRootIsMissing_ShouldUseProfileScopedDefaultDirectory()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Shell:MachineProfile"] = "StackingLine"
                })
                .Build();

            var result = ShellRuntimePathResolver.Resolve(baseDirectory, configuration);

            Assert.Equal("StackingLine", result.ProfileName);
            Assert.Equal(Path.Combine(baseDirectory, "data", "profiles", "StackingLine"), result.RuntimeDataRoot);
            Assert.Equal(Path.Combine(result.RuntimeDataRoot, "db"), result.DatabaseDirectory);
            Assert.Equal(Path.Combine(result.RuntimeDataRoot, "context"), result.ContextDirectory);
            Assert.Equal(Path.Combine(result.DiagnosticsDirectory, "logs"), result.LogDirectory);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void Resolve_WhenRuntimeDataRootIsConfigured_ShouldResolveRelativeRootAgainstBaseDirectory()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "edge-runtime-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Shell:MachineProfile"] = "HomogenizationLine",
                    ["Shell:RuntimeDataRoot"] = ".\\profiles\\homogenization"
                })
                .Build();

            var result = ShellRuntimePathResolver.Resolve(baseDirectory, configuration);

            Assert.Equal(
                Path.Combine(baseDirectory, "profiles", "homogenization"),
                result.RuntimeDataRoot);
            Assert.Equal(Path.Combine(result.RuntimeDataRoot, "recipe"), result.RecipeDirectory);
            Assert.Equal(Path.Combine(result.RuntimeDataRoot, "excel"), result.ExcelDirectory);
            Assert.Contains("HomogenizationLine", result.FallbackCrashLogPath, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }
}
