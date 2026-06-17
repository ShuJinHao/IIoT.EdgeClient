using IIoT.Edge.SharedKernel.DataPipeline;

using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
namespace IIoT.Edge.Application.Abstractions.Mes;

public interface IMesFallbackBufferStore : IFallbackBufferStore<MesFallbackRecord>
{
}
