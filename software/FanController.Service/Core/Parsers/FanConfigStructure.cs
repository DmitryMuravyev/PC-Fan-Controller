////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// FanConfigStructure.cs
////////////////////////////////////////////////////////////////////////////////////////

namespace FanController.Service.Core.Parsers;

//
// Types and flags:
//

/// <summary>
/// Hardware parameters
/// </summary>
public enum Fan_Hardware : byte
{
    Fan_Hardware_Undefined                  = 0b00000000,           // No hardware installed/detected
    Fan_Hardware_PWM_Available              = 0b00000001,           // PWM Control available via MAX31790 or internal Timer
    Fan_Hardware_RPM_Available              = 0b00000010,           // RPM measurement available via MAX31790 or internal Timer
    Fan_Hardware_Voltmeter_Available        = 0b00000100,           // Voltage monitoring available (ADC or INA or smth)
    Fan_Hardware_MOSFET_Available           = 0b00001000,           // Fan power is controlled by MOSFET or DC-DC Enable pin
    Fan_Hardware_DCDC_Available             = 0b00010000,           // Digipot or DAC available for voltage control via DC-DC
}


/// <summary>
/// Connected fan's parameters
/// </summary>
public enum Fan_Model : UInt16
{
    Fan_Model_Unconnected                   = 0b0000000000000000,   // No fan connected
    Fan_Model_Connected                     = 0b0000000000000001,   // 2-wire fan connected (no tacho, no PWM, fan connected)
    Fan_Model_Tachometer                    = 0b0000000000000010,   // 3-wire fan connected (tacho enabled)
    Fan_Model_PWM                           = 0b0000000000000100,   // 4-wire fan connected (PWM enabled)

    Fan_Model_DCDC_Range_Detected           = 0b0000000000010000,   // The DC-DC control range is detected
    Fan_Model_PWM_Range_Detected            = 0b0000000000100000,   // The PWM control range is detected

    Fan_Model_PWM_Freewheel_Tachometer      = 0b0000000010000000,   // The tachometer is functional at 0% PWM
    Fan_Model_PWM_Excessive_Full_Speed      = 0b0000000100000000,   // Significant speed increase between 99% and 100% PWM

    Fan_Model_Size_Detected                 = 0b0000010000000000,   // Fan size detected
    Fan_Model_Size_Large                    = 0b0000100000000000,   // Fan size: 	1 = Large fan size (strong inertia),
                                                                    //				0 = Small fan size (low inertia).
    Fan_Model_Power_Detected                = 0b0001000000000000,   // Fan power detected
    Fan_Model_Power_High                    = 0b0010000000000000,   // Fan power:	1 = Powerful fan (spins up quickly to high speeds),
                                                                    //				0 = Low-power fan (spins up slowly).
    Fan_Model_Class_Detected                = 0b0100000000000000,   // Fan family (class) detected
    Fan_Model_Class_Server                  = 0b1000000000000000,   // Fan family:	1 = Server family (high RPM, 4 pins),
                                                                    // 				0 = Desktop family (low RPM, 2/3/4 pins).

    Fan_Model_Connectivity_Mask             = 0b0000000000000111,   // Fan wiring
    Fan_Model_Capabilities_Mask             = 0b0000000110110000,   // Fan hardware capabilities mask
    Fan_Model_Features_Mask                 = 0b1111110000000000    // Fan model features mask
}


/// <summary>
/// Fan Control Mode
/// </summary>
public enum Fan_Control_Mode : byte
{
    Fan_Control_Disabled                    = 0b00000000,           // No fan connected or it is disabled by user
    Fan_Control_PWM                         = 0b00000001,           // PWM Control Mode
    Fan_Control_DCDC                        = 0b00000010,           // DC-DC Control Mode
    Fan_Control_MOSFET                      = 0b00000011,           // ON/OFF (MOSFET or DC-DC Enable pin) Control Mode
    Fan_Control_Mask                        = 0b00000011,           // Mask for fan Control Mode
}


/// <summary>
/// Fan operation mode
/// </summary>
public enum Fan_Operation_Mode : byte
{
    Fan_Operation_Normal                    = 0b00000000,           // Recalculation of the Control Value in accordance with the basis level of fan's power
    Fan_Operation_Manual                    = 0b00000001,           // Only correction of the Control Value to the operating range
    Fan_Operation_Silent                    = 0b00000010,           // Minimum rotation speed
    Fan_Operation_Turbo                     = 0b00000011,           // 100%
    Fan_Operation_Mask                      = 0b00000011            // Operation parameters mask
}


