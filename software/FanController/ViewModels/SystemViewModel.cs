////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SystemViewModel.cs
////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Grpc.Core;
using ProtoBuf.Grpc;
using FanController.Assets.Localization;
using FanController.Contracts;
using FanController.Controls;


namespace FanController.ViewModels;

/// <summary>
/// Substitution coefficient vector for a single fan:
/// describes how much each other fan can compensate if this fan fails.
/// </summary>
public partial class SubstitutionVector : ObservableObject
{
    // Fan index and its configured name (blank: not configured)
    private readonly int _index;
    private string? _configuredName;

    /// <summary>Display name: the configured fan name, or the localized default one.</summary>
    [ObservableProperty]
    private string _fanName = string.Empty;

    /// <summary>
    /// One coefficient per fan slot.
    /// The slot matching this fan's own index is always <see cref="SubstitutionCoefficient.Unavailable"/>
    /// because a fan cannot substitute itself.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SubstitutionCoefficient> _substitutionCoefficients = [];


    /// <summary>
    /// Initializes the vector from an existing coefficient array.
    /// </summary>
    public SubstitutionVector(string? name, int index, SubstitutionCoefficient[] coefficients)
    {
        _index = index;
        SetConfiguredName(name);

        // Use Length, not LINQ Count(), on arrays
        for (int i = 0; i < coefficients.Length; i++)
        {
            // A fan cannot substitute itself
            SubstitutionCoefficients.Add(i == index
                ? SubstitutionCoefficient.Unavailable
                : coefficients[i]);
        }
    }


    /// <summary>
    /// Initializes the vector with placeholder
    /// <see cref="SubstitutionCoefficient.Unavailable"/> values.
    /// Use <see cref="UpdateWithArray"/> to populate real coefficients later.
    /// </summary>
    public SubstitutionVector(string? name, int fanCount, int index)
    {
        _index = index;
        SetConfiguredName(name);

        // Fill with Unavailable placeholders until real data arrives
        for (int i = 0; i < fanCount; i++)
            SubstitutionCoefficients.Add(SubstitutionCoefficient.Unavailable);
    }


    /// <summary>
    /// Sets the configured fan name (blank: the default name is shown).
    /// </summary>
    public void SetConfiguredName(string? name)
    {
        _configuredName = name;
        RefreshFanName();
    }


    /// <summary>
    /// Rebuilds the display name (also after a language change).
    /// </summary>
    public void RefreshFanName() =>
        FanName = string.IsNullOrWhiteSpace(_configuredName)
            ? string.Format(Strings.FanDefaultNameFormat, _index)
            : _configuredName;


    /// <summary>
    /// Replaces all coefficients in-place from <paramref name="array"/>.
    /// Silently ignored if the array length does not match the vector length.
    /// </summary>
    public void UpdateWithArray(SubstitutionCoefficient[] array)
    {
        if (array.Length != SubstitutionCoefficients.Count) return;

        for (int i = 0; i < array.Length; i++)
            SubstitutionCoefficients[i] = array[i];
    }
}


/// <summary>
/// System Tab contents View Model
/// </summary>
public partial class SystemViewModel : ViewModelBase, IDisposable
{
    // Some constants
    public static class SystemViewConstants
    {
        // Minimum interval between slider-driven commands
        public const int CommandThrottleIntervalMs = 250;

        // Incoming snapshots don't overwrite a slider for this long after the user moved it
        public const long UserChangeHoldMs = 2000;
    }


    private volatile bool _disposed = false;

    // Serializes concurrent fan config / substitution matrix stream handlers
    private readonly SemaphoreSlim _snapshotLock = new(1, 1);

    // Protection against sending multiple commands
    // when moving the slider or toggling switches.
    private readonly ThrottledDebounce<SystemBehaviorDto> _behaviorThrottle;

    // Prevention of command transmission during device-initiated
    // state updates and alse from incoming state snapshots overwriting
    // controls while the user is actively editing (set before sending
    // a command, cleared on the next incoming snapshot).
    private SystemBehaviorDto _expectedBehavior = DtoDefaults.Controller.NewBehavior();
    private AlarmProfile _expectedAlarmProfile = DtoDefaults.Controller.DefaultAlarmProfile;
    private long _lastUserBehaviorChangeTick = 0;   // Environment.TickCount64


    /// <summary>
    /// Raised when the fan count changes and the substitution matrix
    /// columns must be rebuilt from scratch.
    /// </summary>
    public event Action? SubstitutionMatrixRecreateColumns;

    private int _fanCount = 0;
    public int FanCount => _fanCount;

    // Prevents the call to CheckSMUserChanges during
    // the update of the Substitution Matrix table.
    private bool _smHardwareUpdating = false;
    public bool SMHardwareUpdating => _smHardwareUpdating;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Observable state

