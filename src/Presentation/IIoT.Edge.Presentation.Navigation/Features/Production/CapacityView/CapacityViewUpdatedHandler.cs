using IIoT.Edge.Application.Abstractions.Events;
using MediatR;

namespace IIoT.Edge.Presentation.Navigation.Features.Production.CapacityView;

public class CapacityViewUpdatedHandler(CapacityViewModel viewModel)
    : INotificationHandler<CapacityUpdatedNotification>
{
    public Task Handle(CapacityUpdatedNotification notification, CancellationToken cancellationToken)
    {
        viewModel.OnCapacityUpdated();
        return Task.CompletedTask;
    }
}
