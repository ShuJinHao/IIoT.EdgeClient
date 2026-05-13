using IIoT.Edge.Application.Abstractions.Events;
using IIoT.Edge.Application.Abstractions.Logging;
using MediatR;

namespace IIoT.Edge.Application.Features.Production.EventHandlers;

/// <summary>
/// Handles cell completed notifications.
/// </summary>
public class CellCompletedEventHandler : INotificationHandler<CellCompletedEvent>
{
    private readonly ILogService _logger;

    public CellCompletedEventHandler(ILogService logger)
    {
        _logger = logger;
    }

    public Task Handle(CellCompletedEvent notification, CancellationToken cancellationToken)
    {
        var cellData = notification.Record.CellData;

        var result = cellData.CellResult switch
        {
            true => "OK",
            false => "NG",
            _ => "Unknown"
        };

        _logger.Info(
            $"[UI Event] Cell completed. Label:{cellData.DisplayLabel}, Process:{cellData.ProcessType}, Result:{result}");

        return Task.CompletedTask;
    }
}
