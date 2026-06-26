namespace IIoT.Edge.Module.DieCutting.Production;

public interface IDieCuttingProductionRecordStore
{
    Task AddAsync(DieCuttingProductionRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DieCuttingProductionRecord>> QueryAsync(
        string moduleId,
        string selectedDeviceKey,
        int limit = 500,
        CancellationToken cancellationToken = default);
}
