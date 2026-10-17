////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SystemInfoStructure.cs
////////////////////////////////////////////////////////////////////////////////////////

namespace FanController.Service.Core.Parsers;

//
// Types and flags:
//

/// <summary>
/// Possible system statuses (there may be several at the same time),
/// sorted by level of danger/importance.
/// </summary>
public enum System_Status : UInt32
{
    Status_Unknown                          = 0b00000000000000000000000000000000,   // Dummy status
    Status_Normal                           = 0b00000000000000000000000000000001,   // All OK
    Status_Warning_Bad_Sensor               = 0b00000000000000000000000000000100,   // Sensor configuration error
    Status_Warning_Fan_RPM                  = 0b00000000000000000000000000010000,   // Low Fan RPM
    Status_Warning_Fan_Power                = 0b00000000000000000000000001000000,   // Low Fan Power
    Status_Warning_Temperature              = 0b00000000000000000000000100000000,   // Attention temperature level
    Status_Alarm_Bad_Sensor                 = 0b00000000000000000010000000000000,   // Sensor connection error
    Status_Alarm_Fan_RPM                    = 0b00000000000000001000000000000000,   // Critical Fan RPM
    Status_Alarm_Fan_Power                  = 0b00000000000000100000000000000000,   // Critical Fan power supply
    Status_Alarm_POTs                       = 0b00000000000010000000000000000000,   // POTs communication error
    Status_Alarm_MAX                        = 0b00000000001000000000000000000000,   // MAX31790 communication error
    Status_Alarm_Fan_Failed                 = 0b00000000100000000000000000000000,   // Failed Fan
    Status_Alarm_Low_RAM                    = 0b00000010000000000000000000000000,   // Insufficient free RAM
    Status_Alarm_Power                      = 0b00001000000000000000000000000000,   // Problems with the power supply of fans
    Status_Alarm_Temperature                = 0b00100000000000000000000000000000,   // Critical temperature
    Status_Alarm_General_Failure            = 0b10000000000000000000000000000000,   // Fatal system failure (usually due to configuration saving errors)
    Status_Category_Warnings                = 0b00000000000000000000011111111110,   // Warnings mask
    Status_Category_Alarms                  = 0b11111111111111111111100000000000,   // Errors mask
    Status_Category_Not_OK                  = Status_Category_Warnings | Status_Category_Alarms     // Alarms and Warnings mask
}


/// <summary>
/// Flags of configuration intervention states
/// </summary>
public enum Configuration_Change_Flags : UInt32
{
    CC_Flag_Empty                           = 0b00000000000000000000000000000000,   // Dummy value
    CC_Flag_No_Config                       = 0b00000000000000000000000000000001,   // No configuration found
    CC_Flag_HW_Failed                       = 0b00000000000000000000000000000010,   // Hardware scanning failure
    CC_Flag_Prewarm_Failed                  = 0b00000000000000000000000000000100,   // Fan pre-warm failure
    CC_Flag_Fan_Failed                      = 0b00000000000000000000000000001000,   // Fan scan failure
    CC_Flag_Sensor_Failed                   = 0b00000000000000000000000000010000,   // Sensor scan failure
    CC_Flag_Too_Many_Sensors_Detected       = 0b00000000000000000000000000100000,   // More sensors were detected than could be stored in memory
    CC_Flag_Too_Many_Sensors_Stored         = 0b00000000000000000000000001000000,   // Saved sensors do not fit in RAM
    CC_Flag_Sensor_Not_Found                = 0b00000000000000000000000010000000,   // The target sensor for the operation wasn't found
    CC_Flag_Sensor_Config_Error             = 0b00000000000000000000000100000000,   // Sensor configuration is not applicable
    CC_Flag_Fan_Config_Error                = 0b00000000000000000000001000000000,   // Fan configuration is not applicable
    CC_Flag_System_Config_Error             = 0b00000000000000000000010000000000,   // System configuration is not applicable
    CC_Flag_EEPROM_Failure                  = 0b00000000000000000000100000000000,   // Writing to the EEPROM failure
    CC_Flag_All_Fan_Channels_Involved       = 0b00001000000000000000000000000000,   // Scanning all Fan channels
    CC_Flag_Initialization_Complete         = 0b00010000000000000000000000000000,   // Controller initialization completion flag
    CC_Flag_HW_In_Progress                  = 0b00100000000000000000000000000000,   // Hardware scanning in progress
    CC_Flag_Fan_In_Progress                 = 0b01000000000000000000000000000000,   // Fan scanning in progress
    CC_Flag_Sensor_In_Progress              = 0b10000000000000000000000000000000,   // Sensor scanning in progress
    CC_Flag_Progress_Mask                   = 0b11100000000000000000000000000000    // Any scanning in progress mask
}


