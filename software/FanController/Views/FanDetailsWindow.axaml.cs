////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// FanDetailsWindow.axaml.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FanController.Assets.Localization;
using FanController.Contracts;
using FanController.ViewModels;


namespace FanController.Views;

/// <summary>
/// Fan details (configuration editor) dialog.
/// Returns the updated <see cref="FanConfigDto"/>, or null if cancelled.
/// </summary>
public partial class FanDetailsWindow : Window
{
    private readonly FanDetailsViewModel _viewModel;

    // Name for the dialog headers: "<new> (<original>)" while the name is being changed
    private string DisplayName =>
        String.IsNullOrWhiteSpace(_viewModel.Name)
            ? String.Format(Strings.FanDefaultNameFormat, _viewModel.Index)
            : (String.IsNullOrWhiteSpace(_viewModel.Original.Name) || (_viewModel.Name == _viewModel.Original.Name))
                ? _viewModel.Name
                : $"{_viewModel.Name} ({_viewModel.Original.Name})";


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    // Default constructor for the Avalonia AXAML design previewer
    public FanDetailsWindow()
        : this(new FanDetailsViewModel(DtoDefaults.Fan.NewConfig()))
    {
    }


    public FanDetailsWindow(FanDetailsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Closing += OnWindowClosing;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Helpers

    // Closes the window without the unsaved changes confirmation
    private void CloseWithResult(FanConfigDto? result)
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

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new ConfirmDialog(
            String.Format(Strings.DialogItemConfigNotSavedHeaderFormat, DisplayName),
            Strings.DialogConfigNotSavedConfirmMessageFormat,
            Strings.ButtonSaveCaption);

        if (await dialog.ShowDialog<bool?>(this) == true)
            CloseWithResult(_viewModel.BuildUpdatedDto());
    }


    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

}

// END-OF-FILE
