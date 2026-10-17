////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// FanDetailsViewModel.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Linq;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using FanController.Contracts;
using static FanController.ViewModels.FanItem;


namespace FanController.ViewModels;

/// <summary>
/// Control mode option for the ComboBox: the mode and whether the fan supports it.
/// </summary>
public sealed record ControlModeOption(FanControlMode Mode, bool IsAvailable);


/// <summary>
/// Fan details (configuration editor) window View Model
/// </summary>
public partial class FanDetailsViewModel : ObservableObject
{
    // Suppresses the PowerProfile → ControlBasis reaction while the profile is being
    // derived from the basis (otherwise the basis would snap to the profile value).
    private bool _syncingPowerProfile = false;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Option lists

    // All available Fan sizes for ComboBox
    public IReadOnlyList<FanModelSize> AllModelSizes { get; } =
        Enum.GetValues<FanModelSize>();

    // All available Fan power options for ComboBox
    public IReadOnlyList<FanModelPower> AllModelPowerTypes { get; } =
        Enum.GetValues<FanModelPower>();

    // All available Fan classes for ComboBox
    public IReadOnlyList<FanModelClass> AllModelClasses { get; } =
        Enum.GetValues<FanModelClass>();

    // Fixed list of allowed PPR values
    public IReadOnlyList<int> PprOptions { get; } = [1, 2, 4];

    // All available Fan Spin-Up options for ComboBox
    public IReadOnlyList<FanOptionSpinPower> AllSpinUpOptions { get; } =
        Enum.GetValues<FanOptionSpinPower>();

    // All available Fan Rate-Of-Change options for ComboBox
    public IReadOnlyList<FanOptionRoc> AllRocOptions { get; } =
        Enum.GetValues<FanOptionRoc>();

    // All Fan Control Modes for ComboBox, with their availability for this fan
    public IReadOnlyList<ControlModeOption> AllControlModes { get; }

    // All available Fan Operation Modes for ComboBox
    public IReadOnlyList<FanOperationMode> AllOperationModes { get; } =
        Enum.GetValues<FanOperationMode>();

    // All available Fan Power Profiles for ComboBox
    public IReadOnlyList<FanPowerProfile> AllPowerProfiles { get; } =
        Enum.GetValues<FanPowerProfile>();


    ////////////////////////////////////////////////////////////////////////////////////////
    // Read-only parameters
    // Kept as separate properties (rather than bound to Original directly),
    // so any of them can later become an editable [ObservableProperty]
    // without changes to the View.

    // Snapshot kept for dirty-check and for building the updated DTO
    public FanConfigDto Original { get; }

    public int Index { get; }

    // Hardware parameters
    public bool HardwarePwmAvailable { get; }
    public bool HardwareRpmAvailable { get; }
    public bool HardwareVoltmeterAvailable { get; }
    public bool HardwareMosfetAvailable { get; }
    public bool HardwareDcdcAvailable { get; }

    public double MaxVoltage { get; }
    public double MinVoltage { get; }
    public double MaxVoltageControl { get; }
    public double MinVoltageControl { get; }

    // Connected Fan parameters
    public bool ModelTachometer { get; }
    public bool ModelPwm { get; }
    public bool ModelDcdcRangeDetected { get; }
    public bool ModelPwmRangeDetected { get; }
    public bool ModelPwmFreewheelTachometer { get; }
    public bool ModelPwmExcessiveFullSpeed { get; }

    public uint MaxPPM { get; }
    public double MinPwmControl { get; }
    public uint PwmControlledMinPpm { get; }
    public uint PwmPenultimatePpm { get; }
    public double MinPwrControl { get; }
    public uint PwrControlledMinPpm { get; }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Editable parameters

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _name = String.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _modelConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private FanModelSize _modelSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private FanModelPower _modelPower;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private FanModelClass _modelClass;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private int _optionPpr;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private FanOptionSpinPower _optionSpinPowerPwm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private FanOptionSpinPower _optionSpinPowerPwr;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private FanOptionRoc _optionRoc;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _optionPidEnabledPwm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _optionPidEnabledPwr;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _optionPidSlowPwm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _optionPidSlowPwr;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _optionAlwaysOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private FanControlMode _controlMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private FanOperationMode _operationMode;

    // Fan control range basis: 0 - 1.0 ~ 0% - 100%
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private double _controlBasis;

