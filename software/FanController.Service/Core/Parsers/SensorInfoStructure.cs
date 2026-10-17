////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SensorInfoStructure.cs
////////////////////////////////////////////////////////////////////////////////////////

namespace FanController.Service.Core.Parsers;

//
// Types and flags:
//

/// <summary>
/// Status flags
/// </summary>
public enum TSensor_Status : byte
{
    TSensor_Status_Connected    = 0b00000001,       // The sensor is connected
    TSensor_Status_Configured   = 0b00000010,       // The sensor is configured correctly
    TSensor_Status_Valid        = 0b00000100,       // Sensor readings are relevant
    TSensor_Status_Error        = 0b00010000,       // Sensor hardware error
    TSensor_Status_Warning      = 0b00100000,       // Temperature warning
    TSensor_Status_Alarm        = 0b01000000,       // Critical temperature
    TSensor_Status_Any_Problems_Mask = TSensor_Status_Alarm | TSensor_Status_Warning | TSensor_Status_Error
}


/*
//
// Prototype declaration:
//

// Current sensor state
typedef struct __attribute__((packed, aligned(4)))
{
	uint32_t totalErrorsCount;						// Total errors (HW errors, temperature alarms and warnings) count
	int16_t temperature;							// Measured sensor's temperature
	uint8_t status;									// Status bits
	uint8_t overcooledByFanMap;						// A map of fans for which the temperature of this sensor is below
													// the lower limit. Used to determine whether the fan can be turned off.
} TSensor_Info;		// 4 + 2 + 1 + 1 = 8 bytes

*/


/// <summary>
/// The structure of the TSensor's state
/// </summary>
public ref struct SensorInfoStructure
{
    /// <summary>
    /// Packed structure size
    /// </summary>
    public const int Size = 8;

    // Packed structure data
    private Span<byte> _dataSpan;


    /// <summary>
    /// Wraps the raw structure data.
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public SensorInfoStructure(Span<byte> data)
    {
        PackedStructureMethods.EnsureSize(data, Size, nameof(SensorInfoStructure));
        _dataSpan = data;
    }


    /// <summary>
    /// Total errors (HW errors, temperature alarms and warnings) count
    /// </summary>
    public UInt32 TotalErrorsCount => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 0);


    /// <summary>
    /// Measured sensor's temperature (1/16 °C)
    /// </summary>
    public Int16 Temperature => PackedStructureMethods.ReadInt16FromSpan(_dataSpan, 4);


    /// <summary>
    /// Status bits (see <see cref="TSensor_Status"/>)
    /// </summary>
    public byte Status => _dataSpan[6];


    /// <summary>
    /// A map of fans for which the temperature of this sensor is below the lower limit
    /// </summary>
    public byte OvercooledByFanMap => _dataSpan[7];

}

// END-OF-FILE
