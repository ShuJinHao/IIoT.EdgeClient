using IIoT.Edge.SharedKernel.DataPipeline.Capacity;

namespace IIoT.Edge.NonUiRegressionTests;

public sealed class TodayCapacityThreadSafetyTests
{
    [Fact]
    public async Task ConcurrentIncrement_ShouldKeepTotalsAndBucketsConsistent()
    {
        var capacity = new TodayCapacity();
        var completedTime = new DateTime(2026, 4, 15, 8, 5, 0);
        var dayStart = new TimeSpan(8, 0, 0);
        var dayEnd = new TimeSpan(20, 0, 0);

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < 500; i++)
                {
                    capacity.Increment(completedTime, isOk: true, dayStart, dayEnd);
                }
            }));

        await Task.WhenAll(tasks);

        Assert.Equal("2026-04-15", capacity.Date);
        Assert.Equal(10000, capacity.TotalAll);
        Assert.Equal(10000, capacity.OkAll);
        Assert.Equal(0, capacity.NgAll);

        var bucket = capacity.HalfHourly.Single(x => x.StartHour == 8 && x.StartMinute == 0);
        Assert.Equal(10000, bucket.Total);
        Assert.Equal(10000, bucket.OkCount);
        Assert.Equal(0, bucket.NgCount);
    }
}
