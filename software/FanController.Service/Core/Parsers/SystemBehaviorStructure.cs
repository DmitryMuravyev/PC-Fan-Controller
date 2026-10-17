////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SystemBehaviorStructure.cs
////////////////////////////////////////////////////////////////////////////////////////

namespace FanController.Service.Core.Parsers;

/*
//
// Prototype declaration:
//

// System behavior config
typedef struct __attribute__((packed, aligned(4)))
{
	uint32_t buzzerAlarmsMask;				// A list of events that a buzzer should respond to.
	uint8_t displayBrightness;				// The brightness level of the display backlight 0-255 (PWM duty cycle).
	uint8_t displayRotation;				// Display rotation 0-90-180-270.
	uint8_t configFlags;					// Buzzer and other appearance flags
} Behavior_Config;				// 4 + 1 + 1 + 1 + 1 (padding byte) = 8 bytes

*/


/// <summary>
/// The structure of the controller's behavior config
/// (a substructure of the system configuration and the system state)
/// </summary>
public ref struct SystemBehaviorStructure
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
    public SystemBehaviorStructure(Span<byte> data)
    {
        PackedStructureMethods.EnsureSize(data, Size, nameof(SystemBehaviorStructure));
        _dataSpan = data;
    }


    /// <summary>
    /// A list of events that a buzzer should respond to (see <see cref="System_Status"/>)
    /// </summary>
    public UInt32 BuzzerAlarmsMask
    {
        get => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 0);
        set => PackedStructureMethods.WriteUInt32IntoSpan(value, _dataSpan, 0);
    }


    /// <summary>
    /// The brightness level of the display backlight 0-255 (PWM duty cycle)
    /// </summary>
    public byte DisplayBrightness
    {
        get => _dataSpan[4];
        set => _dataSpan[4] = value;
    }


    /// <summary>
    /// Display rotation (see <see cref="Display_Rotation"/>)
    /// </summary>
    public byte DisplayRotation
    {
        get => _dataSpan[5];
        set => _dataSpan[5] = value;
    }


    /// <summary>
    /// Buzzer and behavior flags (see <see cref="Config_Flags"/>)
    /// </summary>
    public byte ConfigFlags
    {
        get => _dataSpan[6];
        set => _dataSpan[6] = value;
    }
}

// END-OF-FILE
