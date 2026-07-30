using System.Net.Sockets;
using IIoT.Edge.Application.Common.Plc;
using McpXLib.Exceptions;

namespace IIoT.Edge.Infrastructure.DeviceComm.Signals;

internal enum PlcOperationFailureKind
{
    TransportDisconnected,
    Timeout,
    ProtocolRejected,
    InvalidResponse,
    ConfigurationInvalid,
    TaskFault
}

internal readonly record struct PlcOperationFailure(
    PlcOperationFailureKind Kind,
    string ReasonCode,
    string ExceptionType)
{
    public bool DisconnectsTransport
        => Kind == PlcOperationFailureKind.TransportDisconnected;

    public string SafeDiagnostic
        => $"原因码={ReasonCode}，异常类型={ExceptionType}";
}

internal static class PlcOperationFailureClassifier
{
    public static PlcOperationFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var exceptions = Enumerate(exception).ToArray();
        return Match<SocketException>(
                   exceptions,
                   PlcOperationFailureKind.TransportDisconnected,
                   PlcTaskRuntimeErrorCodes.TransportDisconnected)
               ?? Match<TimeoutException>(
                   exceptions,
                   PlcOperationFailureKind.Timeout,
                   PlcTaskRuntimeErrorCodes.Timeout)
               ?? Match<McProtocolException>(
                   exceptions,
                   PlcOperationFailureKind.ProtocolRejected,
                   PlcTaskRuntimeErrorCodes.ProtocolRejected)
               ?? MatchAny(
                   exceptions,
                   static current => current is RecivePacketException or InvalidDataException,
                   PlcOperationFailureKind.InvalidResponse,
                   PlcTaskRuntimeErrorCodes.InvalidResponse)
               ?? MatchAny(
                   exceptions,
                   static current => current is DeviceAddressException
                       or FormatException
                       or ArgumentException
                       or NotSupportedException,
                   PlcOperationFailureKind.ConfigurationInvalid,
                   PlcTaskRuntimeErrorCodes.ConfigurationInvalid)
               ?? Match<IOException>(
                   exceptions,
                   PlcOperationFailureKind.TaskFault,
                   PlcTaskRuntimeErrorCodes.TaskFault)
               ?? Match<OperationCanceledException>(
                   exceptions,
                   PlcOperationFailureKind.TaskFault,
                   PlcTaskRuntimeErrorCodes.TaskFault)
               ?? new PlcOperationFailure(
                   PlcOperationFailureKind.TaskFault,
                   PlcTaskRuntimeErrorCodes.TaskFault,
                   exception.GetType().Name);
    }

    public static bool IsCallerCancellation(
        Exception exception,
        CancellationToken callerToken)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return callerToken.IsCancellationRequested
               && Enumerate(exception).Any(static current => current is OperationCanceledException);
    }

    private static PlcOperationFailure? Match<TException>(
        IReadOnlyCollection<Exception> exceptions,
        PlcOperationFailureKind kind,
        string reasonCode)
        where TException : Exception
        => MatchAny(
            exceptions,
            static current => current is TException,
            kind,
            reasonCode);

    private static PlcOperationFailure? MatchAny(
        IReadOnlyCollection<Exception> exceptions,
        Func<Exception, bool> predicate,
        PlcOperationFailureKind kind,
        string reasonCode)
    {
        var matched = exceptions.FirstOrDefault(predicate);
        return matched is null
            ? null
            : new PlcOperationFailure(kind, reasonCode, matched.GetType().Name);
    }

    private static IEnumerable<Exception> Enumerate(Exception exception)
    {
        var pending = new Stack<Exception>();
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(exception);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            yield return current;
            if (current is AggregateException aggregate)
            {
                for (var index = aggregate.InnerExceptions.Count - 1; index >= 0; index--)
                {
                    pending.Push(aggregate.InnerExceptions[index]);
                }
            }
            else if (current.InnerException is not null)
            {
                pending.Push(current.InnerException);
            }
        }
    }
}
