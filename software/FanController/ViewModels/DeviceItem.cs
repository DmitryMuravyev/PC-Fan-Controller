////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// DeviceItem.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using FanController.Services;


namespace FanController.ViewModels;

/// <summary>
/// View Model of one controller for the device selection ComboBox.
///   IsHostReachable — a stream to the host daemon is delivering data
///   IsAvailable     — the host reports the controller as available
///   IsConnected     — USB link between the host and the controller (from OverallStateDto)
///   IsActive        — the controller is activated by software (from OverallStateDto)
/// </summary>
public partial class DeviceItem : ObservableObject, IDisposable
{
    private bool _disposed = false;

    public RemoteDevice Device { get; }
    public ServiceItem Service { get; }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Identity

    public string Id => Device.Id;
    public string Name => Device.Name;
    public string Version => Device.Version;
    public string HostName => Service.Config.Name;
    public string DisplayName => $"{Service.Config.Name} - {Device.Name} {Device.Version} ({Device.Id})";


    ////////////////////////////////////////////////////////////////////////////////////////
    // Observable state

    public bool IsHostReachable => Service.IsReachable;
    public bool IsAvailable => Device.IsAvailable;

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _isActive = false;

    // The host is reachable and the controller is available.
    // The USB state (IsConnected / IsActive) is separate: an available controller
    // can be temporarily disconnected from the host daemon.
    public bool IsAvailableOverall => IsHostReachable && IsAvailable;

    // Raised when IsAvailableOverall may have changed
    public event EventHandler? DeviceAvailabilityUpdated;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction & disposal

    public DeviceItem(ServiceItem service, RemoteDevice device)
    {
        Service = service;
        Device = device;

        Service.ServiceConfigChanged += OnHostConfigChanged;
        Service.ServiceReachabilityChanged += OnHostReachabilityChanged;
        Service.DeviceUpdated += OnDeviceUpdated;
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Service.ServiceConfigChanged -= OnHostConfigChanged;
        Service.ServiceReachabilityChanged -= OnHostReachabilityChanged;
        Service.DeviceUpdated -= OnDeviceUpdated;

        GC.SuppressFinalize(this);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ServiceItem event handlers (raised on the UI thread)

    private void OnHostConfigChanged(object? sender, EventArgs e)
    {
        if (sender != Service) return;

        OnPropertyChanged(nameof(HostName));
        OnPropertyChanged(nameof(DisplayName));
    }


    private void OnHostReachabilityChanged(object? sender, ServiceReachabilityEventArgs e)
    {
        if (sender != Service) return;

        OnPropertyChanged(nameof(IsHostReachable));
        OnPropertyChanged(nameof(IsAvailableOverall));
        DeviceAvailabilityUpdated?.Invoke(this, EventArgs.Empty);
    }


    private void OnDeviceUpdated(object? sender, DeviceEventArgs e)
    {
        if ((sender != Service) || (e.Device != Device)) return;

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Version));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(IsAvailableOverall));
        DeviceAvailabilityUpdated?.Invoke(this, EventArgs.Empty);
    }

}

// END-OF-FILE