/// <summary>
/// User action requests
/// </summary>
public enum User_Action_Requests : UInt16
{
    UA_Request_Empty                        = 0,            // Dummy value

    UA_Request_Set_Fan_Control_Value        = 8,            // Set new Control Value for the Fan
    UA_Request_Set_Fan_Control_Basis        = 9,            // Set new Control Basis for the Fan
    UA_Request_Set_Fan_Control_Mode         = 10,           // Set Fan's Control Mode
    UA_Request_Set_Fan_Operation_Mode       = 11,           // Set Fan's Operation Mode
    UA_Request_Reset_Fan                    = 12,           // Reset Fan
    UA_Request_Reset_Sensor                 = 13,           // Reset Sensor

    UA_Request_Set_Behavior_Config          = 32,           // Set Behavior Config without saving it to the system Configuration

    UA_Request_System_Config_Update         = 64,           // Replace System Configuration with the specified one
    UA_Request_Fan_Config_Update            = 65,           // Replace Fan Configuration with the specified one
    UA_Request_Substitution_Matrix_Update   = 66,           // Replace Fan Substitution Matrix with the specified one
    UA_Request_Sensor_Config_Update         = 67,           // Replace Sensor Configuration with the specified one
    UA_Request_Sensor_Add                   = 68,           // Add Software Sensor
    UA_Request_Sensor_Remove                = 69,           // Delete Software Sensor

    UA_Request_Start_Clean_Config_Page      = 128,          // Start New Configuration Page
    UA_Request_Finalize_Config_Page         = 129,          // Write Config OK Marker and 'close' Configuration Page
    UA_Request_Config_Reload                = 130,          // Reload Configuration (for future use)

    UA_Request_Factory_Reset                = 192,          // Erase all configuration pages and reboot

    UA_Request_Hardware_Scan                = 256,          // Scan Hardware		- 0b0000000100000000
    UA_Request_Fan_Scan                     = 512,          // Scan Fan(s)			- 0b0000001000000000
    UA_Request_Sensor_Scan                  = 1024,         // Scan Sensors			- 0b0000010000000000
    UA_Request_Scan_Mask                    = 1792,         // Scan request mask	- 0b0000011100000000

    UA_Request_Initiated_By_Menu            = 32768         // The action was initiated through the menu
                                                            // and the controller's function buttons.
}


/// <summary>
/// User Action flags
/// </summary>
public enum User_Action_Flags : byte
{
    UA_Flag_Empty                           = 0b00000000,   // Dummy value
    UA_Flag_Last_Config_Request_Done        = 0b00000001,   // Last request successfully done
    UA_Flag_Last_Config_Request_Error       = 0b00000010,   // Last request failed
    UA_Flag_Config_Page_Started             = 0b00000100,   // Clean Configuration Flash page started

