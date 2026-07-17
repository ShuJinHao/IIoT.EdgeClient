using Avalonia.Controls;
using IIoT.Edge.Presentation.Navigation.Features.DiagnosticsView;
using IIoT.Edge.UI.Shared.Avalonia.Controls;

namespace IIoT.Edge.Shell.UiTests;

public sealed class DiagnosticsPageHeadlessTests
{
    [AvaloniaFact]
    public void DiagnosticsPage_ShouldLoadMesDiagnosticsGridWithScenarioColumn()
    {
        var page = new DiagnosticsPage();

        var grid = NameScope.GetNameScope(page)?.Find<EdgeDataGrid>("MesUploadDiagnosticsGrid");

        Assert.NotNull(grid);
        Assert.False(grid!.AutoGenerateColumns);
        Assert.Equal(7, grid.Columns.Count);
        Assert.IsType<EdgeTextColumn>(grid.Columns[0]);
        Assert.IsType<EdgeTextColumn>(grid.Columns[1]);
        Assert.IsType<EdgeTextColumn>(grid.Columns[2]);
        Assert.Equal(130, grid.Columns[2].MinWidth);
    }
}
