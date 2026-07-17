using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using Xunit;

namespace IIoT.Edge.UI.Shared.Tests;

public sealed class SharedControlBehaviorTests
{
    [AvaloniaFact]
    public void SummaryAndTimelineControls_WhenPropertiesChange_ShouldExposeDerivedRuntimeState()
    {
        var summary = new EdgeInfoSummaryCard
        {
            Title = "Runtime summary",
            SummaryItems = new[] { "one", "two" },
            SummaryItemMinWidth = 120,
            NoticeMessage = "Real warning",
            NoticeStatus = EdgeVisualStatus.Warning
        };
        var items = new EdgeSummaryItemsControl
        {
            SummaryItems = summary.SummaryItems,
            ItemMinWidth = summary.SummaryItemMinWidth,
            Orientation = Orientation.Horizontal
        };
        var timeline = new EdgeStatusTimeline
        {
            Title = "Runtime events",
            ItemsSource = new[] { "started" },
            IsEmpty = false
        };

        Assert.True(summary.HasNotice);
        Assert.Equal(EdgeVisualStatus.Warning, summary.NoticeStatus);
        Assert.Equal(120, items.ItemMinWidth);
        Assert.Equal(Orientation.Horizontal, items.Orientation);
        Assert.True(timeline.HasTitle);
        Assert.True(timeline.HasItems);

        summary.NoticeMessage = " ";
        timeline.IsEmpty = true;

        Assert.False(summary.HasNotice);
        Assert.False(timeline.HasItems);
    }

    [AvaloniaFact]
    public void DataGrid_WhenViewportAndDensityChange_ShouldApplySharedBehaviorWithoutPageOverrides()
    {
        var grid = new EdgeDataGrid
        {
            Density = EdgeDataGridDensity.Diagnostic,
            ViewportMaxHeight = 480,
            HorizontalScrollBarReserveHeight = 10
        };

        Assert.Contains("edge-data-grid", grid.Classes);
        Assert.Contains("density-diagnostic", grid.Classes);
        Assert.DoesNotContain("density-compact", grid.Classes);
        Assert.Equal(480, grid.MaxHeight);
        Assert.Equal(10, grid.HorizontalScrollBarReserveHeight);

        grid.ViewportMaxHeight = 0;

        Assert.Equal(double.PositiveInfinity, grid.MaxHeight);
    }

    [AvaloniaFact]
    public void StatusSegmentBar_WhenGeometryIsConfigured_ShouldKeepPropertyDrivenDimensions()
    {
        var segments = new EdgeStatusSegmentBar
        {
            SegmentWidth = 32,
            SegmentHeight = 9,
            SegmentSpacing = 3,
            ItemsSource = new[] { "healthy", "warning" }
        };

        Assert.Equal(32, segments.SegmentWidth);
        Assert.Equal(9, segments.SegmentHeight);
        Assert.Equal(3, segments.SegmentSpacing);
        Assert.Equal(2, segments.Items.Count);
    }
}
