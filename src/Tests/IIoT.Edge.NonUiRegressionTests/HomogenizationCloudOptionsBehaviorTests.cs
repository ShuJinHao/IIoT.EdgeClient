using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Payload;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationCloudOptionsBehaviorTests
{
    [Fact]
    public void HomogenizationCloudCodeOptions_ShouldUseConfiguredStatusLevelMapping()
    {
        var options = new HomogenizationCloudCodeOptions
        {
            EquipmentStatusLevels =
            {
                ["7"] = "ERROR"
            }
        };

        var normalLevel = options.ResolveEquipmentStatusLevel(new HomogenizationEquipmentStatusSnapshot
        {
            StatusCode = 1,
            StatusText = "空闲"
        });
        var configuredLevel = options.ResolveEquipmentStatusLevel(new HomogenizationEquipmentStatusSnapshot
        {
            StatusCode = 7,
            StatusText = "空闲"
        });

        Assert.Equal("INFO", normalLevel);
        Assert.Equal("ERROR", configuredLevel);
    }
}