/// <summary>
/// Fan options (start/spin options, Pulses Per Revolution and so on).
/// Option values and bit shifts share this enum (some values coincide).
/// </summary>
public enum Fan_Options : UInt32
{
    // Fan Pulses Per Revolution options
    // RPM = PPM / PPR = PPM >> Fan_Options_PPR
    Fan_Options_PPR_Default                 = 0b01,                 // Pulses Per Revolution = 2
    Fan_Options_PPR_1                       = 0b00,                 // RPM = PPM = PPM >> 0
    Fan_Options_PPR_2                       = 0b01,                 // RPM = PPM / 2 = PPM >> 1 (default value)
    Fan_Options_PPR_4                       = 0b10,                 // RPM = PPM / 4 = PPM >> 2
    Fan_Options_PPR_Mask                    = 0b11,                 // PPR option mask

    // Spin-up control options
    // Value is calculated as a quarter of the Control Range multiplied by some coefficient.
    Fan_Options_Spin_Default                = 0b100,                // Default spin-up Control = 100%
    Fan_Options_Spin_0                      = 0b000,                // The fan does not need to be spinned-up and rotates even when the Control Value is zero.
    Fan_Options_Spin_25                     = 0b001,                // Spin-up value = 1/4 of Control Range * 1 = 25%
    Fan_Options_Spin_50                     = 0b010,                // Spin-up value = 1/4 of Control Range * 2 = 50%
    Fan_Options_Spin_75                     = 0b011,                // Spin-up value = 1/4 of Control Range * 3 = 75%
    Fan_Options_Spin_100                    = 0b100,                // Spin-up value = 1/4 of Control Range * 4 = 100% (default)
    Fan_Options_Spin_Mask                   = 0b111,                // Spin-up option mask
    Fan_Options_Spin_PWM_Bit_Shift          = 2,                    // PWM spin-up option first bit address
    Fan_Options_Spin_PWR_Bit_Shift          = 5,                    // PWR spin-up option first bit address

    // Same for Rate-of-Change
    // Increment or decrement of the Control Value per one step
    // Examples are given for the case when calls to the Control Value update procedure are performed 100 times per second:
    Fan_Options_ROC_Default                 = 0b1001,               // Default value = 512 (1.28s)
    Fan_Options_ROC_1                       = 0b0000,               // 655 seconds for the full range (100 calls per second)
    Fan_Options_ROC_2                       = 0b0001,               // 328 seconds for the full range (100 calls per second)
    Fan_Options_ROC_4                       = 0b0010,               // 164 seconds
    Fan_Options_ROC_8                       = 0b0011,               // 82 seconds
    Fan_Options_ROC_16                      = 0b0100,               // 41s
    Fan_Options_ROC_32                      = 0b0101,               // 20.5s
    Fan_Options_ROC_64                      = 0b0110,               // 10.2s
    Fan_Options_ROC_128                     = 0b0111,               // 5.1s
    Fan_Options_ROC_256                     = 0b1000,               // 2.56s
    Fan_Options_ROC_512                     = 0b1001,               // 1.28s
    Fan_Options_ROC_1024                    = 0b1010,               // 0.64s
    Fan_Options_ROC_2048                    = 0b1011,               // 0.32s
    Fan_Options_ROC_4096                    = 0b1100,               // 0.16s
    Fan_Options_ROC_8192                    = 0b1101,               // 0.08s
    Fan_Options_ROC_16384                   = 0b1110,               // 0.04s
    Fan_Options_ROC_Disabled                = 0b1111,               // 0s (instantaneous change)
    Fan_Options_ROC_Mask                    = 0b1111,               // ROC option mask
    Fan_Options_ROC_Bit_Shift               = 8,                    // ROC option first bit address

    // Fan rotation speed PID controller options:
    Fan_Options_PID_Enabled_PWM_Bit_Shift   = 16,                   // Enable PID for PWM Control Mode
    Fan_Options_PID_Enabled_PWR_Bit_Shift   = 17,                   // Enable PID for DC-DC Control Mode
    Fan_Options_PID_Slow_PWM_Bit_Shift      = 18,                   // Use "slow" PID coefficients for PWM Control Mode (may be useful for server fans)
    Fan_Options_PID_Slow_PWR_Bit_Shift      = 19,                   // Use "slow" PID coefficients for DC-DC Control Mode (may be useful for large high-inertia fans)

    // Other fan options:
    Fan_Options_Always_On_Shift             = 20                    // The fan will not be powered off in Operating Mode = Fan_Operation_Stopped
}


