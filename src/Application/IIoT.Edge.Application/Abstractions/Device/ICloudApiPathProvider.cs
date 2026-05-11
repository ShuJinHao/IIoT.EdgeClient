namespace IIoT.Edge.Application.Abstractions.Device;

/// <summary>
/// 云端 API 路径提供器契约。
/// 为上层提供 Cloud 上传和查询相关接口路径。
/// </summary>
public interface ICloudApiPathProvider
{
    string GetProcessUploadPath();
    string GetCapacityHourlyPath();
    string GetCapacitySummaryPath();
    string GetCapacitySummaryRangePath();
}
