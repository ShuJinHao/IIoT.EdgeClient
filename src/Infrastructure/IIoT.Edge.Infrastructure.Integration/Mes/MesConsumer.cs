using IIoT.Edge.Application.Common.Identity;
using IIoT.Edge.Module.Contracts.DataPipeline;
using IIoT.Edge.Module.Contracts.Identity;
using IIoT.Edge.Module.Contracts.Logging;
using IIoT.Edge.Module.Contracts.Mes;
using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Infrastructure.Integration;
using IIoT.Edge.Module.Contracts.DataPipeline.CellData;

namespace IIoT.Edge.Infrastructure.Integration.Mes;

public sealed class MesConsumer : IMesConsumer
{
#pragma warning disable CS0618 // Exact v2 ABI adapter surface; formal v3 records never resolve this uploader set.
    private readonly IMesUploadGate _uploadGate;
    private readonly IProcessIntegrationRegistry _processIntegrationRegistry;
    private readonly IMesUploadDiagnosticsStore _diagnosticsStore;
    private readonly ILogService _logger;
    private readonly IReadOnlyDictionary<string, IProcessMesUploaderV3> _v3Uploaders;
    private readonly IReadOnlyDictionary<string, IProcessMesUploader> _legacyUploaders;
    private readonly IDevicePluginRuntimeContext? _runtimeContext;

    public string Name => "MES";
    public int Order => 20;
    public ConsumerFailureMode FailureMode => ConsumerFailureMode.Durable;
    public DataPipelineRetryChannel RetryChannel => DataPipelineRetryChannel.Mes;

    public MesConsumer(
        IMesUploadGate uploadGate,
        IEnumerable<IProcessMesUploader> uploaders,
        IProcessIntegrationRegistry processIntegrationRegistry,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ILogService logger)
    {
        _uploadGate = uploadGate;
        _processIntegrationRegistry = processIntegrationRegistry;
        _diagnosticsStore = diagnosticsStore;
        _logger = logger;
        _legacyUploaders = uploaders.ToDictionary(
            uploader => uploader.ProcessType,
            StringComparer.OrdinalIgnoreCase);
        _v3Uploaders = new Dictionary<string, IProcessMesUploaderV3>(
            StringComparer.OrdinalIgnoreCase);
    }

    public MesConsumer(
        IMesUploadGate uploadGate,
        IEnumerable<IProcessMesUploaderV3> v3Uploaders,
        IProcessIntegrationRegistry processIntegrationRegistry,
        IMesUploadDiagnosticsStore diagnosticsStore,
        ILogService logger,
        IDevicePluginRuntimeContext runtimeContext,
        IEnumerable<IProcessMesUploader>? legacyUploaders = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        _uploadGate = uploadGate;
        _processIntegrationRegistry = processIntegrationRegistry;
        _diagnosticsStore = diagnosticsStore;
        _logger = logger;
        _runtimeContext = runtimeContext;
        _v3Uploaders = v3Uploaders.ToDictionary(
            uploader => uploader.ProcessType,
            StringComparer.OrdinalIgnoreCase);
        _legacyUploaders = (legacyUploaders ?? [])
            .ToDictionary(
                uploader => uploader.ProcessType,
                StringComparer.OrdinalIgnoreCase);
    }
#pragma warning restore CS0618

