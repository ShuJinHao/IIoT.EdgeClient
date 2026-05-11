using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules.Cloud;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.Homogenization.Integration;

/// <summary>
/// 匀浆 Cloud 上传器。插件只负责匀浆工序 payload 映射，HTTP 发送、幂等键和补偿由统一 Cloud 通道处理。
/// </summary>
public sealed class HomogenizationCloudUploader
    : CloudUploadChannelBase<HomogenizationCellData, HomogenizationProcessRecordsCloudPayload>
{
    private const int SchemaVersion = 1;

    public HomogenizationCloudUploader(
        ICloudApiPathProvider cloudApiPathProvider,
        ICloudHttpClient cloudHttp,
        ILogService logger)
        : base(
            HomogenizationModuleIdentity.ProcessType,
            ProcessUploadMode.Batch,
            cloudApiPathProvider.GetProcessUploadPath(),
            cloudHttp,
            logger)
    {
    }

    protected override HomogenizationProcessRecordsCloudPayload BuildPayload(
        ProcessCloudUploadContext context,
        IReadOnlyList<HomogenizationCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records)
        => new(
            TypeKey: HomogenizationModuleIdentity.ProcessType,
            ProcessType: HomogenizationModuleIdentity.ProcessType,
            SchemaVersion: SchemaVersion,
            DeviceId: context.Device.DeviceId,
            Records: cellData.Select(data => BuildRecordPayload(context, data)).ToArray());

    private static HomogenizationProcessRecordCloudPayload BuildRecordPayload(
        ProcessCloudUploadContext context,
        HomogenizationCellData data)
        => new(
            TypeKey: HomogenizationModuleIdentity.ProcessType,
            ProcessType: HomogenizationModuleIdentity.ProcessType,
            SchemaVersion: SchemaVersion,
            DeviceId: context.Device.DeviceId,
            Barcode: data.TrayCode,
            CellResult: data.CellResult,
            CompletedTime: data.CompletedTime,
            Payload: BuildBusinessPayload(data));

    private static HomogenizationProcessRecordBusinessCloudPayload BuildBusinessPayload(HomogenizationCellData data)
        => new(
            PlcName: data.DeviceName,
            DeviceCode: data.DeviceCode,
            InboundTime: data.InboundTime,
            RuntimeStatus: data.RuntimeStatus,
            RealtimeSnapshot: data.RealtimeSnapshot,
            RecipeSnapshot: data.RecipeSnapshot,
            EquipmentStatusSnapshot: data.EquipmentStatusSnapshot,
            CntActualKg: data.CntActualKg,
            CntTargetKg: data.CntTargetKg,
            CntTankAWeightKg: data.CntTankAWeightKg,
            CntTankBWeightKg: data.CntTankBWeightKg,
            NmpActualKg: data.NmpActualKg,
            NmpTargetKg: data.NmpTargetKg,
            GlueActualKg: data.GlueActualKg,
            SetStirringTimeMinutes: data.SetStirringTimeMinutes,
            RemainingStirringTimeMinutes: data.RemainingStirringTimeMinutes,
            SetDispersionTimeMinutes: data.SetDispersionTimeMinutes,
            RemainingDispersionTimeMinutes: data.RemainingDispersionTimeMinutes,
            BatchNumber: data.BatchNumber,
            MainBatchPlan: data.MainBatchPlan);
}
