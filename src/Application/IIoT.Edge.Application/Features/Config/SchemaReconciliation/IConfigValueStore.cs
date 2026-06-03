namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public interface IConfigValueStore
{
    string SchemaId { get; }

    Task<IReadOnlyCollection<string>> GetExistingKeysAsync(CancellationToken cancellationToken = default);

    Task InsertAsync(ConfigSchemaItem item, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
