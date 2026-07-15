using IIoT.Edge.Application.Abstractions.Cloud;
using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Common.Device;
using IIoT.Edge.Infrastructure.Integration.Config;

namespace IIoT.Edge.Infrastructure.Integration.EdgeHost;

public sealed class EdgeHostPlcRuntimeStateReporter(
    IEdgeHostPlcRuntimeStateSnapshotProvider snapshotProvider,
    ICloudHttpClient cloudHttp,
    ICloudApiEndpointProvider endpointProvider,
    IDeviceService deviceService,
    ILocalSystemRuntimeConfigService runtimeConfig,
    ILogService logger) : IEdgeHostPlcRuntimeStateReporter
{
    public async Task<EdgeHostPlcRuntimeStateReportResult> ReportOnceAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!runtimeConfig.Current.SystemCloudEnabled)
            {
                return EdgeHostPlcRuntimeStateReportResult.Skipped("cloud_disabled");
            }

            var session = await ResolveDeviceSessionAsync(cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                return EdgeHostPlcRuntimeStateReportResult.Skipped("device_unidentified");
            }

            if (!deviceService.CanUploadToCloud)
            {
                return EdgeHostPlcRuntimeStateReportResult.Skipped(
                    deviceService.CurrentUploadGate.Reason.ToReasonCode());
            }

            var states = await snapshotProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
            var payload = new EdgeHostPlcRuntimeStateReport(
                session.DeviceId,
                session.ClientCode,
                DateTime.UtcNow,
                states);
            var result = await cloudHttp
                .PostAsync(
                    endpointProvider.GetEdgeHostPlcRuntimeStatesPath(),
                    payload,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess)
            {
                return EdgeHostPlcRuntimeStateReportResult.Succeeded(states.Count);
            }

            return EdgeHostPlcRuntimeStateReportResult.Failed(result.ReasonCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Debug($"[PLC 状态上报] 本轮执行异常，已跳过：{ex.Message}");
            return EdgeHostPlcRuntimeStateReportResult.Failed("exception");
        }
    }

    private async Task<DeviceSession?> ResolveDeviceSessionAsync(CancellationToken cancellationToken)
    {
        var session = deviceService.CurrentDevice;
        if (session is not null && session.DeviceId != Guid.Empty)
        {
            return session;
        }

        try
        {
            await deviceService.RefreshBootstrapAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Debug($"[PLC 状态上报] Bootstrap 刷新失败，跳过本轮：{ex.Message}");
            return null;
        }

        session = deviceService.CurrentDevice;
        return session is not null && session.DeviceId != Guid.Empty ? session : null;
    }
}
