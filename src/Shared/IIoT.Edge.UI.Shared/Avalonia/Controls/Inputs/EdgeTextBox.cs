using Avalonia.Controls;
using System;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

/// <summary>
/// 共享文本输入控件：统一表单输入的边框、米白底、聚焦高亮与禁用观感。
/// 复用 Fluent 默认 TextBox 模板（不自定义 Template），视觉差异全部由
/// EdgeControls.axaml 中的 "edge-text-box" 样式集中定义。
/// 内联扁平场景（嵌入表格单元格）追加 Classes="flat"。
/// </summary>
public class EdgeTextBox : TextBox
{
    public EdgeTextBox()
    {
        Classes.Add("edge-text-box");
    }

    protected override Type StyleKeyOverride => typeof(TextBox);
}
