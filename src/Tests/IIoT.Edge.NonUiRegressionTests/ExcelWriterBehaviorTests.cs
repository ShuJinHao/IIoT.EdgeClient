using ClosedXML.Excel;
using IIoT.Edge.Infrastructure.Integration.Export.Excel;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class ExcelWriterBehaviorTests
{
    [Fact]
    public async Task ExcelConsumer_ShouldUseInjectedWriter()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var writer = new RecordingExcelWriter();
            var productionTime = new FakeProductionTimeProvider
            {
                FixedUtcNow = new DateTime(2026, 5, 8, 1, 2, 3, DateTimeKind.Utc)
            };
            var consumer = new ExcelConsumer(
                tempDirectory,
                new FakeLogService(),
                productionTime,
                writer);

            var result = await consumer.ProcessAsync(new CellCompletedRecord
            {
                CellData = new TestCellData
                {
                    Barcode = "CELL-001",
                    TrayCode = "TRAY-001",
                    CellResult = false,
                    CompletedTime = new DateTime(2026, 5, 8, 0, 0, 0, DateTimeKind.Utc)
                }
            });

            Assert.True(result);
            Assert.Equal(1, writer.CallCount);
            Assert.EndsWith("2026-05-08_生产数据.xlsx", writer.LastFilePath);
            Assert.Contains(nameof(TestCellData.Barcode), writer.LastColumns);
            Assert.Equal("CELL-001", writer.LastRowData[nameof(TestCellData.Barcode)]);
            Assert.Equal("TRAY-001", writer.LastRowData[nameof(TestCellData.TrayCode)]);
            Assert.Equal("NG", writer.LastRowData[nameof(TestCellData.CellResult)]);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ClosedXmlExcelWriter_WhenFileDoesNotExist_ShouldCreateHeaderAndDataRow()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(tempDirectory, "production.xlsx");
            var writer = new ClosedXmlExcelWriter();

            writer.AppendRow(
                filePath,
                ["Barcode", "CellResult"],
                new Dictionary<string, string>
                {
                    ["Barcode"] = "CELL-001",
                    ["CellResult"] = "NG"
                });

            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheets.Single();
            Assert.Equal("生产数据", worksheet.Name);
            Assert.Equal("Barcode", worksheet.Cell(1, 1).GetString());
            Assert.Equal("CellResult", worksheet.Cell(1, 2).GetString());
            Assert.True(worksheet.Cell(1, 1).Style.Font.Bold);
            Assert.Equal("CELL-001", worksheet.Cell(2, 1).GetString());
            Assert.Equal("NG", worksheet.Cell(2, 2).GetString());
            Assert.True(worksheet.Cell(2, 2).Style.Font.Bold);
            Assert.Equal(XLColor.Red, worksheet.Cell(2, 2).Style.Font.FontColor);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ClosedXmlExcelWriter_WhenFileExists_ShouldAppendNewColumnsAndRows()
    {
        var tempDirectory = CreateTempDirectory();
        try
        {
            var filePath = Path.Combine(tempDirectory, "production.xlsx");
            var writer = new ClosedXmlExcelWriter();

            writer.AppendRow(
                filePath,
                ["Barcode"],
                new Dictionary<string, string>
                {
                    ["Barcode"] = "CELL-001"
                });
            writer.AppendRow(
                filePath,
                ["Barcode", "TrayCode"],
                new Dictionary<string, string>
                {
                    ["Barcode"] = "CELL-002",
                    ["TrayCode"] = "TRAY-002"
                });

            using var workbook = new XLWorkbook(filePath);
            var worksheet = workbook.Worksheets.Single();
            Assert.Equal("Barcode", worksheet.Cell(1, 1).GetString());
            Assert.Equal("TrayCode", worksheet.Cell(1, 2).GetString());
            Assert.True(worksheet.Cell(1, 2).Style.Font.Bold);
            Assert.Equal("CELL-001", worksheet.Cell(2, 1).GetString());
            Assert.Equal("", worksheet.Cell(2, 2).GetString());
            Assert.Equal("CELL-002", worksheet.Cell(3, 1).GetString());
            Assert.Equal("TRAY-002", worksheet.Cell(3, 2).GetString());
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "edge-excel-writer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingExcelWriter : IExcelWriter
    {
        public int CallCount { get; private set; }

        public string LastFilePath { get; private set; } = string.Empty;

        public IReadOnlyList<string> LastColumns { get; private set; } = [];

        public IReadOnlyDictionary<string, string> LastRowData { get; private set; } = new Dictionary<string, string>();

        public void AppendRow(
            string filePath,
            IReadOnlyList<string> columns,
            IReadOnlyDictionary<string, string> rowData)
        {
            CallCount++;
            LastFilePath = filePath;
            LastColumns = columns.ToArray();
            LastRowData = rowData.ToDictionary();
        }
    }
}
