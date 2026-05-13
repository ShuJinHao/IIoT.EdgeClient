using IIoT.Edge.Application.Abstractions.Events;
using MediatR;

namespace IIoT.Edge.Presentation.Panels.Features.Equipment;

public class EquipmentCapacityUpdatedHandler(EquipmentViewModel viewModel)
    : INotificationHandler<CapacityUpdatedNotification>
{
    public Task Handle(CapacityUpdatedNotification notification, CancellationToken cancellationToken)
    {
        viewModel.OnCapacityUpdated();
        return Task.CompletedTask;
    }
}
