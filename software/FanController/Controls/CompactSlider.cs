////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// CompactSlider.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;


namespace FanController.Controls;

/// <summary>
/// Compact horizontal slider for table rows, with optional snapping to ticks
/// and an optional range display (the "rangeTrack" style class shows
/// RangeStart..RangeEnd on the track instead of the plain track line).
/// </summary>
public class CompactSlider : TemplatedControl
{
    // Template parts
    private Track? _track;
    private Thumb? _thumb;

    // True while the thumb is being dragged. CoerceValue doesn't snap meanwhile:
    // the Track moves the thumb smoothly, and the value is snapped once on DragCompleted.
    private bool _isDragging;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Routed events
    // Raised when the user grabs / releases the thumb (a click on the track raises both)

    public static readonly RoutedEvent<RoutedEventArgs> ThumbDragStartedEvent =
        RoutedEvent.Register<CompactSlider, RoutedEventArgs>(
            nameof(ThumbDragStarted), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> ThumbDragCompletedEvent =
        RoutedEvent.Register<CompactSlider, RoutedEventArgs>(
            nameof(ThumbDragCompleted), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs> ThumbDragStarted
    {
        add => AddHandler(ThumbDragStartedEvent, value);
        remove => RemoveHandler(ThumbDragStartedEvent, value);
    }

    public event EventHandler<RoutedEventArgs> ThumbDragCompleted
    {
        add => AddHandler(ThumbDragCompletedEvent, value);
        remove => RemoveHandler(ThumbDragCompletedEvent, value);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Styled properties

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(Minimum), 0.0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(Maximum), 1.0);

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CompactSlider, double>(
            nameof(Value), 0.0,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay,
            coerce: CoerceValue);

    // Keyboard step without snapping
    public static readonly StyledProperty<double> SmallChangeProperty =
        AvaloniaProperty.Register<CompactSlider, double>(nameof(SmallChange), 0.01);

    // Tick interval (0: no ticks)
    public static readonly StyledProperty<double> TickFrequencyProperty =
        AvaloniaProperty.Register<CompactSlider, double>(
            nameof(TickFrequency), 0.0,
            coerce: CoerceTickFrequency);

    public static readonly StyledProperty<bool> IsSnapToTickEnabledProperty =
        AvaloniaProperty.Register<CompactSlider, bool>(nameof(IsSnapToTickEnabled), false);

    // Displayed range (normalized 0..1, independent of Minimum/Maximum)
    public static readonly StyledProperty<double> RangeStartProperty =
        AvaloniaProperty.Register<CompactSlider, double>(
            nameof(RangeStart), 0.0, coerce: CoerceNormalized);

    public static readonly StyledProperty<double> RangeEndProperty =
        AvaloniaProperty.Register<CompactSlider, double>(
            nameof(RangeEnd), 1.0, coerce: CoerceNormalized);


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

    public double SmallChange
    {
        get => GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    public double TickFrequency
    {
        get => GetValue(TickFrequencyProperty);
        set => SetValue(TickFrequencyProperty, value);
    }

    public bool IsSnapToTickEnabled
    {
        get => GetValue(IsSnapToTickEnabledProperty);
        set => SetValue(IsSnapToTickEnabledProperty, value);
    }

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

    /// <summary>True while the user is dragging the thumb.</summary>
    public bool IsDragging => _isDragging;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Coercion

    /// <summary>
    /// Clamps the value to [Minimum, Maximum] and, when <see cref="IsSnapToTickEnabled"/>
    /// is set (and the thumb is not being dragged), rounds it to the nearest tick.
    /// </summary>
    private static double CoerceValue(AvaloniaObject obj, double value)
    {
        var slider = (CompactSlider)obj;
        double min = slider.Minimum;

        // Minimum > Maximum (e.g. while bindings are being updated): the range is empty
        double max = Math.Max(min, slider.Maximum);

        if (double.IsNaN(value)) return min;

        return (slider.IsSnapToTickEnabled && !slider._isDragging)
            ? Snap(value, min, max, slider.TickFrequency)
            : Math.Clamp(value, min, max);
    }


    // Clamps to [min, max] and rounds to the nearest tick (if tickFrequency > 0)
    private static double Snap(double value, double min, double max, double tickFrequency)
    {
        double clamped = Math.Clamp(value, min, max);
        if (tickFrequency <= 0.0) return clamped;

        double steps = Math.Round((clamped - min) / tickFrequency, MidpointRounding.AwayFromZero);

        // Clamped again against floating-point error (e.g. 0.1 * 3 = 0.30000000000000004)
        return Math.Clamp(min + (steps * tickFrequency), min, max);
    }


    // TickFrequency must be >= 0
    private static double CoerceTickFrequency(AvaloniaObject obj, double value) =>
        Math.Max(0.0, value);


    // RangeStart / RangeEnd are normalized to [0, 1]
    private static double CoerceNormalized(AvaloniaObject obj, double value) =>
        Math.Clamp(value, 0.0, 1.0);


    ////////////////////////////////////////////////////////////////////////////////////////
    // Property changes

    // The value is re-coerced when the range or the snapping parameters change
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if ((change.Property == TickFrequencyProperty) ||
            (change.Property == IsSnapToTickEnabledProperty) ||
            (change.Property == MinimumProperty) ||
            (change.Property == MaximumProperty))
        {
            CoerceValue(ValueProperty);
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Template parts

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        // Detach from the previous thumb
        if (_thumb is not null)
        {
            _thumb.DragStarted -= OnThumbDragStarted;
            _thumb.DragCompleted -= OnThumbDragCompleted;
        }

        base.OnApplyTemplate(e);
        _track = e.NameScope.Find<Track>("PART_Track");
        _thumb = _track?.Thumb;

        // Attach to the new thumb
        if (_thumb is not null)
        {
            _thumb.DragStarted += OnThumbDragStarted;
            _thumb.DragCompleted += OnThumbDragCompleted;
        }
    }


    private void OnThumbDragStarted(object? sender, VectorEventArgs e)
    {
        _isDragging = true;
        RaiseEvent(new RoutedEventArgs(ThumbDragStartedEvent));
    }


    private void OnThumbDragCompleted(object? sender, VectorEventArgs e)
    {
        _isDragging = false;

        // Snap the final value once
        CoerceValue(ValueProperty);

        RaiseEvent(new RoutedEventArgs(ThumbDragCompletedEvent));
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Input

    // Click-to-seek on the track
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.Handled || (_track is null)) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Keyboard control after a click
        Focus();

        // A press on the thumb starts dragging (handled by the thumb)
        if ((_thumb is not null) && new Rect(_thumb.Bounds.Size).Contains(e.GetPosition(_thumb)))
            return;

        double thumbWidth = _thumb?.Bounds.Width ?? 0;
        double trackLength = _track.Bounds.Width - thumbWidth;
        if (trackLength <= 0) return;

        double ratio = Math.Clamp(
            (e.GetPosition(_track).X - (thumbWidth / 2)) / trackLength, 0.0, 1.0);

        // CoerceValue snaps it
        Value = Minimum + (ratio * (Maximum - Minimum));

        // Instant interaction — a click counts as drag start + end
        RaiseEvent(new RoutedEventArgs(ThumbDragStartedEvent));
        RaiseEvent(new RoutedEventArgs(ThumbDragCompletedEvent));

        e.Handled = true;
    }


    /// <summary>
    /// Arrow keys move by one tick when snap is enabled, or by
    /// <see cref="SmallChange"/> otherwise (matching the Avalonia Slider).
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // When snap is active the natural step is one tick:
        // the keyboard never lands between ticks.
        double step = (IsSnapToTickEnabled && (TickFrequency > 0.0))
            ? TickFrequency
            : SmallChange;

        switch (e.Key)
        {
            case Key.Left:
            case Key.Down:
                Value = Math.Max(Minimum, Value - step);
                e.Handled = true;
                break;

            case Key.Right:
            case Key.Up:
                Value = Math.Min(Maximum, Value + step);
                e.Handled = true;
                break;

            case Key.Home:
                Value = Minimum;
                e.Handled = true;
                break;

            case Key.End:
                Value = Maximum;
                e.Handled = true;
                break;
        }
    }
}

// END-OF-FILE
