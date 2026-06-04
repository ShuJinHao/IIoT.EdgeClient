namespace IIoT.Edge.Application.Abstractions.Cloud;

public sealed record CloudRequestOptions
{
    public string? IdempotencyKey { get; init; }
}
