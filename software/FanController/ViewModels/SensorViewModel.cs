////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SensorViewModel.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Grpc.Core;
using ProtoBuf.Grpc;
using FanController.Assets.Localization;
using FanController.Contracts;


namespace FanController.ViewModels;

/// <summary>
/// Sensors Tab contents View Model
/// </summary>
public partial class SensorViewModel : ViewModelBase
{
    // Host sensor name construction limits
    private const int MaxSensorNameLength = 8;
    private const int MaxDevicePrefixLength = 4;

    // Serializes concurrent sensor count / config / state and fan config stream handlers
    private readonly SemaphoreSlim _snapshotLock = new(1, 1);

    private int _sensorCount = 0;

    // Fan names (by fan index) for the per-fan output captions
    private string[] _fanNames = [];

    // Dominant sensor tracking, per fan: index of the active sensor with the
    // greatest output (-1 if none) and that output value
    private int[] _dominantSensorIndexes = [];
    private double[] _dominantOutputs = [];

    /// <summary>
    /// Raised for every received sensor configuration (with its serial number),
    /// so the host sensor list can mark the sensors that are already added.
    /// </summary>
    public event EventHandler<byte[]>? SensorFound;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Observable state

    public ObservableCollection<SensorItem> Sensors { get; } = new();

    [ObservableProperty]
    private bool _selectionNotEmpty = false;

    [ObservableProperty]
    private int _selectedSensorsCount = 0;

    // The selection is not empty and every selected sensor can be removed
    [ObservableProperty]
    private bool _selectedSensorsDeletable = false;

    // Bound to the "select all" CheckBox in the table header
    private bool _allSensorsSelected = false;
    public bool AllSensorsSelected
    {
        get => _allSensorsSelected;
        set
        {
            foreach (var sensor in Sensors) sensor.SetSelectedSilently(value);
            CheckSelectedSensors();
        }
    }

    // Sensor highlighted in the table (shown in the status panels)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentSensor))]
    private SensorItem? _currentSensor;

