using IIoT.Edge.Application.Modules.Mes;

namespace IIoT.Edge.Module.Homogenization.Config;

/// <summary>
/// 匀浆 MES 接口配置。
/// </summary>
public sealed class HomogenizationMesOptions
{
    /// <summary>
    /// MES 签名令牌，用于生成接口 sign 字段。
    /// </summary>
    public string SignToken { get; set; } = string.Empty;

    /// <summary>
    /// 各 MES 接口相对路径。
    /// </summary>
    public HomogenizationMesPathOptions Paths { get; set; } = new();
}

/// <summary>
/// 匀浆 MES 接口相对路径配置。
/// </summary>
public sealed class HomogenizationMesPathOptions : IMesUploadPathOptions
{
    /// <summary>
    /// 进站校验接口路径。
    /// </summary>
    public string Inbound { get; set; } = string.Empty;

    /// <summary>
    /// 出料数据接口路径。
    /// </summary>
    public string Outbound { get; set; } = string.Empty;

    /// <summary>
    /// 配方参数接口路径。
    /// </summary>
    public string Recipe { get; set; } = string.Empty;

    /// <summary>
    /// 实时数据接口路径。
    /// </summary>
    public string Realtime { get; set; } = string.Empty;

    /// <summary>
    /// 设备状态接口路径。
    /// </summary>
    public string EquipmentStatus { get; set; } = string.Empty;

    internal void AppendValidationErrors(ICollection<string> errors)
    {
        HomogenizationOptionValidation.Require(Inbound, "MES 进站接口路径", errors);
        HomogenizationOptionValidation.Require(Outbound, "MES 出料接口路径", errors);
        HomogenizationOptionValidation.Require(Recipe, "MES 工艺参数接口路径", errors);
        HomogenizationOptionValidation.Require(Realtime, "MES 实时数据接口路径", errors);
        HomogenizationOptionValidation.Require(EquipmentStatus, "MES 设备状态接口路径", errors);
    }
}