    // Snapshots

    [ObservableProperty]
    private SystemConfigDto _systemConfigDto = DtoDefaults.Controller.NewConfig();

    [ObservableProperty]
    private SystemStateDto _systemStateDto = DtoDefaults.Controller.NewState();

    private SubstitutionMatrixDto _substitutionMatrixDto = DtoDefaults.SubstitutionMatrix.NewMatrix();


    // All available Display Rotation options for the ComboBox
    public IReadOnlyList<DisplayRotation> AllDisplayRotationOptions { get; } =
        Enum.GetValues<DisplayRotation>();

    // All available Buzzer Waveform options
    public IReadOnlyList<BuzzerWaveform> AllBuzzerWaveformOptions { get; } =
        Enum.GetValues<BuzzerWaveform>();

    // All available Buzzer Alarm options
    public IReadOnlyList<AlarmProfile> AllBuzzerAlarmProfiles { get; } =
        Enum.GetValues<AlarmProfile>();

    // All valid substitution coefficients
    //static private List<string> _coefficientLabels { get; } = Enumerable.Range(0, 16).Select(i => $"{i * 10}%").ToList();
    public IReadOnlyList<SubstitutionCoefficient> AllSubstitutionCoefficients { get; } = [
            SubstitutionCoefficient.Coefficient0,
            SubstitutionCoefficient.Coefficient10,
            SubstitutionCoefficient.Coefficient20,
            SubstitutionCoefficient.Coefficient30,
            SubstitutionCoefficient.Coefficient40,
            SubstitutionCoefficient.Coefficient50,
            SubstitutionCoefficient.Coefficient60,
            SubstitutionCoefficient.Coefficient70,
            SubstitutionCoefficient.Coefficient80,
            SubstitutionCoefficient.Coefficient90,
            SubstitutionCoefficient.Coefficient100,
            SubstitutionCoefficient.Coefficient110,
            SubstitutionCoefficient.Coefficient120,
            SubstitutionCoefficient.Coefficient130,
            SubstitutionCoefficient.Coefficient140,
            SubstitutionCoefficient.Coefficient150 ];


    // Normal High Load Power Bus Voltage
    [ObservableProperty]
    private DisplayRotation _displayRotation = DtoDefaults.Controller.DefaultDisplayRotation;

    // Normal High Load Power Bus Voltage
    [ObservableProperty]
    private double _displayBrightness = DtoDefaults.Controller.DefaultDisplayBrightness;

    // Normal High Load Power Bus Voltage
    [ObservableProperty]
    private BuzzerWaveform _buzzerWaveformOption = DtoDefaults.Controller.DefaultBuzzerWaveform;

    // Buzzer Alarm triggers template
    [ObservableProperty]
    private AlarmProfile _buzzerAlarmProfile = DtoDefaults.Controller.DefaultAlarmProfile;


    // Buzzer Alarm/Warning triggers
    [ObservableProperty] private bool _buzzerOptionWarningBadSensor = false;

    [ObservableProperty] private bool _buzzerOptionWarningFanRPM = false;

    [ObservableProperty] private bool _buzzerOptionWarningFanPower = false;

    [ObservableProperty] private bool _buzzerOptionWarningTemperature = false;

    [ObservableProperty] private bool _buzzerOptionAlarmBadSensor = false;

    [ObservableProperty] private bool _buzzerOptionAlarmFanRPM = false;

    [ObservableProperty] private bool _buzzerOptionAlarmFanPower = false;

    [ObservableProperty] private bool _buzzerOptionAlarmPOTs = false;

    [ObservableProperty] private bool _buzzerOptionAlarmMAX = false;

    [ObservableProperty] private bool _buzzerOptionAlarmFanFailed = false;

    [ObservableProperty] private bool _buzzerOptionAlarmLowRAM = false;

    [ObservableProperty] private bool _buzzerOptionAlarmPower = false;

    [ObservableProperty] private bool _buzzerOptionAlarmTemperature = false;

    [ObservableProperty] private bool _buzzerOptionAlarmGeneralFailure = false;


    // Normal High Load Power Bus Voltage
    [ObservableProperty]
    private bool _professionalModeOption = false;

    // Normal High Load Power Bus Voltage
    [ObservableProperty]
    private bool _fanModelDetectionOption = false;

    // Normal High Load Power Bus Voltage
    [ObservableProperty]
    private bool _lockSubstitutionMatrixOption = false;


    // Current controller firmware version
    [ObservableProperty]
    private string _firmwareVersion = String.Empty;

    // The controller firmware supports display
    [ObservableProperty]
    private bool _displayEnabled = false;

    // The controller firmware supports UART Logger.
    [ObservableProperty]
    private bool _loggerEnabled = false;

