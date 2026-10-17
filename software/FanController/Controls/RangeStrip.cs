////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// RangeStrip.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;


namespace FanController.Controls;

/// <summary>
/// Displays a configured range (e.g. the permissible fan power range) as a filled
/// part of a track. RangeStart and RangeEnd are normalized to 0..1;
/// nothing is filled if RangeEnd is not greater than RangeStart.
/// </summary>
public class RangeStrip : Control
{
    ////////////////////////////////////////////////////////////////////////////////////////
    // Styled properties

    public static readonly StyledProperty<double> RangeStartProperty =
        AvaloniaProperty.Register<RangeStrip, double>(
            nameof(RangeStart), 0.0, coerce: CoercePosition);

    public static readonly StyledProperty<double> RangeEndProperty =
        AvaloniaProperty.Register<RangeStrip, double>(
            nameof(RangeEnd), 1.0, coerce: CoercePosition);

    public static readonly StyledProperty<double> StripHeightProperty =
        AvaloniaProperty.Register<RangeStrip, double>(
            nameof(StripHeight), 6.0, coerce: CoerceNonNegative);

    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<RangeStrip, double>(
            nameof(CornerRadius), 3.0, coerce: CoerceNonNegative);

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<RangeStrip, IBrush?>(
            nameof(TrackBrush),
            new ImmutableSolidColorBrush(Color.Parse("#30808080")));

    public static readonly StyledProperty<IBrush?> FillBrushProperty =
        AvaloniaProperty.Register<RangeStrip, IBrush?>(
            nameof(FillBrush),
            new ImmutableSolidColorBrush(Color.Parse("#4CAF50")));


    public double RangeStart
    {
        get => GetValue(RangeStartProperty);
        set => SetValue(RangeStartProperty, value);
    }

    public double RangeEnd
    {
        get => GetValue(RangeEndProperty);
        set => SetValue(RangeEndProperty, value);
    }

    public double StripHeight
    {
        get => GetValue(StripHeightProperty);
        set => SetValue(StripHeightProperty, value);
    }

    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public IBrush? FillBrush
    {
        get => GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    static RangeStrip()
    {
        AffectsRender<RangeStrip>(
            RangeStartProperty,
            RangeEndProperty,
            StripHeightProperty,
            CornerRadiusProperty,
            TrackBrushProperty,
            FillBrushProperty);

        AffectsMeasure<RangeStrip>(StripHeightProperty);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Coercion

    private static double CoercePosition(AvaloniaObject obj, double value) =>
        double.IsNaN(value) ? 0.0 : Math.Clamp(value, 0.0, 1.0);

    private static double CoerceNonNegative(AvaloniaObject obj, double value) =>
        double.IsNaN(value) ? 0.0 : Math.Max(0.0, value);


    ////////////////////////////////////////////////////////////////////////////////////////
    // Layout & rendering

    protected override Size MeasureOverride(Size availableSize)
    {
        // Without a width constraint (e.g. an Auto column) a fixed default width is used
        double width = double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width;
        return new Size(width, StripHeight);
    }


    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        double h = StripHeight;
        double y = (Bounds.Height - h) / 2;
        double cr = CornerRadius;
        var trackRect = new Rect(0, y, w, h);

        // Background track
        if (TrackBrush is not null)
            context.DrawRectangle(TrackBrush, null, trackRect, cr, cr);

        // Filled range, clipped to the rounded track shape
        double start = RangeStart;
        double end = Math.Max(start, RangeEnd);

        if ((FillBrush is not null) && (end > start))
        {
            var fillRect = new Rect(w * start, y, w * (end - start), h);

            using (context.PushClip(new RoundedRect(trackRect, cr, cr)))
            {
                context.DrawRectangle(FillBrush, null, fillRect);
            }
        }
    }
}

// END-OF-FILE
