namespace IIoT.Edge.ArchitectureFixtures;

internal sealed class InvalidAsync
{
    internal async void Start()
    {
        await Task.Yield();
    }

    internal int BlockOnTask(Task<int> task)
        => task.GetAwaiter().GetResult();
}
