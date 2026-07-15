using IIoT.Edge.SharedKernel.Configuration;

namespace IIoT.Edge.Testing;

public static class EdgeEnvironmentTestScope
{
    private static readonly object Sync = new();

    public static void WithDataRootOverride(string dataRoot, Action action)
    {
        lock (Sync)
        {
            var variable = EdgeClientProgramDataPaths.ProgramDataRootEnvironmentVariable;
            var originalValue = Environment.GetEnvironmentVariable(variable);
            try
            {
                Environment.SetEnvironmentVariable(variable, dataRoot);
                action();
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, originalValue);
            }
        }
    }
}
