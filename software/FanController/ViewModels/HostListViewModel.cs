////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// HostListViewModel.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using FanController.Services;
using FanController.Settings;


namespace FanController.ViewModels;

/// <summary>
/// Host list window View Model: the configured hosts (kept in sync with
/// <see cref="ServiceList"/>) and the host list editing operations.
/// Dispose it when the window is closed.
/// </summary>
public partial class HostListViewModel : ObservableObject, IDisposable
{
    private readonly ServiceList _serviceList;
    private bool _disposed = false;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Observable state

    public ObservableCollection<HostItem> HostItems { get; } = new();

    [ObservableProperty]
    private bool _selectionNotEmpty = false;

    // A host list operation is in progress (e.g. a removed host is being disconnected)
    [ObservableProperty]
    private bool _isBusy = false;

    // Bound to the "select all" CheckBox in the table header
    private bool _allHostsSelected = false;
    public bool AllHostsSelected
    {
        get => _allHostsSelected;
        set
        {
            foreach (var host in HostItems) host.SetSelectedSilently(value);
            CheckSelectedHosts();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentHost))]
    private HostItem? _selectedHost;

    public bool HasCurrentHost => SelectedHost is not null;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction & disposal

    public HostListViewModel(ServiceList serviceList)
    {
        _serviceList = serviceList;

        _serviceList.HostAdded += OnHostAdded;
        _serviceList.HostRemoved += OnHostRemoved;

        // Already-loaded hosts
        foreach (var service in _serviceList.Items)
            AddHostItem(service);
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _serviceList.HostAdded -= OnHostAdded;
        _serviceList.HostRemoved -= OnHostRemoved;

        foreach (var host in HostItems)
            host.Dispose();

        GC.SuppressFinalize(this);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModel logic

    /// <summary>
    /// Recalculates the selection-dependent properties.
    /// Called by <see cref="HostItem"/> when its selection changes.
    /// </summary>
    public void CheckSelectedHosts()
    {
        int count = HostItems.Count(host => host.Selected);

        SelectionNotEmpty = (count > 0);
        _allHostsSelected = SelectionNotEmpty && (count == HostItems.Count);
        OnPropertyChanged(nameof(AllHostsSelected));
    }


    public bool HostEndpointExists(string host, int port, Guid excludeId) =>
        _serviceList.HostEndpointExists(host, port, excludeId);


    ////////////////////////////////////////////////////////////////////////////////////////
    // Host list operations

    public Task<ServiceListEditResult> AddHostAsync(HostConfig config) =>
        RunAsync(() => _serviceList.AddHostAsync(config));

    public Task<ServiceListEditResult> UpdateHostAsync(HostConfig config) =>
        RunAsync(() => _serviceList.UpdateHostAsync(config));

    public Task<ServiceListEditResult> RemoveHostAsync(Guid id) =>
        RunAsync(() => _serviceList.RemoveHostAsync(id));

    public Task<ServiceListEditResult> ReconnectHostAsync(Guid id) =>
        RunAsync(() => _serviceList.ReconnectHostAsync(id));

    public Task<(ServiceListEditResult Result, HostConfig? FailedHostConfig)> RemoveSelectedHostsAsync() =>
        ForSelectedAsync(_serviceList.RemoveHostAsync);

    public Task<(ServiceListEditResult Result, HostConfig? FailedHostConfig)> ReconnectSelectedHostsAsync() =>
        ForSelectedAsync(_serviceList.ReconnectHostAsync);


    private async Task<ServiceListEditResult> RunAsync(Func<Task<ServiceListEditResult>> operation)
    {
        IsBusy = true;
        try
        {
            return await operation();
        }
        finally
        {
            IsBusy = false;
        }
    }


    /// <summary>
    /// Applies the operation to every selected host and returns the first failure (if any).
    /// </summary>
    private async Task<(ServiceListEditResult Result, HostConfig? FailedHostConfig)> ForSelectedAsync(
        Func<Guid, Task<ServiceListEditResult>> operation)
    {
        // Snapshot: removed hosts leave HostItems during the loop
        var configs = HostItems
            .Where(host => host.Selected)
            .Select(host => host.Service.Config)
            .ToList();

        IsBusy = true;
        try
        {
            var firstFailure = ServiceListEditResult.Success;
            HostConfig? failedConfig = null;

            foreach (var config in configs)
            {
                var result = await operation(config.Id);
                if ((result != ServiceListEditResult.Success) && (failedConfig is null))
                {
                    firstFailure = result;
                    failedConfig = config;
                }
            }

            return (firstFailure, failedConfig);
        }
        finally
        {
            IsBusy = false;
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ServiceList event handlers

    private void OnHostAdded(object? sender, ServiceItemEventArgs e)
    {
        if (sender == _serviceList) AddHostItem(e.Service);
    }


    private void OnHostRemoved(object? sender, ServiceItemEventArgs e)
    {
        if (sender != _serviceList) return;

        var host = HostItems.FirstOrDefault(h => h.Service == e.Service);
        if (host is null) return;

        if (SelectedHost == host) SelectedHost = null;

        HostItems.Remove(host);
        host.Dispose();
        CheckSelectedHosts();
    }


    private void AddHostItem(ServiceItem service)
    {
        if (_disposed || HostItems.Any(h => h.Service == service)) return;

        HostItems.Add(new HostItem(service, this));
        CheckSelectedHosts();
    }

}

// END-OF-FILE
