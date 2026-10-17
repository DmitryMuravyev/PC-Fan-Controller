////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// DTOs.cs
////////////////////////////////////////////////////////////////////////////////////////

using System.Diagnostics.CodeAnalysis;
using ProtoBuf;


namespace FanController.Contracts;

////////////////////////////////////////////////////////////////////////////////////////
// Wire format rules (protobuf-net)
//
// - Every serialized member has an explicit [ProtoMember(n)] number. Numbers are never
//   changed or reused: a new member gets a new number, a removed one leaves a gap.
// - Enum values are part of the wire format: they are explicit and never renumbered.
// - Default values (0, false, null) are not transmitted, and the receiver keeps its own
//   initializer for an omitted member. Initializers must therefore equal the default
//   value (or be an empty string / collection).
// - Any incompatible change increments ContractInfo.ApiVersion.


////////////////////////////////////////////////////////////////////////////////////////
// Inner Types
// All 'enum' values will be mapped into UI strings.

/// <summary>
/// Communication Protocol Operation Results
/// </summary>
public enum OperationResult
{
    OK                          = 0,    // OK
    NACK                        = 1,    // Command Not ACKnowledged
    Busy                        = 2,    // System is busy
    RequestScheduled            = 3,    // The request will be executed in the main thread (in this case, the main loop).
    RequestProcessing           = 4,    // The request has been previously received and is being processed.
    RequestDone                 = 5,    // User Action successfully done
    RequestError                = 6,    // Error processing User Action
    RequestUnknownResult        = 7,    // The request was processed, but the result of execution is unknown.

    SystemNotConfigured         = 8,    // No System Configuration available

    FanNotFound                 = 9,    // There is no fan with this index in the System
    FanNotConfigured            = 10,   // No Fan Configuration available

    SensorNotFound              = 11,   // There is no sensor with this SN in the System
    SensorNotConfigured         = 12,   // No Sensor Configuration available
    SensorAlreadyExistsError    = 13,   // The sensor being added already exists

    ConfigurationUpdateError    = 14,   // Failed to update configuration (for future use)
    FlashPageAlreadyStarted     = 15,   // The previously started Flash page must be closed before starting a new one
    FlashNoPageStarted          = 16    // The new configuration page in Flash has not been erased
}


/// <summary>
/// Client command kinds (the values match the DeviceCommand ProtoInclude numbers)
/// </summary>
public enum CommandKind
{
    None                        = 0,    // Not a client command
    SetActive                   = 1,
    FactoryReset                = 2,
    Reboot                      = 3,
    PrepareToHostShutdown       = 4,
    SetFullConfiguration        = 5,
    SetPartialConfiguration     = 6,
    SetSystemConfiguration      = 7,
    SetSystemBehavior           = 8,
    ResetFan                    = 9,
    RescanFanHardware           = 10,
    RescanAllFanHardware        = 11,
    RescanFanModel              = 12,
    RescanAllFanModels          = 13,
    SetFanConfiguration         = 14,
    SetFanControlMode           = 15,
    SetFanOperationMode         = 16,
    SetFanControlBasis          = 17,
    SetFanControlValue          = 18,
    SetFanSubstitutionMatrix    = 19,
    ResetSensor                 = 20,
    RescanSensors               = 21,
    SetSensorConfiguration      = 22,
    AddSensor                   = 23,
    DeleteSensor                = 24,
    DeleteSensors               = 25
}


/// <summary>
/// Display rotation
/// </summary>
public enum DisplayRotation
{
    Rotate0                     = 0,    // 0
    Rotate90                    = 1,    // 90
    Rotate180                   = 2,    // 180
    Rotate270                   = 3     // 270
}


/// <summary>
/// Buzzer Alarm Profiles
/// </summary>
public enum AlarmProfile : byte
{
    Mute                        = 0,
    AlarmsOnly                  = 1,
    Noisy                       = 2,
    Custom                      = 3
}


/// <summary>
/// Buzzer waveform options
/// </summary>
public enum BuzzerWaveform
{
    Cricket                     = 0,    // Cricket-like sound  (100Hz pulses of 1kHz)
    Loud                        = 1     // Loud mode (1kHz)
}


/// <summary>
/// Fan Control Mode
/// </summary>
public enum FanControlMode
{
    ControlDisabled             = 0,    // No fan connected or it is disabled by user
    ControlPWM                  = 1,    // PWM Control Mode
    ControlDCDC                 = 2,    // DC-DC Control Mode
    ControlONOFF                = 3     // ON/OFF (MOSFET or DC-DC Enable pin) Control Mode
}


