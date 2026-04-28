namespace IIoT.Edge.Module.Homogenization.Payload;

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
