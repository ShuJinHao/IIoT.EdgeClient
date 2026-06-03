namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public interface IConfigSchemaSource
{
    string SchemaId { get; }

    Task<IReadOnlyCollection<ConfigSchemaItem>> GetItemsAsync(CancellationToken cancellationToken = default);
}
