using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Runtime.Base;
using IIoT.Edge.Runtime.Context;
using IIoT.Edge.Runtime.Scan.Base;
using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Runtime.Scan.Implementations;

public class LoadingScanTask : PlcTaskBase, IScanTask
{
    private static readonly TimeSpan BarcodeReadTimeout = TimeSpan.FromSeconds(3);

    private readonly string _taskName;
    private readonly int _triggerIndex;
    private readonly int _responseIndex;
    private readonly IBarcodeReader _barcodeReader;
    private readonly Func<string, Task<bool>> _localDuplicateChecker;
    private readonly Func<string, Task<bool>>? _extraValidator;

    private const ushort TriggerStart = 11;
    private const ushort TriggerIdle = 10;
    private const ushort ResponseOk = 11;
    private const ushort ResponseNg = 12;
    private const ushort ResponseIdle = 10;

    public override string TaskName => _taskName;

    public string? LastScannedCode { get; private set; }
    public int CurrentStep => Step;
    public bool? LastResult { get; private set; }
    public DateTime? LastCompletedTime { get; private set; }

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
        string scanSource = "LoadingScan")
        : base(buffer, context, logger)
    {
        _taskName = taskName;
        _triggerIndex = triggerIndex;
        _responseIndex = responseIndex;
        _barcodeReader = barcodeReader;
        _localDuplicateChecker = localDuplicateChecker;
        _extraValidator = extraValidator;
    }

    protected override async Task DoCoreAsync()
    {
        switch (Step)
        {
            case 0:
                if (Buffer.GetReadValue(_triggerIndex) == TriggerStart)
                {
                    Logger.Info($"[{Context.DeviceName}] {TaskName} triggered.");
                    Step = 10;
                }
                break;

            case 10:
                var barcodes = await TryReadBarcodesAsync().ConfigureAwait(false);
                if (barcodes is null)
                {
                    LastScannedCode = null;
                    LastResult = false;
                    Buffer.SetWriteValue(_responseIndex, ResponseNg);
                    Step = 30;
                    break;
                }

                if (barcodes.Length == 0 || barcodes.All(string.IsNullOrWhiteSpace))
                {
                    Logger.Warn($"[{Context.DeviceName}] {TaskName} did not receive any barcode.");
                    LastScannedCode = null;
                    LastResult = false;
                    Buffer.SetWriteValue(_responseIndex, ResponseNg);
                    Step = 30;
                    break;
                }

                var allOk = true;
                var scanTimeUtc = DateTime.UtcNow;

                foreach (var barcode in barcodes)
                {
                    if (string.IsNullOrWhiteSpace(barcode))
                    {
                        continue;
                    }

                    var isDuplicate = await _localDuplicateChecker(barcode).ConfigureAwait(false);
                    if (isDuplicate)
                    {
                        Logger.Warn($"[{Context.DeviceName}] {TaskName} barcode {barcode} was rejected as duplicate.");
                        allOk = false;
                        continue;
                    }

                    if (_extraValidator is not null)
                    {
                        var extraOk = await _extraValidator(barcode).ConfigureAwait(false);
                        if (!extraOk)
                        {
                            Logger.Warn($"[{Context.DeviceName}] {TaskName} barcode {barcode} failed extra validation.");
                            allOk = false;
                            continue;
                        }
                    }

                    BarcodeScanned?.Invoke(barcode, scanTimeUtc);
                }

                LastScannedCode = barcodes.LastOrDefault(static b => !string.IsNullOrWhiteSpace(b));
                LastResult = allOk;
                Buffer.SetWriteValue(_responseIndex, allOk ? ResponseOk : ResponseNg);

                Logger.Info(
                    $"[{Context.DeviceName}] {TaskName} processed {barcodes.Length} barcode(s). Result:{(allOk ? "OK" : "NG")}");
                Step = 30;
                break;

            case 30:
                if (Buffer.GetReadValue(_triggerIndex) == TriggerIdle)
                {
                    Buffer.SetWriteValue(_responseIndex, ResponseIdle);
                    LastCompletedTime = DateTime.UtcNow;
                    Logger.Info($"[{Context.DeviceName}] {TaskName} finished.");
                    Step = 0;
                }
                break;
        }
    }

    private async Task<string[]?> TryReadBarcodesAsync()
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(TaskCancellationToken);
        var readTask = _barcodeReader.ReadAsync(timeoutCts.Token);
        var timeoutTask = Task.Delay(BarcodeReadTimeout, TaskCancellationToken);
        var completedTask = await Task.WhenAny(readTask, timeoutTask).ConfigureAwait(false);

        if (completedTask != readTask)
        {
            TaskCancellationToken.ThrowIfCancellationRequested();
            timeoutCts.Cancel();
            Logger.Warn(
                $"[{Context.DeviceName}] {TaskName} barcode read timed out after {BarcodeReadTimeout.TotalSeconds:0}s.");
            return null;
        }

        return await readTask.ConfigureAwait(false);
    }
}
