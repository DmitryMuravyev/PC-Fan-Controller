////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// HostDetailsWindow.axaml.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FanController.Assets.Localization;
using FanController.Settings;
using FanController.ViewModels;


namespace FanController.Views;

/// <summary>
/// Host details dialog: adds a new host or edits an existing one.
/// Returns the resulting <see cref="HostConfig"/>, or null if cancelled.
/// </summary>
public partial class HostDetailsWindow : Window
{
    private readonly HostDetailsViewModel _viewModel;

    // Name for the dialog headers (a new host may not have a name yet)
    private string DisplayName =>
        !String.IsNullOrWhiteSpace(_viewModel.Name) ? _viewModel.Name.Trim()
        : !String.IsNullOrWhiteSpace(_viewModel.Host) ? _viewModel.Host.Trim()
        : Strings.HostAddWindowTitle;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    // Default constructor for the Avalonia AXAML design previewer.
    // No ViewModel here: it requires the host list.
    public HostDetailsWindow()
    {
        _viewModel = null!;
        InitializeComponent();
    }


    public HostDetailsWindow(HostDetailsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Closing += OnWindowClosing;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Helpers

    // Closes the window without the unsaved changes confirmation
    private void CloseWithResult(HostConfig? result)
    {
        Closing -= OnWindowClosing;
        Close(result);
    }


    // Any close request (Cancel button, title bar, system): confirm discarding the changes
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_viewModel.IsDirty) return;

        e.Cancel = true;

        var dialog = new ConfirmDialog(
            String.Format(Strings.DialogItemConfigNotSavedHeaderFormat, DisplayName),
            Strings.DialogConfigNotSavedDiscardMessageFormat,
            Strings.ButtonCloseCaption);

        if (await dialog.ShowDialog<bool?>(this) == true)
            CloseWithResult(null);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Command buttons

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.IsValid)
            CloseWithResult(_viewModel.BuildConfig());
    }


    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

}

// END-OF-FILE
