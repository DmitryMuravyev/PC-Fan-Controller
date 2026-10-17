////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// HostSensorListWindow.axaml.cs
////////////////////////////////////////////////////////////////////////////////////////

using Avalonia.Controls;
using Avalonia.Interactivity;
using FanController.Assets.Localization;
using FanController.Contracts;
using FanController.ViewModels;
using System;
using System.Threading.Tasks;


namespace FanController.Views;

/// <summary>
/// Host Sensor List dialog: browse the host sensors and add them
/// as software sensors one by one without closing the window.
/// </summary>
public partial class HostSensorListWindow : Window
{
    private readonly HostSensorListViewModel _viewModel;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    // Default constructor for the Avalonia AXAML design previewer.
    // No ViewModel here: it starts polling the host as soon as it is created.
    public HostSensorListWindow()
    {
        _viewModel = null!;
        InitializeComponent();
    }


    public HostSensorListWindow(HostSensorListViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }


    // The window must fit the screen (e.g. 768 px high laptop screens, display scaling)
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WindowScreenFit.FitToScreen(this);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Helpers

    /// <summary>
    /// Displays a dialog box with a generic command execution error message. 
    /// </summary>
    private Task<bool?> ShowCommandErrorMessage(Window window)
    {
        var errorDialog = new ConfirmDialog(
            Strings.DialogCommandErrorHeaderFormat,
            Strings.DialogCommandErrorMessageFormat,
            Strings.ButtonOkCaption,
            Strings.DialogErrorTitle,
            true);

        return errorDialog.ShowDialog<bool?>(window);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Command buttons

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if ((_viewModel.SelectedNode is not { } node) ||
            (_viewModel.PrepareSensorConfig(node) is not { } prepared))
        {
            // Wrong sensor selected (normally prevented by the disabled Add button)
            var dialog = new ConfirmDialog(
                Strings.DialogWrongHostSensorHeaderFormat,
                Strings.DialogWrongHostSensorMessageFormat,
                Strings.ButtonOkCaption,
                null,
                true);

            await dialog.ShowDialog<bool?>(this);
            return;
        }

        var details = new SensorDetailsWindow(
            new SensorDetailsViewModel(prepared.Config, prepared.FanOutputs, isAddWindow: true));

        // ShowDialog returns whatever Close(result) was called with
        var confirmedConfig = await details.ShowDialog<SensorConfigDto?>(this);

        if ((confirmedConfig is not null) && !await _viewModel.AddSoftwareSensor(node, confirmedConfig))
            await ShowCommandErrorMessage(this);
    }


    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

}

// END-OF-FILE
