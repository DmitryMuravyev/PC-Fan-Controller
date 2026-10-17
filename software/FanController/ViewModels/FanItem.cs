////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// FanItem.cs
////////////////////////////////////////////////////////////////////////////////////////


using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using FanController.Contracts;
using FanController.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;


namespace FanController.ViewModels;

/// <summary>
/// View Model of a single fan (one row of the Fans tab table).
/// </summary>
public partial class FanItem : ObservableObject, IDisposable
{
    // Some constants
    public static class FanItemConstants
    {
        public const double PowerProfileTolerance = 0.01;
        public const double HardwareScanMaxDurationMinutes = 0.5;
        public const double ModelScanMaxDurationMinutes = 10;

        // Minimum interval between slider-driven commands
        public const int CommandThrottleIntervalMs = 250;

        // Incoming snapshots don't overwrite a slider for this long after the user moved it
        public const long UserChangeHoldMs = 2000;
    }


    // Fan Overall status
    public enum FanStatus : byte
    {
        StatusDisabled = 0,
        StatusRunning = 1,
        StatusHalted = 2,
        StatusPreWarming = 3,
        StatusWarming = 4,
        StatusScanning = 5,
        StatusWarning = 6,
        StatusError = 7,
        StatusFailed = 8
    }

    public enum FanPowerProfile : byte
    {
        ProfileEasy = 0,
        ProfileChill = 1,
        ProfileNormal = 2,
        ProfileBusy = 3,
        ProfileHard = 4,
        ProfileCustom = 5
    }

    // Control basis of each predefined profile (Custom has none)
    public static readonly Dictionary<FanPowerProfile, double> ProfileBasis = new()
    {
        [FanPowerProfile.ProfileEasy] = 0.125,   // 0 ... 1 - (0.5 - 0.125) * 2) => 0 ... 0.25
        [FanPowerProfile.ProfileChill] = 0.25,   // 0 ... 0.5
        [FanPowerProfile.ProfileNormal] = 0.5,   // 0 ... 1.0
        [FanPowerProfile.ProfileBusy] = 0.75,    // (0.75 - 0.5) * 2 ... 1.0 => 0.5 ... 1.0
        [FanPowerProfile.ProfileHard] = 0.875,   // (0.875 - 0.5) * 2 ... 1.0 => 0.75 ... 1.0
    };


    private readonly FanViewModel _parent;
    private volatile bool _disposed = false;

    // Protection against sending multiple commands when moving the sliders.
    private readonly ThrottledDebounce<double> _basisThrottle;
    private readonly ThrottledDebounce<double> _valueThrottle;

