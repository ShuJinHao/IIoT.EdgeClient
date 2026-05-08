using IIoT.Edge.SharedKernel.Context;

namespace IIoT.Edge.Runtime.Context;

internal interface IProductionContextRuntimeStateCopier
{
    void Copy(ProductionContext source, ProductionContext target);
}

internal sealed class ProductionContextRuntimeStateCopier : IProductionContextRuntimeStateCopier
{
    public void Copy(ProductionContext source, ProductionContext target)
    {
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
