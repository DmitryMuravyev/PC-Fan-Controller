////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// WindowScreenFit.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using Avalonia.Controls;


namespace FanController.Views;

/// <summary>
/// Keeps a window within the working area of its screen
/// (small laptop screens, display scaling).
/// </summary>
public static class WindowScreenFit
{
    // Space left for the window frame and title bar
    private const double FrameMargin = 40;


    /// <summary>
    /// Reduces the window size (and its minimum size, if necessary) to the screen.
    /// Call after the window has been opened.
    /// </summary>
    public static void FitToScreen(Window window)
    {
        if (window.Screens.ScreenFromWindow(window) is not { } screen) return;

        double availableHeight = (screen.WorkingArea.Height / screen.Scaling) - FrameMargin;
        double availableWidth = (screen.WorkingArea.Width / screen.Scaling) - FrameMargin;

        if (window.MinHeight > availableHeight) window.MinHeight = availableHeight;
        if (window.MinWidth > availableWidth) window.MinWidth = availableWidth;

        if (window.ClientSize.Height > availableHeight) window.Height = availableHeight;
        if (window.ClientSize.Width > availableWidth) window.Width = availableWidth;
    }
}

// END-OF-FILE
