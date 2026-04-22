namespace IIoT.Edge.Module.Homogenization.Payload;

public sealed class HomogenizationCellDataValidator
{
    public bool TryValidate(HomogenizationCellData cellData, out string? error)
    {
        ArgumentNullException.ThrowIfNull(cellData);

        if (string.IsNullOrWhiteSpace(cellData.Barcode))
        {
            error = "Barcode is required.";
            return false;
        }

        error = null;
        return true;
    }
}