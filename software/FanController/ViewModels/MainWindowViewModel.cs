////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// MainWindowViewModel.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Grpc.Core;
using ProtoBuf.Grpc;
using FanController.Assets.Localization;
using FanController.Contracts;
using FanController.Services;
using FanController.Settings;


namespace FanController.ViewModels;

/// <summary>
/// Main window View Model: the controller list and selection, the overall and
/// system state of the selected controller, and the tab View Models.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    // Device Overall States
    public enum OverallStates : byte
    {
        StateNormal = 0,    // All OK
        StateWarning = 1,   // Some warnings appeared
        StateError = 2      // Some errors appeared or Controller disconnected
    }

    private static readonly SystemStateDto EmptySystemState = DtoDefaults.Controller.NewState();

    private volatile bool _disposed = false;

    private readonly ServiceList _serviceList;
    private readonly JsonSettingsStore _settingsStore;

    // Services (by ID) whose device events are subscribed to
    private readonly HashSet<Guid> _subscribedServices = new();

    // The selected controller has been connected (USB) since it was selected
    private bool _wasConnected = false;

    // The selected controller is scanning or initializing
    private bool _isSelectedDeviceBusy = false;


    // System parameters View Model
    public SystemViewModel System { get; }

    // Fans parameters View Model
    public FanViewModel Fans { get; }

    // Sensors parameters View Model
    public SensorViewModel Sensors { get; }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Observable state

    public ObservableCollection<DeviceItem> Devices { get; } = new();

    private DeviceItem? _selectedDevice;
    public DeviceItem? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (_selectedDevice?.Id == value?.Id) return;
            _selectedDevice = value;
            OnPropertyChanged();
            OnSelectedDeviceChanged();
        }
    }

    [ObservableProperty]
    private SystemStateDto _systemState = EmptySystemState;

    // Busy overlay
    [ObservableProperty]
    private double _selectedDeviceBusyProgress = 0.0;

    [ObservableProperty]
    private bool _selectedDeviceProgressIndeterminate = false;

    [ObservableProperty]
    private string _selectedDeviceBusyMessage = String.Empty;


    // Complex status properties

    // Host is reachable and the device is available
    public bool IsSelectedDeviceOnline => SelectedDevice?.IsAvailableOverall ?? false;

    public bool IsSelectedDeviceBusy => IsSelectedDeviceOnline && _isSelectedDeviceBusy;

    // Error: alarms, or the controller was connected but is offline / USB-disconnected now
    public OverallStates SelectedDeviceOverallState =>
        (SystemState.StatusCategoryAlarms ||
         (_wasConnected && (!IsSelectedDeviceOnline || !(SelectedDevice?.IsConnected ?? false))))
            ? OverallStates.StateError
            : SystemState.StatusCategoryWarnings
                ? OverallStates.StateWarning
                : OverallStates.StateNormal;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction & disposal

    public MainWindowViewModel(ServiceList serviceList,
        JsonSettingsStore settingsStore,
        CancellationToken appToken)
        : base(appToken)
    {
        _serviceList = serviceList;
        _settingsStore = settingsStore;

        System = new SystemViewModel(ViewModelCts.Token);
        Fans = new FanViewModel(ViewModelCts.Token);
        Sensors = new SensorViewModel(ViewModelCts.Token);

        _serviceList.HostAdded += OnHostAdded;
        _serviceList.HostRemoved += OnHostRemoved;

        // Already-loaded hosts
        foreach (var service in _serviceList.Items)
            SubscribeService(service);
    }


    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            _serviceList.HostAdded -= OnHostAdded;
            _serviceList.HostRemoved -= OnHostRemoved;

            foreach (var service in _serviceList.Items)
                UnsubscribeService(service);

            // No selection changes here: the tab ViewModels are disposed below
            foreach (var device in Devices)
                ReleaseDevice(device);

            System.Dispose();
            Fans.Dispose();
            Sensors.Dispose();
        }

        // Base cancels DeviceCts and ViewModelCts last
        base.Dispose(disposing);
    }


    // HostListViewModel factory (used by the MainWindow code-behind)
    public HostListViewModel CreateHostListViewModel() =>
        new(_serviceList);


    ////////////////////////////////////////////////////////////////////////////////////////
    // ServiceList / ServiceItem event handlers

    private void OnHostAdded(object? sender, ServiceItemEventArgs e)
    {
        if (sender != _serviceList) return;
        SubscribeService(e.Service);
    }


    private void OnHostRemoved(object? sender, ServiceItemEventArgs e)
    {
        if (sender != _serviceList) return;

        UnsubscribeService(e.Service);

        // The host's devices go away with it
        foreach (var device in Devices.Where(d => d.Service == e.Service).ToList())
            RemoveDevice(device.Id);
    }


    private void OnDeviceAdded(object? sender, DeviceEventArgs e)
    {
        if (sender is ServiceItem service) AddDevice(service, e.Device);
    }


    private void OnDeviceRemoved(object? sender, DeviceEventArgs e)
    {
        if (sender is ServiceItem) RemoveDevice(e.Device.Id);
    }


    private void OnDeviceAvailabilityUpdated(object? sender, EventArgs e)
    {
        if (sender is DeviceItem device) UpdateDeviceAvailability(device);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Device list management

    private void SubscribeService(ServiceItem service)
    {
        if (_disposed || !_subscribedServices.Add(service.Config.Id)) return;

        service.DeviceAdded += OnDeviceAdded;
        service.DeviceRemoved += OnDeviceRemoved;

        // Already-known devices (duplicates from the events are ignored by AddDevice)
        foreach (var device in service.Devices)
            AddDevice(service, device);
    }


    private void UnsubscribeService(ServiceItem service)
    {
        if (!_subscribedServices.Remove(service.Config.Id)) return;

        service.DeviceAdded -= OnDeviceAdded;
        service.DeviceRemoved -= OnDeviceRemoved;
    }


    private void AddDevice(ServiceItem service, RemoteDevice device)
    {
        if (_disposed || Devices.Any(d => d.Id == device.Id)) return;

        var deviceItem = new DeviceItem(service, device);
        deviceItem.DeviceAvailabilityUpdated += OnDeviceAvailabilityUpdated;
        Devices.Add(deviceItem);

        TryAutoSelect(deviceItem);
    }


    private void RemoveDevice(string deviceId)
    {
        if (_disposed) return;

        var device = Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null) return;

        // Deselection clears the state of this and the tab ViewModels (see ClearState)
        if (SelectedDevice == device)
            SelectedDevice = null;

        Devices.Remove(device);
        ReleaseDevice(device);
    }


    private void ReleaseDevice(DeviceItem device)
    {
        device.DeviceAvailabilityUpdated -= OnDeviceAvailabilityUpdated;
        device.Dispose();
    }


    private void UpdateDeviceAvailability(DeviceItem device)
    {
        if (_disposed || !Devices.Contains(device)) return;

        if (SelectedDevice == device)
            NotifySelectedDeviceStatusChanged();
        else
            TryAutoSelect(device);
    }


    // Selects the last used controller once it is available (if nothing is selected yet)
    private void TryAutoSelect(DeviceItem device)
    {
        if ((SelectedDevice is null) &&
            device.IsAvailableOverall &&
            (device.Id == _settingsStore.Settings.LastSelectedDevice))
        {
            SelectedDevice = device;
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Selected device

    private void OnSelectedDeviceChanged()
    {
        var deviceId = _selectedDevice?.Id;
        var service = _selectedDevice?.Service;

        SetActiveDevice(deviceId, service);
        System.SetActiveDevice(deviceId, service);
        Fans.SetActiveDevice(deviceId, service);
        Sensors.SetActiveDevice(deviceId, service);
    }


    private void NotifySelectedDeviceStatusChanged()
    {
        OnPropertyChanged(nameof(IsSelectedDeviceOnline));
        OnPropertyChanged(nameof(IsSelectedDeviceBusy));
        OnPropertyChanged(nameof(SelectedDeviceOverallState));
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModelBase overrides

    protected override void ClearState()
    {
        SystemState = EmptySystemState;
        _wasConnected = _selectedDevice?.IsConnected ?? false;
        _isSelectedDeviceBusy = false;
        SelectedDeviceBusyProgress = 0.0;
        SelectedDeviceProgressIndeterminate = false;
        NotifySelectedDeviceStatusChanged();
    }


    protected override void SwitchActiveDevice(
        string? newDeviceId,
        string? oldDeviceId)
    {
        if ((newDeviceId is null) || (ActiveService is null)) return;

        DeviceCts = CancellationTokenSource
            .CreateLinkedTokenSource(ViewModelCts.Token, ActiveService.DisposalToken);

        Watch(() => WatchOverallStateAsync(newDeviceId, DeviceCts.Token));
        Watch(() => WatchSystemStateAsync(newDeviceId, DeviceCts.Token));

        // The previous controller is not deactivated: its service must keep running
        // (host sensor readings, periodic state queries). The service also activates
        // a detected controller automatically; the explicit activation is just in case.
        _ = ActivateDeviceAsync(newDeviceId);
        _ = PersistSelectedDeviceAsync();
    }


    // The busy overlay caption is built in code
    protected override void OnCultureChanged() => UpdateBusyState();


    ////////////////////////////////////////////////////////////////////////////////////////
    // Stream watchers

    private async Task WatchOverallStateAsync(string deviceId,
        CancellationToken ct)
    {
        await foreach (var result in ActiveService!.WatchDeviceOverallStateAsync(
                           new DeviceIdRequest { DeviceId = deviceId },
                           new CallContext(new CallOptions(cancellationToken: ct))))
        {
            if ((SelectedDevice is null) || (SelectedDevice.Id != result.Id)) continue;

            SelectedDevice.IsConnected = result.OverallState.IsConnected;
            SelectedDevice.IsActive = result.OverallState.IsActive;
            _wasConnected |= result.OverallState.IsConnected;

            OnPropertyChanged(nameof(SelectedDeviceOverallState));
        }
    }


    private async Task WatchSystemStateAsync(string deviceId,
        CancellationToken ct)
    {
        await foreach (var result in ActiveService!.WatchDeviceSystemStateAsync(
                           new DeviceIdRequest { DeviceId = deviceId },
                           new CallContext(new CallOptions(cancellationToken: ct))))
        {
            if (SelectedDeviceId != result.Id) continue;

            SystemState = result.SystemState;
            UpdateBusyState();

            OnPropertyChanged(nameof(IsSelectedDeviceBusy));
            OnPropertyChanged(nameof(SelectedDeviceOverallState));
        }
    }


    // Busy overlay: scanning / initialization progress of the selected controller
    private void UpdateBusyState()
    {
        _isSelectedDeviceBusy =
            SystemState.ConfigFlagAnyScanInProgress || SystemState.ConfigFlagInitializationInProgress;

        if (!_isSelectedDeviceBusy)
        {
            SelectedDeviceProgressIndeterminate = false;
            SelectedDeviceBusyProgress = 0.0;
            return;
        }

        SelectedDeviceBusyProgress = (double)SystemState.ScanProgress;

        // The progress is determinate only for single-channel hardware / fan scans
        (SelectedDeviceBusyMessage, SelectedDeviceProgressIndeterminate) = SystemState switch
        {
            { ConfigFlagHWInProgress: true } =>
                (Strings.OverlayHardwareScanningCaption, SystemState.ConfigFlagAllFanChannelsInvolved),
            { ConfigFlagFanInProgress: true } =>
                (Strings.OverlayFanScanningCaption, SystemState.ConfigFlagAllFanChannelsInvolved),
            { ConfigFlagSensorInProgress: true } =>
                (Strings.OverlaySensorScanningCaption, true),
            { ConfigFlagInitializationInProgress: true } =>
                (Strings.OverlayInitializationCaption, true),
            _ =>
                (Strings.OverlayBusyCaption, true)
        };
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Device commands

    public Task<bool> ActivateDeviceAsync(string deviceId) =>
        SetDeviceActiveAsync(deviceId, true);

    public Task<bool> DeactivateDeviceAsync(string deviceId) =>
        SetDeviceActiveAsync(deviceId, false);


    private Task<bool> SetDeviceActiveAsync(string deviceId, bool active)
    {
        var service = Devices.FirstOrDefault(d => d.Id == deviceId)?.Service;
        if (service is null)
            return Task.FromResult(false);

        return TryExecuteAsync(() => service.ExecuteCommandAsync(
            new ExecuteCommandRequest
            {
                DeviceId = deviceId,
                Command = new SetActiveCommand { Active = active }
            },
            new CallContext(new CallOptions(
                cancellationToken: ViewModelCts.Token))));
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Settings persistence

    // Write failures are logged by the store
    private Task PersistSelectedDeviceAsync()
    {
        string selectedId = SelectedDeviceId ?? String.Empty;
        return _settingsStore.UpdateAsync(s => s.LastSelectedDevice = selectedId);
    }

}

// END-OF-FILE
