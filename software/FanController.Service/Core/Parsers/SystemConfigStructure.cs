////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SystemConfigStructure.cs
////////////////////////////////////////////////////////////////////////////////////////

namespace FanController.Service.Core.Parsers;

//
// Types and flags:
//

/// <summary>
/// Buzzer modes
/// </summary>
public enum Buzzer_Mode : UInt32
{
    Buzzer_Off_Mode             = 0x00,                                 // Silent mode
    Buzzer_Alarms_Only_Mode     = System_Status.Status_Category_Alarms, // Alarms only
    Buzzer_Noisy_Mode           = System_Status.Status_Category_Not_OK  // Alarms and Warnings
}


/// <summary>
/// Hardware-based display rotation.
/// Values match firmware rotation encoding.
/// </summary>
public enum Display_Rotation : byte
{
    Rotate270   = 0,
    Rotate0     = 1,
    Rotate90    = 2,
    Rotate180   = 3
}


/// <summary>
/// Configuration flags
/// </summary>
public enum Config_Flags : byte
{
    Config_Flag_Empty                       = 0b00000000,       // Dummy status

    // The Pro mode implies the ability to switch between PWM and DC-DC Control Modes
    // in case of problems with one of them, and also prohibits the fan from leaving
    // the FAILED status if it has got it once.
    Config_Flag_Professional_Mode           = Fan_Global_Config_Flags.Fan_Config_Flag_Professional_Mode,

    // Try to detect the features of the ventilator (size, power, class).
    Config_Flag_Fan_Model_Detection         = Fan_Global_Config_Flags.Fan_Config_Flag_Fan_Model_Detection,

    Config_Flag_Buzzer_Loud_Mode            = 0b00000100,       // Buzzer option:
                                                                //	0 = cricket mode,
                                                                //	1 = continuous tone (louder).
    Config_Flag_Lock_Substitution_Matrix    = 0b00001000        // If we want to use a custom load distribution matrix from failed fans
                                                                // to the remaining working ones, then we need to set this flag.
}


/// <summary>
/// Firmware options flags
/// </summary>
public enum Firmware_Flags : byte
{
    Firmware_Flag_Empty                     = 0b00000000,       // Dummy flag
    Firmware_Flag_Display_Enabled           = 0b00000001,       // Display supported by firmware
    Firmware_Flag_UART_Logger_Enabled       = 0b00000010        // UART-Logger supported by firmware
}


/*
//
// Prototype declaration:
//

// System Configuration
typedef struct __attribute__((packed, aligned(4)))
{
	uint32_t firmwareVersion;				// Firmware version for compatibility check
	Behavior_Config behaviorConfig;			// Display/Buzzer and system behavior configuration
	uint16_t normalHLPBVoltage;				// High Load Power Bus voltage (fan supply line).
	uint8_t firmwareFlags;					// Firmware hardcoded options (display, logging, etc.)
} System_Config;				// 4 + 8 + 2 + 1 + 1 (padding byte) = 16 bytes

*/


/// <summary>
/// The structure of the controller's configuration
/// </summary>
public ref struct SystemConfigStructure
{
    /// <summary>
    /// Packed structure size
    /// </summary>
    public const int Size = 16;

    // Packed structure data
    private Span<byte> _dataSpan;
    private SystemBehaviorStructure _systemBehavior;


    /// <summary>
    /// Wraps the raw structure data.
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public SystemConfigStructure(Span<byte> data)
    {
        PackedStructureMethods.EnsureSize(data, Size, nameof(SystemConfigStructure));
        _dataSpan = data;
        _systemBehavior = new SystemBehaviorStructure(_dataSpan.Slice(4, SystemBehaviorStructure.Size));
    }


    /// <summary>
    /// Firmware version for compatibility check
    /// </summary>
    public UInt32 FirmwareVersion
    {
        get => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 0);
        set => PackedStructureMethods.WriteUInt32IntoSpan(value, _dataSpan, 0);
    }


    /// <summary>
    /// System Behavior substructure
    /// </summary>
    public SystemBehaviorStructure SystemBehavior => _systemBehavior;


    /// <summary>
    /// High Load Power Bus voltage (fan supply line)
    /// </summary>
    public UInt16 NormalHLPBVoltage
    {
        get => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 12);
        set => PackedStructureMethods.WriteUInt16IntoSpan(value, _dataSpan, 12);
    }


    /// <summary>
    /// Firmware hardcoded options: display, logging, etc. (see <see cref="Firmware_Flags"/>)
    /// </summary>
    public byte FirmwareFlags
    {
        get => _dataSpan[14];
        set => _dataSpan[14] = value;
    }
}

// END-OF-FILE
