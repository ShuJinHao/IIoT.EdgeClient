using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.Runtime.Context;
using IIoT.Edge.Runtime.Scan.Base;

namespace IIoT.Edge.Runtime.Scan.Implementations;

/// <summary>
/// 上料扫码任务（通用，完全解耦）
/// 
/// 状态机：等待 PLC 触发、读取条码、本地查重、可选额外校验、写结果、确认完成并复位
/// 
/// 扫码任务只返回条码字符串，不操作 CellData，不依赖任何工序类型
/// 流程状态机拿到条码后自己创建强类型 CellData 放进 Context
/// </summary>
public class LoadingScanTask : PlcTaskBase, IScanTask
{
    private readonly string _taskName;
    public override string TaskName => _taskName;

    public string? LastScannedCode { get; private set; }
    public int CurrentStep => Step;
    public bool? LastResult { get; private set; }
    public DateTime? LastCompletedTime { get; private set; }

    private readonly int _triggerIndex;
    private readonly int _responseIndex;

    private const ushort TRIGGER_START = 11;
    private const ushort TRIGGER_IDLE = 10;
    private const ushort RESPONSE_OK = 11;
    private const ushort RESPONSE_NG = 12;
    private const ushort RESPONSE_IDLE = 10;

    private readonly IBarcodeReader _barcodeReader;
    private readonly Func<string, Task<bool>> _localDuplicateChecker;
    private readonly Func<string, Task<bool>>? _extraValidator;
    private readonly string _scanSource;

    /// <summary>
    /// 扫码成功后触发，外部（流程状态机）订阅此事件拿条码
    /// </summary>
    public event Action<string, DateTime>? BarcodeScanned;

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

                    // 1. 本地查重
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

                    // 3. 通知外部：扫码成功，流程状态机负责创建 CellData
                    BarcodeScanned?.Invoke(barcode, scanTime);
                }

                LastScannedCode = barcodes.LastOrDefault(b => !string.IsNullOrEmpty(b));
                LastResult = allOk;

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
