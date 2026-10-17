////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// HostItem.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using FanController.Services;


namespace FanController.ViewModels;

/// <summary>
/// View Model of one host (a row of the host list).
/// </summary>
public partial class HostItem : ObservableObject, IDisposable
{
    // Host status
    public enum HostStatus : byte
    {
        StatusOffline       = 0,
        StatusOnline        = 1,
        StatusIncompatible  = 2      // The host's service uses another contract version
    }

    private readonly HostListViewModel _parent;
    private bool _disposed = false;

    public ServiceItem Service { get; }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Configuration

    public string Name => Service.Config.Name;
    public string Address => Service.Config.Host;
    public string Port => $"{Service.Config.Port}";
    public string FullAddress => $"{Service.Config.Host}:{Service.Config.Port}";


    ////////////////////////////////////////////////////////////////////////////////////////
    // Observable state

    private bool _selected = false;
    public bool Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                _parent.CheckSelectedHosts();
        }
    }

    public bool IsReachable => Service.IsReachable;
    public bool IsCompatible => Service.IsCompatible;

    // Version of the host's service (empty until the first connection)
    public string ServiceVersion => Service.ServiceInfo?.ServiceVersion ?? String.Empty;

    public HostStatus Status =>
        !IsCompatible ? HostStatus.StatusIncompatible :
        IsReachable ? HostStatus.StatusOnline :
        HostStatus.StatusOffline;

    // Controllers currently available on the host
    // (unplugged ones are kept by the service as unavailable)
    public int DeviceCount => Service.Devices.Count(device => device.IsAvailable);


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction & disposal

    public HostItem(ServiceItem service, HostListViewModel parent)
    {
        Service = service;
        _parent = parent;

        Service.ServiceConfigChanged += OnHostConfigChanged;
        Service.ServiceReachabilityChanged += OnHostReachabilityChanged;
        Service.ServiceInfoChanged += OnHostServiceInfoChanged;
        Service.DeviceAdded += OnDevicesChanged;
        Service.DeviceUpdated += OnDevicesChanged;
        Service.DeviceRemoved += OnDevicesChanged;
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Service.ServiceConfigChanged -= OnHostConfigChanged;
        Service.ServiceReachabilityChanged -= OnHostReachabilityChanged;
        Service.ServiceInfoChanged -= OnHostServiceInfoChanged;
        Service.DeviceAdded -= OnDevicesChanged;
        Service.DeviceUpdated -= OnDevicesChanged;
        Service.DeviceRemoved -= OnDevicesChanged;

        GC.SuppressFinalize(this);
    }


    /// <summary>
    /// Bulk selection by the parent ViewModel, without the per-item
    /// <see cref="HostListViewModel.CheckSelectedHosts"/> callback.
    /// </summary>
    internal void SetSelectedSilently(bool value) =>
        SetProperty(ref _selected, value, nameof(Selected));


    ////////////////////////////////////////////////////////////////////////////////////////
    // ServiceItem event handlers (raised on the UI thread)

    private void OnHostConfigChanged(object? sender, EventArgs e)
    {
        if (sender != Service) return;

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(FullAddress));
    }


    private void OnHostReachabilityChanged(object? sender, ServiceReachabilityEventArgs e)
    {
        if (sender != Service) return;

        OnPropertyChanged(nameof(IsReachable));
        OnPropertyChanged(nameof(Status));
    }


    private void OnHostServiceInfoChanged(object? sender, EventArgs e)
    {
        if (sender != Service) return;

        OnPropertyChanged(nameof(IsCompatible));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ServiceVersion));
    }


    private void OnDevicesChanged(object? sender, DeviceEventArgs e)
    {
        if (sender != Service) return;

        OnPropertyChanged(nameof(DeviceCount));
    }

}

// END-OF-FILE
