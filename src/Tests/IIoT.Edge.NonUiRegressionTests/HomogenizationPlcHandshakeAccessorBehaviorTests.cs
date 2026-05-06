using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Abstractions.Plc.Signals;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Config.Hardware;
using IIoT.Edge.Module.Homogenization.Runtime;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class HomogenizationPlcHandshakeAccessorBehaviorTests
{
    [Fact]
    public void IsTriggeredAndIsReset_ShouldUseConfiguredHandshakeCodes()
    {
        var signals = new FakeInteractionSignals();
        var accessor = new HomogenizationPlcHandshakeAccessor(signals, CreateCodeOptions());

        signals.SetRead(HomogenizationPlcSignals.Interaction.出料触发, 11);

        Assert.True(accessor.IsTriggered(HomogenizationPlcSignals.Interaction.出料触发));
        Assert.False(accessor.IsReset(HomogenizationPlcSignals.Interaction.出料触发));

        signals.SetRead(HomogenizationPlcSignals.Interaction.出料触发, 10);

        Assert.False(accessor.IsTriggered(HomogenizationPlcSignals.Interaction.出料触发));
        Assert.True(accessor.IsReset(HomogenizationPlcSignals.Interaction.出料触发));
    }

    [Fact]
    public void Replies_ShouldWriteConfiguredCodesToPairedAckSignal()
    {
        var signals = new FakeInteractionSignals();
        var accessor = new HomogenizationPlcHandshakeAccessor(signals, CreateCodeOptions());

        accessor.ReplyOk(HomogenizationPlcSignals.Interaction.出料触发);
        Assert.Equal((ushort)21, signals.GetWrite(HomogenizationPlcSignals.Interaction.出料应答));

        accessor.ReplyException(HomogenizationPlcSignals.Interaction.出料触发);
        Assert.Equal((ushort)22, signals.GetWrite(HomogenizationPlcSignals.Interaction.出料应答));

        accessor.ReplyMesNg(HomogenizationPlcSignals.Interaction.出料触发);
        Assert.Equal((ushort)23, signals.GetWrite(HomogenizationPlcSignals.Interaction.出料应答));

        accessor.ReplyReset(HomogenizationPlcSignals.Interaction.出料触发);
        Assert.Equal((ushort)10, signals.GetWrite(HomogenizationPlcSignals.Interaction.出料应答));
    }

    [Fact]
    public void ReplyResult_ShouldMapMesOutcomeWithoutTaskReadingRawCodes()
    {
        var signals = new FakeInteractionSignals();
        var accessor = new HomogenizationPlcHandshakeAccessor(signals, CreateCodeOptions());

        accessor.ReplyResult(HomogenizationPlcSignals.Interaction.进站触发, MesCallResult.BusinessRejected("NG"));
        Assert.Equal((ushort)23, signals.GetWrite(HomogenizationPlcSignals.Interaction.进站应答));

        accessor.ReplyResult(HomogenizationPlcSignals.Interaction.进站触发, MesCallResult.TransportFailure("网络异常"));
        Assert.Equal((ushort)22, signals.GetWrite(HomogenizationPlcSignals.Interaction.进站应答));
    }

    private static HomogenizationPlcCodeOptions CreateCodeOptions()
        => new()
        {
            SignalReset = 10,
            SignalTrigger = 11,
            AckOk = 21,
            AckException = 22,
            AckMesNg = 23
        };

    private sealed class FakeInteractionSignals : ILogicalSignalAccessor<HomogenizationPlcSignals.Interaction>
    {
        private readonly Dictionary<HomogenizationPlcSignals.Interaction, ushort> _reads = [];
        private readonly Dictionary<HomogenizationPlcSignals.Interaction, ushort> _writes = [];

        public bool CanRead(HomogenizationPlcSignals.Interaction key) => true;

        public bool CanWrite(HomogenizationPlcSignals.Interaction key) => true;

        public bool TryReadUInt16(HomogenizationPlcSignals.Interaction key, out ushort value)
            => _reads.TryGetValue(key, out value);

        public ushort ReadUInt16(HomogenizationPlcSignals.Interaction key)
            => _reads.TryGetValue(key, out var value) ? value : (ushort)0;

        public short ReadInt16(HomogenizationPlcSignals.Interaction key)
            => throw new NotSupportedException();

        public string ReadAscii(HomogenizationPlcSignals.Interaction key)
            => throw new NotSupportedException();

        public IReadOnlyList<int> ReadIntArray(HomogenizationPlcSignals.Interaction key, int count)
            => throw new NotSupportedException();

        public IReadOnlyList<bool> ReadBoolArray(HomogenizationPlcSignals.Interaction key, int count)
            => throw new NotSupportedException();

        public IReadOnlyList<double> ReadFloatArray(HomogenizationPlcSignals.Interaction key, int count)
            => throw new NotSupportedException();

        public void WriteUInt16(HomogenizationPlcSignals.Interaction key, ushort value)
            => _writes[key] = value;

        public void SetRead(HomogenizationPlcSignals.Interaction key, ushort value)
            => _reads[key] = value;

        public ushort GetWrite(HomogenizationPlcSignals.Interaction key)
            => _writes[key];
    }
}