/// <summary>
/// Fan operation mode
/// </summary>
public enum FanOperationMode
{
    OperationNormal             = 0,    // Fan controlled by temperature sensors
    OperationManual             = 1,    // Manual fan control
    OperationSilent             = 2,    // Minimum rotation speed
    OperationTurbo              = 3     // 100%
}


/// <summary>
/// Fan model size (Medium: reserved, not reported by the current firmware)
/// </summary>
public enum FanModelSize
{
    SizeUnknown                 = 0,    // Fan size not detected
    SizeSmall                   = 1,    // Small fan size (low inertia)
    SizeMedium                  = 2,    // Medium fan size (noticeable inertia)
    SizeLarge                   = 3     // Large fan size (strong inertia)
}


/// <summary>
/// Fan model power consumption (Medium: reserved, not reported by the current firmware)
/// </summary>
public enum FanModelPower
{
    PowerUnknown                = 0,    // Fan power not detected
    PowerLow                    = 1,    // Small power consumption fan (low current)
    PowerMedium                 = 2,    // Medium power consumption fan (noticeable current)
    PowerLarge                  = 3     // Large power consumption fan (large current)
}


/// <summary>
/// Fan model class (Micro: reserved, not reported by the current firmware)
/// </summary>
public enum FanModelClass
{
    ClassUnknown                = 0,    // Fan class undetected
    ClassMicro                  = 1,    // Micro family (low RPM, low power, 2/3 pins)
    ClassDesktop                = 2,    // Desktop family (low RPM, 2/3/4 pins)
    ClassServer                 = 3     // Server family (high RPM, 4 pins)
}


/// <summary>
/// Fan option spin power
/// </summary>
public enum FanOptionSpinPower
{
    SpinPower0                  = 0,    // The fan does not need to be spun up and rotates even when the Control Value is zero.
    SpinPower25                 = 1,    // Spin-up value = 1/4 of full Control Range * 1 = 25%
    SpinPower50                 = 2,    // Spin-up value = 1/4 of full Control Range * 2 = 50%
    SpinPower75                 = 3,    // Spin-up value = 1/4 of full Control Range * 3 = 75%
    SpinPower100                = 4     // Spin-up value = 1/4 of full Control Range * 4 = 100% (default)
}


/// <summary>
/// Fan option Rate Of Change
/// </summary>
public enum FanOptionRoc
{
    Roc1                        = 0,    // 655 seconds for the full range (100 calls per second)
    Roc2                        = 1,    // 328 seconds for the full range (100 calls per second)
    Roc4                        = 2,    // 164 seconds
    Roc8                        = 3,    // 82 seconds
    Roc16                       = 4,    // 41s
    Roc32                       = 5,    // 20.5s
    Roc64                       = 6,    // 10.2s
    Roc128                      = 7,    // 5.1s
    Roc256                      = 8,    // 2.56s
    Roc512                      = 9,    // 1.28s (default)
    Roc1024                     = 10,   // 0.64s
    Roc2048                     = 11,   // 0.32s
    Roc4096                     = 12,   // 0.16s
    Roc8192                     = 13,   // 0.08s
    Roc16384                    = 14,   // 0.04s
    RocDisabled                 = 15    // 0s (instantaneous change)
}


/// <summary>
/// Substitution Matrix coefficients.
/// The values match the firmware's 4-bit encoding (0..15 = 0%..150% in 10% steps).
/// </summary>
public enum SubstitutionCoefficient
{
    Unavailable                 = -1,   // Used to cross a fan with itself
    Coefficient0                = 0,    // 0%
    Coefficient10               = 1,    // 10%
    Coefficient20               = 2,    // 20%
    Coefficient30               = 3,    // 30%
    Coefficient40               = 4,    // 40%
    Coefficient50               = 5,    // 50%
    Coefficient60               = 6,    // 60%
    Coefficient70               = 7,    // 70%
    Coefficient80               = 8,    // 80%
    Coefficient90               = 9,    // 90%
    Coefficient100              = 10,   // 100%
    Coefficient110              = 11,   // 110%
    Coefficient120              = 12,   // 120%
    Coefficient130              = 13,   // 130%
    Coefficient140              = 14,   // 140%
    Coefficient150              = 15    // 150%
}


/// <summary>
/// Supported temperature sensor types
/// </summary>
public enum TSensorType
{
    Unknown                     = 0,    // The value will be used for the search

    // On-board sensors:
    MCU                         = 1,    // MCU internal temperature sensor
    DS1841                      = 2,    // DS1841 digiPOT internal sensor

    // Connectable sensors:
    DS18B20                     = 16,   // DS18B20 digital sensor (oneWire)

