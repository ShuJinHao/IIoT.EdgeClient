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
    CloudEnabled
}
