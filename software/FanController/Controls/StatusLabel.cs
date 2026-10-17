////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// StatusLabel.cs
////////////////////////////////////////////////////////////////////////////////////////

using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;


namespace FanController.Controls;

/// <summary>
/// Status display element. The active / inactive state is shown by the background
/// and text opacity, or by swapping to the Inactive* brushes when they are set;
/// the font weight may change with the state too. Shows Placeholder if Text is empty.
/// </summary>
public class StatusLabel : TemplatedControl
{
    ////////////////////////////////////////////////////////////////////////////////////////
    // Styled properties

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StatusLabel, string?>(nameof(Text), defaultValue: null);

    public static readonly StyledProperty<string?> PlaceholderProperty =
        AvaloniaProperty.Register<StatusLabel, string?>(nameof(Placeholder), defaultValue: null);

    // Horizontal alignment of the text within the label
    public static readonly StyledProperty<HorizontalAlignment> TextAlignmentProperty =
        AvaloniaProperty.Register<StatusLabel, HorizontalAlignment>(nameof(TextAlignment), defaultValue: HorizontalAlignment.Center);

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<StatusLabel, bool>(nameof(IsActive), defaultValue: false);

    public static readonly StyledProperty<double> ActiveBackgroundOpacityProperty =
        AvaloniaProperty.Register<StatusLabel, double>(nameof(ActiveBackgroundOpacity), defaultValue: 0.75);

    public static readonly StyledProperty<double> ActiveTextOpacityProperty =
        AvaloniaProperty.Register<StatusLabel, double>(nameof(ActiveTextOpacity), defaultValue: 1.0);

    public static readonly StyledProperty<double> InactiveBackgroundOpacityProperty =
        AvaloniaProperty.Register<StatusLabel, double>(nameof(InactiveBackgroundOpacity), defaultValue: 0.1);

    public static readonly StyledProperty<double> InactiveTextOpacityProperty =
        AvaloniaProperty.Register<StatusLabel, double>(nameof(InactiveTextOpacity), defaultValue: 0.3);


    /// <summary>
    /// When set, this brush is used as the background in the IsActive=false state.
    /// When null (default), the opacity-based dimming is used instead.
    /// </summary>
    public static readonly StyledProperty<IBrush?> InactiveBackgroundProperty =
        AvaloniaProperty.Register<StatusLabel, IBrush?>(nameof(InactiveBackground), defaultValue: null);

    /// <summary>
    /// When set, this brush is used as the text foreground in the IsActive=false state.
    /// When null (default), the opacity-based dimming is used instead.
    /// </summary>
    public static readonly StyledProperty<IBrush?> InactiveForegroundProperty =
        AvaloniaProperty.Register<StatusLabel, IBrush?>(nameof(InactiveForeground), defaultValue: null);

    /// <summary>
    /// When set, this FontWeight is applied to the text in the IsActive=false state.
    /// When null (default), FontWeight is passed through unchanged for both states.
    /// </summary>
    public static readonly StyledProperty<FontWeight?> InactiveFontWeightProperty =
        AvaloniaProperty.Register<StatusLabel, FontWeight?>(nameof(InactiveFontWeight), defaultValue: null);


    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public HorizontalAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public double ActiveBackgroundOpacity
    {
        get => GetValue(ActiveBackgroundOpacityProperty);
        set => SetValue(ActiveBackgroundOpacityProperty, value);
    }

    public double ActiveTextOpacity
    {
        get => GetValue(ActiveTextOpacityProperty);
        set => SetValue(ActiveTextOpacityProperty, value);
    }

    public double InactiveBackgroundOpacity
    {
        get => GetValue(InactiveBackgroundOpacityProperty);
        set => SetValue(InactiveBackgroundOpacityProperty, value);
    }

    public double InactiveTextOpacity
    {
        get => GetValue(InactiveTextOpacityProperty);
        set => SetValue(InactiveTextOpacityProperty, value);
    }

    public IBrush? InactiveBackground
    {
        get => GetValue(InactiveBackgroundProperty);
        set => SetValue(InactiveBackgroundProperty, value);
    }

    public IBrush? InactiveForeground
    {
        get => GetValue(InactiveForegroundProperty);
        set => SetValue(InactiveForegroundProperty, value);
    }

