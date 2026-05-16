using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;

namespace IIoT.Edge.Host.Bootstrap;

public sealed class CriticalPersistenceFallbackWriter : ICriticalPersistenceFallbackWriter
{
    private readonly ICrashLogWriter _crashLogWriter;

    public CriticalPersistenceFallbackWriter(ICrashLogWriter crashLogWriter)
    {
        _crashLogWriter = crashLogWriter ?? throw new ArgumentNullException(nameof(crashLogWriter));
    }

    public void Write(string source, string details, Exception? exception = null)
        => _crashLogWriter.Write(source, exception, details);
}
