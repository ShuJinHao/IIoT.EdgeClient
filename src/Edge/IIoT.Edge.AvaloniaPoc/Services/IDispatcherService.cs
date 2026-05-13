namespace IIoT.Edge.AvaloniaPoc.Services;

public interface IDispatcherService
{
    void Post(Action action);
}