    // Predefined basis preset (Custom when the basis matches none of them)
    [ObservableProperty]
    private FanPowerProfile _powerProfile;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public FanDetailsViewModel(FanConfigDto dto)
    {
        Original = dto;

        // Read-only
        Index = dto.Index;

        HardwarePwmAvailable = dto.HardwarePwmAvailable;
        HardwareRpmAvailable = dto.HardwareRpmAvailable;
        HardwareVoltmeterAvailable = dto.HardwareVoltmeterAvailable;
        HardwareMosfetAvailable = dto.HardwareMosfetAvailable;
        HardwareDcdcAvailable = dto.HardwareDcdcAvailable;

        MaxVoltage = dto.MaxVoltage;
        MinVoltage = dto.MinVoltage;
        MaxVoltageControl = dto.MaxVoltageControl;
        MinVoltageControl = dto.MinVoltageControl;

        ModelTachometer = dto.ModelTachometer;
        ModelPwm = dto.ModelPwm;
        ModelDcdcRangeDetected = dto.ModelDcdcRangeDetected;
        ModelPwmRangeDetected = dto.ModelPwmRangeDetected;
        ModelPwmFreewheelTachometer = dto.ModelPwmFreewheelTachometer;
        ModelPwmExcessiveFullSpeed = dto.ModelPwmExcessiveFullSpeed;

        MaxPPM = dto.MaxPPM;
        MinPwmControl = dto.MinPwmControl;
        PwmControlledMinPpm = dto.PwmControlledMinPpm;
        PwmPenultimatePpm = dto.PwmPenultimatePpm;
        MinPwrControl = dto.MinPwrControl;
        PwrControlledMinPpm = dto.PwrControlledMinPpm;

        // Depends on the capabilities above
        AllControlModes = Enum.GetValues<FanControlMode>()
            .Select(mode => new ControlModeOption(mode, IsControlModeAvailable(mode)))
            .ToArray();

        // Editable
        Name = dto.Name;

        ModelConnected = dto.ModelConnected;
        ModelSize = dto.ModelSize;
        ModelPower = dto.ModelPower;
        ModelClass = dto.ModelClass;

        OptionPpr = dto.OptionPpr;
        OptionSpinPowerPwm = dto.OptionSpinPowerPwm;
        OptionSpinPowerPwr = dto.OptionSpinPowerPwr;
        OptionRoc = dto.OptionRoc;
        OptionPidEnabledPwm = dto.OptionPidEnabledPwm;
        OptionPidEnabledPwr = dto.OptionPidEnabledPwr;
        OptionPidSlowPwm = dto.OptionPidSlowPwm;
        OptionPidSlowPwr = dto.OptionPidSlowPwr;
        OptionAlwaysOn = dto.OptionAlwaysOn;

        ControlMode = dto.ControlMode;
        OperationMode = dto.OperationMode;

        // Set directly: no basis ↔ profile synchronization is needed here
        _controlBasis = dto.ControlBasis;
        _powerProfile = FindPowerProfile(dto.ControlBasis);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModel logic

    // Control modes supported by this fan (Disabled is always available)
    private bool IsControlModeAvailable(FanControlMode mode) => mode switch
    {
        FanControlMode.ControlPWM => ModelPwmRangeDetected,
        FanControlMode.ControlDCDC => ModelDcdcRangeDetected,
        FanControlMode.ControlONOFF => HardwareMosfetAvailable,
        _ => true
    };


    /// <summary>
    /// True when any editable parameter differs from the original configuration.
    /// </summary>
    public bool IsDirty =>
        (Name != Original.Name) ||
        (ModelConnected != Original.ModelConnected) ||
        (ModelSize != Original.ModelSize) ||
        (ModelPower != Original.ModelPower) ||
        (ModelClass != Original.ModelClass) ||
        (OptionPpr != Original.OptionPpr) ||
        (OptionSpinPowerPwm != Original.OptionSpinPowerPwm) ||
        (OptionSpinPowerPwr != Original.OptionSpinPowerPwr) ||
        (OptionRoc != Original.OptionRoc) ||
        (OptionPidEnabledPwm != Original.OptionPidEnabledPwm) ||
        (OptionPidEnabledPwr != Original.OptionPidEnabledPwr) ||
        (OptionPidSlowPwm != Original.OptionPidSlowPwm) ||
        (OptionPidSlowPwr != Original.OptionPidSlowPwr) ||
        (OptionAlwaysOn != Original.OptionAlwaysOn) ||
        (ControlMode != Original.ControlMode) ||
        (OperationMode != Original.OperationMode) ||
        (Math.Abs(ControlBasis - Original.ControlBasis) > FanItemConstants.PowerProfileTolerance);


    /// <summary>
    /// Builds the updated configuration (the read-only parameters are kept from the original).
    /// </summary>
    public FanConfigDto BuildUpdatedDto() => Original with
    {
        Name = Name,
        ModelConnected = ModelConnected,
        ModelSize = ModelSize,
        ModelPower = ModelPower,
        ModelClass = ModelClass,
        OptionPpr = OptionPpr,
        OptionSpinPowerPwm = OptionSpinPowerPwm,
        OptionSpinPowerPwr = OptionSpinPowerPwr,
        OptionRoc = OptionRoc,
        OptionPidEnabledPwm = OptionPidEnabledPwm,
        OptionPidEnabledPwr = OptionPidEnabledPwr,
        OptionPidSlowPwm = OptionPidSlowPwm,
        OptionPidSlowPwr = OptionPidSlowPwr,
        OptionAlwaysOn = OptionAlwaysOn,
        ControlMode = ControlMode,
        OperationMode = OperationMode,
        ControlBasis = ControlBasis
    };


    ////////////////////////////////////////////////////////////////////////////////////////
    // Property changed handlers

    partial void OnControlBasisChanged(double value)
    {
        _syncingPowerProfile = true;
        PowerProfile = FindPowerProfile(value);
        _syncingPowerProfile = false;
    }

    partial void OnPowerProfileChanged(FanPowerProfile value)
    {
        // Custom has no basis: the current one is kept
        if (!_syncingPowerProfile && ProfileBasis.TryGetValue(value, out double basis))
            ControlBasis = basis;
    }

}

// END-OF-FILE
