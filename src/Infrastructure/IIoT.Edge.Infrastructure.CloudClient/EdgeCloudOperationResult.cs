namespace IIoT.Edge.Infrastructure.CloudClient;

public enum EdgeCloudFailureKind
{
    None,
    HttpFailure,
    EmptyPayload,
    Timeout,
    NetworkFailure,
    UnexpectedFailure,
    Cancelled
}

public sealed record EdgeCloudOperationResult<T>(
    bool Success,
    T? Value = default,
    EdgeCloudFailureKind FailureKind = EdgeCloudFailureKind.None,
    int? StatusCode = null,
    string? ErrorMessage = null,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public IReadOnlyDictionary<string, string> ResponseHeaders { get; } =
        Headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static EdgeCloudOperationResult<T> Succeeded(
        T value,
        IReadOnlyDictionary<string, string>? headers = null)
        => new(true, value, Headers: headers);

    public static EdgeCloudOperationResult<T> Failed(
        EdgeCloudFailureKind failureKind,
        string? errorMessage,
        int? statusCode = null,
        IReadOnlyDictionary<string, string>? headers = null)
        => new(false, FailureKind: failureKind, StatusCode: statusCode, ErrorMessage: errorMessage, Headers: headers);
}

public sealed record EdgeCloudOperationResult(
    bool Success,
    EdgeCloudFailureKind FailureKind = EdgeCloudFailureKind.None,
    int? StatusCode = null,
    string? ErrorMessage = null,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public IReadOnlyDictionary<string, string> ResponseHeaders { get; } =
        Headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static EdgeCloudOperationResult Succeeded(IReadOnlyDictionary<string, string>? headers = null)
        => new(true, Headers: headers);

    public static EdgeCloudOperationResult Failed(
        EdgeCloudFailureKind failureKind,
        string? errorMessage,
        int? statusCode = null,
        IReadOnlyDictionary<string, string>? headers = null)
        => new(false, failureKind, statusCode, errorMessage, headers);
}
