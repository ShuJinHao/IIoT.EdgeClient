namespace IIoT.Edge.Application.Features.Config.SchemaReconciliation;

public interface IConfigSchemaReconciler
{
    Task ReconcileAsync(CancellationToken cancellationToken = default);
}
