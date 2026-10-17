////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// HostSensorListViewModel.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using FanController.Contracts;
using static FanController.ViewModels.SensorItem;


namespace FanController.ViewModels;

public enum TreeNodeType
{
    Device,
    Sensor
}


/// <summary>
/// Node of the host hardware tree: a device (with its sub-devices and sensors)
/// or a temperature sensor.
/// </summary>
public partial class HostSensorTreeNode : ObservableObject
{
    // Common
    public TreeNodeType NodeType { get; }
    public string Name { get; }
    public string? Info { get; }
    public HostHardwareType HardwareType { get; }
    public ObservableCollection<HostSensorTreeNode> Children { get; } = new();

    // Sensor-specific
    public byte[]? ID { get; }
    public string IDText { get; } = String.Empty;
    public string Path { get; } = String.Empty;
    public double? MaxTemperature { get; }
    public double? CritTemperature { get; }

    // Name of the device the sensor belongs to (used to build the new sensor name)
    public string? ParentDeviceName { get; }

    // Helpers for binding
    public bool IsDevice => NodeType == TreeNodeType.Device;
    public bool IsSensor => NodeType == TreeNodeType.Sensor;

    // The sensor can be added to the controller as a software sensor
    public bool CanBeAdded =>
        IsSensor && (ID is not null) && (ParentDeviceName is not null) &&
        !AlreadyExists && !IsAddPending;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Observable state

    // The controller already has a sensor with this ID
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBeAdded))]
    private bool _alreadyExists = false;

    // The add command has been sent, the controller has not reported the new sensor yet
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBeAdded))]
    private bool _isAddPending = false;

    // Current readings (updated periodically)
    [ObservableProperty]
    private double _temperature = 0.0;

    [ObservableProperty]
    private bool _isValid = false;

    // Overall status derived from the readings and the host-reported limits
    [ObservableProperty]
    private SensorStatus _status = SensorStatus.StatusDisabled;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public HostSensorTreeNode(HostDeviceDto device)
    {
        NodeType = TreeNodeType.Device;
        Name = CapitalizeFirstCharacter(device.Name);
        Info = String.IsNullOrWhiteSpace(device.Info) ? null : device.Info;
        HardwareType = device.Type;
    }


    public HostSensorTreeNode(HostSensorDto sensor, string parentDeviceName, bool alreadyExists)
    {
        NodeType = TreeNodeType.Sensor;
        Name = CapitalizeFirstCharacter(sensor.Name);
        Info = String.IsNullOrWhiteSpace(sensor.Info) ? null : sensor.Info;
        HardwareType = HostHardwareType.Sensor;

        ID = sensor.Id;
        IDText = BitConverter.ToString(sensor.Id);
        Path = sensor.Path;
        MaxTemperature = sensor.MaxTemperature;
        CritTemperature = sensor.CritTemperature;
        ParentDeviceName = parentDeviceName;

        _alreadyExists = alreadyExists;
        _temperature = sensor.Temperature;
        _isValid = sensor.IsValid;
        UpdateStatus();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Logic

    public void UpdateReadings(double temperature, bool isValid)
    {
        Temperature = temperature;
        IsValid = isValid;
        UpdateStatus();
    }


    private void UpdateStatus()
    {
        // Ordered by priority: the first matching condition wins
        if (CritTemperature.HasValue && (Temperature > CritTemperature.Value))
            Status = SensorStatus.StatusAlarm;
        else if (MaxTemperature.HasValue && (Temperature > MaxTemperature.Value))
            Status = SensorStatus.StatusWarning;
        else if (!IsValid)
            Status = SensorStatus.StatusOutdated;
        else
            Status = SensorStatus.StatusValid;
    }


    private static string CapitalizeFirstCharacter(string text) =>
        String.IsNullOrEmpty(text)
            ? text
            : char.ToUpperInvariant(text[0]) + text[1..];

}


/// <summary>
/// Compares byte arrays (sensor IDs) by content, for use as dictionary keys.
/// </summary>
public sealed class ByteArrayComparer : IEqualityComparer<byte[]>
{
    public static readonly ByteArrayComparer Instance = new();

    public bool Equals(byte[]? x, byte[]? y) =>
        ReferenceEquals(x, y) ||
        ((x is not null) && (y is not null) && x.AsSpan().SequenceEqual(y));

