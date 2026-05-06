using ClosedXML.Excel;

namespace IIoT.Edge.Infrastructure.Integration.Export.Excel;

/// <summary>
/// 通用 Excel 写入工具
/// 
/// 只管写入，不管数据从哪来
/// 调用方给文件路径、列名列表、数据行（字典），写入即可
/// 
/// 用法：
///   var columns = new[] { "字段A", "字段B", "结果" };
///   var rowData = new Dictionary&lt;string, string&gt;
///   {
///       ["字段A"] = "CELL001",
///       ["字段B"] = "BATCH001",
///       ["结果"] = "OK"
///   };
///   ExcelWriter.AppendRow(filePath, columns, rowData);
/// </summary>
public static class ExcelWriter
{
    /// <summary>
    /// 追加一行数据到 Excel 文件
    /// 文件不存在则创建并写入表头
    /// 文件已存在则检查是否有新列需要追加
    /// </summary>
    public static void AppendRow(
        string filePath,
        IReadOnlyList<string> columns,
        Dictionary<string, string> rowData)
    {
        if (File.Exists(filePath))
        {
            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheets.First();

            var existingHeaders = ReadHeaders(worksheet);

            // 追加新列
            var newColumns = columns.Where(c => !existingHeaders.Contains(c)).ToList();
            if (newColumns.Count > 0)
            {
                var nextCol = existingHeaders.Count + 1;
                foreach (var col in newColumns)
                {
                    worksheet.Cell(1, nextCol).Value = col;
                    StyleHeaderCell(worksheet.Cell(1, nextCol));
                    existingHeaders.Add(col);
                    nextCol++;
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

            for (int i = 0; i < columns.Count; i++)
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
        Dictionary<string, string> rowData)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            var value = rowData.TryGetValue(header, out var v) ? v : "";

            worksheet.Cell(row, i + 1).Value = value;

            // NG 标红
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

        for (int col = 1; col <= headerRow.LastCellUsed()!.Address.ColumnNumber; col++)
        {
            var value = headerRow.Cell(col).GetString();
            if (!string.IsNullOrEmpty(value))
                headers.Add(value);
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
