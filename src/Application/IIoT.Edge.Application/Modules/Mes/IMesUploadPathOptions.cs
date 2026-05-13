namespace IIoT.Edge.Application.Modules.Mes;

/// <summary>
/// MES 标准上传场景的路径集合。这里只定义路径槽位，具体路径值仍由插件配置提供。
/// </summary>
public interface IMesUploadPathOptions
{
    /// <summary>
    /// 进站校验接口相对路径。
    /// </summary>
    string Inbound { get; }

    /// <summary>
    /// 出站/出料上传接口相对路径。
    /// </summary>
    string Outbound { get; }

    /// <summary>
    /// 配方/工艺参数上传接口相对路径。
    /// </summary>
    string Recipe { get; }

    /// <summary>
    /// 实时数据上传接口相对路径。
    /// </summary>
    string Realtime { get; }

    /// <summary>
    /// 设备状态上传接口相对路径。
    /// </summary>
    string EquipmentStatus { get; }
}