/// <summary>
/// Global static configuration flags
/// </summary>
public enum Fan_Global_Config_Flags : byte
{
    Fan_Config_Flag_Empty                   = 0b00000000,           // Dummy flag
    Fan_Config_Flag_Professional_Mode       = 0b00000001,           // The Pro mode implies the ability to switch between PWM and DC-DC
                                                                    // Control Modes in case of problems with one of them, and also prohibits
                                                                    // the fan from leaving the FAILED status if it has got it once.
    Fan_Config_Flag_Fan_Model_Detection     = 0b00000010,           // Try to detect the features of the ventilator (size, power, class).
    Fan_Config_Flag_Mask                    = 0b00000011
}


/*
//
// Prototype declarations:
//

// The structure defining the Hardware and Fan configuration
typedef struct __attribute__((packed, aligned(4)))
{
	// Parameters that imply the possibility of user configuration/
	// Initial values of runtime parameters
	uint8_t controlMode;                // Hardware Control mode: PWM, DC-DC, or power MOSFET ON/OFF
    uint8_t operationMode;              // Fan operation mode: Sensor controlled, Silent, Turbo or Manual.
    uint16_t controlBasis;              // The basis level of fan's power. If Control Basis = FAN_CONTROL_HALF_RANGE,
                                        // then the fan will operate over the entire range of available speeds.
                                        // By shifting the Basis down, we limit the maximum RPM, and by shifting it up,
                                        // we force the fan to work in the upper part of its operating range.

    char name[FAN_NAME_SIZE + 1];       // The displayed name of the fan
    uint16_t model;                     // Connected fan's capabilities
    uint32_t options;                   // Rate-Of-Change, Pulses Per Revolution and so on.

    // Parameters that can only be determined automatically
    // Hardware capabilities
    uint8_t hardware;                   // Hardware's and connected fan's capabilities

    // Power HW metrics
    uint16_t maxVoltage;                // Maximum DC-DC voltage
    uint16_t minVoltage;                // Minimum DC-DC voltage
    uint16_t maxVoltageControl;         // The Control Value corresponding to the maximum DC-DC voltage.
    uint16_t minVoltageControl;         // The Control Value corresponding to the minimum DC-DC voltage.

    // Common Fan metrics
    uint32_t maxPPM;                    // Fan's maximum Pulses Per Minute.

    // PWM metrics
    uint16_t minPWMControl;             // The minimum Control value for the PWM Control mode at which the fan still will rotate.
    uint32_t pwmControlledMinPPM;       // Minimum PPM for the PWM Control Mode.
    uint32_t pwmPenultimatePPM;         // Fan's PPM at the 99% PWM

    // Power Fan metrics
    uint16_t minPWRControl;             // The minimum Control value for the DC-DC Control mode at which the fan still will rotate.
    uint32_t pwrControlledMinPPM;       // Minimum PPM for the DC-DC Control Mode.
} Fan_Config;       // 1 + 1 + 2 + 8 + 1 + 2 + 4 + 1 + 2 + 2 + 2 + 2 + 4 + 2 + 4 + 4 + 2 + 4 = 48 bytes

*/