    public FontWeight? InactiveFontWeight
    {
        get => GetValue(InactiveFontWeightProperty);
        set => SetValue(InactiveFontWeightProperty, value);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Computed direct properties (used by the control template only)

    /// <summary>Effective background brush (PART_BorderElement).</summary>
    public static readonly DirectProperty<StatusLabel, IBrush?> ComputedBackgroundProperty =
        AvaloniaProperty.RegisterDirect<StatusLabel, IBrush?>(
            nameof(ComputedBackground),
            o => o.ComputedBackground);

    /// <summary>Effective foreground brush (PART_TextBlockElement).</summary>
    public static readonly DirectProperty<StatusLabel, IBrush?> ComputedForegroundProperty =
        AvaloniaProperty.RegisterDirect<StatusLabel, IBrush?>(
            nameof(ComputedForeground),
            o => o.ComputedForeground);

    /// <summary>Effective background opacity (1.0 when InactiveBackground is set).</summary>
    public static readonly DirectProperty<StatusLabel, double> ComputedBackgroundOpacityProperty =
        AvaloniaProperty.RegisterDirect<StatusLabel, double>(
            nameof(ComputedBackgroundOpacity),
            o => o.ComputedBackgroundOpacity);

    /// <summary>Effective text opacity (1.0 when InactiveForeground is set).</summary>
    public static readonly DirectProperty<StatusLabel, double> ComputedForegroundOpacityProperty =
        AvaloniaProperty.RegisterDirect<StatusLabel, double>(
            nameof(ComputedForegroundOpacity),
            o => o.ComputedForegroundOpacity);

    /// <summary>Effective FontWeight (PART_TextBlockElement).</summary>
    public static readonly DirectProperty<StatusLabel, FontWeight> ComputedFontWeightProperty =
        AvaloniaProperty.RegisterDirect<StatusLabel, FontWeight>(
            nameof(ComputedFontWeight),
            o => o.ComputedFontWeight);


    private IBrush? _computedBackground;
    private IBrush? _computedForeground;
    private double _computedBackgroundOpacity;
    private double _computedForegroundOpacity;
    private FontWeight _computedFontWeight;


    public IBrush? ComputedBackground
    {
        get => _computedBackground;
        private set => SetAndRaise(ComputedBackgroundProperty, ref _computedBackground, value);
    }

    public IBrush? ComputedForeground
    {
        get => _computedForeground;
        private set => SetAndRaise(ComputedForegroundProperty, ref _computedForeground, value);
    }

    public double ComputedBackgroundOpacity
    {
        get => _computedBackgroundOpacity;
        private set => SetAndRaise(ComputedBackgroundOpacityProperty, ref _computedBackgroundOpacity, value);
    }

    public double ComputedForegroundOpacity
    {
        get => _computedForegroundOpacity;
        private set => SetAndRaise(ComputedForegroundOpacityProperty, ref _computedForegroundOpacity, value);
    }

    public FontWeight ComputedFontWeight
    {
        get => _computedFontWeight;
        private set => SetAndRaise(ComputedFontWeightProperty, ref _computedFontWeight, value);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public StatusLabel()
    {
        // The initial state must match the defaults (inactive)
        UpdateComputedValues();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // State computation

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Re-compute whenever any relevant input changes
        if ((change.Property == IsActiveProperty) ||
            (change.Property == BackgroundProperty) ||
            (change.Property == ForegroundProperty) ||
            (change.Property == InactiveBackgroundProperty) ||
            (change.Property == InactiveForegroundProperty) ||
            (change.Property == InactiveFontWeightProperty) ||
            (change.Property == FontWeightProperty) ||
            (change.Property == ActiveBackgroundOpacityProperty) ||
            (change.Property == ActiveTextOpacityProperty) ||
            (change.Property == InactiveBackgroundOpacityProperty) ||
            (change.Property == InactiveTextOpacityProperty))
        {
            UpdateComputedValues();
        }
    }


    private void UpdateComputedValues()
    {
        bool active = IsActive;

        // Background
        if (InactiveBackground is IBrush inactiveBackground)
        {
            // Swap mode: Background (active) <-> InactiveBackground (inactive)
            ComputedBackground = active ? Background : inactiveBackground;
            ComputedBackgroundOpacity = 1.0;    // the brush shows the state
        }
        else
        {
            // Opacity mode: the same brush with a state-dependent opacity
            ComputedBackground = Background;
            ComputedBackgroundOpacity = active ? ActiveBackgroundOpacity : InactiveBackgroundOpacity;
        }

        // Foreground
        if (InactiveForeground is IBrush inactiveForeground)
        {
            // Swap mode: Foreground (active) <-> InactiveForeground (inactive)
            ComputedForeground = active ? Foreground : inactiveForeground;
            ComputedForegroundOpacity = 1.0;
        }
        else
        {
            // Opacity mode: the same brush with a state-dependent opacity
            ComputedForeground = Foreground;
            ComputedForegroundOpacity = active ? ActiveTextOpacity : InactiveTextOpacity;
        }

        // FontWeight: swapped only if InactiveFontWeight is set
        ComputedFontWeight = (!active && (InactiveFontWeight is FontWeight inactiveFontWeight))
            ? inactiveFontWeight
            : FontWeight;
    }
}

// END-OF-FILE
