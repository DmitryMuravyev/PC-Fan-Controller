////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// HostListWindow.axaml.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using FanController.Assets.Localization;
using FanController.Services;
using FanController.Settings;
using FanController.ViewModels;


namespace FanController.Views;

/// <summary>
/// Host list dialog: add, edit, remove and reconnect the daemon hosts.
/// </summary>
public partial class HostListWindow : Window
{
    private readonly HostListViewModel _viewModel;

    // "Select all" CheckBox in the table header (bound in code, see TryBindHeaderCheckBox)
    private CheckBox? _headerCheckBox = null;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    // Default constructor for the Avalonia AXAML design previewer.
    // No ViewModel here: it requires the host list.
    public HostListWindow()
    {
        _viewModel = null!;
        InitializeComponent();
    }


    public HostListWindow(HostListViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Helpers

    private void OnHeaderCheckBoxInitialized(object? sender, EventArgs e)
    {
        _headerCheckBox = sender as CheckBox;
        TryBindHeaderCheckBox();
    }


    private void TryBindHeaderCheckBox()
    {
        if ((_headerCheckBox is null) || (_viewModel is null)) return;

        _headerCheckBox.Bind(
            CheckBox.IsCheckedProperty,
            new Binding(nameof(HostListViewModel.AllHostsSelected))
            {
                Source = _viewModel,
                Mode = BindingMode.TwoWay
            });
    }


    /// <summary>
    /// Reports a failed host list operation. If only the settings could not be saved,
    /// the change itself is applied, so a dedicated header is used.
    /// </summary>
    private Task ShowEditResultAsync(string failedHeader, ServiceListEditResult result, HostConfig config)
    {
        string header = (result == ServiceListEditResult.SettingsSaveFailed)
            ? Strings.DialogSettingsSaveFailedHeaderFormat
            : failedHeader;

        return new ConfirmDialog(
            header,
            GetHostListErrorMessage(result, config),
            Strings.ButtonOkCaption,
            Strings.DialogErrorTitle,
            true).ShowDialog<bool?>(this);
    }


    private static string GetHostListErrorMessage(ServiceListEditResult result, HostConfig config) =>
        result switch
        {
            ServiceListEditResult.NameAlreadyExist =>
                String.Format(Strings.DialogHostListNameAlreadyExistMessageFormat, config.Name),
            ServiceListEditResult.EndpointAlreadyExist =>
                String.Format(Strings.DialogHostListEndpointAlreadyExistMessageFormat, config.Host, config.Port),
            ServiceListEditResult.ItemNotFound =>
                Strings.DialogHostListItemNotFoundMessageFormat,
            ServiceListEditResult.SettingsSaveFailed =>
                Strings.DialogHostListSettingsSaveFailedMessageFormat,
            _ =>
                Strings.DialogHostListUnknownErrorMessageFormat
        };


    ////////////////////////////////////////////////////////////////////////////////////////
    // Command buttons & menu items
    // Toolbar buttons apply to the selected hosts, row menu items to that host only.

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        var details = new HostDetailsWindow(new HostDetailsViewModel(_viewModel, null));

        var config = await details.ShowDialog<HostConfig?>(this);
        if (config is null) return;     // Cancelled

        var result = await _viewModel.AddHostAsync(config);
        if (result != ServiceListEditResult.Success)
            await ShowEditResultAsync(Strings.DialogHostListAddFailedHeaderFormat, result, config);
    }


    private async void OnEditClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is not HostItem host) return;

        var details = new HostDetailsWindow(new HostDetailsViewModel(_viewModel, host.Service.Config));

        var config = await details.ShowDialog<HostConfig?>(this);
        if (config is null) return;     // Cancelled

        var result = await _viewModel.UpdateHostAsync(config);
        if (result != ServiceListEditResult.Success)
            await ShowEditResultAsync(Strings.DialogHostListEditFailedHeaderFormat, result, config);
    }


    private async void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        ServiceListEditResult result;
        HostConfig? failedConfig;

        if ((sender is MenuItem menuItem) && (menuItem.DataContext is HostItem host))
        {
            var dialog = new ConfirmDialog(
                Strings.DialogRemoveHostHeaderFormat,
                String.Format(Strings.DialogRemoveHostMessageFormat, host.Name));

            if (await dialog.ShowDialog<bool?>(this) != true) return;

            failedConfig = host.Service.Config;
            result = await _viewModel.RemoveHostAsync(failedConfig.Id);
        }
        else if ((sender is Button) && _viewModel.SelectionNotEmpty)
        {
            var dialog = new ConfirmDialog(
                Strings.DialogRemoveHostsHeaderFormat,
                Strings.DialogRemoveHostsMessageFormat);

            if (await dialog.ShowDialog<bool?>(this) != true) return;

            (result, failedConfig) = await _viewModel.RemoveSelectedHostsAsync();
        }
        else return;

        if ((result != ServiceListEditResult.Success) && (failedConfig is not null))
            await ShowEditResultAsync(Strings.DialogHostListRemoveFailedHeaderFormat, result, failedConfig);
    }


    private async void OnReconnectClick(object? sender, RoutedEventArgs e)
    {
        ServiceListEditResult result;
        HostConfig? failedConfig;

        if ((sender is MenuItem menuItem) && (menuItem.DataContext is HostItem host))
        {
            failedConfig = host.Service.Config;
            result = await _viewModel.ReconnectHostAsync(failedConfig.Id);
        }
        else if ((sender is Button) && _viewModel.SelectionNotEmpty)
        {
            (result, failedConfig) = await _viewModel.ReconnectSelectedHostsAsync();
        }
        else return;

        if ((result != ServiceListEditResult.Success) && (failedConfig is not null))
            await ShowEditResultAsync(Strings.DialogHostListReconnectFailedHeaderFormat, result, failedConfig);
    }


    private void OnDoneClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

}

// END-OF-FILE
