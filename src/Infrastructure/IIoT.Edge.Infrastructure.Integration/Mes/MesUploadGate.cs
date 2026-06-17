using IIoT.Edge.Application.Abstractions.Config;

using IIoT.Edge.Application.Abstractions.Mes;
using IIoT.Edge.Application.Abstractions.Shared;
namespace IIoT.Edge.Infrastructure.Integration.Mes;

public sealed class MesUploadGate : IMesUploadGate
{
    private readonly ILocalSystemRuntimeConfigService _runtimeConfig;
    private readonly IExternalHeartbeatStateStore _heartbeatStateStore;

    public MesUploadGate(
        ILocalSystemRuntimeConfigService runtimeConfig,
        IExternalHeartbeatStateStore heartbeatStateStore)
    {
        _runtimeConfig = runtimeConfig;
        _heartbeatStateStore = heartbeatStateStore;
    }

    public ExternalSystemKind System => ExternalSystemKind.Mes;

    public UploadGateSnapshot GetSnapshot()
    {
        if (!_runtimeConfig.Current.MesUploadEnabled)
        {
            return UploadGateSnapshot.Blocked(System, "mes_upload_disabled", "MES 上传已被配置关闭。");
        }

        var heartbeat = _heartbeatStateStore.Get(ExternalSystemKind.Mes);
        if (heartbeat.IsReady)
        {
            return UploadGateSnapshot.Ready(System);
        }

        return UploadGateSnapshot.Blocked(
            System,
            string.IsNullOrWhiteSpace(heartbeat.ReasonCode) ? "mes_heartbeat_not_ready" : heartbeat.ReasonCode,
            heartbeat.Message);
    }
}
