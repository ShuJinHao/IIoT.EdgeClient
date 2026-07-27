namespace IIoT.Edge.Shell.Core;

internal enum ShellDispatcherExceptionDisposition
{
    FatalStartup,
    RecoverRuntime
}

internal static class ShellDispatcherExceptionPolicy
{
    public static ShellDispatcherExceptionDisposition Resolve(bool mainWindowReady)
        => mainWindowReady
            ? ShellDispatcherExceptionDisposition.RecoverRuntime
            : ShellDispatcherExceptionDisposition.FatalStartup;
}