    UA_Flag_System_Config_Changed           = 0b00010000,   // System Configuration was changed
    UA_Flag_Fan_Config_Changed              = 0b00100000,   // Fan Configuration was changed
    UA_Flag_Substitution_Matrix_Changed     = 0b01000000,   // Fan Substitution Matrix changed
    UA_Flag_Sensor_Config_Changed           = 0b10000000,   // Sensor Configuration was changed
    UA_Flag_Config_Changes_Mask             = 0b11110000    // Something has changed mask
}


/*
//
// Prototype declaration:
//

// Current status of the controller
typedef struct __attribute__((packed, aligned(4)))
{
	uint32_t status;						// Status bits
	uint32_t configurationChangeFlags;		// HW/Fan/Sensor scanning and Configuration updating flags
	uint32_t freeRAM;						// Available RAM size
	Behavior_Config behaviorConfig;			// Display/Buzzer and system behavior configuration
	uint16_t highLoadPowerBusVoltage;		// Fan power supply circuit voltage
	uint16_t digitalPowerBusVoltage;		// Digital power supply circuit voltage
	uint16_t mcuPowerBusVoltage;			// MCU Vdd voltage
	uint16_t userActionRequest;				// Requested User Action
	uint8_t userActionFlags;				// Flags for correct processing of user actions
	uint8_t scanProgress;					// Scan progress as a percentage
	uint8_t lastHostRequestErrorCode;		// Last USB-communication request result
} Sys_Info;			// 4 + 4 + 4 + 8 + 2 + 2 + 2 + 2 + 1 + 1 + 1 + 1 (padding bytes) = 32 bytes

*/


/// <summary>
/// The structure of the controller's state
/// </summary>
public ref struct SystemInfoStructure
{
    /// <summary>
    /// Packed structure size
    /// </summary>
    public const int Size = 32;

    // Packed structure data
    private Span<byte> _dataSpan;
    private SystemBehaviorStructure _systemBehavior;


    /// <summary>
    /// Wraps the raw structure data.
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public SystemInfoStructure(Span<byte> data)
    {
        PackedStructureMethods.EnsureSize(data, Size, nameof(SystemInfoStructure));
        _dataSpan = data;
        _systemBehavior = new SystemBehaviorStructure(_dataSpan.Slice(12, SystemBehaviorStructure.Size));
    }


    /// <summary>
    /// Status bits (see <see cref="System_Status"/>)
    /// </summary>
    public UInt32 Status => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 0);


    /// <summary>
    /// HW/Fan/Sensor scanning and Configuration updating flags (see <see cref="Configuration_Change_Flags"/>)
    /// </summary>
    public UInt32 ConfigurationChangeFlags => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 4);


    /// <summary>
    /// Available RAM size
    /// </summary>
    public UInt32 FreeRAM => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 8);


    /// <summary>
    /// System Behavior substructure
    /// </summary>
    public SystemBehaviorStructure SystemBehavior => _systemBehavior;


    /// <summary>
    /// Fan power supply circuit voltage
    /// </summary>
    public UInt16 HighLoadPowerBusVoltage => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 20);


    /// <summary>
    /// Digital power supply circuit voltage
    /// </summary>
    public UInt16 DigitalPowerBusVoltage => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 22);


    /// <summary>
    /// MCU Vdd voltage
    /// </summary>
    public UInt16 McuPowerBusVoltage => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 24);


    /// <summary>
    /// Requested User Action (see <see cref="User_Action_Requests"/>)
    /// </summary>
    public UInt16 UserActionRequest => PackedStructureMethods.ReadUInt16FromSpan(_dataSpan, 26);


    /// <summary>
    /// Flags for correct processing of user actions (see <see cref="User_Action_Flags"/>)
    /// </summary>
    public byte UserActionFlags => _dataSpan[28];


    /// <summary>
    /// Scan progress as a percentage
    /// </summary>
    public byte ScanProgress => _dataSpan[29];


    /// <summary>
    /// Last USB-communication request result
    /// </summary>
    public byte LastHostRequestErrorCode => _dataSpan[30];
}

// END-OF-FILE
