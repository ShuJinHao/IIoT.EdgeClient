using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 共享表格操作列。用于承载 EdgeActionButton 等真实命令入口。
/// </summary>
public class EdgeActionColumn : DataGridTemplateColumn
{
    public EdgeActionColumn()
    {
        CanUserResize = false;
    }

    protected override Control GenerateElement(DataGridCell cell, object dataItem)
    {
        cell.Classes.Add("edge-action-cell");
        return base.GenerateElement(cell, dataItem);
    }
}
