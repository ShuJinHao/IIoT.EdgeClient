using System.Reflection;
using System.Security;
using System.Text.Json;

namespace IIoT.Edge.Host.Bootstrap;

internal static class StartupExceptionBoundary
{
    public static bool IsApprovedPathFailure(Exception exception)
        => exception is ArgumentException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException
            or SecurityException;

    public static bool IsApprovedManifestFailure(Exception exception)
        => IsApprovedPathFailure(exception)
            || exception is JsonException;

    public static bool IsApprovedPluginLoadFailure(Exception exception)
        => IsApprovedPathFailure(exception)
            || exception is BadImageFormatException
                or FileLoadException
                or ReflectionTypeLoadException
                or TypeLoadException
                or MissingMethodException
                or MemberAccessException
                or AmbiguousMatchException
                or TargetInvocationException;
}