    public int GetHashCode(byte[] obj)
    {
        var hashCode = new HashCode();
        hashCode.AddBytes(obj);
        return hashCode.ToHashCode();
    }
}


/// <summary>
/// Host Sensor List window View Model: the host hardware tree with periodically
/// updated readings. Selected sensors can be added as software sensors one by one.
/// </summary>
public partial class HostSensorListViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    private const int ReadingsUpdateIntervalMs = 1000;

    private readonly SensorViewModel _parent;
    private bool _disposed = false;

    // Readings update loop
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _timerTask;

    // Sensor nodes by ID (for the readings and "already exists" updates)
    private readonly Dictionary<byte[], HostSensorTreeNode> _sensorNodes =
        new(ByteArrayComparer.Instance);


    ////////////////////////////////////////////////////////////////////////////////////////
    // Observable state

    public ObservableCollection<HostSensorTreeNode> DeviceTree { get; } = new();

    [ObservableProperty]
    private HostSensorTreeNode? _selectedNode;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction & disposal

    public HostSensorListViewModel(SensorViewModel parent, IReadOnlyList<HostDeviceDto> devices)
    {
        _parent = parent;

        foreach (var device in devices)
            DeviceTree.Add(CreateDeviceNode(device));

        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ReadingsUpdateIntervalMs));
        _timerTask = RunReadingsLoopAsync(_cts.Token);
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _timer.Dispose();
        _cts.Dispose();

        GC.SuppressFinalize(this);
    }


    /// <summary>
    /// Stops the readings loop and waits for it to finish.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _timer.Dispose();

        // The loop handles cancellation itself
        await _timerTask;

        _cts.Dispose();

        GC.SuppressFinalize(this);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModel logic

    private HostSensorTreeNode CreateDeviceNode(HostDeviceDto device)
    {
        var node = new HostSensorTreeNode(device);

        // Sub-devices (recursively)
        if (device.SubDevices is not null)
            foreach (var subDevice in device.SubDevices)
                node.Children.Add(CreateDeviceNode(subDevice));

        // Sensors (with the capitalized device name for the new sensor names)
        if (device.Sensors is not null)
            foreach (var sensor in device.Sensors)
            {
                var sensorNode = new HostSensorTreeNode(
                    sensor, node.Name, _parent.CheckSensorExists(sensor.Id));

                node.Children.Add(sensorNode);
                _sensorNodes.TryAdd(sensor.Id, sensorNode);
            }

        return node;
    }


    private async Task RunReadingsLoopAsync(CancellationToken token)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(token))
            {
                var readings = await _parent.GetHostSensorReadings();
                if (token.IsCancellationRequested) break;

                if (readings is not null)
                    UpdateReadings(readings);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }


    private void UpdateReadings(IReadOnlyList<HostShortSensorDto> readings)
    {
        foreach (var reading in readings)
        {
            if (_sensorNodes.TryGetValue(reading.Id, out var node))
                node.UpdateReadings(reading.Temperature, reading.IsValid);
        }
    }


    /// <summary>
    /// <see cref="SensorViewModel.SensorFound"/> handler:
    /// marks the host sensor as already added to the controller.
    /// </summary>
    public void OnParentSensorFound(object? sender, byte[] serialNumber)
    {
        if (_sensorNodes.TryGetValue(serialNumber, out var node))
        {
            node.AlreadyExists = true;
            node.IsAddPending = false;
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Adding sensors

    /// <summary>
    /// Builds the initial software sensor configuration for the host sensor node.
    /// Returns null if the node can't be added.
    /// </summary>
    public (SensorConfigDto Config, ObservableCollection<FanOutput> FanOutputs)?
        PrepareSensorConfig(HostSensorTreeNode node)
    {
        if (!node.CanBeAdded || (node.ID is null) || (node.ParentDeviceName is null))
            return null;

        return _parent.PrepareHostSensorConfig(
            node.ID, node.ParentDeviceName, node.Name,
            node.MaxTemperature, node.CritTemperature);
    }


    /// <summary>
    /// Adds the software sensor. The node stays locked until the controller
    /// reports the new sensor, so the same sensor can't be added twice.
    /// </summary>
    public async Task<bool> AddSoftwareSensor(HostSensorTreeNode node, SensorConfigDto config)
    {
        node.IsAddPending = true;

        bool succeeded = await _parent.AddSoftwareSensor(config);
        if (!succeeded) node.IsAddPending = false;

        return succeeded;
    }

}

// END-OF-FILE
