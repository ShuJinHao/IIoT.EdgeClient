using IIoT.Edge.Application.Abstractions.Cloud;
using System.Net;

namespace IIoT.Edge.Infrastructure.Integration.Http;

/// <summary>
/// Cloud HTTP 最终安全门。系统开关关闭时在进入传输层前终止请求。
/// </summary>
public sealed class CloudExecutionPolicyHandler(
    ICloudExecutionPolicy executionPolicy) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (executionPolicy.IsEnabled)
        {
            return base.SendAsync(request, cancellationToken);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            RequestMessage = request,
            ReasonPhrase = "Cloud communication disabled"
        });
    }
}
