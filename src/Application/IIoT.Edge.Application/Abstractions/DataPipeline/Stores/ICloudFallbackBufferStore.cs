using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

public interface ICloudFallbackBufferStore : IFallbackBufferStore<CloudFallbackRecord>
{
}
