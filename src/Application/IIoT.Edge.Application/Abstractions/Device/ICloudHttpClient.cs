namespace IIoT.Edge.Application.Abstractions.Device;

/// <summary>
/// 云端 HTTP 客户端接口。
///
/// 封装对云端 API 的通用 GET 和 POST 调用。
/// 所有需要访问云端 API 的组件都应依赖该抽象。
/// </summary>
public interface ICloudHttpClient
{
    /// <summary>
    /// 向指定地址提交 JSON 数据。
    /// </summary>
    /// <returns>返回云端调用结果，不抛出异常。</returns>
    Task<CloudCallResult> PostAsync(
        string url,
        object payload,
        CloudRequestOptions? options = null);

    /// <summary>
    /// 向指定地址提交 JSON 数据，并返回响应内容。
    /// </summary>
    /// <returns>返回云端调用结果及响应内容，不抛出异常。</returns>
    Task<CloudCallResult<string>> PostWithResponseAsync(
        string url,
        object payload,
        CloudRequestOptions? options = null);

    /// <summary>
    /// 发起 GET 请求。
    /// </summary>
    /// <returns>返回云端调用结果及响应内容，不抛出异常。</returns>
    Task<CloudCallResult<string>> GetAsync(
        string url,
        CloudRequestOptions? options = null);
}
