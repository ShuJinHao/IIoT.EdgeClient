using IIoT.Edge.Application.Abstractions.DataPipeline;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Module.ScanCaptureStarter.Constants;
using IIoT.Edge.Runtime.Scan.Implementations;
using IIoT.Edge.Runtime.Signals;
using IIoT.Edge.SharedKernel.Context;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.Module.ScanCaptureStarter.Runtime;

public sealed class StarterStationRuntimeFactory : IStationRuntimeFactory
{
    public string ModuleId => StarterModuleConstants.ModuleId;

    public List<IPlcTask> CreateTasks(
        IServiceProvider serviceProvider,
        IPlcBuffer buffer,
        ProductionContext context)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(context);

        var logger = serviceProvider.GetRequiredService<ILogService>();
        var pipelineService = serviceProvider.GetRequiredService<IDataPipelineService>();
        var barcodeReaderFactory = serviceProvider.GetRequiredService<IBarcodeReaderFactory>();

        var barcodeReader = context.DeviceId > 0
            ? barcodeReaderFactory.Create(context.DeviceId, StarterBarcodeReaderProfile.DefaultOptions)
            : new EmptyBarcodeReader();
        var signalAccessor = BufferLogicalSignalAccessor.Create(
            buffer,
            context,
            StarterPlcSignalProfile.LogicalSignals);

        var loadingScanTask = new LoadingScanTask(
            buffer,
            signalAccessor,
            context,
            logger,
            StarterModuleConstants.ScanTaskName,
            StarterPlcSignalProfile.ReadSignals[0].Label,
            StarterPlcSignalProfile.WriteSignals[0].Label,
            barcodeReader,
            barcode => Task.FromResult(IsDuplicate(context, barcode)));

        loadingScanTask.BarcodeScanned += (barcode, observedAt) =>
        {
            context.Set(StarterModuleConstants.LastScannedBarcodeKey, barcode);
            context.Set(StarterModuleConstants.LastScannedAtKey, observedAt);
            context.Set(StarterModuleConstants.PendingBarcodeKey, barcode);
            context.Set(StarterModuleConstants.PendingBarcodeObservedAtKey, observedAt);
        };

        return
        [
            loadingScanTask,
            new StarterSignalCaptureTask(
                buffer,
                signalAccessor,
                context,
                pipelineService,
                logger)
        ];
    }

    private static bool IsDuplicate(ProductionContext context, string barcode)
        => context.HasCell(barcode)
            || string.Equals(
                context.Get<string>(StarterModuleConstants.PendingBarcodeKey),
                barcode,
                StringComparison.OrdinalIgnoreCase);

    private sealed class EmptyBarcodeReader : IBarcodeReader
    {
        public Task<string[]> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Array.Empty<string>());
    }
}
