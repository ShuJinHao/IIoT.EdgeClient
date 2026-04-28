using IIoT.Edge.Domain.Config.Aggregates;
using IIoT.Edge.SharedKernel.Domain;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class DomainEntityBehaviorTests
{
    [Fact]
    public void BaseEntityId_WhenInspected_ShouldNotExposePublicSetter()
    {
        var property = typeof(BaseEntity<int>).GetProperty(nameof(BaseEntity<int>.Id));
        var setter = property?.SetMethod;

        Assert.NotNull(property);
        Assert.NotNull(setter);
        Assert.False(setter!.IsPublic);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SystemConfigEntity_WhenKeyInvalid_ShouldReject(string key)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            SystemConfigEntity.Create(key, "value"));
    }

    [Fact]
    public void SystemConfigEntity_WhenSortOrderInvalid_ShouldReject()
    {
        var entity = SystemConfigEntity.Create("Mes.Address", "http://mes");

        Assert.ThrowsAny<ArgumentException>(() => entity.UpdateSortOrder(-1));
    }

    [Theory]
    [InlineData(0, "切刀速度")]
    [InlineData(1, "")]
    [InlineData(1, "   ")]
    public void DeviceParamEntity_WhenRequiredFieldsInvalid_ShouldReject(
        int networkDeviceId,
        string name)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            DeviceParamEntity.Create(networkDeviceId, name, "100"));
    }

    [Fact]
    public void DeviceParamEntity_WhenSortOrderInvalid_ShouldReject()
    {
        var entity = DeviceParamEntity.Create(1, "切刀速度", "100");

        Assert.ThrowsAny<ArgumentException>(() => entity.UpdateSortOrder(-1));
    }
}
