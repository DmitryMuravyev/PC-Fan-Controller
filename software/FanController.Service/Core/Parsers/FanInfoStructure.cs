////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// FanInfoStructure.cs
////////////////////////////////////////////////////////////////////////////////////////

namespace FanController.Service.Core.Parsers;

//
// Types and flags:
//

/// <summary>
/// Fan status
/// </summary>
public enum Fan_Status : UInt32
{
    // Hardware errors
    Fan_Status_PWM_HW_Error         = 0b00000000000000000000000000000001,   // PWM operation/connection error
    Fan_Status_TACH_HW_Error        = 0b00000000000000000000000000000010,   // PPM measurement operation/connection error
    Fan_Status_Voltmeter_HW_Error   = 0b00000000000000000000000000000100,   // Voltage measurement operation/connection error
    Fan_Status_MOSFET_HW_Error      = 0b00000000000000000000000000001000,   // Fan power On/Off GPIO error
    Fan_Status_DCDC_HW_Error        = 0b00000000000000000000000000010000,   // DigiPOT or DAC connection error

    // Runtime errors
    Fan_Status_PPM_Warning          = 0b00000000000000000000010000000000,   // Fan PPM differs from the expected by more than 1/4
    Fan_Status_PPM_Alarm            = 0b00000000000000000000100000000000,   // Fan PPM differs from the expected by more than 1/2
    Fan_Status_PWR_Warning          = 0b00000000000000000001000000000000,   // Fan power voltage differs from the expected by more than 1/4
    Fan_Status_PWR_Alarm            = 0b00000000000000000010000000000000,   // Fan power voltage differs from the expected by more than 1/2
    Fan_Status_Failed               = 0b00000000000000000100000000000000,   // The fan could not be started or it stopped completely

    // Service flags
    Fan_Status_Halted               = 0b00000000000001000000000000000000,   // Temporary halt the fan (e.g. if the sensors are overcooled)
    Fan_Status_Switched_Off         = 0b00000000000010000000000000000000,   // The Control signals are set to minimum values (used
                                                                    // to turn off the power of an unplugged fan).
    Fan_Status_Running              = 0b00000000000100000000000000000000,   // Fan isn't disabled or stopped
    Fan_Status_Spinning_Up          = 0b00000000001000000000000000000000,   // Fan spinning up is in progress
    Fan_Status_Spin_Up_Failed       = 0b00000000010000000000000000000000,   // Spin-up attempt failed
    Fan_Status_PID_Suspended        = 0b00000000100000000000000000000000,   // The PID controller is suspended due to significant change in Control Value
    Fan_Status_PID_Action_Required  = 0b00000001000000000000000000000000,   // The Target values have changed and we need a PID reaction
                                                                           // to start moving towards the target PPM.
    Fan_Status_PreWarming           = 0b00000010000000000000000000000000,   // Fan Pre-Warming is in progress
    Fan_Status_Warming              = 0b00000100000000000000000000000000,   // Fan Warming is in progress
    Fan_Status_Scanning             = 0b00001000000000000000000000000000,   // Fan scanning (hardware or model) is in progress
    Fan_Status_Fatal_Failure        = 0b00010000000000000000000000000000,   // Indicates that the fan failure cannot be fixed except by
                                                                     // resetting the environment, restarting the device, or re-scanning the fan.
    Fan_Status_HW_Errors_Mask       = 0b00000000000000000000000000011111,   // Hardware errors mask
    Fan_Status_Warnings_Mask        = 0b00000000000000000001010000000000,   // Warnings mask
    Fan_Status_Alarms_Mask          = 0b00000000000000000110100000000000,   // Alarms mask
    Fan_Status_Any_Problems_Mask    = Fan_Status_HW_Errors_Mask | Fan_Status_Warnings_Mask | Fan_Status_Alarms_Mask | Fan_Status_Fatal_Failure
}


/*
//
// Prototype declaration:
//

// Current Fan state
typedef struct __attribute__((packed, aligned(4)))
{
	// Status
	uint32_t status;                // Status bits
    uint32_t totalErrorsCount;      // Total errors (HW errors, fan alarms and fan warnings) count
    uint32_t currentPPM;            // Measured fan's Pulses Per Minute
    uint16_t currentVoltage;        // Measured fan's power voltage

    // Control parameters initialized from the configuration
    uint16_t controlBasis;          // The basis level of fan's power.
    uint8_t controlMode;            // Hardware Control mode: PWM, DC-DC, or power MOSFET ON/OFF
    uint8_t operationMode;          // Fan operation mode: Sensor controlled, Silent, Turbo or Manual.

    // Runtime parameters
    uint16_t controlValue;          // The basis on which more accurate Control Values will be calculated.

    uint16_t scaledControl;         // A Control Value scaled to the current fan Control basis
                                    // (actually to the selected fan power range).
} Fan_Info;     // 4 + 4 + 4 + 2 + 2 + 1 + 1 + 2 + 2 + 2 (padding) = 24 bytes

*/


/// <summary>
/// The structure of the fan's state
/// </summary>
public ref struct FanInfoStructure
{
    /// <summary>
    /// Packed structure size
    /// </summary>
    public const int Size = 24;

    // Packed structure data
    private Span<byte> _dataSpan;


    /// <summary>
    /// Wraps the raw structure data.
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public FanInfoStructure(Span<byte> data)
    {
        PackedStructureMethods.EnsureSize(data, Size, nameof(FanInfoStructure));
        _dataSpan = data;
    }


    /// <summary>
    /// Status bits (see <see cref="Fan_Status"/>)
    /// </summary>
    public UInt32 Status => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 0);


    /// <summary>
    /// Total errors (HW errors, fan alarms and fan warnings) count
    /// </summary>
    public UInt32 TotalErrorsCount => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 4);


    /// <summary>
    /// Measured fan's Pulses Per Minute
    /// </summary>
    public UInt32 CurrentPPM => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 8);


    /// <summary>
    /// Measured fan's power voltage
    /// </summary>
    public UInt16 CurrentVoltage => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 12);


    /// <summary>
    /// The basis level of fan's power
    /// </summary>
    public UInt16 ControlBasis => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 14);


    /// <summary>
    /// Hardware Control mode: PWM, DC-DC, or power MOSFET ON/OFF (see <see cref="Fan_Control_Mode"/>)
    /// </summary>
    public byte ControlMode => _dataSpan[16];


    /// <summary>
    /// Fan operation mode: Sensor controlled, Silent, Turbo or Manual (see <see cref="Fan_Operation_Mode"/>)
    /// </summary>
    public byte OperationMode => _dataSpan[17];


    /// <summary>
    /// The basis on which more accurate Control Values will be calculated
    /// </summary>
    public UInt16 ControlValue => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 18);


    /// <summary>
    /// A Control Value scaled to the current fan Control basis
    /// (actually to the selected fan power range).
    /// </summary>
    public UInt16 ScaledControl => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 20);

}

// END-OF-FILE
