using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 共享文本表格列。业务页面只能声明列语义，列视觉统一由 EdgeDataGrid 控制。
/// </summary>
public class EdgeTextColumn : DataGridTextColumn
{
    protected override Control GenerateElement(DataGridCell cell, object dataItem)
    {
        cell.Classes.Add("edge-text-cell");

        var element = base.GenerateElement(cell, dataItem);
        if (element is TextBlock textBlock)
        {
            ApplyTextPresentation(textBlock);
        }

        return element;
    }

    protected override Control GenerateEditingElementDirect(DataGridCell cell, object dataItem)
    {
        cell.Classes.Add("edge-text-cell");

        var element = base.GenerateEditingElementDirect(cell, dataItem);
        if (element is TextBox textBox)
        {
            textBox.Classes.Add("edge-text-box");
            textBox.Classes.Add("flat");
        }

        return element;
    }

    private static void ApplyTextPresentation(TextBlock textBlock)
    {
        textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        textBlock.TextWrapping = TextWrapping.NoWrap;
        textBlock.MaxLines = 1;
        textBlock.Bind(
            ToolTip.TipProperty,
            new Binding(nameof(TextBlock.Text))
            {
                Source = textBlock
            });
    }
}
