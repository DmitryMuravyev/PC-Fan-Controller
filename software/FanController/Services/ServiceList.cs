////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// ServiceList.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FanController.Settings;


namespace FanController.Services;

// Event args

public class ServiceItemEventArgs : EventArgs
{
    public ServiceItem Service { get; }
    public ServiceItemEventArgs(ServiceItem service) => Service = service;
}


// List edit results

public enum ServiceListEditResult
{
    Success,
    NameAlreadyExist,       // For some future use
    EndpointAlreadyExist,
    ItemNotFound,
    SettingsSaveFailed      // The change is applied, but the settings file couldn't be written
}


/// <summary>
/// The configured daemon hosts (one <see cref="ServiceItem"/> each), kept in sync
/// with the settings. Used from the UI thread; the events are raised on it.
/// </summary>
public sealed class ServiceList : IAsyncDisposable
{
    // Events
    public event EventHandler<ServiceItemEventArgs>? HostAdded;
    public event EventHandler<ServiceItemEventArgs>? HostUpdated;
    public event EventHandler<ServiceItemEventArgs>? HostRemoved;

    private readonly List<ServiceItem> _items = new();
    private readonly JsonSettingsStore _settingsStore;
    private readonly TimeSpan _reconnectDelay;
    private readonly CancellationToken _appToken;
    private bool _disposed;

    public IReadOnlyList<ServiceItem> Items => _items;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction & disposal

    public ServiceList(
        JsonSettingsStore settingsStore,
        TimeSpan reconnectDelay,
        CancellationToken appToken)
    {
        _settingsStore = settingsStore;
        _reconnectDelay = reconnectDelay;
        _appToken = appToken;

        foreach (var config in settingsStore.Settings.Hosts)
        {
            // Skip broken or duplicated entries (e.g. a hand-edited settings file)
            if ((config is null) ||
                String.IsNullOrWhiteSpace(config.Host) ||
                _items.Any(item => (item.Config.Id == config.Id) ||
                                   item.Config.HasEndpoint(config.Host, config.Port)))
            {
                Debug.WriteLine($"[ServiceList] Skipped invalid host entry '{config?.Name}'.");
                continue;
            }

            CreateItem(config);
        }
    }


    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Disconnect all hosts in parallel
        await Task.WhenAll(_items.Select(item => item.DisposeAsync().AsTask()));

        foreach (var item in _items)
            HostRemoved?.Invoke(this, new ServiceItemEventArgs(item));

        _items.Clear();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Public API

    // Host names may repeat; only the endpoint (address + port) must be unique
    public bool HostEndpointExists(string host, int port, Guid? excludeId = null) =>
        _items.Any(item => (item.Config.Id != excludeId) && item.Config.HasEndpoint(host, port));


    public async Task<ServiceListEditResult> AddHostAsync(HostConfig config)
    {
        if (HostEndpointExists(config.Host, config.Port))
            return ServiceListEditResult.EndpointAlreadyExist;

        var item = CreateItem(config);
        HostAdded?.Invoke(this, new ServiceItemEventArgs(item));

        return await PersistAsync();
    }


    public async Task<ServiceListEditResult> UpdateHostAsync(HostConfig config)
    {
        var item = FindItem(config.Id);
        if (item is null) return ServiceListEditResult.ItemNotFound;

        if (HostEndpointExists(config.Host, config.Port, config.Id))
            return ServiceListEditResult.EndpointAlreadyExist;

        await item.UpdateConfigAsync(config);
        HostUpdated?.Invoke(this, new ServiceItemEventArgs(item));

        return await PersistAsync();
    }


    public async Task<ServiceListEditResult> RemoveHostAsync(Guid id)
    {
        var item = FindItem(id);
        if (item is null) return ServiceListEditResult.ItemNotFound;

        _items.Remove(item);

        // Disconnects the host and removes its devices
        await item.DisposeAsync();
        HostRemoved?.Invoke(this, new ServiceItemEventArgs(item));

        return await PersistAsync();
    }


    public async Task<ServiceListEditResult> ReconnectHostAsync(Guid id)
    {
        var item = FindItem(id);
        if (item is null) return ServiceListEditResult.ItemNotFound;

        await item.ReconnectAsync();
        return ServiceListEditResult.Success;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Helpers

    private ServiceItem CreateItem(HostConfig config)
    {
        var item = new ServiceItem(config, _reconnectDelay);
        _items.Add(item);
        item.StartWatcher(_appToken);
        return item;
    }


    private ServiceItem? FindItem(Guid id) =>
        _items.FirstOrDefault(item => item.Config.Id == id);


    private async Task<ServiceListEditResult> PersistAsync()
    {
        var hosts = _items.Select(item => item.Config).ToList();
        bool saved = await _settingsStore.UpdateAsync(s => s.Hosts = hosts);

        return saved ? ServiceListEditResult.Success : ServiceListEditResult.SettingsSaveFailed;
    }

}

// END-OF-FILE
