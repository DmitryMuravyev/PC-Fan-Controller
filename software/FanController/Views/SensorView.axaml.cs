////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SensorView.axaml.cs
////////////////////////////////////////////////////////////////////////////////////////

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Interactivity;
using FanController.Assets.Localization;
using FanController.Contracts;
using FanController.ViewModels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;


namespace FanController.Views;

/// <summary>
/// Sensors Tab contents View
/// </summary>
public partial class SensorView : UserControl
{
    // View Model object for direct accessing it's properties.
    private SensorViewModel? _viewModel = null;

    // "Select all" CheckBox in the table header (bound in code, see TryBindHeaderCheckBox)
    private CheckBox? _headerCheckBox;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public SensorView()
    {
        InitializeComponent();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Helpers

    /// <summary>
    /// Tracking the assignment of the initialized ViewModel object to the DataContext.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.CommandCompleted -= OnCommandCompleted;

        _viewModel = DataContext as SensorViewModel;

        if (_viewModel is not null)
            _viewModel.CommandCompleted += OnCommandCompleted;

        TryBindHeaderCheckBox();
    }


    private void OnHeaderCheckBoxInitialized(object? sender, EventArgs e)
    {
        _headerCheckBox = (CheckBox)sender!;
        TryBindHeaderCheckBox();
    }


    // Reports the device errors of the commands awaited in the background
    private async void OnCommandCompleted(object? sender, CommandCompletedEventArgs e)
    {
        if (e.Outcome != CommandOutcome.Failed) return;

        // The tab may not be displayed: fall back to the main window
        Window? window = (TopLevel.GetTopLevel(this) as Window) ??
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        if (window is not null)
            await ShowCommandErrorMessage(window);
    }


    // Binds once both the header CheckBox and the ViewModel are available
    private void TryBindHeaderCheckBox()
    {
        if ((_headerCheckBox is null) || (_viewModel is null)) return;

        _headerCheckBox.Bind(
            CheckBox.IsCheckedProperty,
            new Binding(nameof(SensorViewModel.AllSensorsSelected))
            {
                Source = _viewModel,
                Mode = BindingMode.TwoWay
            });
    }


    /// <summary>
    /// Resolves the owning <see cref="Window"/> and current ViewModel.
    /// Returns false (and sets both out params to null) if either is unavailable.
    /// </summary>
    private bool TryGetWindowAndViewModel(
        [NotNullWhen(true)] out Window? window,
        [NotNullWhen(true)] out SensorViewModel? viewModel)
    {
        window = TopLevel.GetTopLevel(this) as Window;
        viewModel = _viewModel;
        return ((window is not null) && (viewModel is not null));
    }


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


    /// <summary>
    /// Asks for confirmation, executes the command and reports a failure.
    /// </summary>
    private async Task ConfirmAndExecuteAsync(string header, string message, Func<Task<bool>> command)
    {
        if (!TryGetWindowAndViewModel(out var window, out _)) return;

        var dialog = new ConfirmDialog(header, message);
        if (await dialog.ShowDialog<bool?>(window) != true) return;

        if (!await command())
            await ShowCommandErrorMessage(window);
    }


    private static string GetSensorName(SensorItem sensor) =>
        String.IsNullOrWhiteSpace(sensor.SensorConfig.Name)
            ? String.Format(Strings.SensorDefaultNameFormat, sensor.Index)
            : sensor.SensorConfig.Name;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Command buttons & menu items
    // Toolbar buttons (DataContext = SensorViewModel) apply to the selected sensors,
    // row menu items (DataContext = SensorItem) apply to that sensor only.

    private async void OnAddSensorClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetWindowAndViewModel(out var window, out var viewModel)) return;

        var devices = await viewModel.GetHostSensorList();

        if (devices is null)
        {
            await ShowCommandErrorMessage(window);
            return;
        }

        if (devices.Count == 0)
        {
            // No Host sensor found
            var dialog = new ConfirmDialog(
                Strings.DialogNoHostSensorHeaderFormat,
                Strings.DialogNoHostSensorMessageFormat,
                Strings.ButtonOkCaption,
                null,
                true);

            await dialog.ShowDialog<bool?>(window);
            return;
        }

        var hostViewModel = new HostSensorListViewModel(viewModel, devices);
        var hostWindow = new HostSensorListWindow(hostViewModel);
        viewModel.SensorFound += hostViewModel.OnParentSensorFound;

        try
        {
            await hostWindow.ShowDialog(window);
        }
        finally
        {
            // Unsubscribe child VM from the sensor events
            viewModel.SensorFound -= hostViewModel.OnParentSensorFound;
            await hostViewModel.DisposeAsync(); // Cleanly awaits loop termination
        }
    }


    private async void OnRemoveSensorClick(object? sender, RoutedEventArgs e)
    {
        if ((sender is MenuItem menuItem) && (menuItem.DataContext is SensorItem sensor))
        {
            await ConfirmAndExecuteAsync(
                String.Format(Strings.DialogDeleteSensorHeaderFormat, GetSensorName(sensor)),
                Strings.DialogDeleteSensorMessageFormat,
                sensor.RemoveSensor);
        }
        else if ((sender is Button button) && (button.DataContext is SensorViewModel viewModel))
        {
            await ConfirmAndExecuteAsync(
                Strings.DialogDeleteSensorsHeaderFormat,
                Strings.DialogDeleteSensorsMessageFormat,
                viewModel.RemoveSelectedSensors);
        }
    }


    private async void OnResetStateClick(object? sender, RoutedEventArgs e)
    {
        bool succeeded;
        if ((sender is MenuItem menuItem) && (menuItem.DataContext is SensorItem sensor))
            succeeded = await sensor.ResetState();
        else if ((sender is Button button) && (button.DataContext is SensorViewModel viewModel))
            succeeded = await viewModel.ResetState();
        else return;

        if (!succeeded && TryGetWindowAndViewModel(out var window, out _))
            await ShowCommandErrorMessage(window);
    }


    private async void OnDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetWindowAndViewModel(out var window, out _)) return;
        if ((sender as MenuItem)?.DataContext is not SensorItem sensor) return;

        var details = new SensorDetailsWindow(
            new SensorDetailsViewModel(sensor.SensorConfig, sensor.FanOutputs));

        // ShowDialog returns whatever Close(result) was called with
        var updated = await details.ShowDialog<SensorConfigDto?>(window);

        if ((updated is not null) && !await sensor.UpdateConfiguration(updated))
            await ShowCommandErrorMessage(window);
    }


    private async void OnRescanSensorsClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not SensorViewModel viewModel) return;

        await ConfirmAndExecuteAsync(
            Strings.DialogScanSensorsHeaderFormat,
            Strings.DialogScanSensorsMessageFormat,
            viewModel.ScanSensors);
    }

}

// END-OF-FILE