    // Software sensors:
    SW                          = 32,   // Some sensor, the readings of which are transmitted
                                        // by the control application via USB.
                                        // Other sensors:
    Other                       = 128   // For future use...
}


/// <summary>
/// Host Hardware types (for the host sensors)
/// </summary>
public enum HostHardwareType
{
    Unknown                     = 0,
    CPU                         = 1,
    Motherboard                 = 2,
    Memory                      = 3,
    GPU                         = 4,
    HDD                         = 5,
    Controller                  = 6,
    Network                     = 7,
    PSU                         = 8,
    Battery                     = 9,
    Cooler                      = 10,
    Sensor                      = 11
}


/// <summary>
/// Base message record type
/// (sent as notification messages from
/// FanController to FanControllerManager)
/// </summary>
public abstract record FanControllerMessage;


////////////////////////////////////////////////////////////////////////////////////////
// Snapshots of the physical device state

/// <summary>
/// Overall State snapshot (pushed on every change)
/// </summary>
[ProtoContract]
public sealed record OverallStateDto : FanControllerMessage
{
    [ProtoMember(1)] public required bool IsActive { get; init; } = false;
    [ProtoMember(2)] public required bool IsConnected { get; init; } = false;

    [SetsRequiredMembers]
    public OverallStateDto() { }
}


/// <summary>
/// Operation result answer snapshot
/// (sent in response to client commands)
/// </summary>
[ProtoContract]
public sealed record OperationResultDto : FanControllerMessage
{
    [ProtoMember(1)] public required OperationResult Result { get; init; }
    [ProtoMember(2)] public int? TargetObjectIndex { get; init; }
    [ProtoMember(3)] public byte[]? TargetObjectSN { get; init; }

    // The client command that produced this result
    [ProtoMember(4)] public Guid CommandId { get; init; }       // Guid.Empty: not correlated
    [ProtoMember(5)] public CommandKind Command { get; init; }

    // The command is finished (for a multi-step command: its last step)
    [ProtoMember(6)] public bool IsFinal { get; init; }
}


/// <summary>
/// System Behavior options snapshot.
/// Used in the System State snapshot,
/// the System Config snapshot, and
/// for the command to set the current system behavior.
/// </summary>
[ProtoContract]
public sealed record SystemBehaviorDto
{
    // Buzzer alarms/warnings mask
    [ProtoMember(1)] public required bool BuzzerFlagWarningBadSensor { get; init; }
    [ProtoMember(2)] public required bool BuzzerFlagWarningFanRPM { get; init; }
    [ProtoMember(3)] public required bool BuzzerFlagWarningFanPower { get; init; }
    [ProtoMember(4)] public required bool BuzzerFlagWarningTemperature { get; init; }
    [ProtoMember(5)] public required bool BuzzerFlagAlarmBadSensor { get; init; }
    [ProtoMember(6)] public required bool BuzzerFlagAlarmFanRPM { get; init; }
    [ProtoMember(7)] public required bool BuzzerFlagAlarmFanPower { get; init; }
    [ProtoMember(8)] public required bool BuzzerFlagAlarmPOTs { get; init; }
    [ProtoMember(9)] public required bool BuzzerFlagAlarmMAX { get; init; }
    [ProtoMember(10)] public required bool BuzzerFlagAlarmFanFailed { get; init; }
    [ProtoMember(11)] public required bool BuzzerFlagAlarmLowRAM { get; init; }
    [ProtoMember(12)] public required bool BuzzerFlagAlarmPower { get; init; }
    [ProtoMember(13)] public required bool BuzzerFlagAlarmTemperature { get; init; }
    [ProtoMember(14)] public required bool BuzzerFlagAlarmGeneralFailure { get; init; }

    // Config options
    [ProtoMember(15)] public required BuzzerWaveform ConfigBuzzerWaveform { get; init; }
    [ProtoMember(16)] public required bool ConfigFlagProfessionalMode { get; init; }
    [ProtoMember(17)] public required bool ConfigFlagFanModelDetection { get; init; }
    [ProtoMember(18)] public required bool ConfigFlagLockSubstitutionMatrix { get; init; }

    // Display parameters
    [ProtoMember(19)] public required double DisplayBrightness { get; init; }
    [ProtoMember(20)] public required DisplayRotation DisplayRotation { get; init; }

    [SetsRequiredMembers]
    public SystemBehaviorDto() { }
}


