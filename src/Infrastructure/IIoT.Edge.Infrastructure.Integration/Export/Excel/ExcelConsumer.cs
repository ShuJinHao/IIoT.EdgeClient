using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.DataPipeline.Consumers;
using IIoT.Edge.Application.Abstractions.Time;
using System.Reflection;

namespace IIoT.Edge.Infrastructure.Integration.Export.Excel;

/// <summary>
/// Excel 本地存储消费者
/// 
/// 从 CellData 强类型属性自动提取列名和数据
/// 调用可注入的 Excel 写入器落盘
/// 按天生成文件：2026-03-25_生产数据.xlsx
/// </summary>
internal sealed class ExcelConsumer : IExcelConsumer
{
    private readonly string _excelDirectory;
    private readonly ILogService _logger;
    private readonly IProductionTimeProvider _productionTime;
    private readonly IExcelWriter _excelWriter;
    private readonly object _fileLock = new();
    public IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode FailureMode
        => IIoT.Edge.Application.Abstractions.DataPipeline.ConsumerFailureMode.BestEffort;
    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.None;
    public string Name => "Excel";
    public int Order => 30;

    public ExcelConsumer(
        string excelDirectory,
        ILogService logger,
        IProductionTimeProvider productionTime,
        IExcelWriter excelWriter)
    {
        _excelDirectory = excelDirectory;
        _logger = logger;
        _productionTime = productionTime;
        _excelWriter = excelWriter;
        Directory.CreateDirectory(_excelDirectory);
    }

    public Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
    {
        try
        {
            var cellData = record.CellData;
            var columns = GetColumnNames(cellData.GetType());
            var rowData = BuildRowData(cellData, columns, _productionTime);

            var completedTime = _productionTime.ToBusinessTime(cellData.CompletedTime ?? _productionTime.UtcNow);
            var fileName = $"{completedTime:yyyy-MM-dd}_生产数据.xlsx";
            var filePath = Path.Combine(_excelDirectory, fileName);

            lock (_fileLock)
            {
                _excelWriter.AppendRow(filePath, columns, rowData);
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
        List<string> columns,
        IProductionTimeProvider productionTime)
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
                DateTime dt => productionTime.FormatBusinessTimestamp(dt),
                _ => value.ToString() ?? ""
            };
        }

        return rowData;
    }
}
