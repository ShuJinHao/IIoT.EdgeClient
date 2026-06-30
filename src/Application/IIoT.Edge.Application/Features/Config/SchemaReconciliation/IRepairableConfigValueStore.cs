namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public interface IRepairableConfigValueStore
{
    Task RepairExistingAsync(ConfigSchemaItem item, CancellationToken cancellationToken = default);
}