    // Prevention of command transmission during device-initiated
    // state updates and also from incoming state snapshots overwriting
    // the sliders while the user is actively moving them.
    private double _expectedControlBasis = 0.0;
    private double _expectedControlValue = 0.0;
    private long _lastUserBasisChangeTick = 0;   // Environment.TickCount64
    private long _lastUserValueChangeTick = 0;   // Environment.TickCount64

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
                _parent.CheckSelectedFans();
        }
    }

    private FanStateDto _fanState = DtoDefaults.Fan.NewState();
    public FanStateDto FanState
    {
        get => _fanState;
        set
        {
            _fanState = value;
            UpdateStatus();
            UpdateCurrentMinPpm();
            SyncControls();
            OnPropertyChanged();
        }
    }

    private FanConfigDto _fanConfig = DtoDefaults.Fan.NewConfig();
    public FanConfigDto FanConfig
    {
        get => _fanConfig;
        set
        {
            _fanConfig = value;
            UpdateCurrentMinPpm();
            OnPropertyChanged();
        }
    }


    // Overall status derived from the state flags
    [ObservableProperty]
    private FanStatus status = FanStatus.StatusDisabled;

    // Fan is in warming or pre-warming mode
    [ObservableProperty]
    private bool _statusWarming = false;

    // Minimum PPM of the active control mode (lower bound of the RPM bar)
    [ObservableProperty]
    private uint _currentMinPpm = 0;

    // Fan control range basis: 0 - 1.0 ~ 0% - 100%
    [ObservableProperty]
    private double _controlBasis = DtoDefaults.Fan.DefaultControlBasis;

    [ObservableProperty]
    private FanPowerProfile _powerProfile = FanPowerProfile.ProfileNormal;

    // Fan control setting: 0 - 1.0 ~ 0% - 100%
    [ObservableProperty]
    private double _controlValue = 0.0;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction & disposal
    public FanItem(int index, FanViewModel parent, CancellationToken deviceToken)
    {
        Index = index;
        _parent = parent;

        _basisThrottle = new ThrottledDebounce<double>(
            action: ApplyControlBasisAsync,
            interval: TimeSpan.FromMilliseconds(FanItemConstants.CommandThrottleIntervalMs));
        _basisThrottle.OnError += ex =>
            Debug.WriteLine($"[Fan {Index} ControlBasis] Apply failed: {ex.Message}");
        _basisThrottle.UpdateToken(deviceToken);

        _valueThrottle = new ThrottledDebounce<double>(
            action: ApplyControlValueAsync,
            interval: TimeSpan.FromMilliseconds(FanItemConstants.CommandThrottleIntervalMs));
        _valueThrottle.OnError += ex =>
            Debug.WriteLine($"[Fan {Index} ControlValue] Apply failed: {ex.Message}");
        _valueThrottle.UpdateToken(deviceToken);
    }


    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _basisThrottle.Dispose();
        _valueThrottle.Dispose();
        GC.SuppressFinalize(this);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModel logic

    private void UpdateStatus()
    {
        // Ordered by priority: the first matching condition wins
        if (FanState.StatusFailed)
            Status = FanStatus.StatusFailed;
        else if (FanState.StatusHWErrors || FanState.StatusAlarms)
            Status = FanStatus.StatusError;
        else if (FanState.StatusWarnings)
            Status = FanStatus.StatusWarning;
        else if (FanState.StatusScanning)
            Status = FanStatus.StatusScanning;
        else if (FanState.StatusWarming)
            Status = FanStatus.StatusWarming;
        else if (FanState.StatusPreWarming)
            Status = FanStatus.StatusPreWarming;
        else if (FanState.ControlMode == FanControlMode.ControlDisabled)
            Status = FanStatus.StatusDisabled;
        else if (FanState.StatusHalted)
            Status = FanStatus.StatusHalted;
        else
            Status = FanStatus.StatusRunning;

        StatusWarming = FanState.StatusWarming || FanState.StatusPreWarming;
    }


    private void UpdateCurrentMinPpm()
    {
        CurrentMinPpm = FanState.ControlMode switch
        {
            FanControlMode.ControlPWM => FanConfig.PwmControlledMinPpm,
            FanControlMode.ControlDCDC => FanConfig.PwrControlledMinPpm,
            _ => 0
        };
    }


    /// <summary>
    /// Applies device-reported control values to the sliders, unless the user
    /// changed them recently (the device may not have applied them yet).
    /// </summary>
    private void SyncControls()
    {
        long now = Environment.TickCount64;

        if ((_expectedControlBasis != FanState.ControlBasis) &&
            ((now - _lastUserBasisChangeTick) >= FanItemConstants.UserChangeHoldMs))
        {
            _expectedControlBasis = FanState.ControlBasis;
            ControlBasis = _expectedControlBasis;

            // Device-initiated basis change: find the matching profile (Custom if none)
            if (PowerProfile != FanPowerProfile.ProfileCustom) 
                PowerProfile = FindPowerProfile(ControlBasis);
        }

        if ((_expectedControlValue != FanState.ControlValue) &&
            ((now - _lastUserValueChangeTick) >= FanItemConstants.UserChangeHoldMs))
        {
            _expectedControlValue = FanState.ControlValue;
            ControlValue = _expectedControlValue;
        }
    }


    public static FanPowerProfile FindPowerProfile(double value)
    {
        foreach (var (profile, profileValue) in ProfileBasis)
        {
            if (Math.Abs(profileValue - value) <= FanItemConstants.PowerProfileTolerance)
                return profile;
        }
        return FanPowerProfile.ProfileCustom;
    }


    /// <summary>
    /// Bulk selection by the parent ViewModel, without the per-item
    /// <see cref="FanViewModel.CheckSelectedFans"/> callback.
    /// </summary>
    internal void SetSelectedSilently(bool value) =>
        SetProperty(ref _selected, value, nameof(Selected));


    ////////////////////////////////////////////////////////////////////////////////////////
    // Device commands

    private Task<bool> SendCommandAsync(DeviceCommand command, TimeSpan? trackTimeout = null) =>
        _parent.SendFanCommandAsync(command, trackTimeout);

    // Scans: the device's answer is awaited up to the scan duration
    // (overloads with a timeout are used when several fans are scanned in a row)
    public Task<bool> RescanHardware() =>
        RescanHardware(CommandTimeouts.FanScan(FanItemConstants.HardwareScanMaxDurationMinutes, 1));

    public Task<bool> RescanHardware(TimeSpan timeout) =>
        SendCommandAsync(new RescanFanHardwareCommand { FanIndex = Index }, timeout);

    public Task<bool> RescanModel() =>
        RescanModel(CommandTimeouts.FanScan(FanItemConstants.ModelScanMaxDurationMinutes, 1));

    public Task<bool> RescanModel(TimeSpan timeout) =>
        SendCommandAsync(new RescanFanModelCommand { FanIndex = Index }, timeout);

    public Task<bool> ResetState() =>
        SendCommandAsync(new ResetFanCommand { FanIndex = Index }, CommandTimeouts.Default);

    public Task<bool> SetControlMode(FanControlMode mode) =>
        SendCommandAsync(new SetFanControlModeCommand { FanIndex = Index, Mode = mode });

    public Task<bool> SetOperationMode(FanOperationMode mode) =>
        SendCommandAsync(new SetFanOperationModeCommand { FanIndex = Index, Mode = mode });

    public Task<bool> UpdateConfiguration(FanConfigDto config) =>
        SendCommandAsync(new SetFanConfigurationCommand { FanIndex = Index, Config = config }, CommandTimeouts.Default);


    /// <summary>
    /// Selects a power profile and sends its control basis to the device.
    /// The Custom profile has no basis: it only unlocks the basis slider.
    /// </summary>
    public Task<bool> SetPowerProfile(FanPowerProfile profile)
    {
        PowerProfile = profile;

        if (!ProfileBasis.TryGetValue(profile, out double basis))
            return Task.FromResult(true);

        // Sent directly (not throttled) so the caller gets the result;
        // the expected value prevents OnControlBasisChanged from sending it again.
        _expectedControlBasis = basis;
        _lastUserBasisChangeTick = Environment.TickCount64;
        ControlBasis = basis;

        return SendCommandAsync(new SetFanControlBasisCommand { FanIndex = Index, Basis = basis });
    }


    // Throttled slider applies
    // The user-change tick is refreshed at the moment of sending (on the UI thread),
    // so the snapshot hold interval always counts from the actual command transmission.

    private Task ApplyControlBasisAsync(double basis, CancellationToken token) =>
        _parent.ApplyFanCommandAsync(
            new SetFanControlBasisCommand { FanIndex = Index, Basis = basis },
            () => _lastUserBasisChangeTick = Environment.TickCount64,
            token);

    private Task ApplyControlValueAsync(double value, CancellationToken token) =>
        _parent.ApplyFanCommandAsync(
            new SetFanControlValueCommand { FanIndex = Index, Value = value },
            () => _lastUserValueChangeTick = Environment.TickCount64,
            token);


    ////////////////////////////////////////////////////////////////////////////////////////
    // Property changed handlers

    partial void OnControlBasisChanged(double value)
    {
        if (!_disposed && (value != _expectedControlBasis))
        {
            _expectedControlBasis = value;
            _lastUserBasisChangeTick = Environment.TickCount64;
            _basisThrottle.Signal(value);
        }
    }

    partial void OnControlValueChanged(double value)
    {
        if (!_disposed && (value != _expectedControlValue))
        {
            _expectedControlValue = value;
            _lastUserValueChangeTick = Environment.TickCount64;
            _valueThrottle.Signal(value);
        }
    }

}

// END-OF-FILE