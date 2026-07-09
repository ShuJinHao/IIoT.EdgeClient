using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeProgressBar : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<EdgeProgressBar, double>(nameof(Minimum), 0d);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<EdgeProgressBar, double>(nameof(Maximum), 100d);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<EdgeProgressBar, double>(nameof(Value), 0d);

    public static readonly StyledProperty<double> TrackThicknessProperty =
        AvaloniaProperty.Register<EdgeProgressBar, double>(nameof(TrackThickness), 4d);

    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<EdgeProgressBar, double>(nameof(Radius), 999d);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<EdgeProgressBar, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<IBrush?> IndicatorBrushProperty =
        AvaloniaProperty.Register<EdgeProgressBar, IBrush?>(nameof(IndicatorBrush));

    static EdgeProgressBar()
    {
        MinimumProperty.Changed.AddClassHandler<EdgeProgressBar>((control, _) => control.InvalidateVisual());
        MaximumProperty.Changed.AddClassHandler<EdgeProgressBar>((control, _) => control.InvalidateVisual());
        ValueProperty.Changed.AddClassHandler<EdgeProgressBar>((control, _) => control.InvalidateVisual());
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
    {
        Classes.Add("edge-progress-bar");
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
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

        var range = Maximum - Minimum;
        if (range <= 0d)
        {
            return;
        }

        var progress = Math.Clamp((Value - Minimum) / range, 0d, 1d);
        if (progress <= 0d || IndicatorBrush is null)
        {
            return;
        }

        var fillWidth = Math.Max(trackRect.Height, trackRect.Width * progress);
        var fillRect = new Rect(trackRect.X, trackRect.Y, Math.Min(fillWidth, trackRect.Width), trackRect.Height);
        context.DrawRectangle(IndicatorBrush, null, new RoundedRect(fillRect, radius));
    }
}
