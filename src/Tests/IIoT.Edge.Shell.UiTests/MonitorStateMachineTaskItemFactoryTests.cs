using IIoT.Edge.Application.Features.Production.Monitor;
using IIoT.Edge.Presentation.Navigation.Features.Production.Monitor;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using Xunit;

namespace IIoT.Edge.Shell.UiTests;

public sealed class MonitorStateMachineTaskItemFactoryTests
{
    [Fact]
    public void CreateItems_WhenTaskHasNoStep_ShouldExposeNeutralVisualStatus()
    {
        var factory = new MonitorStateMachineTaskItemFactory(new TestAppLanguageService());

        var item = Assert.Single(factory.CreateItems([
            new MonitorStateMachineTaskSnapshot(
                "Heartbeat",
                "心跳任务",
                Enabled: true,
                CanRun: true,
                HasSavedBinding: true,
                StepValue: null,
                StepText: string.Empty,
                UnavailableReason: string.Empty,
                IsHeartbeatLike: true,
                RequiredSignalCount: 1,
                MissingRequiredSignalCount: 0,
                MissingRequiredSignalsSummary: string.Empty)
        ]));

        Assert.Equal(EdgeVisualStatus.Default, item.VisualStatus);
        Assert.Equal("暂无步骤", item.StepValueText);
    }
}