/// <summary>
/// System State snapshot (pushed on every update cycle)
/// </summary>
[ProtoContract]
public sealed record SystemStateDto : FanControllerMessage
{
    // System status flags
    [ProtoMember(1)] public required bool StatusNormal { get; init; }
    [ProtoMember(2)] public required bool StatusWarningBadSensor { get; init; }
    [ProtoMember(3)] public required bool StatusWarningFanRPM { get; init; }
    [ProtoMember(4)] public required bool StatusWarningFanPower { get; init; }
    [ProtoMember(5)] public required bool StatusWarningTemperature { get; init; }
    [ProtoMember(6)] public required bool StatusAlarmBadSensor { get; init; }
    [ProtoMember(7)] public required bool StatusAlarmFanRPM { get; init; }
    [ProtoMember(8)] public required bool StatusAlarmFanPower { get; init; }
    [ProtoMember(9)] public required bool StatusAlarmPOTs { get; init; }
    [ProtoMember(10)] public required bool StatusAlarmMAX { get; init; }
    [ProtoMember(11)] public required bool StatusAlarmFanFailed { get; init; }
    [ProtoMember(12)] public required bool StatusAlarmLowRam { get; init; }
    [ProtoMember(13)] public required bool StatusAlarmPower { get; init; }
    [ProtoMember(14)] public required bool StatusAlarmTemperature { get; init; }
    [ProtoMember(15)] public required bool StatusAlarmGeneralFailure { get; init; }
    [ProtoMember(16)] public required bool StatusCategoryWarnings { get; init; }
    [ProtoMember(17)] public required bool StatusCategoryAlarms { get; init; }

    // Configuration intervention flags
    [ProtoMember(18)] public required bool ConfigFlagNoConfig { get; init; }
    [ProtoMember(19)] public required bool ConfigFlagHWFailed { get; init; }
    [ProtoMember(20)] public required bool ConfigFlagPrewarmFailed { get; init; }
    [ProtoMember(21)] public required bool ConfigFlagFanFailed { get; init; }
    [ProtoMember(22)] public required bool ConfigFlagSensorFailed { get; init; }
    [ProtoMember(23)] public required bool ConfigFlagTooManySensorsDetected { get; init; }
    [ProtoMember(24)] public required bool ConfigFlagTooManySensorsStored { get; init; }
    [ProtoMember(25)] public required bool ConfigFlagSensorNotFound { get; init; }
    [ProtoMember(26)] public required bool ConfigFlagSensorConfigError { get; init; }
    [ProtoMember(27)] public required bool ConfigFlagFanConfigError { get; init; }
    [ProtoMember(28)] public required bool ConfigFlagSystemConfigError { get; init; }
    [ProtoMember(29)] public required bool ConfigFlagEEPROMFailure { get; init; }
    [ProtoMember(30)] public required bool ConfigFlagAllFanChannelsInvolved { get; init; }
    [ProtoMember(31)] public required bool ConfigFlagInitializationInProgress { get; init; }
    [ProtoMember(32)] public required bool ConfigFlagHWInProgress { get; init; }
    [ProtoMember(33)] public required bool ConfigFlagFanInProgress { get; init; }
    [ProtoMember(34)] public required bool ConfigFlagSensorInProgress { get; init; }
    [ProtoMember(35)] public required bool ConfigFlagAnyScanInProgress { get; init; }

    // System environment
    [ProtoMember(36)] public required int FreeRAM { get; init; }
    [ProtoMember(37)] public required double HighLoadPowerBusVoltage { get; init; }
    [ProtoMember(38)] public required double DigitalPowerBusVoltage { get; init; }
    [ProtoMember(39)] public required double McuPowerBusVoltage { get; init; }

    // User action requests
    [ProtoMember(40)] public required ushort UserActionRequest { get; init; }

    // User Action Flags
    [ProtoMember(41)] public required bool ActionFlagLastConfigRequestDone { get; init; }
    [ProtoMember(42)] public required bool ActionFlagLastConfigRequestError { get; init; }
    [ProtoMember(43)] public required bool ActionFlagConfigPageStarted { get; init; }
    [ProtoMember(44)] public required bool ActionFlagSystemConfigChanged { get; init; }
    [ProtoMember(45)] public required bool ActionFlagFanConfigChanged { get; init; }
    [ProtoMember(46)] public required bool ActionFlagSubstitutionMatrixChanged { get; init; }
    [ProtoMember(47)] public required bool ActionFlagSensorConfigChanged { get; init; }
    [ProtoMember(48)] public required bool ActionFlagConfigChanged { get; init; }

    // Scan progress as a percentage
    [ProtoMember(49)] public required byte ScanProgress { get; init; }

    // Last communication request result
    [ProtoMember(50)] public required OperationResult LastHostRequestErrorCode { get; init; }

