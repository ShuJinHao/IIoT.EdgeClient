using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using System.Reflection;

namespace IIoT.Edge.Infrastructure.Integration.Export.Excel;

/// <summary>
/// Excel 本地存储消费者
/// 
/// 从 CellData 强类型属性自动提取列名和数据
/// 调用 ExcelWriter 通用工具写入
/// 按天生成文件：2026-03-25_生产数据.xlsx
/// </summary>
public class ExcelConsumer : IExcelConsumer
{
    private readonly string _excelDirectory;
    private readonly ILogService _logger;
    private readonly object _fileLock = new();
    public IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode FailureMode
        => IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode.BestEffort;
    public string? RetryChannel => null;
    public string Name => "Excel";
    public int Order => 30;

    public ExcelConsumer(string excelDirectory, ILogService logger)
    {
        _excelDirectory = excelDirectory;
        _logger = logger;
        Directory.CreateDirectory(_excelDirectory);
    }

    public Task<bool> ProcessAsync(CellCompletedRecord record)
    {
        try
        {
            var cellData = record.CellData;
            var columns = GetColumnNames(cellData.GetType());
            var rowData = BuildRowData(cellData, columns);

            var completedTime = cellData.CompletedTime ?? DateTime.Now;
            var fileName = $"{completedTime:yyyy-MM-dd}_生产数据.xlsx";
            var filePath = Path.Combine(_excelDirectory, fileName);

            lock (_fileLock)
            {
                ExcelWriter.AppendRow(filePath, columns, rowData);
            }

            _logger.Info($"[Excel] 写入成功，{cellData.DisplayLabel}");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"[Excel] 写入失败，{record.CellData.DisplayLabel}，{ex.Message}");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 从强类型属性提取列名（排除 ProcessType 和 DisplayLabel）
    /// </summary>
    private static List<string> GetColumnNames(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != nameof(CellDataBase.ProcessType)
                     && p.Name != nameof(CellDataBase.DisplayLabel))
            .Select(p => p.Name)
            .ToList();
    }

    /// <summary>
    /// 从强类型对象构建行数据
    /// </summary>
    private static Dictionary<string, string> BuildRowData(
        CellDataBase cellData,
        List<string> columns)
    {
        var rowData = new Dictionary<string, string>();
        var type = cellData.GetType();

        foreach (var column in columns)
        {
            var prop = type.GetProperty(column);
            var value = prop?.GetValue(cellData);

            rowData[column] = value switch
            {
                null => "",
                bool b => b ? "OK" : "NG",
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                _ => value.ToString() ?? ""
            };
        }

        return rowData;
    }
}
