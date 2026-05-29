using Avalonia;
using Avalonia.Controls;
using System;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public enum EdgeDataGridDensity
{
    Compact,
    Normal
}

public class EdgeDataGrid : DataGrid
{
    public static readonly StyledProperty<EdgeDataGridDensity> DensityProperty =
        AvaloniaProperty.Register<EdgeDataGrid, EdgeDataGridDensity>(
            nameof(Density),
            EdgeDataGridDensity.Compact);

    public static readonly StyledProperty<double> ViewportMaxHeightProperty =
        AvaloniaProperty.Register<EdgeDataGrid, double>(
            nameof(ViewportMaxHeight),
            360d);

    static EdgeDataGrid()
    {
        DensityProperty.Changed.AddClassHandler<EdgeDataGrid>((grid, _) => grid.UpdateDensityClasses());
        ViewportMaxHeightProperty.Changed.AddClassHandler<EdgeDataGrid>((grid, _) => grid.UpdateViewportLimit());
    }

    public EdgeDataGrid()
    {
        Classes.Add("edge-data-grid");
        UpdateDensityClasses();
        UpdateViewportLimit();
    }

    protected override Type StyleKeyOverride => typeof(DataGrid);

    public EdgeDataGridDensity Density
    {
        get => GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    public double ViewportMaxHeight
    {
        get => GetValue(ViewportMaxHeightProperty);
        set => SetValue(ViewportMaxHeightProperty, value);
    }

    private void UpdateDensityClasses()
    {
        SetClass("density-compact", Density == EdgeDataGridDensity.Compact);
        SetClass("density-normal", Density == EdgeDataGridDensity.Normal);
    }

    private void UpdateViewportLimit()
    {
        MaxHeight = ViewportMaxHeight > 0d ? ViewportMaxHeight : double.PositiveInfinity;
    }

    private void SetClass(string name, bool enabled)
    {
        if (enabled)
        {
            Classes.Add(name);
        }
        else
        {
            Classes.Remove(name);
        }
    }
}
