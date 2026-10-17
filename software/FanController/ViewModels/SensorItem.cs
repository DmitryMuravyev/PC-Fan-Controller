////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SensorItem.cs
////////////////////////////////////////////////////////////////////////////////////////

using CommunityToolkit.Mvvm.ComponentModel;
using FanController.Assets.Localization;
using FanController.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Threading.Tasks;


namespace FanController.ViewModels;

/// <summary>
/// Influence of a sensor on a single fan: the configured thresholds and impacts
/// (editable in the sensor details dialog) and the current state of the influence.
/// </summary>
public partial class FanOutput : ObservableValidator
{
    // To reliably detect changes in Impact settings
    private const double ImpactTolerance = 0.01;            

    // Suppresses the dirty-flag update while the values are being loaded
    private bool _updatingConfig = false;

    // Original snapshot for dirty-check
    private int _originalLowThreshold = DtoDefaults.Sensor.DefaultLowTemperature;
    private int _originalHighThreshold = DtoDefaults.Sensor.DefaultHighTemperature;
    private double _originalImpact = DtoDefaults.Sensor.DefaultImpact;
    private double _originalUnavailableImpact = DtoDefaults.Sensor.DefaultUnavailableImpact;

    [ObservableProperty]
    private string _fanName = String.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(FanOutput), nameof(ValidateThresholds))]
    private int _lowThreshold = DtoDefaults.Sensor.DefaultLowTemperature;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(FanOutput), nameof(ValidateThresholds))]
    private int _highThreshold = DtoDefaults.Sensor.DefaultHighTemperature;

    // Maximum output (at or above the high threshold): 0 - 1.0 ~ 0% - 100%
    [ObservableProperty]
    private double _impact = DtoDefaults.Sensor.DefaultImpact;

    // Output when the sensor readings are unavailable (unless the sensor is ignorable)
    [ObservableProperty]
    private double _unavailableImpact = DtoDefaults.Sensor.DefaultUnavailableImpact;

    // Current output: 0 - 1.0 ~ 0% - 100% (calculated the same way as in the firmware)
    [ObservableProperty]
    private double _output = 0.0;

    // The sensor temperature is below this fan's lower limit (reported by the device).
    // The fan is stopped when it is overcooled by all sensors.
    [ObservableProperty]
    private bool _overcooled = false;

    // This sensor has the greatest (decisive) influence on this fan
    [ObservableProperty]
    private bool _dominant = false;

    // The settings differ from the original snapshot
    [ObservableProperty]
    private bool _isDirty = false;

    // Raised when the thresholds (validation state) or the dirty flag change
    public event EventHandler? FlagsChanged;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public FanOutput(string fanName, int? lowTemperature = null, int? highTemperature = null)
    {
        FanName = fanName;

        if ((lowTemperature is not null) &&
            (highTemperature is not null) &&
            (lowTemperature.Value <= highTemperature.Value))
        {
            _originalLowThreshold = _lowThreshold = lowTemperature.Value;
            _originalHighThreshold = _highThreshold = highTemperature.Value;
        }
    }


    // Copy (used as the editable working copy in the sensor details dialog)
    public FanOutput(FanOutput original)
    {
        _fanName = original.FanName;
        _originalLowThreshold = _lowThreshold = original.LowThreshold;
        _originalHighThreshold = _highThreshold = original.HighThreshold;
        _originalImpact = _impact = original.Impact;
        _originalUnavailableImpact = _unavailableImpact = original.UnavailableImpact;
        UpdateDirtyFlag();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Logic

    /// <summary>
    /// Loads the settings for the fan <paramref name="index"/> from the sensor
    /// configuration and makes them the new original snapshot.
    /// </summary>
    public void UpdateWithConfig(int index, SensorConfigDto config)
    {
        _updatingConfig = true;
        _originalLowThreshold = LowThreshold = config.LowThresholds[index];
        _originalHighThreshold = HighThreshold = config.HighThresholds[index];
        _originalImpact = Impact = config.FanImpact[index];
        _originalUnavailableImpact = UnavailableImpact = config.UnavailableSensorFanImpact[index];
        _updatingConfig = false;
        UpdateDirtyFlag();
    }


    /// <summary>
    /// Copies the settings of another output (the original snapshot is kept,
    /// so the copied values are reported as changes).
    /// </summary>
    public void UpdateWithFanOutput(FanOutput original)
    {
        _updatingConfig = true;
        LowThreshold = original.LowThreshold;
        HighThreshold = original.HighThreshold;
        Impact = original.Impact;
        UnavailableImpact = original.UnavailableImpact;
        _updatingConfig = false;
        UpdateDirtyFlag();
    }


    private void UpdateDirtyFlag()
    {
        IsDirty =
            (LowThreshold != _originalLowThreshold) ||
            (HighThreshold != _originalHighThreshold) ||
            (Math.Abs(Impact - _originalImpact) > ImpactTolerance) ||
            (Math.Abs(UnavailableImpact - _originalUnavailableImpact) > ImpactTolerance);
    }


    public static ValidationResult ValidateThresholds(int value, ValidationContext ctx)
    {
        var item = (FanOutput)ctx.ObjectInstance;
        return item.HighThreshold >= item.LowThreshold
            ? ValidationResult.Success!
            : ctx.MemberName == nameof(HighThreshold) ?
                new ValidationResult(Strings.ValidationHighTemperatureThresholdFormat) :
                new ValidationResult(Strings.ValidationLowTemperatureThresholdFormat);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Property changed handlers

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_updatingConfig) return;

        switch (e.PropertyName)
        {
            case nameof(LowThreshold):
            case nameof(HighThreshold):
            case nameof(Impact):
            case nameof(UnavailableImpact):
                UpdateDirtyFlag();
                break;
        }
    }

    // Cross-validate both when either changes
    partial void OnLowThresholdChanged(int value)
    {
        ValidateProperty(HighThreshold, nameof(HighThreshold));
        FlagsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnHighThresholdChanged(int value)
    {
        ValidateProperty(LowThreshold, nameof(LowThreshold));
        FlagsChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIsDirtyChanged(bool value)
    {
        FlagsChanged?.Invoke(this, EventArgs.Empty);
    }

}


/// <summary>
/// View Model of a single sensor (one row of the Sensors tab table).
/// </summary>
public partial class SensorItem : ObservableObject
{
    // Sensor Overall status
    public enum SensorStatus : byte
    {
        StatusDisabled = 0,
        StatusValid = 1,
        StatusWarning = 2,
        StatusOutdated = 3,
        StatusAlarm = 4,
        StatusError = 5
    }


    private readonly SensorViewModel _parent;
    public int Index { get; }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Observable state

    private bool _selected = false;
    public bool Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                _parent.CheckSelectedSensors();
        }
    }

    private SensorStateDto _sensorState = DtoDefaults.Sensor.NewState();
    public SensorStateDto SensorState
    {
        get => _sensorState;
        set
        {
            _sensorState = value;
            UpdateStatus();
            RecalculateFanOutputs();
            OnPropertyChanged();
        }
    }

    private SensorConfigDto _sensorConfig = DtoDefaults.Sensor.NewConfig();
    public SensorConfigDto SensorConfig
    {
        get => _sensorConfig;
        set
        {
            _sensorConfig = value;
            ReadableSerialNumber = Convert.ToHexString(value.SerialNumber);
            UpdateFanOutputs();
            UpdateStatus();
            RecalculateFanOutputs();
            OnPropertyChanged();
        }
    }

    // Overall status derived from the state flags and the configuration
    [ObservableProperty]
    private SensorStatus _status = SensorStatus.StatusDisabled;

    [ObservableProperty]
    private string _readableSerialNumber = String.Empty;

    // Influence of this sensor on each fan
    public ObservableCollection<FanOutput> FanOutputs { get; } = [];


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public SensorItem(int index, SensorViewModel parent)
    {
        Index = index;
        _parent = parent;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModel logic

    private void UpdateStatus()
    {
        // Ordered by priority: the first matching condition wins
        if (SensorState.StatusError || !SensorState.StatusConnected)
            Status = SensorStatus.StatusError;
        else if (SensorState.StatusAlarm)
            Status = SensorStatus.StatusAlarm;
        else if (SensorState.StatusWarning)
            Status = SensorStatus.StatusWarning;
        else if (!SensorState.StatusValid)
            Status = SensorStatus.StatusOutdated;
        else if (!SensorConfig.IsActive)
            Status = SensorStatus.StatusDisabled;
        else
            Status = SensorStatus.StatusValid;
    }


    // Rebuilds (when the fan count changes) and reloads the per-fan outputs from the configuration
    private void UpdateFanOutputs()
    {
        int fanCount = Math.Min(
            Math.Min(SensorConfig.LowThresholds.Length, SensorConfig.HighThresholds.Length),
            Math.Min(SensorConfig.FanImpact.Length, SensorConfig.UnavailableSensorFanImpact.Length));

        if (FanOutputs.Count != fanCount)
        {
            FanOutputs.Clear();
            for (int i = 0; i < fanCount; i++)
                FanOutputs.Add(new FanOutput(_parent.GetFanName(i)));
        }

        for (int i = 0; i < fanCount; i++)
            FanOutputs[i].UpdateWithConfig(i, SensorConfig);
    }


    public void UpdateFanName(int fanIndex)
    {
        if (fanIndex < FanOutputs.Count)
            FanOutputs[fanIndex].FanName = _parent.GetFanName(fanIndex);
    }


    // Refreshes all fan names (e.g. the localized default names after a language change)
    public void UpdateFanNames()
    {
        for (int i = 0; i < FanOutputs.Count; i++)
            FanOutputs[i].FanName = _parent.GetFanName(i);
    }


    /// <summary>
    /// Updates the per-fan outputs. The output values are not transmitted by the device
    /// and are calculated here; the overcooled flags are taken from the sensor state.
    /// </summary>
    private void RecalculateFanOutputs()
    {
        bool isActive = SensorConfig.IsActive;
        bool[] overcooledMap = SensorState.OvercooledByFanMap;

        for (int i = 0; i < FanOutputs.Count; i++)
        {
            FanOutput fanOutput = FanOutputs[i];
            fanOutput.Output = CalculateOutput(fanOutput);
            fanOutput.Overcooled = !isActive || 
                ((i < overcooledMap.Length) && overcooledMap[i]);
        }
    }


    // Replicates the firmware calculation of the sensor output for a single fan
    private double CalculateOutput(FanOutput fanOutput)
    {
        if (!SensorConfig.IsActive) return 0.0;

        // Unavailable readings: either ignored or the configured fallback output
        if (!SensorState.StatusValid)
            return SensorConfig.IsIgnorable ? 0.0 : fanOutput.UnavailableImpact;

        double temperature = SensorState.Temperature;
        double low = fanOutput.LowThreshold;
        double high = fanOutput.HighThreshold;

        if (temperature < low) return 0.0;
        if ((low >= high) || (temperature >= high)) return fanOutput.Impact;

        // Linear between the thresholds: 0 at low, Impact at high
        return (temperature - low) * fanOutput.Impact / (high - low);
    }


    /// <param name="dominantSensorIndexes">Index of the dominant sensor for each fan (-1 if none).</param>
    public void UpdateDominantOutputs(int[] dominantSensorIndexes)
    {
        for (int i = 0; i < FanOutputs.Count; i++)
        {
            FanOutputs[i].Dominant = (i < dominantSensorIndexes.Length) &&
                                     (dominantSensorIndexes[i] == Index);
        }
    }


    /// <summary>
    /// Bulk selection by the parent ViewModel, without the per-item
    /// <see cref="SensorViewModel.CheckSelectedSensors"/> callback.
    /// </summary>
    internal void SetSelectedSilently(bool value) =>
        SetProperty(ref _selected, value, nameof(Selected));


    ////////////////////////////////////////////////////////////////////////////////////////
    // Device commands

    private Task<bool> SendCommandAsync(DeviceCommand command) =>
        _parent.SendSensorCommandAsync(command);

    public Task<bool> ResetState() =>
        SendCommandAsync(new ResetSensorCommand { SerialNumber = SensorConfig.SerialNumber });

    public Task<bool> UpdateConfiguration(SensorConfigDto config) =>
        SendCommandAsync(new SetSensorConfigurationCommand { Config = config });

    public Task<bool> RemoveSensor() =>
        SendCommandAsync(new DeleteSensorCommand { SerialNumber = SensorConfig.SerialNumber });

}

// END-OF-FILE
