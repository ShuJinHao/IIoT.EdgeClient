namespace IIoT.Edge.Infrastructure.Integration.Export.Excel;

internal interface IExcelWriter
{
    void AppendRow(
        string filePath,
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, string> rowData);
}
