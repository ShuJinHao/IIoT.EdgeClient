using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

namespace IIoT.Edge.Shell.Core;

public sealed class CriticalPersistenceFallbackWriter : ICriticalPersistenceFallbackWriter
{
    public void Write(string source, string details, Exception? exception = null)
        => CrashLogWriter.Write(source, exception, details);
}
