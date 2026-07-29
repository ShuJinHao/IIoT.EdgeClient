using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace IIoT.Edge.Infrastructure.DeviceComm.Plc;

public sealed class PlcRuntimeConnectionSignal
{
    private readonly Channel<bool> _changes = Channel.CreateUnbounded<bool>(
        new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = true,
            SingleWriter = false
        });
    private int _lastState = -1;

    public bool IsConnected => Volatile.Read(ref _lastState) == 1;

    public void Report(bool isConnected)
    {
        var nextState = isConnected ? 1 : 0;
        if (Interlocked.Exchange(ref _lastState, nextState) != nextState)
        {
            _changes.Writer.TryWrite(isConnected);
        }
    }

    public async IAsyncEnumerable<bool> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var state in _changes.Reader
                           .ReadAllAsync(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return state;
        }
    }

    public void Complete()
        => _changes.Writer.TryComplete();
}