    // System Behavior options snapshot
    [ProtoMember(51, IsRequired = true)] public required SystemBehaviorDto SystemBehavior { get; init; } = new SystemBehaviorDto();

    [SetsRequiredMembers]
    public SystemStateDto() { }
}


/// <summary>
/// System Config snapshot (pushed on every change)
/// </summary>
[ProtoContract]
public sealed record SystemConfigDto : FanControllerMessage
{
    /// <summary>
    /// Firmware version
    /// </summary>
    [ProtoContract]
    public sealed record FirmwareVersion
    {
        [ProtoMember(1)] public ushort Major { get; init; }
        [ProtoMember(2)] public ushort Minor { get; init; }

        public FirmwareVersion() { }

        public FirmwareVersion(ushort major, ushort minor)
        {
            Major = major;
            Minor = minor;
        }
    }

    [ProtoMember(1, IsRequired = true)] public required FirmwareVersion Version { get; init; } = new FirmwareVersion();

    // High Load Power Bus voltage (fan supply line)
    [ProtoMember(2)] public required double NormalHLPBVoltage { get; init; }

    // Firmware options
    [ProtoMember(3)] public required bool FirmwareFlagDisplayEnabled { get; init; }
    [ProtoMember(4)] public required bool FirmwareFlagUARTLoggerEnabled { get; init; }

    // System Behavior options snapshot
    [ProtoMember(5, IsRequired = true)] public required SystemBehaviorDto SystemBehavior { get; init; } = new SystemBehaviorDto();

    [SetsRequiredMembers]
    public SystemConfigDto() { }
}


/// <summary>
/// Fan Count snapshot (pushed on every change of fans list)
/// </summary>
[ProtoContract]
public sealed record FanCountDto : FanControllerMessage
{
    [ProtoMember(1)] public required int FanCount { get; init; }
}


/// <summary>
/// Fan State snapshot (pushed on every update cycle)
/// </summary>
[ProtoContract]
public sealed record FanStateDto : FanControllerMessage
{
    [ProtoMember(1)] public required int Index { get; init; }

    // Hardware errors
    [ProtoMember(2)] public required bool StatusHWErrorPWM { get; init; }
    [ProtoMember(3)] public required bool StatusHWErrorTACH { get; init; }
    [ProtoMember(4)] public required bool StatusHWErrorVoltmeter { get; init; }
    [ProtoMember(5)] public required bool StatusHWErrorMOSFET { get; init; }
    [ProtoMember(6)] public required bool StatusHWErrorDCDC { get; init; }

    // Runtime errors
    [ProtoMember(7)] public required bool StatusWarningPPM { get; init; }
    [ProtoMember(8)] public required bool StatusAlarmPPM { get; init; }
    [ProtoMember(9)] public required bool StatusWarningPWR { get; init; }
    [ProtoMember(10)] public required bool StatusAlarmPWR { get; init; }
    [ProtoMember(11)] public required bool StatusFailed { get; init; }

    // Service flags
    [ProtoMember(12)] public required bool StatusHalted { get; init; }
    [ProtoMember(13)] public required bool StatusSwitchedOff { get; init; }

    [ProtoMember(14)] public required bool StatusRunning { get; init; }
    [ProtoMember(15)] public required bool StatusSpinningUp { get; init; }
    [ProtoMember(16)] public required bool StatusSpinUpFailed { get; init; }
    [ProtoMember(17)] public required bool StatusPIDSuspended { get; init; }
    [ProtoMember(18)] public required bool StatusPIDActionRequired { get; init; }

    [ProtoMember(19)] public required bool StatusPreWarming { get; init; }
    [ProtoMember(20)] public required bool StatusWarming { get; init; }
    [ProtoMember(21)] public required bool StatusScanning { get; init; }
    [ProtoMember(22)] public required bool StatusFatalFailure { get; init; }

    [ProtoMember(23)] public required bool StatusHWErrors { get; init; }
    [ProtoMember(24)] public required bool StatusWarnings { get; init; }
    [ProtoMember(25)] public required bool StatusAlarms { get; init; }
    [ProtoMember(26)] public required bool StatusAnyProblems { get; init; }

    // Runtime parameters
    [ProtoMember(27)] public required uint TotalErrorsCount { get; init; }
    [ProtoMember(28)] public required uint CurrentPPM { get; init; }
    [ProtoMember(29)] public required double CurrentVoltage { get; init; }

    // Control parameters
    [ProtoMember(30)] public required FanControlMode ControlMode { get; init; }
    [ProtoMember(31)] public required FanOperationMode OperationMode { get; init; }
    [ProtoMember(32)] public required double ControlBasis { get; init; }
    [ProtoMember(33)] public required double ControlValue { get; init; }
    [ProtoMember(34)] public required double ScaledControl { get; init; }

