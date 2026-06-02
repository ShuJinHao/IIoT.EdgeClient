using Avalonia.Controls;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 共享布尔表格列。复用 Edge CheckBox 视觉，避免业务页直接暴露原生列类型。
/// </summary>
public class EdgeCheckColumn : DataGridCheckBoxColumn
{
    protected override Control GenerateElement(DataGridCell cell, object dataItem)
    {
        cell.Classes.Add("edge-check-cell");

        var element = base.GenerateElement(cell, dataItem);
        if (element is CheckBox checkBox)
        {
            checkBox.Classes.Add("edge-check-box");
        }

        return element;
    }

    protected override Control GenerateEditingElementDirect(DataGridCell cell, object dataItem)
    {
        cell.Classes.Add("edge-check-cell");

        var element = base.GenerateEditingElementDirect(cell, dataItem);
        if (element is CheckBox checkBox)
        {
            checkBox.Classes.Add("edge-check-box");
        }

        return element;
    }
}
