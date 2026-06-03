namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public sealed record ConfigSchemaItem(
    string Key,
    string DefaultValue,
    IReadOnlyDictionary<string, string>? Metadata = null);