    public bool HasCurrentSensor => CurrentSensor is not null;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public SensorViewModel(CancellationToken parentToken)
        : base(parentToken)
    {
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModelBase overrides

    protected override void ClearState()
    {
        // Sensors and fan names belong to the previous device
        ClearSensors();
        _fanNames = [];
    }


    protected override void SwitchActiveDevice(
        string? newDeviceId,
        string? oldDeviceId)
    {
        if (newDeviceId is null || ActiveService is null) return;

        DeviceCts = CancellationTokenSource
            .CreateLinkedTokenSource(ViewModelCts.Token, ActiveService.DisposalToken);

        Watch(() => WatchSensorCountAsync(newDeviceId, DeviceCts.Token));
        Watch(() => WatchSensorConfigAsync(newDeviceId, DeviceCts.Token));
        Watch(() => WatchSensorStateAsync(newDeviceId, DeviceCts.Token));
        Watch(() => WatchFanConfigAsync(newDeviceId, DeviceCts.Token));
    }


    // The default fan names ("Fan N") are localized
    protected override void OnCultureChanged()
    {
        foreach (var sensor in Sensors)
            sensor.UpdateFanNames();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Stream watchers

    private async Task WatchSensorCountAsync(string deviceId,
        CancellationToken ct)
    {
        await foreach (var result in ActiveService!.WatchDeviceSensorCountAsync(
                           new DeviceIdRequest { DeviceId = deviceId },
                           new CallContext(new CallOptions(cancellationToken: ct))))
        {
            if (SelectedDeviceId != result.Id) continue;

            await _snapshotLock.WaitAsync(ct);
            try
            {
                CheckSensorCount(result.SensorCount);
            }
            finally { _snapshotLock.Release(); }
        }
    }


    private async Task WatchSensorConfigAsync(string deviceId,
        CancellationToken ct)
    {
        await foreach (var result in ActiveService!.WatchDeviceSensorConfigAsync(
                           new DeviceIdRequest { DeviceId = deviceId },
                           new CallContext(new CallOptions(cancellationToken: ct))))
        {
            if (SelectedDeviceId != result.Id) continue;

            await _snapshotLock.WaitAsync(ct);
            try
            {
                CheckSensorCount(result.SensorCount);

                if (result.SensorConfig.Index < _sensorCount)
                {
                    SensorItem sensor = Sensors[result.SensorConfig.Index];
                    sensor.SensorConfig = result.SensorConfig;

                    // Thresholds / IsActive affect this sensor's outputs only;
                    // a changed fan count needs a full rebuild.
                    if (sensor.FanOutputs.Count != _dominantSensorIndexes.Length)
                        RebuildDominantOutputs();
                    else
                        UpdateDominantOutputs(sensor);

                    CheckSelectedSensors();
                    SensorFound?.Invoke(this, result.SensorConfig.SerialNumber);
                }
            }
            finally { _snapshotLock.Release(); }
        }
    }


    private async Task WatchSensorStateAsync(string deviceId,
        CancellationToken ct)
    {
        await foreach (var result in ActiveService!.WatchDeviceSensorStateAsync(
                           new DeviceIdRequest { DeviceId = deviceId },
                           new CallContext(new CallOptions(cancellationToken: ct))))
        {
            if (SelectedDeviceId != result.Id) continue;

            await _snapshotLock.WaitAsync(ct);
            try
            {
                if (result.SensorState.Index < _sensorCount)
                {
                    SensorItem sensor = Sensors[result.SensorState.Index];
                    sensor.SensorState = result.SensorState;
                    UpdateDominantOutputs(sensor);
                }
            }
            finally { _snapshotLock.Release(); }
        }
    }


    private async Task WatchFanConfigAsync(string deviceId,
        CancellationToken ct)
    {
        await foreach (var result in ActiveService!.WatchDeviceFanConfigAsync(
                           new DeviceIdRequest { DeviceId = deviceId },
                           new CallContext(new CallOptions(cancellationToken: ct))))
        {
            if (SelectedDeviceId != result.Id) continue;

            await _snapshotLock.WaitAsync(ct);
            try
            {
                // Already known names are kept when the fan count changes
                if (_fanNames.Length != result.FanCount)
                    Array.Resize(ref _fanNames, result.FanCount);

                int fanIndex = result.FanConfig.Index;
                if (fanIndex < _fanNames.Length)
                {
                    _fanNames[fanIndex] = result.FanConfig.Name;
                    foreach (var sensor in Sensors) sensor.UpdateFanName(fanIndex);
                }
            }
            finally { _snapshotLock.Release(); }
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModel logic

    private void CheckSensorCount(int sensorCount)
    {
        if (_sensorCount == sensorCount) return;

        ClearSensors();
        _sensorCount = sensorCount;
        for (int i = 0; i < _sensorCount; i++)
            Sensors.Add(new SensorItem(i, this));
    }


    private void ClearSensors()
    {
        CurrentSensor = null;
        Sensors.Clear();
        _sensorCount = 0;
        _dominantSensorIndexes = [];
        _dominantOutputs = [];
        CheckSelectedSensors();
    }


    /// <summary>
    /// Recalculates the selection-dependent properties.
    /// Called by <see cref="SensorItem"/> when its selection changes.
    /// </summary>
    public void CheckSelectedSensors()
    {
        var selected = Sensors.Where(sensor => sensor.Selected).ToList();

        SelectedSensorsCount = selected.Count;
        SelectionNotEmpty = (selected.Count > 0);
        SelectedSensorsDeletable = SelectionNotEmpty &&
            selected.All(sensor => sensor.SensorConfig.IsDeletable);
        _allSensorsSelected = SelectionNotEmpty && (selected.Count == Sensors.Count);
        OnPropertyChanged(nameof(AllSensorsSelected));
    }


    /// <summary>
    /// Returns the configured fan name, or the default one if it is not known yet.
    /// </summary>
    public string GetFanName(int fanIndex) =>
        ((fanIndex < _fanNames.Length) && !String.IsNullOrWhiteSpace(_fanNames[fanIndex]))
            ? _fanNames[fanIndex]
            : String.Format(Strings.FanDefaultNameFormat, fanIndex);


    //////////////////////////
    // Dominant sensor (per fan): the active sensor with the greatest output, the same
    // way as the firmware does it. Ties go to the lower sensor index, a zero output
    // is never dominant.

    /// <summary>
    /// Incremental update after a single sensor's outputs have changed: O(fans).
    /// A fan is fully rescanned only when the output of its dominant sensor decreases.
    /// </summary>
    private void UpdateDominantOutputs(SensorItem sensor)
    {
        int fanCount = Math.Min(sensor.FanOutputs.Count, _dominantSensorIndexes.Length);

        for (int i = 0; i < fanCount; i++)
        {
            double output = GetEffectiveOutput(sensor, i);
            int dominant = _dominantSensorIndexes[i];

            if (dominant == sensor.Index)
            {
                if (output >= _dominantOutputs[i])
                    _dominantOutputs[i] = output;           // Still the greatest
                else
                    SetDominant(i, FindDominantSensor(i));  // Another sensor may be greater now
            }
            else if ((output > _dominantOutputs[i]) ||
                     ((output == _dominantOutputs[i]) && (output > 0.0) && (sensor.Index < dominant)))
            {
                SetDominant(i, (sensor.Index, output));
            }
        }
    }


    /// <summary>
    /// Full recalculation for all fans: O(sensors × fans).
    /// Used only when the fan count changes.
    /// </summary>
    private void RebuildDominantOutputs()
    {
        int fanCount = (Sensors.Count > 0) ? Sensors.Max(sensor => sensor.FanOutputs.Count) : 0;
        _dominantSensorIndexes = new int[fanCount];
        _dominantOutputs = new double[fanCount];

        for (int i = 0; i < fanCount; i++)
            (_dominantSensorIndexes[i], _dominantOutputs[i]) = FindDominantSensor(i);

        foreach (var sensor in Sensors)
            sensor.UpdateDominantOutputs(_dominantSensorIndexes);
    }


    // Full scan of a single fan: O(sensors). Sensors are ordered by index,
    // so the strict comparison gives ties to the lower index.
    private (int SensorIndex, double Output) FindDominantSensor(int fanIndex)
    {
        int dominant = -1;
        double maxOutput = 0.0;

        foreach (var sensor in Sensors)
        {
            double output = GetEffectiveOutput(sensor, fanIndex);
            if (output > maxOutput)
            {
                maxOutput = output;
                dominant = sensor.Index;
            }
        }

        return (dominant, maxOutput);
    }


    // Inactive sensors and missing outputs never compete
    private static double GetEffectiveOutput(SensorItem sensor, int fanIndex) =>
        (sensor.SensorConfig.IsActive && (fanIndex < sensor.FanOutputs.Count))
            ? sensor.FanOutputs[fanIndex].Output
            : 0.0;


    private void SetDominant(int fanIndex, (int SensorIndex, double Output) dominant)
    {
        int previous = _dominantSensorIndexes[fanIndex];
        _dominantSensorIndexes[fanIndex] = dominant.SensorIndex;
        _dominantOutputs[fanIndex] = dominant.Output;

        if (previous == dominant.SensorIndex) return;

        SetDominantFlag(previous, fanIndex, false);
        SetDominantFlag(dominant.SensorIndex, fanIndex, true);
    }


    private void SetDominantFlag(int sensorIndex, int fanIndex, bool value)
    {
        if ((sensorIndex >= 0) && (sensorIndex < Sensors.Count) &&
            (fanIndex < Sensors[sensorIndex].FanOutputs.Count))
            Sensors[sensorIndex].FanOutputs[fanIndex].Dominant = value;
    }


    /// <summary>
    /// Runs the given command against every selected sensor and reports overall success.
    /// </summary>
    private async Task<bool> ForSelectedAsync(Func<SensorItem, Task<bool>> action)
    {
        // Snapshot: the list may be rebuilt (device switch) between awaits
        string? deviceId = SelectedDeviceId;
        var selectedSensors = Sensors.Where(sensor => sensor.Selected).ToList();

        bool overallResult = true;
        foreach (var sensor in selectedSensors)
        {
            if (SelectedDeviceId != deviceId) return false;
            overallResult &= await action(sensor);
        }
        return overallResult;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Host (software) sensors

    public bool CheckSensorExists(byte[] serialNumber) =>
        Sensors.Any(sensor => serialNumber.SequenceEqual(sensor.SensorConfig.SerialNumber));


    /// <summary>
    /// Builds the initial configuration of a new software sensor from the host sensor
    /// description. The result is reviewed by the user before it is added.
    /// </summary>
    public (SensorConfigDto Config, ObservableCollection<FanOutput> FanOutputs)
        PrepareHostSensorConfig(byte[] serialNumber, string deviceName, string sensorName,
                                double? maxTemperature, double? critTemperature)
    {
        // Temperature limits: host-reported max/critical values, shifted defaults otherwise
        const int alarmMargin = DtoDefaults.Sensor.DefaultAlarmTemperature -
                                DtoDefaults.Sensor.DefaultWarningTemperature;

        int? hostMax = maxTemperature.HasValue ? (int)Math.Round(maxTemperature.Value) : null;
        int? hostCrit = critTemperature.HasValue ? (int)Math.Round(critTemperature.Value) : null;

        int warnTemp = Math.Clamp(hostMax ?? (hostCrit - alarmMargin) ?? DtoDefaults.Sensor.DefaultWarningTemperature, 0, 255);
        int alarmTemp = Math.Clamp(hostCrit ?? (warnTemp + alarmMargin), warnTemp, 255);

        int highTemp = Math.Max(0, warnTemp -
            (DtoDefaults.Sensor.DefaultWarningTemperature - DtoDefaults.Sensor.DefaultHighTemperature));
        int lowTemp = Math.Max(0, highTemp -
            (DtoDefaults.Sensor.DefaultHighTemperature - DtoDefaults.Sensor.DefaultLowTemperature));

        var fanOutputs = new ObservableCollection<FanOutput>(
            Enumerable.Range(0, _fanNames.Length)
                .Select(i => new FanOutput(GetFanName(i), lowTemp, highTemp)));

        SensorConfigDto newConfig = new SensorConfigDto
        {
            Index = _sensorCount,

            SerialNumber = serialNumber,
            Name = BuildHostSensorName(deviceName, sensorName),
            Type = TSensorType.SW,

            // Configuration
            Setting = DtoDefaults.Sensor.DefaultSWSetting,
            WarningThreshold = warnTemp,
            AlarmThreshold = alarmTemp,

            IsVisible = true,
            IsActive = true,
            IsDeletable = true,
            IsPermanent = true,
            IsIgnorable = false,

            LowThresholds = fanOutputs.Select(item => item.LowThreshold).ToArray(),
            HighThresholds = fanOutputs.Select(item => item.HighThreshold).ToArray(),

            FanImpact = fanOutputs.Select(item => item.Impact).ToArray(),
            UnavailableSensorFanImpact = fanOutputs.Select(item => item.UnavailableImpact).ToArray()
        };

        return (newConfig, fanOutputs);
    }


    /// <summary>
    /// Builds a short sensor name from the host device and sensor names,
    /// e.g. "AMD Ryzen 7" + "Core #3 (Tctl)" → "AMD C3".
    /// </summary>
    private static string BuildHostSensorName(string deviceName, string sensorName)
    {
        char[] separators = [' ', '_', '-'];
        string[] deviceParts = deviceName.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        string[] sensorParts = sensorName.Split(separators, StringSplitOptions.RemoveEmptyEntries);

        // Device prefix: the first short non-numeric word (e.g. vendor or "CPU"),
        // otherwise the beginning of the first word.
        string? prefix = deviceParts.FirstOrDefault(part =>
            (part.Length <= MaxDevicePrefixLength) && !part.All(char.IsDigit));
        if ((prefix is null) && (deviceParts.Length > 0))
            prefix = deviceParts[0][..Math.Min(MaxDevicePrefixLength, deviceParts[0].Length)];

        string name = (prefix is null) ? String.Empty : (prefix + ' ');

        // Sensor part: the first number found (an index) preceded by the initials of the
        // preceding words; without a number - the initials, or the first word if there's
        // only one. All-capital words (abbreviations) are skipped.
        string initials = String.Empty;
        string firstWord = String.Empty;
        string index = String.Empty;
        foreach (string part in sensorParts)
        {
            string digits = new string(part.Where(char.IsDigit).ToArray());
            if ((digits.Length > 0) && int.TryParse(digits, out int number))
            {
                index = number.ToString();
                break;
            }
            else if (!part.All(char.IsUpper))
            {
                initials += part[0];
                if (firstWord.Length == 0) firstWord = part;
            }
        }

        if (index.Length > 0)
        {
            int maxInitials = Math.Max(0, MaxSensorNameLength - name.Length - index.Length);
            if (initials.Length > maxInitials) initials = initials[..maxInitials];
            name += initials + index;
        }
        else
        {
            name += (initials.Length > 1) ? initials : firstWord;
        }

        if (name.Length > MaxSensorNameLength) name = name[..MaxSensorNameLength];
        return name.TrimEnd();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Device commands

    public Task<bool> ResetState() =>
        ForSelectedAsync(sensor => sensor.ResetState());

    public Task<bool> ScanSensors() =>
        SendTrackedCommandAsync(new RescanSensorsCommand(), CommandTimeouts.SensorScan);

    public Task<bool> AddSoftwareSensor(SensorConfigDto config) =>
        SendSensorCommandAsync(new AddSensorCommand { Config = config });


    public Task<bool> RemoveSelectedSensors()
    {
        byte[][] serialNumbers = Sensors
            .Where(sensor => sensor.Selected)
            .Select(sensor => sensor.SensorConfig.SerialNumber)
            .ToArray();

        return serialNumbers.Length switch
        {
            0 => Task.FromResult(true),
            1 => SendSensorCommandAsync(new DeleteSensorCommand { SerialNumber = serialNumbers[0] }),
            _ => SendSensorCommandAsync(new DeleteSensorsCommand { SerialNumbers = serialNumbers })
        };
    }


    /// <summary>
    /// Sends a sensor-related command to the active device
    /// (false if not accepted; a device error is reported by CommandCompleted).
    /// </summary>
    public Task<bool> SendSensorCommandAsync(DeviceCommand command) =>
        SendTrackedCommandAsync(command, CommandTimeouts.Default);


    /// <summary>
    /// Requests the host hardware tree. Returns null if the request failed.
    /// </summary>
    public Task<List<HostDeviceDto>?> GetHostSensorList()
    {
        if ((SelectedDeviceId is null) || (ActiveService is null))
            return Task.FromResult<List<HostDeviceDto>?>(null);

        return TryExecuteAsync<List<HostDeviceDto>?>(
            async () => (await ActiveService.GetHostDeviceListAsync(
                new EmptyRequest(),
                new CallContext(new CallOptions(
                    cancellationToken: ViewModelCts.Token)))).Devices,
            fallback: null);
    }


    /// <summary>
    /// Requests the current host sensor readings. Returns null if the request failed.
    /// </summary>
    public Task<List<HostShortSensorDto>?> GetHostSensorReadings()
    {
        if ((SelectedDeviceId is null) || (ActiveService is null))
            return Task.FromResult<List<HostShortSensorDto>?>(null);

        return TryExecuteAsync<List<HostShortSensorDto>?>(
            async () => (await ActiveService.GetHostSensorListAsync(
                new EmptyRequest(),
                new CallContext(new CallOptions(
                    cancellationToken: ViewModelCts.Token)))).Sensors,
            fallback: null);
    }

}

// END-OF-FILE