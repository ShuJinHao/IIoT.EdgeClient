namespace IIoT.Edge.Application.Abstractions.Device;

public sealed record CloudRequestOptions
{
    public string? IdempotencyKey { get; init; }
}
