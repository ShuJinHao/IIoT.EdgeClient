namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 通用链路可识别的标准参数槽位，不表达任何具体工序业务。
/// </summary>
public enum ModuleParamRole
{
    None,
    MesEnabled,
    MesBaseUrl,
    MesHealthPath,
    MesUpperComputerNo,
    MesOperationCode,
    MesSignToken,
    StationNo,
    /// <summary>
    /// 插件生产数据 Cloud 上传开关；不得控制系统 bootstrap、设备日志或 Cloud 补传。
    /// </summary>
    DataReadLoopIntervalMs
}