    public async Task<bool> ProcessAsync(CellCompletedRecord record, CancellationToken cancellationToken = default)
    {
        if (!record.CellData.UploadTargets.HasFlag(DataPipelineUploadTargets.Mes))
        {
            return true;
        }

        var processType = !string.IsNullOrWhiteSpace(record.ProcessType)
            ? record.ProcessType.Trim()
            : record.CellData.ProcessType;
        var logContext = UploadTraceLogFormatter.Format(record, "MES");
        var identityKind = DataPipelineRecordIdentityClassifier.Classify(record);
        var v3IdentityFailure = ValidateV3Identity(
            record,
            identityKind,
            _runtimeContext?.Current);
        if (v3IdentityFailure is not null)
        {
            _diagnosticsStore.RecordFailure(
                processType,
                v3IdentityFailure,
                UploadDiagnosticsContextFactory.CreateMesContext(record));
            _logger.Error($"{logContext}[MES直传] 结果=Failed，原因码={v3IdentityFailure}。");
            return false;
        }
        var isRegistered = _processIntegrationRegistry.HasMesUploader(processType);
        if (!isRegistered)
        {
            const string reason = "uploader_not_registered";
            _diagnosticsStore.RecordFailure(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            _logger.Error(
                $"{logContext}[MES直传] 结果=Failed，原因码={reason}。");
            return false;
        }

        var gate = _uploadGate.GetSnapshot();
        if (!gate.CanUpload)
        {
            var reason = string.IsNullOrWhiteSpace(gate.ReasonCode)
                ? "mes_upload_gate_blocked"
                : gate.ReasonCode;
            _diagnosticsStore.RecordBlocked(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            _logger.Warn(
                $"{logContext}[MES直传] 结果=Blocked，原因码=mes_upload_gate_blocked；" +
                "将交接到 MES 持久补偿链。");
            return false;
        }

        MesCallResult result;
        if (identityKind == DataPipelineRecordIdentityKind.CompleteV3)
        {
            if (!_v3Uploaders.TryGetValue(processType, out var uploader))
            {
                const string reason = "mes_v3_uploader_required";
                _diagnosticsStore.RecordFailure(
                    processType,
                    reason,
                    UploadDiagnosticsContextFactory.CreateMesContext(record));
                _logger.Error(
                    $"{logContext}[MES直传] 结果=Failed，原因码={reason}。");
                return false;
            }

            var identity = _runtimeContext!.Current;
            var uploadContext = new DevicePluginUploadContext(
                new DevicePluginIdentity(
                    identity.ClientCode,
                    identity.ModuleId,
                    identity.ProcessType));
            result = await uploader
                .UploadAsync(uploadContext, [record], cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            if (!_legacyUploaders.TryGetValue(processType, out var uploader))
            {
                const string reason = "uploader_not_found";
                _diagnosticsStore.RecordFailure(
                    processType,
                    reason,
                    UploadDiagnosticsContextFactory.CreateMesContext(record));
                _logger.Error(
                    $"{logContext}[MES直传] 结果=Failed，原因码={reason}。");
                return false;
            }

#pragma warning disable CS0618 // v2 ABI compatibility path; formal v3 never enters this branch.
            var uploadContext = new ProcessUploadContext(
                new IIoT.Edge.Module.Contracts.Device.DeviceSession
                {
                    DeviceName = record.ResolveDeviceName()
                });
            result = await uploader
                .UploadAsync(uploadContext, [record], cancellationToken)
                .ConfigureAwait(false);
#pragma warning restore CS0618
        }
        if (result.Outcome == MesCallOutcome.Success)
        {
            _diagnosticsStore.RecordSuccess(processType, UploadDiagnosticsContextFactory.CreateMesContext(record));
            _logger.Info($"{logContext}[MES直传] 结果=Uploaded。");
            return true;
        }

        if (result.Outcome == MesCallOutcome.Disabled)
        {
            const string reason = "mes_uploader_disabled";
            _diagnosticsStore.RecordBlocked(processType, reason, UploadDiagnosticsContextFactory.CreateMesContext(record));
            _logger.Warn(
                $"{logContext}[MES直传] 结果=Blocked，原因码={reason}；" +
                "将交接到 MES 持久补偿链。");
            return false;
        }

        var failureReason = UploadTraceLogFormatter.ReasonCode("mes_upload", result.Outcome);
        _diagnosticsStore.RecordFailure(processType, failureReason, UploadDiagnosticsContextFactory.CreateMesContext(record));
        _logger.Error(
            $"{logContext}[MES直传] 结果=Failed，原因码={failureReason}；" +
            "将交接到 MES 持久补偿链。");
        return false;
    }

    private static string? ValidateV3Identity(
        CellCompletedRecord record,
        DataPipelineRecordIdentityKind identityKind,
        DevicePluginRuntimeIdentity? runtimeIdentity)
    {
        if (identityKind == DataPipelineRecordIdentityKind.LegacyV2)
        {
            return null;
        }

        if (identityKind == DataPipelineRecordIdentityKind.IncompleteV3)
        {
            return "mes_v3_identity_incomplete";
        }

        if (runtimeIdentity is null || !runtimeIdentity.IsV3)
        {
            return "mes_v3_runtime_identity_required";
        }

        if (string.IsNullOrWhiteSpace(runtimeIdentity.ClientCode)
            || !string.Equals(
                record.ClientCode.Trim(),
                runtimeIdentity.ClientCode.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return "mes_v3_client_code_mismatch";
        }

        if (!string.Equals(
                record.ProcessType.Trim(),
                runtimeIdentity.ProcessType,
                StringComparison.Ordinal) ||
            !string.Equals(
                record.ModuleId.Trim(),
                runtimeIdentity.ModuleId,
                StringComparison.Ordinal))
        {
            return "mes_v3_plugin_identity_mismatch";
        }

        return null;
    }
}
