////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SensorDetailsViewModel.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using FanController.Assets.Localization;
using FanController.Contracts;


namespace FanController.ViewModels;

/// <summary>
/// Sensor details (configuration editor) window View Model.
/// Also used to set up a new software sensor before it is added.
/// </summary>
public partial class SensorDetailsViewModel : ObservableValidator
{
    // Aggregated state of the per-fan outputs
    private bool _isFanOutputsDirty = false;
    private bool _isFanOutputsInvalid = false;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Read-only parameters
    // Kept as separate properties (rather than bound to Original directly),
    // so any of them can later become editable without changes to the View.

    // Snapshot kept for dirty-check and for building the updated DTO
    public SensorConfigDto Original { get; }

    // Window mode: adding a new (software) sensor or editing an existing one
    public bool IsAddWindow { get; }

    public int Index { get; }
    public TSensorType Type { get; }
    public string ReadableSerialNumber { get; }
    public bool IsDeletable { get; }
    public bool IsPermanent { get; }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Editable parameters

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _name = String.Empty;

    // Type-specific setting: hardware sensor configuration (displayed read-only),
    // or software sensor startup timeout in seconds
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private UInt32 _setting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _isVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private bool _isIgnorable;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0, 255)]
    [CustomValidation(typeof(SensorDetailsViewModel), nameof(ValidateTemperatureThresholds))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private int _warningThreshold;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Range(0, 255)]
    [CustomValidation(typeof(SensorDetailsViewModel), nameof(ValidateTemperatureThresholds))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private int _alarmThreshold;

    // Working copies of the per-fan outputs (the caller's ones stay unchanged)
    public ObservableCollection<FanOutput> FanOutputs { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentFanOutput))]
    private FanOutput? _currentFanOutput;

    public bool HasCurrentFanOutput => CurrentFanOutput is not null;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction

    public SensorDetailsViewModel(SensorConfigDto dto, IEnumerable<FanOutput> fanOutputs, bool isAddWindow = false)
    {
        Original = dto;
        IsAddWindow = isAddWindow;

        // Read-only
        Index = dto.Index;
        Type = dto.Type;
        ReadableSerialNumber = BitConverter.ToString(dto.SerialNumber);
        IsDeletable = dto.IsDeletable;
        IsPermanent = dto.IsPermanent;

        // Editable
        Name = dto.Name;
        Setting = dto.Setting;
        IsVisible = dto.IsVisible;
        IsActive = dto.IsActive;
        IsIgnorable = dto.IsIgnorable;
        WarningThreshold = dto.WarningThreshold;
        AlarmThreshold = dto.AlarmThreshold;

        FanOutputs = new ObservableCollection<FanOutput>(
            fanOutputs.Select(item => new FanOutput(item)));
        foreach (var item in FanOutputs) item.FlagsChanged += OnFanOutputFlagsChanged;

        ErrorsChanged += (_, _) => OnPropertyChanged(nameof(IsValid));

        // The initial values may already be invalid (e.g. host-reported limits)
        ValidateAllProperties();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModel logic

    // No validation errors in the sensor parameters and in the per-fan outputs
    public bool IsValid => !HasErrors && !_isFanOutputsInvalid;


    /// <summary>
    /// True when any editable parameter differs from the original configuration.
    /// </summary>
    public bool IsDirty =>
        _isFanOutputsDirty ||
        (Name != Original.Name) ||
        (Setting != Original.Setting) ||
        (IsVisible != Original.IsVisible) ||
        (IsActive != Original.IsActive) ||
        (IsIgnorable != Original.IsIgnorable) ||
        (WarningThreshold != Original.WarningThreshold) ||
        (AlarmThreshold != Original.AlarmThreshold);


    public static ValidationResult? ValidateTemperatureThresholds(object? value, ValidationContext ctx)
    {
        if (ctx.ObjectInstance is not SensorDetailsViewModel vm)
            return ValidationResult.Success;

        return vm.AlarmThreshold >= vm.WarningThreshold
            ? ValidationResult.Success
            : ctx.MemberName == nameof(AlarmThreshold)
                ? new ValidationResult(Strings.ValidationAlarmTemperatureThresholdFormat)
                : new ValidationResult(Strings.ValidationWarningTemperatureThresholdFormat);
    }


    /// <summary>
    /// Copies the settings of the selected fan output to all other outputs.
    /// </summary>
    public void CloneCurrentFanOutputSettings()
    {
        if (CurrentFanOutput is null) return;

        foreach (var item in FanOutputs)
            if (item != CurrentFanOutput) item.UpdateWithFanOutput(CurrentFanOutput);
    }


    /// <summary>
    /// Builds the updated configuration (the read-only parameters are kept from the original).
    /// </summary>
    public SensorConfigDto BuildUpdatedDto() => Original with
    {
        Name = Name,
        Setting = Setting,
        IsVisible = IsVisible,
        IsActive = IsActive,
        IsIgnorable = IsIgnorable,
        WarningThreshold = WarningThreshold,
        AlarmThreshold = AlarmThreshold,
        LowThresholds = FanOutputs.Select(item => item.LowThreshold).ToArray(),
        HighThresholds = FanOutputs.Select(item => item.HighThreshold).ToArray(),
        FanImpact = FanOutputs.Select(item => item.Impact).ToArray(),
        UnavailableSensorFanImpact = FanOutputs.Select(item => item.UnavailableImpact).ToArray()
    };


    ////////////////////////////////////////////////////////////////////////////////////////
    // Property changed handlers

    // Cross-validate both thresholds when either changes
    partial void OnWarningThresholdChanged(int value) =>
        ValidateProperty(AlarmThreshold, nameof(AlarmThreshold));

    partial void OnAlarmThresholdChanged(int value) =>
        ValidateProperty(WarningThreshold, nameof(WarningThreshold));


    private void OnFanOutputFlagsChanged(object? sender, EventArgs e)
    {
        bool dirty = FanOutputs.Any(item => item.IsDirty);
        bool invalid = FanOutputs.Any(item => item.HasErrors);

        if (_isFanOutputsDirty != dirty)
        {
            _isFanOutputsDirty = dirty;
            OnPropertyChanged(nameof(IsDirty));
        }

        if (_isFanOutputsInvalid != invalid)
        {
            _isFanOutputsInvalid = invalid;
            OnPropertyChanged(nameof(IsValid));
        }
    }

}

// END-OF-FILE
