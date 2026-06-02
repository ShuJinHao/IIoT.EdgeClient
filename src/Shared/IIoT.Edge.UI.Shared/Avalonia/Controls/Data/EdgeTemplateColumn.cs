using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 共享模板表格列。用于确实需要自定义单元格内容的业务列。
/// </summary>
public class EdgeTemplateColumn : DataGridTemplateColumn
{
    protected override Control GenerateElement(DataGridCell cell, object dataItem)
    {
        cell.Classes.Add("edge-template-cell");
        return base.GenerateElement(cell, dataItem);
    }
}
