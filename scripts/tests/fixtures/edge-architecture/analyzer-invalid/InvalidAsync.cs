namespace IIoT.Edge.ArchitectureFixtures;

internal sealed class InvalidAsync
{
    internal async void Start()
    {
        await Task.Yield();
    }
}
