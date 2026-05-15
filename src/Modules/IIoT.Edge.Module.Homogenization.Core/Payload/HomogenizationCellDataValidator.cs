namespace IIoT.Edge.Module.Homogenization.Payload;

/// <summary>
/// 匀浆出料电芯数据校验器，当前只校验进入 DataPipeline 前必须具备托盘码。
/// </summary>
public sealed class HomogenizationCellDataValidator
{
    public bool TryValidate(HomogenizationCellData cellData, out string? error)
    {
        ArgumentNullException.ThrowIfNull(cellData);

        if (string.IsNullOrWhiteSpace(cellData.TrayCode))
        {
            error = "托盘码不能为空。";
            return false;
        }

        error = null;
        return true;
    }
}
