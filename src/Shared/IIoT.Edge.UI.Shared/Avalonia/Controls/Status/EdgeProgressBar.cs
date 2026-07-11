using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Automation.Peers;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeProgressBar : RangeBase
{
    private const double IndeterminateSegmentRatio = 0.3d;
    private const double IndeterminatePhaseStep = 0.025d;

    public static readonly StyledProperty<bool> IsIndeterminateProperty =
        AvaloniaProperty.Register<EdgeProgressBar, bool>(nameof(IsIndeterminate));

    public static readonly StyledProperty<double> TrackThicknessProperty =
        AvaloniaProperty.Register<EdgeProgressBar, double>(nameof(TrackThickness), 4d);

    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<EdgeProgressBar, double>(nameof(Radius), 999d);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<EdgeProgressBar, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> IndicatorBrushProperty =
        AvaloniaProperty.Register<EdgeProgressBar, IBrush?>(nameof(IndicatorBrush));

    private readonly IEdgeProgressAnimationClock _animationClock;
    private readonly List<Visual> _visibilityAncestors = [];
    private bool _isAttachedToVisualTree;
    private double _indeterminatePhase;

    static EdgeProgressBar()
    {
        MinimumProperty.Changed.AddClassHandler<EdgeProgressBar>((control, _) => control.InvalidateVisual());
        MaximumProperty.Changed.AddClassHandler<EdgeProgressBar>((control, _) => control.InvalidateVisual());
        ValueProperty.Changed.AddClassHandler<EdgeProgressBar>((control, _) => control.InvalidateVisual());
        IsIndeterminateProperty.Changed.AddClassHandler<EdgeProgressBar>((control, _) =>
        {
            control.UpdateAnimationState();
            control.InvalidateVisual();
        });
        IsVisibleProperty.Changed.AddClassHandler<EdgeProgressBar>((control, _) => control.UpdateAnimationState());
        TrackThicknessProperty.Changed.AddClassHandler<EdgeProgressBar>((control, _) =>
        {
            control.InvalidateMeasure();
            control.InvalidateVisual();
        });
        RadiusProperty.Changed.AddClassHandler<EdgeProgressBar>((control, _) => control.InvalidateVisual());
        TrackBrushProperty.Changed.AddClassHandler<EdgeProgressBar>((control, _) => control.InvalidateVisual());
        IndicatorBrushProperty.Changed.AddClassHandler<EdgeProgressBar>((control, _) => control.InvalidateVisual());
    }

    public EdgeProgressBar()
        : this(new DispatcherEdgeProgressAnimationClock())
    {
    }

    internal EdgeProgressBar(IEdgeProgressAnimationClock animationClock)
    {
        _animationClock = animationClock ?? throw new ArgumentNullException(nameof(animationClock));
        _animationClock.Tick += OnAnimationTick;
        Classes.Add("edge-progress-bar");
    }

    internal double IndeterminatePhase => _indeterminatePhase;

    public bool IsIndeterminate
    {
        get => GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public double TrackThickness
    {
        get => GetValue(TrackThicknessProperty);
        set => SetValue(TrackThicknessProperty, value);
    }

    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? IndicatorBrush
    {
        get => GetValue(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer()
        => new ProgressBarAutomationPeer(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttachedToVisualTree = true;
        SubscribeToAncestorVisibility();
        UpdateAnimationState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
        UpdateAnimationState();
        UnsubscribeFromAncestorVisibility();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var trackHeight = Math.Max(0d, Math.Min(TrackThickness, Bounds.Height));
        if (trackHeight <= 0d)
        {
            return;
        }

        var trackRect = new Rect(
            0,
            (Bounds.Height - trackHeight) / 2d,
            Bounds.Width,
            trackHeight);
        var radius = Math.Max(0d, Math.Min(Radius, trackRect.Height / 2d));
        var roundedTrack = new RoundedRect(trackRect, radius);

        if (TrackBrush is not null)
        {
            context.DrawRectangle(TrackBrush, null, roundedTrack);
        }

        if (IndicatorBrush is null)
        {
            return;
        }

        if (IsIndeterminate)
        {
            var indeterminateRect = CalculateIndeterminateFillRect(trackRect, _indeterminatePhase);
            if (indeterminateRect.Width > 0d)
            {
                context.DrawRectangle(IndicatorBrush, null, new RoundedRect(indeterminateRect, radius));
            }

            return;
        }

        var fillWidth = CalculateDeterminateFillWidth(
            trackRect.Width,
            trackRect.Height,
            Minimum,
            Maximum,
            Value);
        if (fillWidth <= 0d)
        {
            return;
        }

        var fillRect = new Rect(trackRect.X, trackRect.Y, fillWidth, trackRect.Height);
        context.DrawRectangle(IndicatorBrush, null, new RoundedRect(fillRect, radius));
    }

    internal static double CalculateDeterminateFillWidth(
        double trackWidth,
        double trackHeight,
        double minimum,
        double maximum,
        double value)
    {
        var range = maximum - minimum;
        if (trackWidth <= 0d || trackHeight <= 0d || range <= 0d)
        {
            return 0d;
        }

        var progress = Math.Clamp((value - minimum) / range, 0d, 1d);
        if (progress <= 0d)
        {
            return 0d;
        }

        return Math.Min(Math.Max(trackHeight, trackWidth * progress), trackWidth);
    }

    internal static Rect CalculateIndeterminateFillRect(Rect trackRect, double phase)
    {
        var segmentWidth = Math.Min(
            trackRect.Width,
            Math.Max(trackRect.Height, trackRect.Width * IndeterminateSegmentRatio));
        var travelWidth = Math.Max(0d, trackRect.Width - segmentWidth);
        var normalizedPhase = Math.Clamp(phase, 0d, 1d);
        var travelProgress = (1d - Math.Cos(normalizedPhase * 2d * Math.PI)) / 2d;
        var segmentX = trackRect.X + (travelWidth * travelProgress);
        return new Rect(segmentX, trackRect.Y, segmentWidth, trackRect.Height);
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (!_isAttachedToVisualTree || !IsEffectivelyVisible || !IsIndeterminate)
        {
            UpdateAnimationState();
            return;
        }

        _indeterminatePhase = (_indeterminatePhase + IndeterminatePhaseStep) % 1d;
        InvalidateVisual();
    }

    private void SubscribeToAncestorVisibility()
    {
        foreach (var ancestor in this.GetVisualAncestors())
        {
            ancestor.PropertyChanged += OnAncestorPropertyChanged;
            _visibilityAncestors.Add(ancestor);
        }
    }

    private void UnsubscribeFromAncestorVisibility()
    {
        foreach (var ancestor in _visibilityAncestors)
        {
            ancestor.PropertyChanged -= OnAncestorPropertyChanged;
        }

        _visibilityAncestors.Clear();
    }

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            UpdateAnimationState();
        }
    }

    private void UpdateAnimationState()
    {
        var shouldRun = _isAttachedToVisualTree && IsEffectivelyVisible && IsIndeterminate;
        if (shouldRun)
        {
            _animationClock.Start();
            return;
        }

        _animationClock.Stop();
    }
}

internal interface IEdgeProgressAnimationClock
{
    event EventHandler? Tick;

    void Start();

    void Stop();
}

internal sealed class DispatcherEdgeProgressAnimationClock : IEdgeProgressAnimationClock
{
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);
    private readonly DispatcherTimer _timer = new(FrameInterval, DispatcherPriority.Render, Dispatcher.UIThread);

    public DispatcherEdgeProgressAnimationClock()
    {
        _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Tick;

    public void Start()
    {
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    public void Stop()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
        }
    }
}