    [SetsRequiredMembers]
    public FanStateDto() { }
}


/// <summary>
/// Fan Config snapshot (pushed on every change)
/// </summary>
[ProtoContract]
public sealed record FanConfigDto : FanControllerMessage
{
    [ProtoMember(1)] public required int Index { get; init; }

    // Fan name
    [ProtoMember(2, IsRequired = true)] public required string Name { get; init; } = String.Empty;

    // Hardware capabilities
    [ProtoMember(3)] public required bool HardwarePwmAvailable { get; init; }
    [ProtoMember(4)] public required bool HardwareRpmAvailable { get; init; }
    [ProtoMember(5)] public required bool HardwareVoltmeterAvailable { get; init; }
    [ProtoMember(6)] public required bool HardwareMosfetAvailable { get; init; }
    [ProtoMember(7)] public required bool HardwareDcdcAvailable { get; init; }

    // Connected fan's capabilities
    [ProtoMember(8)] public required bool ModelConnected { get; init; }
    [ProtoMember(9)] public required bool ModelTachometer { get; init; }
    [ProtoMember(10)] public required bool ModelPwm { get; init; }

    [ProtoMember(11)] public required bool ModelDcdcRangeDetected { get; init; }
    [ProtoMember(12)] public required bool ModelPwmRangeDetected { get; init; }
    [ProtoMember(13)] public required bool ModelPwmFreewheelTachometer { get; init; }
    [ProtoMember(14)] public required bool ModelPwmExcessiveFullSpeed { get; init; }

    [ProtoMember(15)] public required FanModelSize ModelSize { get; init; } = FanModelSize.SizeUnknown;
    [ProtoMember(16)] public required FanModelPower ModelPower { get; init; } = FanModelPower.PowerUnknown;
    [ProtoMember(17)] public required FanModelClass ModelClass { get; init; } = FanModelClass.ClassUnknown;

    // Fan options
    [ProtoMember(18)] public required int OptionPpr { get; init; }
    [ProtoMember(19)] public required FanOptionSpinPower OptionSpinPowerPwm { get; init; }
    [ProtoMember(20)] public required FanOptionSpinPower OptionSpinPowerPwr { get; init; }
    [ProtoMember(21)] public required FanOptionRoc OptionRoc { get; init; }

    [ProtoMember(22)] public required bool OptionPidEnabledPwm { get; init; }
    [ProtoMember(23)] public required bool OptionPidEnabledPwr { get; init; }
    [ProtoMember(24)] public required bool OptionPidSlowPwm { get; init; }
    [ProtoMember(25)] public required bool OptionPidSlowPwr { get; init; }
    [ProtoMember(26)] public required bool OptionAlwaysOn { get; init; }

    // Power HW metrics
    [ProtoMember(27)] public required double MaxVoltage { get; init; }
    [ProtoMember(28)] public required double MinVoltage { get; init; }
    [ProtoMember(29)] public required double MaxVoltageControl { get; init; }
    [ProtoMember(30)] public required double MinVoltageControl { get; init; }

    // Common Fan metrics
    [ProtoMember(31)] public required uint MaxPPM { get; init; }

    // PWM-controlled metrics
    [ProtoMember(32)] public required double MinPwmControl { get; init; }
    [ProtoMember(33)] public required uint PwmControlledMinPpm { get; init; }
    [ProtoMember(34)] public required uint PwmPenultimatePpm { get; init; }

    // DC-DC-controlled metrics
    [ProtoMember(35)] public required double MinPwrControl { get; init; }
    [ProtoMember(36)] public required uint PwrControlledMinPpm { get; init; }

    // Control settings
    [ProtoMember(37)] public required FanControlMode ControlMode { get; init; }
    [ProtoMember(38)] public required FanOperationMode OperationMode { get; init; }
    [ProtoMember(39)] public required double ControlBasis { get; init; }

    [SetsRequiredMembers]
    public FanConfigDto() { }
}


/// <summary>
/// Wraps one row of the Substitution Matrix
/// (protobuf-net does not support jagged arrays such as T[][])
/// </summary>
[ProtoContract]
public sealed record SubstitutionMatrixRow
{
    [ProtoMember(1, IsPacked = true)] public required SubstitutionCoefficient[] Coefficients { get; init; } = [];

    [SetsRequiredMembers]
    public SubstitutionMatrixRow() { }
}


/// <summary>
/// Substitution Matrix snapshot (pushed on every change)
/// </summary>
[ProtoContract]
public sealed record SubstitutionMatrixDto : FanControllerMessage
{
    // Flat list of row wrappers — protobuf-net can serialize this
    [ProtoMember(1)] public required SubstitutionMatrixRow[] Rows { get; init; } = [];

