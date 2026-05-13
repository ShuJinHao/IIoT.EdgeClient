using IIoT.Edge.Infrastructure.DeviceComm.Plc.Store;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class PlcBufferConcurrencyTests
{
    [Fact]
    public void GetWriteBuffer_ShouldReturnSnapshotInsteadOfLiveMutableArray()
    {
        var buffer = new PlcBuffer(readSize: 4, writeSize: 2);

        buffer.SetWriteValue(0, 1);
        var snapshot1 = buffer.GetWriteBuffer();

        buffer.SetWriteValue(0, 2);
        var snapshot2 = buffer.GetWriteBuffer();

        Assert.Equal((ushort)1, snapshot1[0]);
        Assert.Equal((ushort)2, snapshot2[0]);
    }

    [Fact]
    public async Task ConcurrentReadUpdates_ShouldNotThrowAndShouldKeepLatestLength()
    {
        var buffer = new PlcBuffer(readSize: 16, writeSize: 2);

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                buffer.UpdateReadBuffer(Enumerable.Repeat((ushort)(i % 10), 16).ToArray());
            }
        });

        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 2000; i++)
            {
                for (var j = 0; j < 16; j++)
                {
                    _ = buffer.GetReadValue(j);
                }
            }
        });

        await Task.WhenAll(writer, reader);

        Assert.True(buffer.Matches(16, 2));
    }

    [Fact]
    public void PlcDataStore_RegisterWithDifferentSize_ShouldReplaceBuffer()
    {
        var store = new PlcDataStore();

        store.Register(1, readSize: 2, writeSize: 2);
        var original = store.GetBuffer(1);

        store.Register(1, readSize: 4, writeSize: 4);
        var replaced = store.GetBuffer(1);

        Assert.NotNull(original);
        Assert.NotNull(replaced);
        Assert.NotSame(original, replaced);
    }
}
