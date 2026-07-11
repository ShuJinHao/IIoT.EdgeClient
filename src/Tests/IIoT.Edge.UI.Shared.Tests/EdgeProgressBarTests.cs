using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using Xunit;

namespace IIoT.Edge.UI.Shared.Tests;

public sealed class EdgeProgressBarTests
{
    [Fact]
    public void PublicApi_ShouldNotExposeUnusedRadiusOverride()
    {
        Assert.Null(typeof(EdgeProgressBar).GetProperty("Radius"));
        Assert.Null(typeof(EdgeProgressBar).GetField("RadiusProperty"));
    }

    [AvaloniaFact]
    public void IndeterminateAnimation_FollowsVisibilityAndVisualTreeLifetime()
    {
        var clock = new ManualAnimationClock();
        var progressBar = new EdgeProgressBar(clock)
        {
            IsIndeterminate = true
        };
        var host = new Border { Child = progressBar };
        var window = new Window { Content = host };

        try
        {
            window.Show();

            Assert.True(clock.IsRunning);
            Assert.Equal(1, clock.StartCount);

            var initialPhase = progressBar.IndeterminatePhase;
            clock.Pulse();
            Assert.True(progressBar.IndeterminatePhase > initialPhase);

            host.IsVisible = false;
            Assert.False(clock.IsRunning);
            Assert.Equal(1, clock.StopCount);

            host.IsVisible = true;
            Assert.True(clock.IsRunning);
            Assert.Equal(2, clock.StartCount);

            progressBar.IsIndeterminate = false;
            Assert.False(clock.IsRunning);
            Assert.Equal(2, clock.StopCount);

            progressBar.IsIndeterminate = true;
            Assert.True(clock.IsRunning);
            Assert.Equal(3, clock.StartCount);
        }
        finally
        {
            window.Close();
        }

        Assert.False(clock.IsRunning);
        Assert.Equal(3, clock.StopCount);
    }

    [AvaloniaFact]
    public async Task DispatcherAnimationClock_TicksOnTheRealHeadlessDispatcher()
    {
        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new DispatcherEdgeProgressAnimationClock();
        clock.Tick += (_, _) => firstFrame.TrySetResult();

        try
        {
            clock.Start();

            await firstFrame.Task.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            Assert.True(firstFrame.Task.IsCompletedSuccessfully);
        }
        finally
        {
            clock.Stop();
        }
    }

    [AvaloniaFact]
    public void DeterminateGeometry_PreservesExistingValueSemantics()
    {
        Assert.Equal(0d, EdgeProgressBar.CalculateDeterminateFillWidth(100d, 4d, 0d, 100d, 0d));
        Assert.Equal(4d, EdgeProgressBar.CalculateDeterminateFillWidth(100d, 4d, 0d, 100d, 1d));
        Assert.Equal(50d, EdgeProgressBar.CalculateDeterminateFillWidth(100d, 4d, 0d, 100d, 50d));
        Assert.Equal(100d, EdgeProgressBar.CalculateDeterminateFillWidth(100d, 4d, 0d, 100d, 150d));
        Assert.Equal(0d, EdgeProgressBar.CalculateDeterminateFillWidth(100d, 4d, 10d, 10d, 10d));

        var progressBar = new EdgeProgressBar { Value = 42d };
        progressBar.IsIndeterminate = true;
        progressBar.IsIndeterminate = false;

        Assert.Equal(42d, progressBar.Value);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(0.25d)]
    [InlineData(0.5d)]
    [InlineData(0.75d)]
    [InlineData(1d)]
    public void IndeterminateGeometry_StaysInsideTheTrack(double phase)
    {
        var track = new global::Avalonia.Rect(10d, 20d, 100d, 4d);

        var fill = EdgeProgressBar.CalculateIndeterminateFillRect(track, phase);

        Assert.InRange(fill.X, track.X, track.Right);
        Assert.InRange(fill.Right, track.X, track.Right);
        Assert.InRange(fill.Width, 0d, track.Width);
        Assert.Equal(track.Y, fill.Y);
        Assert.Equal(track.Height, fill.Height);
    }

    [Fact]
    public void IndeterminateGeometry_HandlesNarrowAndZeroWidthTracks()
    {
        var narrowTrack = new global::Avalonia.Rect(3d, 5d, 4d, 8d);
        var narrowFill = EdgeProgressBar.CalculateIndeterminateFillRect(narrowTrack, 0.5d);
        Assert.Equal(narrowTrack, narrowFill);

        var zeroWidthTrack = new global::Avalonia.Rect(3d, 5d, 0d, 8d);
        var zeroWidthFill = EdgeProgressBar.CalculateIndeterminateFillRect(zeroWidthTrack, 0.5d);
        Assert.Equal(zeroWidthTrack, zeroWidthFill);
    }

    [AvaloniaFact]
    public void AutomationPeer_ExposesReadOnlyProgressRangeValues()
    {
        var progressBar = new AutomationTestProgressBar
        {
            Minimum = 10d,
            Maximum = 90d,
            Value = 42d
        };

        var peer = progressBar.CreateAutomationPeer();
        var rangeProvider = Assert.IsAssignableFrom<IRangeValueProvider>(peer);

        Assert.Equal(AutomationControlType.ProgressBar, peer.GetAutomationControlType());
        Assert.True(rangeProvider.IsReadOnly);
        Assert.Equal(10d, rangeProvider.Minimum);
        Assert.Equal(90d, rangeProvider.Maximum);
        Assert.Equal(42d, rangeProvider.Value);
    }

    private sealed class ManualAnimationClock : IEdgeProgressAnimationClock
    {
        public event EventHandler? Tick;

        public bool IsRunning { get; private set; }

        public int StartCount { get; private set; }

        public int StopCount { get; private set; }

        public void Start()
        {
            if (IsRunning)
            {
                return;
            }

            IsRunning = true;
            StartCount++;
        }

        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            StopCount++;
        }

        public void Pulse()
        {
            if (IsRunning)
            {
                Tick?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private sealed class AutomationTestProgressBar : EdgeProgressBar
    {
        public AutomationPeer CreateAutomationPeer()
            => OnCreateAutomationPeer();
    }
}
