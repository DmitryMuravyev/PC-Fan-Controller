////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SensorDetailsWindow.axaml.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FanController.Assets.Localization;
using FanController.Contracts;
using FanController.ViewModels;


namespace FanController.Views;

/// <summary>
/// Sensor details (configuration editor) dialog, also used to set up a new software sensor.
/// Returns the updated <see cref="SensorConfigDto"/>, or null if cancelled.
/// </summary>
public partial class SensorDetailsWindow : Window
{
    private readonly SensorDetailsViewModel _viewModel;

    // Name for the dialog headers: "<new> (<original>)" while the name is being changed
    private string DisplayName =>
        String.IsNullOrWhiteSpace(_viewModel.Name)
            ? String.Format(Strings.SensorDefaultNameFormat, _viewModel.Index)
            : (String.IsNullOrWhiteSpace(_viewModel.Original.Name) || (_viewModel.Name == _viewModel.Original.Name))
                ? _viewModel.Name
                : $"{_viewModel.Name} ({_viewModel.Original.Name})";


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    // Default constructor for the Avalonia AXAML design previewer
    public SensorDetailsWindow()
        : this(new SensorDetailsViewModel(DtoDefaults.Sensor.NewConfig(), []))
    {
    }


    public SensorDetailsWindow(SensorDetailsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Closing += OnWindowClosing;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Helpers

    // Closes the window without the unsaved changes confirmation
    private void CloseWithResult(SensorConfigDto? result)
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
            _viewModel.IsAddWindow ? Strings.ButtonAddSensorCaption : Strings.ButtonSaveCaption);

        if (await dialog.ShowDialog<bool?>(this) == true)
            CloseWithResult(_viewModel.BuildUpdatedDto());
    }


    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }


    private async void OnCloneSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.CurrentFanOutput is not FanOutput fanOutput) return;

        var dialog = new ConfirmDialog(
            String.Format(Strings.DialogCloneItemConfigHeaderFormat, fanOutput.FanName),
            Strings.DialogCloneItemConfigMessageFormat,
            Strings.ButtonCloneCaption);

        if (await dialog.ShowDialog<bool?>(this) == true)
            _viewModel.CloneCurrentFanOutputSettings();
    }

}

// END-OF-FILE