    [SetsRequiredMembers]
    public SubstitutionMatrixDto() { }
}


/// <summary>
/// Sensor Count snapshot (pushed on every change of sensors list)
/// </summary>
[ProtoContract]
public sealed record SensorCountDto : FanControllerMessage
{
    [ProtoMember(1)] public required int SensorCount { get; init; }
}


/// <summary>
/// Sensor State snapshot (pushed on every update cycle)
/// </summary>
[ProtoContract]
public sealed record SensorStateDto : FanControllerMessage
{
    [ProtoMember(1)] public required int Index { get; init; }

    // Status flags
    [ProtoMember(2)] public required bool StatusConnected { get; init; }
    [ProtoMember(3)] public required bool StatusConfigured { get; init; }
    [ProtoMember(4)] public required bool StatusValid { get; init; }
    [ProtoMember(5)] public required bool StatusError { get; init; }
    [ProtoMember(6)] public required bool StatusWarning { get; init; }
    [ProtoMember(7)] public required bool StatusAlarm { get; init; }
    [ProtoMember(8)] public required bool StatusAnyProblems { get; init; }

    // Sensor Temperature
    [ProtoMember(9)] public required double Temperature { get; init; }

    // Other Runtime parameters
    [ProtoMember(10)] public required uint TotalErrorsCount { get; init; }
    [ProtoMember(11, IsPacked = true)] public required bool[] OvercooledByFanMap { get; init; } = [];

    [SetsRequiredMembers]
    public SensorStateDto() { }
}


/// <summary>
/// Sensor Config snapshot (pushed on every change)
/// </summary>
[ProtoContract]
public sealed record SensorConfigDto : FanControllerMessage
{
    [ProtoMember(1)] public required int Index { get; init; }

    // Sensor identity
    [ProtoMember(2, IsRequired = true)] public required string Name { get; init; } = String.Empty;
    [ProtoMember(3)] public required byte[] SerialNumber { get; init; } = [];
    [ProtoMember(4)] public required TSensorType Type { get; init; }

    // Configuration
    [ProtoMember(5)] public required UInt32 Setting { get; init; }
    [ProtoMember(6)] public required int WarningThreshold { get; init; }
    [ProtoMember(7)] public required int AlarmThreshold { get; init; }

    [ProtoMember(8)] public required bool IsVisible { get; init; }
    [ProtoMember(9)] public required bool IsActive { get; init; }
    [ProtoMember(10)] public required bool IsDeletable { get; init; }
    [ProtoMember(11)] public required bool IsPermanent { get; init; }
    [ProtoMember(12)] public required bool IsIgnorable { get; init; }

    // Per-fan parameters
    [ProtoMember(13, IsPacked = true)] public required int[] LowThresholds { get; init; } = [];
    [ProtoMember(14, IsPacked = true)] public required int[] HighThresholds { get; init; } = [];
    [ProtoMember(15, IsPacked = true)] public required double[] FanImpact { get; init; } = [];
    [ProtoMember(16, IsPacked = true)] public required double[] UnavailableSensorFanImpact { get; init; } = [];

    [SetsRequiredMembers]
    public SensorConfigDto() { }
}


/// <summary>
/// Host Device snapshot (sent upon request).
/// Empty lists arrive as null.
/// </summary>
[ProtoContract]
public sealed record HostDeviceDto
{
    [ProtoMember(1)] public required string Name { get; init; }
    [ProtoMember(2)] public required string Info { get; init; }
    [ProtoMember(3)] public required HostHardwareType Type { get; init; }
    [ProtoMember(4)] public required HostDeviceDto[]? SubDevices { get; init; }
    [ProtoMember(5)] public required HostSensorDto[]? Sensors { get; init; }
}


/// <summary>
/// Host Sensor snapshot (sent upon request)
/// </summary>
[ProtoContract]
public sealed record HostSensorDto
{
    [ProtoMember(1)] public required byte[] Id { get; init; }
    [ProtoMember(2)] public required string Name { get; init; }
    [ProtoMember(3)] public required string Path { get; init; }
    [ProtoMember(4)] public required string Info { get; init; }
    [ProtoMember(5)] public required double Temperature { get; init; }
    [ProtoMember(6)] public required double? MaxTemperature { get; init; }
    [ProtoMember(7)] public required double? CritTemperature { get; init; }
    [ProtoMember(8)] public required bool IsValid { get; init; }
}


