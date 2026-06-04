using IIoT.Edge.SharedKernel.DataPipeline;

using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
namespace IIoT.Edge.Application.Abstractions.Cloud;

public interface ICloudFallbackBufferStore : IFallbackBufferStore<CloudFallbackRecord>
{
}