/// <summary>
/// The structure of the Fan configuration
/// </summary>
public ref struct FanConfigStructure
{
    /// <summary>
    /// Packed structure size
    /// </summary>
    public const int Size = 48;

    // Fan name field size (including the terminating '\0')
    public const int NameSize = 9;

    // Packed structure data
    private Span<byte> _dataSpan;


    /// <summary>
    /// Wraps the raw structure data.
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public FanConfigStructure(Span<byte> data)
    {
        PackedStructureMethods.EnsureSize(data, Size, nameof(FanConfigStructure));
        _dataSpan = data;
    }


    /// <summary>
    /// Hardware Control mode: PWM, DC-DC, or power MOSFET ON/OFF (see <see cref="Fan_Control_Mode"/>)
    /// </summary>
    public byte ControlMode
    {
        get => _dataSpan[0];
        set => _dataSpan[0] = value;
    }


    /// <summary>
    /// Fan operation mode: Sensor controlled, Silent, Turbo or Manual (see <see cref="Fan_Operation_Mode"/>)
    /// </summary>
    public byte OperationMode
    {
        get => _dataSpan[1];
        set => _dataSpan[1] = value;
    }


    /// <summary>
    /// The basis level of fan's power. If Control Basis = FAN_CONTROL_HALF_RANGE,
    /// then the fan will operate over the entire range of available speeds.
    /// By shifting the Basis down, we limit the maximum RPM, and by shifting it up,
    /// we force the fan to work in the upper part of its operating range.
    /// </summary>
    public UInt16 ControlBasis
    {
        get => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 2);
        set => PackedStructureMethods.WriteUInt16IntoSpan(value, _dataSpan, 2);
    }


    /// <summary>
    /// The displayed name of the fan (raw field)
    /// </summary>
    public Span<byte> Name
    {
        get => _dataSpan.Slice(4, NameSize);
        set => value.CopyTo(_dataSpan.Slice(4, NameSize));
    }


    /// <summary>
    /// The displayed name of the fan (up to NameSize - 1 ASCII characters)
    /// </summary>
    public string NameString
    {
        get => PackedStructureMethods.ReadAsciiString(Name);
        set => PackedStructureMethods.WriteAsciiString(value, Name);
    }


    /// <summary>
    /// Connected fan's capabilities (see <see cref="Fan_Model"/>)
    /// </summary>
    public UInt16 Model
    {
        get => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 13);
        set => PackedStructureMethods.WriteUInt16IntoSpan(value, _dataSpan, 13);
    }


    /// <summary>
    /// Rate-Of-Change, Pulses Per Revolution and so on (see <see cref="Fan_Options"/>)
    /// </summary>
    public UInt32 Options
    {
        get => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 15);
        set => PackedStructureMethods.WriteUInt32IntoSpan(value, _dataSpan, 15);
    }


    /// <summary>
    /// Hardware's and connected fan's capabilities (see <see cref="Fan_Hardware"/>)
    /// </summary>
    public byte Hardware
    {
        get => _dataSpan[19];
        set => _dataSpan[19] = value;
    }


    /// <summary>
    /// Maximum DC-DC voltage
    /// </summary>
    public UInt16 MaxVoltage
    {
        get => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 20);
        set => PackedStructureMethods.WriteUInt16IntoSpan(value, _dataSpan, 20);
    }


    /// <summary>
    /// Minimum DC-DC voltage
    /// </summary>
    public UInt16 MinVoltage
    {
        get => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 22);
        set => PackedStructureMethods.WriteUInt16IntoSpan(value, _dataSpan, 22);
    }


    /// <summary>
    /// The Control Value corresponding to the maximum DC-DC voltage.
    /// </summary>
    public UInt16 MaxVoltageControl
    {
        get => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 24);
        set => PackedStructureMethods.WriteUInt16IntoSpan(value, _dataSpan, 24);
    }


    /// <summary>
    /// The Control Value corresponding to the minimum DC-DC voltage.
    /// </summary>
    public UInt16 MinVoltageControl
    {
        get => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 26);
        set => PackedStructureMethods.WriteUInt16IntoSpan(value, _dataSpan, 26);
    }


    /// <summary>
    /// Fan's maximum Pulses Per Minute.
    /// </summary>
    public UInt32 MaxPPM
    {
        get => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 28);
        set => PackedStructureMethods.WriteUInt32IntoSpan(value, _dataSpan, 28);
    }


    /// <summary>
    /// The minimum Control value for the PWM Control mode at which the fan still will rotate.
    /// </summary>
    public UInt16 MinPWMControl
    {
        get => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 32);
        set => PackedStructureMethods.WriteUInt16IntoSpan(value, _dataSpan, 32);
    }


    /// <summary>
    /// Minimum PPM for the PWM Control Mode.
    /// </summary>
    public UInt32 PwmControlledMinPPM
    {
        get => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 34);
        set => PackedStructureMethods.WriteUInt32IntoSpan(value, _dataSpan, 34);
    }


    /// <summary>
    /// Fan's PPM at the 99% PWM
    /// </summary>
    public UInt32 PwmPenultimatePPM
    {
        get => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 38);
        set => PackedStructureMethods.WriteUInt32IntoSpan(value, _dataSpan, 38);
    }


    /// <summary>
    /// The minimum Control value for the DC-DC Control mode at which the fan still will rotate.
    /// </summary>
    public UInt16 MinPWRControl
    {
        get => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 42);
        set => PackedStructureMethods.WriteUInt16IntoSpan(value, _dataSpan, 42);
    }


    /// <summary>
    /// Minimum PPM for the DC-DC Control Mode.
    /// </summary>
    public UInt32 PwrControlledMinPPM
    {
        get => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 44);
        set => PackedStructureMethods.WriteUInt32IntoSpan(value, _dataSpan, 44);
    }

}

// END-OF-FILE
