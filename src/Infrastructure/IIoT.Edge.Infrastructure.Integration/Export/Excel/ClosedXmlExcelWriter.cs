using ClosedXML.Excel;

namespace IIoT.Edge.Infrastructure.Integration.Export.Excel;

/// <summary>
/// 使用 ClosedXML 追加本地 Excel 生产数据。
/// </summary>
internal sealed class ClosedXmlExcelWriter : IExcelWriter
{
    public void AppendRow(
        string filePath,
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, string> rowData)
    {
        if (File.Exists(filePath))
        {
            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheets.First();

            var existingHeaders = ReadHeaders(worksheet);
            var newColumns = columns.Where(column => !existingHeaders.Contains(column)).ToList();
            if (newColumns.Count > 0)
            {
                var nextColumn = existingHeaders.Count + 1;
                foreach (var column in newColumns)
                {
                    worksheet.Cell(1, nextColumn).Value = column;
                    StyleHeaderCell(worksheet.Cell(1, nextColumn));
                    existingHeaders.Add(column);
                    nextColumn++;
                }
            }

            var nextRow = worksheet.LastRowUsed()!.RowNumber() + 1;
            WriteRow(worksheet, nextRow, existingHeaders, rowData);

            workbook.Save();
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.AddWorksheet("生产数据");

            for (var i = 0; i < columns.Count; i++)
            {
                worksheet.Cell(1, i + 1).Value = columns[i];
                StyleHeaderCell(worksheet.Cell(1, i + 1));
            }

            WriteRow(worksheet, 2, columns, rowData);

            worksheet.Columns().AdjustToContents(1, 1, 50);
            workbook.SaveAs(filePath);
        }
    }

    private static void WriteRow(
        IXLWorksheet worksheet,
        int row,
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> rowData)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            var value = rowData.TryGetValue(header, out var rowValue) ? rowValue : "";

            worksheet.Cell(row, i + 1).Value = value;

            if (value == "NG")
            {
                worksheet.Cell(row, i + 1).Style.Font.FontColor = XLColor.Red;
                worksheet.Cell(row, i + 1).Style.Font.Bold = true;
            }
        }
    }

    private static List<string> ReadHeaders(IXLWorksheet worksheet)
    {
        var headers = new List<string>();
        var headerRow = worksheet.Row(1);

        for (var column = 1; column <= headerRow.LastCellUsed()!.Address.ColumnNumber; column++)
        {
            var value = headerRow.Cell(column).GetString();
            if (!string.IsNullOrEmpty(value))
            {
                headers.Add(value);
            }
        }

        return headers;
    }

    private static void StyleHeaderCell(IXLCell cell)
    {
        cell.Style.Font.Bold = true;
        cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }
}
