////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// FanView.axaml.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Controls.ApplicationLifetimes;
using FanController.Assets.Localization;
using FanController.Contracts;
using FanController.Controls;
using FanController.ViewModels;
using static FanController.ViewModels.FanItem;


namespace FanController.Views;

/// <summary>
/// Fans Tab contents View
/// </summary>
public partial class FanView : UserControl
{
    // View Model object for direct accessing it's properties.
    private FanViewModel? _viewModel = null;

    // "Select all" CheckBox in the table header (bound in code, see TryBindHeaderCheckBox)
    private CheckBox? _headerCheckBox;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public FanView()
    {
        InitializeComponent();

        // The previously used method for limiting the sending
        // of multiple commands when moving the slider.
        //FanListDataGrid.AddHandler(CompactSlider.ThumbDragStartedEvent, OnAnySliderDragStarted);
        //FanListDataGrid.AddHandler(CompactSlider.ThumbDragCompletedEvent, OnAnySliderDragCompleted);
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

        _viewModel = DataContext as FanViewModel;

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
        if (_headerCheckBox is null || DataContext is not FanViewModel vm)
            return;

        _headerCheckBox.Bind(
            CheckBox.IsCheckedProperty,
            new Binding(nameof(vm.AllFansSelected))
            {
                Source = vm,
                Mode = BindingMode.TwoWay
            });
    }


    /// <summary>
    /// Resolves the owning <see cref="Window"/> and current ViewModel.
    /// Returns false (and sets both out params to null) if either is unavailable.
    /// </summary>
    private bool TryGetWindowAndViewModel(
        [NotNullWhen(true)] out Window? window,
        [NotNullWhen(true)] out FanViewModel? viewModel)
    {
        window = TopLevel.GetTopLevel(this) as Window;
        viewModel = _viewModel;
        return (window is not null) && (viewModel is not null);
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


    private static string GetFanName(FanItem fan) =>
        String.IsNullOrWhiteSpace(fan.FanConfig.Name)
            ? String.Format(Strings.FanDefaultNameFormat, fan.Index)
            : fan.FanConfig.Name;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Command buttons & menu items
    // Toolbar buttons (DataContext = FanViewModel) apply to the selected fans,
    // row menu items (DataContext = FanItem) apply to that fan only.

    private async void OnDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (!TryGetWindowAndViewModel(out var window, out _)) return;
        if ((sender as MenuItem)?.DataContext is not FanItem fan) return;

        var details = new FanDetailsWindow(new FanDetailsViewModel(fan.FanConfig));

        // ShowDialog returns whatever Close(result) was called with
        var updated = await details.ShowDialog<FanConfigDto?>(window);

        if ((updated is not null) && !await fan.UpdateConfiguration(updated))
            await ShowCommandErrorMessage(window);
    }


    private async void OnResetStateClick(object? sender, RoutedEventArgs e)
    {
        bool succeeded;
        if ((sender is MenuItem menuItem) && (menuItem.DataContext is FanItem fan))
            succeeded = await fan.ResetState();
        else if ((sender is Button button) && (button.DataContext is FanViewModel viewModel))
            succeeded = await viewModel.ResetState();
        else return;

        if (!succeeded)
        {
            if (!TryGetWindowAndViewModel(out var window, out _)) return;
            await ShowCommandErrorMessage(window);
        }
    }


    private async void OnControlModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.Tag is not FanControlMode mode) return;
        if (menuItem.DataContext is not FanItem fan) return;

        if (!await fan.SetControlMode(mode))
        {
            if (!TryGetWindowAndViewModel(out var window, out _)) return;
            await ShowCommandErrorMessage(window);
        }
    }


    private async void OnOperationModeClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not FanOperationMode mode) return;

        bool succeeded;
        if ((sender is MenuItem menuItem) && (menuItem.DataContext is FanItem fan))
            succeeded = await fan.SetOperationMode(mode);
        else if ((sender is Button button) && (button.DataContext is FanViewModel viewModel))
            succeeded = await viewModel.SetOperationMode(mode);
        else return;

        if (!succeeded)
        {
            if (!TryGetWindowAndViewModel(out var window, out _)) return;
            await ShowCommandErrorMessage(window);
        }
    }


    private async void OnPowerProfileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.Tag is not FanPowerProfile profile) return;

        bool succeeded;
        if (menuItem.DataContext is FanItem fan)
            succeeded = await fan.SetPowerProfile(profile);
        else if (menuItem.DataContext is FanViewModel fvm)
            succeeded = await fvm.SetPowerProfile(profile);
        else return;

        if (!succeeded)
        {
            if (!TryGetWindowAndViewModel(out var window, out _)) return;
            await ShowCommandErrorMessage(window);
        }
    }


    private async void OnRescanHardwareClick(object? sender, RoutedEventArgs e)
    {
        if ((sender is MenuItem menuItem) && (menuItem.DataContext is FanItem fan))
        {
            await ConfirmAndExecuteAsync(
                String.Format(Strings.DialogScanFanHardwareHeaderFormat, GetFanName(fan)),
                String.Format(Strings.DialogScanFanHardwareMessageFormat,
                    FanItemConstants.HardwareScanMaxDurationMinutes),
                fan.RescanHardware);
        }
        else if ((sender is Button button) && (button.DataContext is FanViewModel viewModel))
        {
            int fanCount = viewModel.ScanAll ? viewModel.Fans.Count : viewModel.SelectedFansCount;
            double minutes = fanCount * FanItemConstants.HardwareScanMaxDurationMinutes;

            await ConfirmAndExecuteAsync(
                viewModel.ScanAll
                    ? Strings.DialogScanAllFanHardwareHeaderFormat
                    : String.Format(Strings.DialogScanSelectedFanHardwareHeaderFormat, fanCount),
                String.Format(viewModel.ScanAll
                    ? Strings.DialogScanAllFanHardwareMessageFormat
                    : Strings.DialogScanSelectedFanHardwareMessageFormat, minutes),
                viewModel.ScanHardware);
        }
    }

    private async void OnRescanModelClick(object? sender, RoutedEventArgs e)
    {
        if ((sender is MenuItem menuItem) && (menuItem.DataContext is FanItem fan))
        {
            await ConfirmAndExecuteAsync(
                String.Format(Strings.DialogScanFanModelHeaderFormat, GetFanName(fan)),
                String.Format(Strings.DialogScanFanModelMessageFormat,
                    FanItemConstants.ModelScanMaxDurationMinutes),
                fan.RescanModel);
        }
        else if ((sender is Button button) && (button.DataContext is FanViewModel viewModel))
        {
            int fanCount = viewModel.ScanAll ? viewModel.Fans.Count : viewModel.SelectedFansCount;
            double minutes = fanCount * FanItemConstants.ModelScanMaxDurationMinutes;

            await ConfirmAndExecuteAsync(
                viewModel.ScanAll
                    ? Strings.DialogScanAllFanModelHeaderFormat
                    : String.Format(Strings.DialogScanSelectedFanModelHeaderFormat, fanCount),
                String.Format(viewModel.ScanAll
                    ? Strings.DialogScanAllFanModelMessageFormat
                    : Strings.DialogScanSelectedFanModelMessageFormat, minutes),
                viewModel.ScanModels);
        }
    }

}

// END-OF-FILE
