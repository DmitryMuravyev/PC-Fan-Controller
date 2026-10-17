////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// BusyTabControl.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using Avalonia;
using Avalonia.Controls;


namespace FanController.Controls;

/// <summary>
/// TabControl that can disable its content and cover it with a busy overlay
/// (message and progress bar) during long operations: initialization, fan scanning, etc.
/// The tab headers stay available.
/// </summary>
public class BusyTabControl : TabControl
{
    ////////////////////////////////////////////////////////////////////////////////////////
    // Styled properties

    // Enable or disable displayed content (TabItems body, but not the header)
    public static readonly StyledProperty<bool> IsContentEnabledProperty =
        AvaloniaProperty.Register<BusyTabControl, bool>(nameof(IsContentEnabled), defaultValue: true);

    // The overlay is shown (the content is disabled meanwhile)
    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<BusyTabControl, bool>(nameof(IsBusy), defaultValue: false);

    // Overlay message
    public static readonly StyledProperty<string?> BusyMessageProperty =
        AvaloniaProperty.Register<BusyTabControl, string?>(nameof(BusyMessage), defaultValue: null);

    // Progress in percent (0..100), shown unless IsIndeterminate
    public static readonly StyledProperty<double> BusyPercentageProperty =
        AvaloniaProperty.Register<BusyTabControl, double>(
            nameof(BusyPercentage), defaultValue: 0.0, coerce: CoercePercentage);

    // In case the task completion percentage is unknown
    public static readonly StyledProperty<bool> IsIndeterminateProperty =
        AvaloniaProperty.Register<BusyTabControl, bool>(nameof(IsIndeterminate), defaultValue: true);


    public bool IsContentEnabled
    {
        get => GetValue(IsContentEnabledProperty);
        set => SetValue(IsContentEnabledProperty, value);
    }

    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string? BusyMessage
    {
        get => GetValue(BusyMessageProperty);
        set => SetValue(BusyMessageProperty, value);
    }

    public double BusyPercentage
    {
        get => GetValue(BusyPercentageProperty);
        set => SetValue(BusyPercentageProperty, value);
    }

    public bool IsIndeterminate
    {
        get => GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Coercion

    private static double CoercePercentage(AvaloniaObject obj, double value) =>
        double.IsNaN(value) ? 0.0 : Math.Clamp(value, 0.0, 100.0);
}

// END-OF-FILE
