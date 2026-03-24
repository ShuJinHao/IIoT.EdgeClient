// 路径：src/Tasks/IIoT.Edge.Task.Scan/Implementations/LoadingScanTask.cs
using IIoT.Edge.Contracts;
using IIoT.Edge.Contracts.Plc.Store;
using IIoT.Edge.Tasks.Base;
using IIoT.Edge.Tasks.Context;
using IIoT.Edge.Tasks.Scan.Base;
using IIoT.Edge.Tasks.Scan.Keys;

namespace IIoT.Edge.Tasks.Scan.Implementations;

/// <summary>
/// 上料扫码任务（通用）
/// 
/// 状态机：等待PLC触发 → 读取条码 → 本地查重(内置) → 额外校验(可选) → 写结果 → 等确认 → 复位
/// 
/// 设计要点：
///   1. TaskName 动态传入，同一PLC可挂多个扫码工位互不冲突
///   2. 条码来源通过 IBarcodeReader 注入（PLC直读 / 扫码枪 / 其他）
///   3. 本地查重内置，每次必走
///   4. 额外校验通过委托注入（MES查重等），可选
///   5. 支持一次读取多个条码（夹具绑定场景）
///   6. Buffer 只做信号握手，条码数据不走Buffer
/// </summary>
public class LoadingScanTask : PlcTaskBase, IScanTask
{
    private readonly string _taskName;
    public override string TaskName => _taskName;

    public string? LastScannedCode { get; private set; }
    public int CurrentStep => Step;
    public bool? LastResult { get; private set; }
    public DateTime? LastCompletedTime { get; private set; }

    // Buffer 信号位索引（只做握手）
    private readonly int _triggerIndex;
    private readonly int _responseIndex;

    // 信号约定值
    private const ushort TRIGGER_START = 11;
    private const ushort TRIGGER_IDLE = 10;
    private const ushort RESPONSE_OK = 11;
    private const ushort RESPONSE_NG = 12;
    private const ushort RESPONSE_IDLE = 10;

    private readonly IBarcodeReader _barcodeReader;
    private readonly Func<string, Task<bool>> _localDuplicateChecker;
    private readonly Func<string, Task<bool>>? _extraValidator;
    private readonly string _scanSource;

    public LoadingScanTask(
        IPlcBuffer buffer,
        ProductionContext context,
        ILogService logger,
        string taskName,
        int triggerIndex,
        int responseIndex,
        IBarcodeReader barcodeReader,
        Func<string, Task<bool>> localDuplicateChecker,
        Func<string, Task<bool>>? extraValidator = null,
        string scanSource = "上料扫码")
        : base(buffer, context, logger)
    {
        _taskName = taskName;
        _triggerIndex = triggerIndex;
        _responseIndex = responseIndex;
        _barcodeReader = barcodeReader;
        _localDuplicateChecker = localDuplicateChecker;
        _extraValidator = extraValidator;
        _scanSource = scanSource;
    }

    protected override async Task DoCoreAsync()
    {
        switch (Step)
        {
            case 0:
                if (Buffer.GetReadValue(_triggerIndex) == TRIGGER_START)
                {
                    Logger.Info($"[{Context.DeviceName}] {TaskName} 触发");
                    Step = 10;
                }
                break;

            case 10:
                var barcodes = await _barcodeReader.ReadAsync().ConfigureAwait(false);

                if (barcodes.Length == 0 || barcodes.All(string.IsNullOrEmpty))
                {
                    Logger.Warn($"[{Context.DeviceName}] {TaskName} 未读取到条码");
                    Buffer.SetWriteValue(_responseIndex, RESPONSE_NG);
                    Step = 30;
                    break;
                }

                var allOk = true;
                var scanTime = DateTime.Now;

                foreach (var barcode in barcodes)
                {
                    if (string.IsNullOrEmpty(barcode)) continue;

                    // 1. 本地查重（内置，必走）
                    var isDuplicate = await _localDuplicateChecker(barcode).ConfigureAwait(false);
                    if (isDuplicate)
                    {
                        Logger.Warn($"[{Context.DeviceName}] {TaskName} " +
                            $"条码:{barcode} 本地查重NG（重码）");
                        allOk = false;
                        continue;
                    }

                    // 2. 额外校验（可选）
                    if (_extraValidator is not null)
                    {
                        var extraOk = await _extraValidator(barcode).ConfigureAwait(false);
                        if (!extraOk)
                        {
                            Logger.Warn($"[{Context.DeviceName}] {TaskName} " +
                                $"条码:{barcode} 额外校验NG");
                            allOk = false;
                            continue;
                        }
                    }

                    // 3. 校验通过，创建电芯数据空间
                    Context.CreateCell(barcode);
                    Context.SetCell(barcode, ScanKeys.ScanResult, true);
                    Context.SetCell(barcode, ScanKeys.ScanTime, scanTime);
                    Context.SetCell(barcode, ScanKeys.ScanSource, _scanSource);
                    Context.SetCell(barcode, ScanKeys.ScanMessage, "OK");
                }

                LastScannedCode = barcodes.LastOrDefault(b => !string.IsNullOrEmpty(b));
                LastResult = allOk;
                Context.Set(ScanKeys.LastScanBarcode, LastScannedCode ?? "");
                Context.Set(ScanKeys.LastScanTime, scanTime);

                Buffer.SetWriteValue(_responseIndex, allOk ? RESPONSE_OK : RESPONSE_NG);

                Logger.Info($"[{Context.DeviceName}] {TaskName} " +
                    $"共{barcodes.Length}个条码，结果:{(allOk ? "全部OK" : "存在NG")}");
                Step = 30;
                break;

            case 30:
                if (Buffer.GetReadValue(_triggerIndex) == TRIGGER_IDLE)
                {
                    Buffer.SetWriteValue(_responseIndex, RESPONSE_IDLE);
                    LastCompletedTime = DateTime.Now;

                    Logger.Info($"[{Context.DeviceName}] {TaskName} 流程结束");
                    Step = 0;
                }
                break;
        }
    }
}