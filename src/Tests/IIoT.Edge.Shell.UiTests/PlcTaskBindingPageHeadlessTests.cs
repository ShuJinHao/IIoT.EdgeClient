using Avalonia.Controls;
using IIoT.Edge.Presentation.Navigation.Features.Hardware.PlcTaskBindingView;
using IIoT.Edge.UI.Shared.Avalonia.Controls;

namespace IIoT.Edge.Shell.UiTests;

public sealed class PlcTaskBindingPageHeadlessTests
{
    [AvaloniaFact]
    public void RecoveryColumns_ShouldUseSharedTableAndRealActionColumn()
    {
        var page = new PlcTaskBindingPage();

        var grid = NameScope
            .GetNameScope(page)?
            .Find<EdgeDataGrid>("TaskBindingGrid");

        Assert.NotNull(grid);
        Assert.False(grid!.AutoGenerateColumns);
        Assert.Equal(9, grid.Columns.Count);
        Assert.IsType<EdgeTextColumn>(grid.Columns[4]);
        Assert.IsType<EdgeTextColumn>(grid.Columns[5]);
        Assert.IsType<EdgeTextColumn>(grid.Columns[6]);
        Assert.IsType<EdgeActionColumn>(grid.Columns[8]);
        Assert.NotNull(((EdgeActionColumn)grid.Columns[8]).CellTemplate);
    }
}
