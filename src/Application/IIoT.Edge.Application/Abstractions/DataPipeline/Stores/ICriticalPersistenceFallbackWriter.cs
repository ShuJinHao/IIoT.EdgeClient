namespace IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

public interface ICriticalPersistenceFallbackWriter
{
    void Write(string source, string details, Exception? exception = null);
}
