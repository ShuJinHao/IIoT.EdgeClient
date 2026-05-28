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

    static EdgeDataGrid()
    {
        DensityProperty.Changed.AddClassHandler<EdgeDataGrid>((grid, _) => grid.UpdateDensityClasses());
    }

    public EdgeDataGrid()
    {
        Classes.Add("edge-data-grid");
        UpdateDensityClasses();
    }

    protected override Type StyleKeyOverride => typeof(DataGrid);

    public EdgeDataGridDensity Density
    {
        get => GetValue(DensityProperty);
        set => SetValue(DensityProperty, value);
    }

    private void UpdateDensityClasses()
    {
        SetClass("density-compact", Density == EdgeDataGridDensity.Compact);
        SetClass("density-normal", Density == EdgeDataGridDensity.Normal);
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
