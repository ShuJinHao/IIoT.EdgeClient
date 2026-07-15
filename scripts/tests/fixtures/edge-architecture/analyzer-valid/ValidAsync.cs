namespace IIoT.Edge.ArchitectureFixtures;

internal static class ValidAsync
{
    internal static async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken);
    }

    internal static string ReadBusinessResult(BusinessOutcome outcome)
        => outcome.Result;
}

internal sealed record BusinessOutcome(string Result);
