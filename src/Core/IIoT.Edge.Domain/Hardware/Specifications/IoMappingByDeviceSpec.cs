using IIoT.Edge.Common.Specification;
using IIoT.Edge.Domain.Hardware.Aggregates;

namespace IIoT.Edge.Domain.Hardware.Specifications;

public class IoMappingByDeviceSpec : Specification<IoMappingEntity>
{
    public IoMappingByDeviceSpec(int networkDeviceId)
    {
        FilterCondition = x => x.NetworkDeviceId == networkDeviceId;
        AddInclude(x => x.NetworkDevice);
        SetOrderBy(x => x.SortOrder);
    }

    public IoMappingByDeviceSpec(int networkDeviceId, int skip, int take)
        : this(networkDeviceId)
    {
        SetPaging(skip, take);
    }
}