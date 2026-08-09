using IIoT.Edge.Module.Contracts.Cloud;
using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Module.Contracts.Device;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Plc;
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

            var authoritative = snapshotProvider is IAuthoritativePlcSnapshotProvider authoritativeProvider
                ? await authoritativeProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
                : null;
            if (authoritative is { IsAuthoritative: false })
            {
                logger.Debug(
                    $"[PLC 状态上报] 权威快照不可用，已跳过本轮：{authoritative.UnavailableReason ?? "unknown"}");
                return EdgeHostPlcRuntimeStateReportResult.Skipped("plc_snapshot_unavailable");
            }

            var states = authoritative is null
                ? await snapshotProvider.GetCurrentAsync(cancellationToken).ConfigureAwait(false)
                : Map(authoritative.Items);
            var payload = new EdgeHostPlcRuntimeStateReport(
                session.DeviceId,
                session.ClientCode,
                DateTime.UtcNow,
                states)
            {
                Authority = authoritative?.IsAuthoritative ?? true,
                Status = authoritative?.Status ?? AuthoritativePlcSnapshotStatus.Authoritative,
                ConfigurationVersion = authoritative?.ConfigurationVersion ?? 0,
                CapturedAtUtc = authoritative?.CapturedAtUtc ?? DateTimeOffset.UtcNow,
                ClearProjection = authoritative?.ClearProjection ?? false,
                UnavailableReason = authoritative?.UnavailableReason
            };
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

    private static IReadOnlyList<EdgeHostPlcRuntimeStateReportItem> Map(
        IReadOnlyList<AuthoritativePlcSnapshotItem> items)
        => items.Select(static item => new EdgeHostPlcRuntimeStateReportItem(
                item.PlcCode,
                item.PlcName,
                item.ConnectionState == PlcConnectionState.Connected,
                item.ConnectionState.ToString(),
                item.LastRealCommunicationAtUtc?.UtcDateTime,
                Protocol: item.Protocol,
                Address: string.IsNullOrWhiteSpace(item.IpAddress)
                    ? null
                    : item.Port.HasValue
                        ? $"{item.IpAddress.Trim()}:{item.Port.Value}"
                        : item.IpAddress.Trim(),
                LastError: item.LastError))
            .ToArray();

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
