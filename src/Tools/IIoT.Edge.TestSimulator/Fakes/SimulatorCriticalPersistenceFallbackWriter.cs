using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Logging;

namespace IIoT.Edge.TestSimulator.Fakes;

public sealed class SimulatorCriticalPersistenceFallbackWriter : ICriticalPersistenceFallbackWriter
{
    private readonly ILogService _logger;

    public SimulatorCriticalPersistenceFallbackWriter(ILogService logger)
    {
        _logger = logger;
    }

    public void Write(string source, string details, Exception? exception = null)
    {
        var message = $"[SimulatorCriticalFallback] source={source} details={details}";
        if (exception is null)
        {
            _logger.Fatal(message);
            return;
        }

        _logger.Fatal($"{message} exception={exception.Message}");
    }
}
