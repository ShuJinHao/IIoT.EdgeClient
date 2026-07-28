using IIoT.Edge.Shell.Core;
using Xunit;

namespace IIoT.Edge.Shell.FilesystemTests;

public sealed class ShellModuleLaunchReadinessTests
{
    [Fact]
    public void Evaluate_WhenConfiguredModuleDidNotActivate_ShouldReturnFailure()
    {
        var result = ShellModuleLaunchReadiness.Evaluate(
            ["AP"],
            []);

        Assert.False(result.Success);
        Assert.Equal(["AP"], result.ConfiguredModuleIds);
        Assert.Empty(result.ActiveModuleIds);
        Assert.Contains("未激活：AP", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WhenConfiguredAndActiveModulesMatch_ShouldReturnSuccess()
    {
        var result = ShellModuleLaunchReadiness.Evaluate(
            [" CP ", "AP"],
            ["ap", "CP"]);

        Assert.True(result.Success);
        Assert.Equal(["AP", "CP"], result.ConfiguredModuleIds);
        Assert.Equal(["ap", "CP"], result.ActiveModuleIds);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Evaluate_WhenMaintenanceProfileHasNoModules_ShouldReturnSuccess()
    {
        var result = ShellModuleLaunchReadiness.Evaluate([], []);

        Assert.True(result.Success);
        Assert.Empty(result.ConfiguredModuleIds);
        Assert.Empty(result.ActiveModuleIds);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void Evaluate_WhenUnexpectedModuleActivated_ShouldReturnFailure()
    {
        var result = ShellModuleLaunchReadiness.Evaluate(
            ["AP"],
            ["AP", "CP"]);

        Assert.False(result.Success);
        Assert.Contains("非配置激活：CP", result.ErrorMessage, StringComparison.Ordinal);
    }
}
