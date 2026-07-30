using IIoT.Edge.Module.Contracts.Runtime;

namespace IIoT.Edge.Host.DataPipeline.Context;

internal interface IProductionContextRuntimeStateCopier
{
    void Copy(ProductionContext source, ProductionContext target);
}

internal sealed class ProductionContextRuntimeStateCopier : IProductionContextRuntimeStateCopier
{
    public void Copy(ProductionContext source, ProductionContext target)
    {
        target.PlcCode = source.PlcCode;
        target.DeviceName = source.DeviceName;
        target.NetworkDeviceId = source.NetworkDeviceId;
        target.TodayCapacity = source.TodayCapacity;

        foreach (var entry in source.StepStateEntries)
        {
            target.StepStateEntries[entry.Key] = entry.Value;
        }

        foreach (var entry in source.DeviceBagEntries)
        {
            target.DeviceBagEntries[entry.Key] = entry.Value;
        }

        foreach (var entry in source.CurrentCellEntries)
        {
            target.CurrentCellEntries[entry.Key] = entry.Value;
        }
    }
}
