using IIoT.Edge.Module.Contracts.Modules;
using IIoT.Edge.Module.Contracts.Tasks;
using IIoT.Edge.Application.Modules.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;
using IIoT.Edge.Module.Sdk.Hardware;
using IIoT.Edge.Module.Contracts.Hardware;

namespace IIoT.Edge.TestPlugin;

public sealed class TestPluginLifecycleProbe
{
    private int _startCount;
    private int _stopCount;
    private int _disposeCount;

    public int StartCount => Volatile.Read(ref _startCount);

    public int StopCount => Volatile.Read(ref _stopCount);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    internal void MarkStarted() => Interlocked.Increment(ref _startCount);

    internal void MarkStopped() => Interlocked.Increment(ref _stopCount);

    internal void MarkDisposed() => Interlocked.Increment(ref _disposeCount);
}

public sealed class TestPluginLifecycleService(TestPluginLifecycleProbe probe)
    : IManagedBackgroundService, IDisposable
{
    private int _started;
    private int _disposed;

    public string ServiceName => "TestPlugin lifecycle probe";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
        {
            probe.MarkStarted();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopIfStarted();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        StopIfStarted();
        probe.MarkDisposed();
    }

    private void StopIfStarted()
    {
        if (Interlocked.Exchange(ref _started, 0) == 1)
        {
            probe.MarkStopped();
        }
    }
}

public sealed class TestPluginHardwareProfileProvider : ModuleHardwareProfileProviderBase
{
    private static readonly IReadOnlyList<ModuleHardwareSignalTemplate> Signals =
    [
        new(
            "TestPlugin.Signal.Input",
            "Test input",
            "DB1.DBW0",
            1,
            "Int16",
            "Read",
            1,
            "单点读数据",
            "Test")
    ];

    public override string ModuleId => DependencyInjection.ModuleKey;

    protected override IReadOnlyList<ModuleHardwareSignalTemplate> TemplateSignals => Signals;

    public override ModulePlcDefaults GetDefaultPlcSettings()
        => new(PlcType.S7.ToString(), 3000, 102);

    protected override string CreateTemplateRemark(ModuleHardwareSignalTemplate signal)
        => signal.DisplayName;
}
