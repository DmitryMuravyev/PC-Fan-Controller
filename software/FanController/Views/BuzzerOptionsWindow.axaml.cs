////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// BuzzerOptionsWindow.axaml.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FanController.ViewModels;


namespace FanController.Views;

/// <summary>
/// Buzzer alarms options window. The switches edit the System ViewModel directly
/// (changes are sent to the device by its behavior throttle).
/// </summary>
public partial class BuzzerOptionsWindow : Window
{
    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    /// <summary>
    /// Default constructor for the Avalonia axaml design previewer
    /// (the application must use the constructor with a ViewModel).
    /// </summary>
    public BuzzerOptionsWindow()
    {
        DataContext = new SystemViewModel(default);
        InitializeComponent();
    }


    /// <summary>
    /// Creates the window for the given System ViewModel.
    /// </summary>
    public BuzzerOptionsWindow(SystemViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Window events

    // The window must fit the screen (e.g. 768 px high laptop screens, display scaling)
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WindowScreenFit.FitToScreen(this);
    }


    private void OnDoneClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

// END-OF-FILE