/// <summary>
/// Reduced Host Sensor snapshot
/// (temperature and validity only),
/// sent upon request.
/// </summary>
[ProtoContract]
public sealed record HostShortSensorDto
{
    [ProtoMember(1)] public required byte[] Id { get; init; }
    [ProtoMember(2)] public required double Temperature { get; init; }
    [ProtoMember(3)] public required bool IsValid { get; init; }
}


////////////////////////////////////////////////////////////////////////////////////////
// Client-Server Data Transfer Objects

/// <summary>
/// Lightweight identity + availability snapshot for device lists.
/// </summary>
[ProtoContract]
public sealed record DeviceSummary
{
    [ProtoMember(1)] public required string Id { get; init; }   // Serial number — unique key
    [ProtoMember(2)] public required string Name { get; init; }
    [ProtoMember(3)] public required string Version { get; init; }
    [ProtoMember(4)] public required bool IsAvailable { get; init; }
}


/// <summary>
/// Controller operation result
/// </summary>
[ProtoContract]
public sealed record DeviceOperationResult
{
    [ProtoMember(1)] public required string Id { get; init; }
    [ProtoMember(2)] public required OperationResultDto OperationResult { get; init; }
}


/// <summary>
/// Controller Overall State snapshot
/// </summary>
[ProtoContract]
public sealed record DeviceOverallState
{
    [ProtoMember(1)] public required string Id { get; init; }   // Serial number = unique key
    [ProtoMember(2)] public required OverallStateDto OverallState { get; init; }
}


/// <summary>
/// Full Controller Config snapshot
/// </summary>
[ProtoContract]
public sealed record DeviceFullConfig
{
    [ProtoMember(1)] public required SystemConfigDto SystemConfig { get; init; }
    [ProtoMember(2)] public required FanConfigDto[] Fans { get; init; }
    [ProtoMember(3)] public required SubstitutionMatrixDto SubstitutionMatrix { get; init; }
    [ProtoMember(4)] public required SensorConfigDto[] Sensors { get; init; }
}


/// <summary>
/// System State snapshot
/// </summary>
[ProtoContract]
public sealed record DeviceSystemState
{
    [ProtoMember(1)] public required string Id { get; init; }
    [ProtoMember(2)] public required SystemStateDto SystemState { get; init; }
}


/// <summary>
/// System Config snapshot
/// </summary>
[ProtoContract]
public sealed record DeviceSystemConfig
{
    [ProtoMember(1)] public required string Id { get; init; }
    [ProtoMember(2)] public required SystemConfigDto SystemConfig { get; init; }
}


/// <summary>
/// Fan Count
/// </summary>
[ProtoContract]
public sealed record DeviceFanCount
{
    [ProtoMember(1)] public required string Id { get; init; }
    [ProtoMember(2)] public required int FanCount { get; init; }
}


/// <summary>
/// Fan State snapshot
/// </summary>
[ProtoContract]
public sealed record DeviceFanState
{
    [ProtoMember(1)] public required string Id { get; init; }
    [ProtoMember(2)] public required FanStateDto FanState { get; init; }
}


/// <summary>
/// Fan Config snapshot
/// </summary>
[ProtoContract]
public sealed record DeviceFanConfig
{
    [ProtoMember(1)] public required string Id { get; init; }
    [ProtoMember(2)] public required int FanCount { get; init; }
    [ProtoMember(3)] public required FanConfigDto FanConfig { get; init; }
}


/// <summary>
/// Substitution Matrix snapshot
/// </summary>
[ProtoContract]
public sealed record DeviceFanSubstitutionMatrix
{
    [ProtoMember(1)] public required string Id { get; init; }
    [ProtoMember(2)] public required int FanCount { get; init; }
    [ProtoMember(3)] public required SubstitutionMatrixDto Matrix { get; init; }
}


/// <summary>
/// Sensor Count
/// </summary>
[ProtoContract]
public sealed record DeviceSensorCount
{
    [ProtoMember(1)] public required string Id { get; init; }
    [ProtoMember(2)] public required int SensorCount { get; init; }
}


/// <summary>
/// Sensor State snapshot
/// </summary>
[ProtoContract]
public sealed record DeviceSensorState
{
    [ProtoMember(1)] public required string Id { get; init; }
    [ProtoMember(2)] public required SensorStateDto SensorState { get; init; }
}


/// <summary>
/// Sensor Config
/// </summary>
[ProtoContract]
public sealed record DeviceSensorConfig
{
    [ProtoMember(1)] public required string Id { get; init; }
    [ProtoMember(2)] public required int SensorCount { get; init; }
    [ProtoMember(3)] public required SensorConfigDto SensorConfig { get; init; }
}

// END-OF-FILE
