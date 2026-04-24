using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Module.Homogenization.Payload;

namespace IIoT.Edge.Module.Homogenization.Integration;

public interface IHomogenizationMesApiService
{
    Task<MesCallResult> UploadInboundAsync(
        DeviceSession? device,
        string trayCode,
        CancellationToken cancellationToken = default);

    Task<MesCallResult> UploadOutboundAsync(
        DeviceSession? device,
        HomogenizationCellData cellData,
        CancellationToken cancellationToken = default);

    Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        HomogenizationRealtimeSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<MesCallResult> UploadRecipeAsync(
        DeviceSession? device,
        HomogenizationRecipeSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<MesCallResult> UploadEquipmentStatusAsync(
        DeviceSession? device,
        HomogenizationEquipmentStatusSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