    // Normal High Load Power Bus Voltage
    [ObservableProperty]
    private double _normalHLPBVoltage = 0;


    // Substitution Matrix

    public ObservableCollection<SubstitutionVector> SubstitutionMatrix { get; } = new();

    /// <summary>
    /// True when the user has edited the substitution matrix but not yet saved.
    /// </summary>
    [ObservableProperty]
    private bool _isDirtySM = false;

    // Bitmask tracking which matrix cells differ from hardware.
    // Bit index = sourceFan * (FanCount - 1) + adjustedDestFan.
    // TODO: Replace with bool[] array when FanCount > 12 becomes possible (128-bit limit)
    private UInt128 _smDirtyMap = 0;


    ////////////////////////////////////////////////////////////////////////////////////////
    // Construction & disposal

    public SystemViewModel(CancellationToken parentToken)
        : base(parentToken)
    {
        _behaviorThrottle = new ThrottledDebounce<SystemBehaviorDto>(
            action: ApplyBehaviorAsync,
            interval: TimeSpan.FromMilliseconds(SystemViewConstants.CommandThrottleIntervalMs));

        _behaviorThrottle.OnError += ex =>
            Debug.WriteLine($"[SystemBehaviorDto] Apply failed: {ex.Message}");
    }


    // Disposal
    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            _behaviorThrottle.Dispose();
        }

        // Base disposes DeviceCts, ViewModelCts, etc.
        base.Dispose(disposing);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModelBase overrides

    protected override void ClearState()
    {
    }


    protected override void SwitchActiveDevice(
        string? newDeviceId,
        string? oldDeviceId)
    {
        if (newDeviceId is null || ActiveService is null) return;

        DeviceCts = CancellationTokenSource
            .CreateLinkedTokenSource(ViewModelCts.Token, ActiveService.DisposalToken);

        // Update throttle with the new device's token
        _behaviorThrottle.UpdateToken(DeviceCts.Token);

        Watch(() => WatchSystemConfigAsync(newDeviceId, DeviceCts.Token));
        Watch(() => WatchSystemStateAsync(newDeviceId, DeviceCts.Token));
        Watch(() => WatchFanConfigAsync(newDeviceId, DeviceCts.Token));
        Watch(() => WatchFanSubstitutionMatrixAsync(newDeviceId, DeviceCts.Token));
    }


    // The default fan names of the substitution matrix (rows and column headers) are localized
    protected override void OnCultureChanged()
    {
        foreach (var vector in SubstitutionMatrix)
            vector.RefreshFanName();
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Stream watchers

    private async Task WatchSystemConfigAsync(string deviceId,
        CancellationToken ct)
    {
        await foreach (var result in ActiveService!.WatchDeviceSystemConfigAsync(
                           new DeviceIdRequest { DeviceId = deviceId },
                           new CallContext(new CallOptions(cancellationToken: ct))))
        {
            if (SelectedDeviceId != result.Id) continue;

            SystemConfigDto = result.SystemConfig;

            FirmwareVersion = $"{SystemConfigDto.Version.Major}.{SystemConfigDto.Version.Minor}";

            /*
            // We'll use SystemStateDto instead
            DisplayBrightness = SystemConfigDto.SystemBehavior.DisplayBrightness;
            DisplayRotation = SystemConfigDto.SystemBehavior.DisplayRotation;

            ConfigFlagProfessionalMode = SystemConfigDto.SystemBehavior.ConfigFlagProfessionalMode;
            ConfigFlagFanModelDetection = SystemConfigDto.SystemBehavior.ConfigFlagFanModelDetection;
            ConfigFlagBuzzerLoudMode = SystemConfigDto.SystemBehavior.ConfigFlagBuzzerLoudMode;
            ConfigFlagLockSubstitutionMatrix = SystemConfigDto.SystemBehavior.ConfigFlagLockSubstitutionMatrix;
            */

            NormalHLPBVoltage = SystemConfigDto.NormalHLPBVoltage;

            DisplayEnabled = SystemConfigDto.FirmwareFlagDisplayEnabled;
            LoggerEnabled = SystemConfigDto.FirmwareFlagUARTLoggerEnabled;

            OnPropertyChanged(nameof(IsDirty));
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

            SystemStateDto = result.SystemState;

            // Update Control values
            if ((_expectedBehavior != SystemStateDto.SystemBehavior) &&
            ((Environment.TickCount64 - _lastUserBehaviorChangeTick) >= SystemViewConstants.UserChangeHoldMs))
            {
                _expectedBehavior = SystemStateDto.SystemBehavior;

                BuzzerOptionWarningBadSensor = SystemStateDto.SystemBehavior.BuzzerFlagWarningBadSensor;
                BuzzerOptionWarningFanRPM = SystemStateDto.SystemBehavior.BuzzerFlagWarningFanRPM;
                BuzzerOptionWarningFanPower = SystemStateDto.SystemBehavior.BuzzerFlagWarningFanPower;
                BuzzerOptionWarningTemperature = SystemStateDto.SystemBehavior.BuzzerFlagWarningTemperature;
                BuzzerOptionAlarmBadSensor = SystemStateDto.SystemBehavior.BuzzerFlagAlarmBadSensor;
                BuzzerOptionAlarmFanRPM = SystemStateDto.SystemBehavior.BuzzerFlagAlarmFanRPM;
                BuzzerOptionAlarmFanPower = SystemStateDto.SystemBehavior.BuzzerFlagAlarmFanPower;
                BuzzerOptionAlarmPOTs = SystemStateDto.SystemBehavior.BuzzerFlagAlarmPOTs;
                BuzzerOptionAlarmMAX = SystemStateDto.SystemBehavior.BuzzerFlagAlarmMAX;
                BuzzerOptionAlarmFanFailed = SystemStateDto.SystemBehavior.BuzzerFlagAlarmFanFailed;
                BuzzerOptionAlarmLowRAM = SystemStateDto.SystemBehavior.BuzzerFlagAlarmLowRAM;
                BuzzerOptionAlarmPower = SystemStateDto.SystemBehavior.BuzzerFlagAlarmPower;
                BuzzerOptionAlarmTemperature = SystemStateDto.SystemBehavior.BuzzerFlagAlarmTemperature;
                BuzzerOptionAlarmGeneralFailure = SystemStateDto.SystemBehavior.BuzzerFlagAlarmGeneralFailure;

                _expectedAlarmProfile = FindAlarmProfile();
                if (BuzzerAlarmProfile != AlarmProfile.Custom) BuzzerAlarmProfile = _expectedAlarmProfile;

                DisplayBrightness = SystemStateDto.SystemBehavior.DisplayBrightness;
                DisplayRotation = SystemStateDto.SystemBehavior.DisplayRotation;

                ProfessionalModeOption = SystemStateDto.SystemBehavior.ConfigFlagProfessionalMode;
                FanModelDetectionOption = SystemStateDto.SystemBehavior.ConfigFlagFanModelDetection;
                BuzzerWaveformOption = SystemStateDto.SystemBehavior.ConfigBuzzerWaveform;
                LockSubstitutionMatrixOption = SystemStateDto.SystemBehavior.ConfigFlagLockSubstitutionMatrix;

                OnPropertyChanged(nameof(IsDirty));
            }
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
                CheckFanCount(result.FanCount);

                if (result.FanConfig.Index < _fanCount)
                {
                    SubstitutionMatrix[result.FanConfig.Index].SetConfiguredName(result.FanConfig.Name);
                }
            }
            finally { _snapshotLock.Release(); }
        }
    }


    private async Task WatchFanSubstitutionMatrixAsync(string deviceId,
        CancellationToken ct)
    {
        await foreach (var result in ActiveService!.WatchDeviceFanSubstitutionMatrixAsync(
                           new DeviceIdRequest { DeviceId = deviceId },
                           new CallContext(new CallOptions(cancellationToken: ct))))
        {
            if (SelectedDeviceId != result.Id) continue;

            await _snapshotLock.WaitAsync(ct);
            try
            {
                CheckFanCount(result.FanCount);

                _smHardwareUpdating = true;
                _substitutionMatrixDto = result.Matrix;
                try
                {
                    for (int i = 0; i < _fanCount; i++)
                    {
                        SubstitutionMatrix[i].UpdateWithArray(result.Matrix.Rows[i].Coefficients);
                    }
                }
                finally
                {
                    // We use a short delay to allow the events (On...Changed) to get through.
                    Dispatcher.UIThread.Post(() => _smHardwareUpdating = false, DispatcherPriority.Loaded);
                    IsDirtySM = false;
                    _smDirtyMap = 0;
                }
            }
            finally { _snapshotLock.Release(); }
        }
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // ViewModel logic

    private void CheckFanCount(int fanCount)
    {
        // Update the list of fans
        if (_fanCount != fanCount)
        {
            _fanCount = fanCount;
            SubstitutionMatrix.Clear();
            for (int i = 0; i < _fanCount; i++)
            {
                SubstitutionMatrix.Add(new SubstitutionVector(null, _fanCount, i));
            }

            SubstitutionMatrixRecreateColumns?.Invoke();
        }
    }


    /// <summary>
    /// Called after the user edits a cell in the substitution matrix.
    /// Updates <see cref="IsDirtySM"/> based on whether the cell differs from the hardware value.
    /// </summary>
    public void CheckSMUserChanges(int sourceFan, int destinationFan)
    {
        bool equals = SubstitutionMatrix[sourceFan].SubstitutionCoefficients[destinationFan] ==
            _substitutionMatrixDto.Rows[sourceFan].Coefficients[destinationFan];

        if (sourceFan == destinationFan)
        {
            return;
        }
        else if (destinationFan > sourceFan)
        {
            destinationFan--;
        }
        int shift = sourceFan * (_fanCount - 1) + destinationFan;

        if (shift > 127)
        {
            IsDirtySM = true;
            return;
        }

        if (equals)
        {
            // TODO: Replace with an array
            _smDirtyMap &= ~(UInt128)(1 << shift);
        }
        else
        {
            // TODO: Replace with an array
            _smDirtyMap |= (UInt128)(1 << shift);
        }

        IsDirtySM = (_smDirtyMap != 0);
    }


    /// <summary>
    /// DTO construction
    /// </summary>
    /// <returns></returns>
    private SystemBehaviorDto GetSystemBehaviorDto()
    {
        return new SystemBehaviorDto
        {
            BuzzerFlagWarningBadSensor = BuzzerOptionWarningBadSensor,
            BuzzerFlagWarningFanRPM = BuzzerOptionWarningFanRPM,
            BuzzerFlagWarningFanPower = BuzzerOptionWarningFanPower,
            BuzzerFlagWarningTemperature = BuzzerOptionWarningTemperature,
            BuzzerFlagAlarmBadSensor = BuzzerOptionAlarmBadSensor,
            BuzzerFlagAlarmFanRPM = BuzzerOptionAlarmFanRPM,
            BuzzerFlagAlarmFanPower = BuzzerOptionAlarmFanPower,
            BuzzerFlagAlarmPOTs = BuzzerOptionAlarmPOTs,
            BuzzerFlagAlarmMAX = BuzzerOptionAlarmMAX,
            BuzzerFlagAlarmFanFailed = BuzzerOptionAlarmFanFailed,
            BuzzerFlagAlarmLowRAM = BuzzerOptionAlarmLowRAM,
            BuzzerFlagAlarmPower = BuzzerOptionAlarmPower,
            BuzzerFlagAlarmTemperature = BuzzerOptionAlarmTemperature,
            BuzzerFlagAlarmGeneralFailure = BuzzerOptionAlarmGeneralFailure,

            ConfigBuzzerWaveform = BuzzerWaveformOption,
            ConfigFlagProfessionalMode = ProfessionalModeOption,
            ConfigFlagFanModelDetection = FanModelDetectionOption,
            ConfigFlagLockSubstitutionMatrix = LockSubstitutionMatrixOption,

            DisplayBrightness = DisplayBrightness,
            DisplayRotation = DisplayRotation
        };
    }


    /// <summary>
    /// Throttled behavior apply
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    private async Task ApplyBehaviorAsync(SystemBehaviorDto dto, CancellationToken token)
    {
        // Snapshot was already captured on the UI thread in Signal(); only
        // SelectedDeviceId/ActiveService still need to be read there.
        string? deviceId = null;
        IDeviceService? service = null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            deviceId = SelectedDeviceId;
            service = ActiveService;
            _lastUserBehaviorChangeTick = Environment.TickCount64; // Refresh last update tick
            OnPropertyChanged(nameof(IsDirty));
        });

        if ((deviceId is null) || (service is null) || (dto is null)) return;

        // Service call — on thread pool
        await TryExecuteAsync(() => service.ExecuteCommandAsync(
            new ExecuteCommandRequest
            {
                DeviceId = deviceId,
                Command = new SetSystemBehaviorCommand { Behavior = dto }
            },
            new CallContext(new CallOptions(cancellationToken: token))));
    }


    /// <summary>
    /// Infers the alarm preset that matches the current individual flag state.
    /// Returns <see cref="AlarmProfile.Custom"/> if no preset matches.
    /// </summary>
    private AlarmProfile FindAlarmProfile()
    {
        // Calculate warnings
        int warnings = 0;
        warnings |= (BuzzerOptionWarningBadSensor ? 1 : 0);
        warnings <<= 1;
        warnings |= (BuzzerOptionWarningFanRPM ? 1 : 0);
        warnings <<= 1;
        warnings |= (BuzzerOptionWarningFanPower ? 1 : 0);
        warnings <<= 1;
        warnings |= (BuzzerOptionWarningTemperature ? 1 : 0);

        bool allWarningsOn = (warnings == 0b1111);
        bool allWarningsOff = (warnings == 0);

        // Calculate alarms
        int alarms = 0;
        alarms |= (BuzzerOptionAlarmBadSensor ? 1 : 0);
        alarms <<= 1;
        alarms |= (BuzzerOptionAlarmFanRPM ? 1 : 0);
        alarms <<= 1;
        alarms |= (BuzzerOptionAlarmFanPower ? 1 : 0);
        alarms <<= 1;
        alarms |= (BuzzerOptionAlarmPOTs ? 1 : 0);
        alarms <<= 1;
        alarms |= (BuzzerOptionAlarmMAX ? 1 : 0);
        alarms <<= 1;
        alarms |= (BuzzerOptionAlarmFanFailed ? 1 : 0);
        alarms <<= 1;
        alarms |= (BuzzerOptionAlarmLowRAM ? 1 : 0);
        alarms <<= 1;
        alarms |= (BuzzerOptionAlarmPower ? 1 : 0);
        alarms <<= 1;
        alarms |= (BuzzerOptionAlarmTemperature ? 1 : 0);
        alarms <<= 1;
        alarms |= (BuzzerOptionAlarmGeneralFailure ? 1 : 0);

        bool allAlarmsOn = (alarms == 0b1111111111);
        bool allAlarmsOff = (alarms == 0);

        // Logic
        if (allAlarmsOn && allWarningsOn) return AlarmProfile.Noisy;
        if (allAlarmsOn && allWarningsOff) return AlarmProfile.AlarmsOnly;
        if (allAlarmsOff && allWarningsOff) return AlarmProfile.Mute;
        return (AlarmProfile.Custom);
    }


    /// <summary>
    /// True when any behavior setting differs from the last saved device config.
    /// </summary>
    public bool IsDirty =>
        (DisplayRotation != SystemConfigDto.SystemBehavior.DisplayRotation) ||
        (DisplayBrightness != SystemConfigDto.SystemBehavior.DisplayBrightness) ||
        (ProfessionalModeOption != SystemConfigDto.SystemBehavior.ConfigFlagProfessionalMode) ||
        (FanModelDetectionOption != SystemConfigDto.SystemBehavior.ConfigFlagFanModelDetection) ||
        (BuzzerWaveformOption != SystemConfigDto.SystemBehavior.ConfigBuzzerWaveform) ||
        (LockSubstitutionMatrixOption != SystemConfigDto.SystemBehavior.ConfigFlagLockSubstitutionMatrix) ||
        (BuzzerOptionWarningBadSensor != SystemConfigDto.SystemBehavior.BuzzerFlagWarningBadSensor) ||
        (BuzzerOptionWarningFanRPM != SystemConfigDto.SystemBehavior.BuzzerFlagWarningFanRPM) ||
        (BuzzerOptionWarningFanPower != SystemConfigDto.SystemBehavior.BuzzerFlagWarningFanPower) ||
        (BuzzerOptionWarningTemperature != SystemConfigDto.SystemBehavior.BuzzerFlagWarningTemperature) ||
        (BuzzerOptionAlarmBadSensor != SystemConfigDto.SystemBehavior.BuzzerFlagAlarmBadSensor) ||
        (BuzzerOptionAlarmFanRPM != SystemConfigDto.SystemBehavior.BuzzerFlagAlarmFanRPM) ||
        (BuzzerOptionAlarmFanPower != SystemConfigDto.SystemBehavior.BuzzerFlagAlarmFanPower) ||
        (BuzzerOptionAlarmPOTs != SystemConfigDto.SystemBehavior.BuzzerFlagAlarmPOTs) ||
        (BuzzerOptionAlarmMAX != SystemConfigDto.SystemBehavior.BuzzerFlagAlarmMAX) ||
        (BuzzerOptionAlarmFanFailed != SystemConfigDto.SystemBehavior.BuzzerFlagAlarmFanFailed) ||
        (BuzzerOptionAlarmLowRAM != SystemConfigDto.SystemBehavior.BuzzerFlagAlarmLowRAM) ||
        (BuzzerOptionAlarmPower != SystemConfigDto.SystemBehavior.BuzzerFlagAlarmPower) ||
        (BuzzerOptionAlarmTemperature != SystemConfigDto.SystemBehavior.BuzzerFlagAlarmTemperature) ||
        (BuzzerOptionAlarmGeneralFailure != SystemConfigDto.SystemBehavior.BuzzerFlagAlarmGeneralFailure);


    ////////////////////////////////////////////////////////////////////////////////////////
    // Device commands

    public Task<bool> UpdateConfiguration()
    {
        if ((SelectedDeviceId is null) || (ActiveService is null))
            return Task.FromResult(false);

        _lastUserBehaviorChangeTick = Environment.TickCount64; // Refresh last update tick
        OnPropertyChanged(nameof(IsDirty));

        // We don't have to use DtoDefaults.System.NewConfig here,
        // but let's keep that in mind for the future.
        SystemConfigDto config = new SystemConfigDto
        {
            // We can skip this block because it will be
            // overriden by the firmware/hardware values.
            // But let's leave it just for a case.
            Version = SystemConfigDto.Version,
            NormalHLPBVoltage = SystemConfigDto.NormalHLPBVoltage,
            FirmwareFlagDisplayEnabled = SystemConfigDto.FirmwareFlagDisplayEnabled,
            FirmwareFlagUARTLoggerEnabled = SystemConfigDto.FirmwareFlagUARTLoggerEnabled,

            SystemBehavior = GetSystemBehaviorDto()
        };

        // To conserve flash memory lifespan, let's write
        // the full configuration at once, rather than
        // splitting the process into two stages:
        // one for the system configuration
        // and one for the substitution matrix.
        /*
        await TryExecuteAsync(() => ActiveService.ExecuteCommandAsync(
            new ExecuteCommandRequest
            {
                DeviceId = SelectedDeviceId,
                Command = new SetSystemConfigurationCommand
                {
                    Config = config
                }
            },
            new CallContext(new CallOptions(
                cancellationToken: ViewModelCts.Token)));
       */

        SubstitutionMatrixDto? substitutionMatrix = null;
        if (IsDirtySM)
        {
            substitutionMatrix = new SubstitutionMatrixDto
            {
                Rows = Enumerable.Range(0, _fanCount)
                .Select(i => new SubstitutionMatrixRow
                {
                    Coefficients = SubstitutionMatrix[i]
                        .SubstitutionCoefficients
                        .ToArray()
                })
                .ToArray()
            };

            // The same applies here:
            // let's conserve flash memory lifespan.
            /*
            await TryExecuteAsync(() => ActiveService.ExecuteCommandAsync(
                new ExecuteCommandRequest
                {
                    DeviceId = SelectedDeviceId,
                    Command = new SetFanSubstitutionMatrixCommand
                    {
                        Matrix = substitutionMatrix
                    }
                },
                new CallContext(new CallOptions(
                    cancellationToken: ViewModelCts.Token)));
            */
        }

        // FanController.Service will automatically fill in
        // the remaining configuration DTOs and send
        // the complete configuration to the device.
        return SendTrackedCommandAsync(
            new SetPartialConfigurationCommand
            {
                SystemConfig = config,
                FanConfigs = null,
                Matrix = substitutionMatrix,
                SensorConfigs = null
            },
            CommandTimeouts.Configuration);
    }


    public Task<DeviceFullConfig?> GetFullConfiguration()
    {
        if (SelectedDeviceId is null || ActiveService is null)
            return Task.FromResult<DeviceFullConfig?>(null);

        return TryExecuteAsync<DeviceFullConfig?>(
            async () => await ActiveService.GetDeviceFullConfigAsync(
                new DeviceIdRequest { DeviceId = SelectedDeviceId },
                new CallContext(new CallOptions(cancellationToken: ViewModelCts.Token))),
            fallback: null);
    }


    public Task<bool> UploadFullConfiguration(DeviceFullConfig config) =>
        SendTrackedCommandAsync(
            new SetFullConfigurationCommand { Config = config },
            CommandTimeouts.Configuration);


    public Task<bool> FactoryResetController() =>
        SendTrackedCommandAsync(new FactoryResetCommand(), CommandTimeouts.Restart);


    public Task<bool> RebootController() =>
        SendTrackedCommandAsync(new RebootCommand(), CommandTimeouts.Restart);


    ////////////////////////////////////////////////////////////////////////////////////////
    // Property changed handlers

    private void OnSomePropertyChanged()
    {
        _lastUserBehaviorChangeTick = Environment.TickCount64; // Refresh last update tick
        OnPropertyChanged(nameof(IsDirty));
        _behaviorThrottle.Signal(GetSystemBehaviorDto());
    }


    partial void OnDisplayRotationChanged(DisplayRotation value)
    {
        if (value != _expectedBehavior.DisplayRotation)
        {
            _expectedBehavior = _expectedBehavior with
            {
                DisplayRotation = value
            };
            OnSomePropertyChanged();
        }
    }

    partial void OnDisplayBrightnessChanged(double value)
    {
        if (value != _expectedBehavior.DisplayBrightness)
        {
            _expectedBehavior = _expectedBehavior with
            {
                DisplayBrightness = value
            };
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerWaveformOptionChanged(BuzzerWaveform value)
    {
        if (value != _expectedBehavior.ConfigBuzzerWaveform)
        {
            _expectedBehavior = _expectedBehavior with
            {
                ConfigBuzzerWaveform = value
            };
            OnSomePropertyChanged();
        }
    }


    partial void OnBuzzerAlarmProfileChanged(AlarmProfile value)
    {
        if ((value != _expectedAlarmProfile) &&
             (value != AlarmProfile.Custom))
        {
            _expectedAlarmProfile = value;

            bool warningsEnabled = (value == AlarmProfile.Noisy);
            bool alarmsEnabled = (warningsEnabled || (value == AlarmProfile.AlarmsOnly));
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagWarningBadSensor = warningsEnabled,
                BuzzerFlagWarningFanRPM = warningsEnabled,
                BuzzerFlagWarningFanPower = warningsEnabled,
                BuzzerFlagWarningTemperature = warningsEnabled,
                BuzzerFlagAlarmBadSensor = alarmsEnabled,
                BuzzerFlagAlarmFanRPM = alarmsEnabled,
                BuzzerFlagAlarmFanPower = alarmsEnabled,
                BuzzerFlagAlarmPOTs = alarmsEnabled,
                BuzzerFlagAlarmMAX = alarmsEnabled,
                BuzzerFlagAlarmFanFailed = alarmsEnabled,
                BuzzerFlagAlarmLowRAM = alarmsEnabled,
                BuzzerFlagAlarmPower = alarmsEnabled,
                BuzzerFlagAlarmTemperature = alarmsEnabled,
                BuzzerFlagAlarmGeneralFailure = alarmsEnabled
            };

            BuzzerOptionWarningBadSensor = warningsEnabled;
            BuzzerOptionWarningFanRPM = warningsEnabled;
            BuzzerOptionWarningFanPower = warningsEnabled;
            BuzzerOptionWarningTemperature = warningsEnabled;
            BuzzerOptionAlarmBadSensor = alarmsEnabled;
            BuzzerOptionAlarmFanRPM = alarmsEnabled;
            BuzzerOptionAlarmFanPower = alarmsEnabled;
            BuzzerOptionAlarmPOTs = alarmsEnabled;
            BuzzerOptionAlarmMAX = alarmsEnabled;
            BuzzerOptionAlarmFanFailed = alarmsEnabled;
            BuzzerOptionAlarmLowRAM = alarmsEnabled;
            BuzzerOptionAlarmPower = alarmsEnabled;
            BuzzerOptionAlarmTemperature = alarmsEnabled;
            BuzzerOptionAlarmGeneralFailure = alarmsEnabled;

            OnSomePropertyChanged();
        }
    }


    //////////////////////////
    // Buzzer flags

    partial void OnBuzzerOptionWarningBadSensorChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagWarningBadSensor)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagWarningBadSensor = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionWarningFanRPMChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagWarningFanRPM)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagWarningFanRPM = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionWarningFanPowerChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagWarningFanPower)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagWarningFanPower = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionWarningTemperatureChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagWarningTemperature)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagWarningTemperature = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionAlarmBadSensorChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagAlarmBadSensor)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagAlarmBadSensor = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionAlarmFanRPMChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagAlarmFanRPM)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagAlarmFanRPM = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionAlarmFanPowerChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagAlarmFanPower)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagAlarmFanPower = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionAlarmPOTsChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagAlarmPOTs)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagAlarmPOTs = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionAlarmMAXChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagAlarmMAX)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagAlarmMAX = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionAlarmFanFailedChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagAlarmFanFailed)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagAlarmFanFailed = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionAlarmLowRAMChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagAlarmLowRAM)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagAlarmLowRAM = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionAlarmPowerChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagAlarmPower)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagAlarmPower = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionAlarmTemperatureChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagAlarmTemperature)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagAlarmTemperature = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }

    partial void OnBuzzerOptionAlarmGeneralFailureChanged(bool value)
    {
        if (value != _expectedBehavior.BuzzerFlagAlarmGeneralFailure)
        {
            _expectedBehavior = _expectedBehavior with
            {
                BuzzerFlagAlarmGeneralFailure = value
            };

            _expectedAlarmProfile = FindAlarmProfile();
            OnSomePropertyChanged();
        }
    }


    //////////////////////////
    // Other flags

    partial void OnProfessionalModeOptionChanged(bool value)
    {
        if (value != _expectedBehavior.ConfigFlagProfessionalMode)
        {
            _expectedBehavior = _expectedBehavior with
            {
                ConfigFlagProfessionalMode = value
            };
            OnSomePropertyChanged();
        }
    }

    partial void OnFanModelDetectionOptionChanged(bool value)
    {
        if (value != _expectedBehavior.ConfigFlagFanModelDetection)
        {
            _expectedBehavior = _expectedBehavior with
            {
                ConfigFlagFanModelDetection = value
            };
            OnSomePropertyChanged();
        }
    }

    partial void OnLockSubstitutionMatrixOptionChanged(bool value)
    {
        if (value != _expectedBehavior.ConfigFlagLockSubstitutionMatrix)
        {
            _expectedBehavior = _expectedBehavior with
            {
                ConfigFlagLockSubstitutionMatrix = value
            };
            OnSomePropertyChanged();
        }
    }

}

// END-OF-FILE