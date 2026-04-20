using System.Net;

namespace IIoT.Edge.Application.Abstractions.Device;

public record CloudCallResult
{
    public CloudCallOutcome Outcome { get; init; } = CloudCallOutcome.Success;

    public HttpStatusCode? HttpStatusCode { get; init; }

    public string ReasonCode { get; init; } = string.Empty;

    public bool IsSuccess => Outcome == CloudCallOutcome.Success;

    public static CloudCallResult Success()
        => new()
        {
            Outcome = CloudCallOutcome.Success,
            ReasonCode = "success"
        };

    public static CloudCallResult Failure(
        CloudCallOutcome outcome,
        string reasonCode,
        HttpStatusCode? httpStatusCode = null)
        => new()
        {
            Outcome = outcome,
            ReasonCode = reasonCode,
            HttpStatusCode = httpStatusCode
        };
}

public record CloudCallResult<T> : CloudCallResult
{
    public T? Payload { get; init; }

    public static CloudCallResult<T> Success(T? payload)
        => new()
        {
            Outcome = CloudCallOutcome.Success,
            ReasonCode = "success",
            Payload = payload
        };

    public new static CloudCallResult<T> Failure(
        CloudCallOutcome outcome,
        string reasonCode,
        HttpStatusCode? httpStatusCode = null)
        => new()
        {
            Outcome = outcome,
            ReasonCode = reasonCode,
            HttpStatusCode = httpStatusCode
        };
}
