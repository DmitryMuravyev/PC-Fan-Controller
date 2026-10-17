////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// SensorConfigStructure.cs
////////////////////////////////////////////////////////////////////////////////////////

namespace FanController.Service.Core.Parsers;

//
// Types and flags:
//

/// <summary>
/// Supported temperature sensor types
/// </summary>
public enum TSensor_Type : byte
{
    TSensor_Type_Unknown                = 0,        // The value will be used for the search
                                                    // On-board sensors: 0 - 15
    TSensor_Type_MCU                    = 1,        // MCU internal temperature sensor
    TSensor_Type_DS1841                 = 2,        // DS1841 digiPOT internal sensor

    // Connectable sensors: 16 - 31
    TSensor_Type_DS18B20                = 16,       // DS18B20 digital sensor (oneWire)

    // Software sensors: 32 - 127
    TSensor_Type_SW                     = 32,       // Some sensor, the readings of which are transmitted
                                                    // by the control application via USB.
    /*
        // There is no need to make specific types for each software sensor...
        TSensor_Type_SW_CPU				= 32,		// Host CPU sensor
        TSensor_Type_SW_GPU				= 33,		// Host GPU sensor
        TSensor_Type_SW_MB				= 34,		// Host motherboard sensor
        TSensor_Type_SW_HDD				= 35,		// Host HDD sensor
    */
    // Other sensors: 128 - ...
    TSensor_Type_Other                  = 128       // For future use...
}


/// <summary>
/// Configuration flags
/// </summary>
public enum TSensor_Flags : byte
{
    TSensor_Flag_Unknown                = 0b00000000,       // Dummy value
    TSensor_Flag_Visible                = 0b00000001,       // The sensor must be displayed on the screen
    TSensor_Flag_Active                 = 0b00000010,       // The sensor readings should affect the fans
    TSensor_Flag_Deletable              = 0b00000100,       // The sensor can be removed (not available for controller hardware sensors)
    TSensor_Flag_Permanent              = 0b00001000,       // The sensor configuration is saved and it will be restored after reset
    TSensor_Flag_Ignore_If_Unavailable  = 0b00010000        // Ignore the sensor (disable alarm and impact on fans) if it is not available
}


/// <summary>
/// Fan impact options
/// </summary>
/*
Impact values:
    0   =   0
    1   =   6.66%
    2   =   13.33%
    3   =   20%
    4   =   26.66%
    5   =   33.33%
    6   =   40%
    7   =   46.66%
    8   =   53.33%
    9   =   60%
    10  =   66.66%
    11  =   73.33%
    12  =   80%
    13  =   86.66%
    14  =   93.33%
    15  =   100%
*/
public enum TSensor_Fan_Impact : byte
{
    // Maximum possible sensor impact on fans
    // Value is calculated as a 1/15 of the full fan control range multiplied by some coefficient.
    TSensor_Fan_Impact_Default                      = 0b00001111,   // Default impact = 100%
    TSensor_Fan_Impact_Mask                         = 0b00001111,   // Impact option mask
    TSensor_Fan_Impact_Sensor_Unavailable_Default   = 0b00001001,   // Set fan speed to 60% if the sensor is unavailable
    TSensor_Fan_Impact_Sensor_Unavailable_Shift     = 4             // Unavailable sensor fan impact shift
}


/*
//
// Prototype declarations:
//
// The structure defining the sensor and its operation parameters
typedef struct __attribute__((packed, aligned(4)))
{
	    uint8_t type;									// Sensor type (see above)
	    char name[TSENSOR_NAME_SIZE + 1];				// The displayed name of the sensor. Let's reserve
													    // one last byte for the end-of-line character '\0', and after
													    // initializing the configuration, we won't change it anymore.
	    uint8_t serialNumber[TSENSOR_SN_SIZE];			// The unique identifier of the sensor
	    uint8_t warningThreshold;						// Threshold for triggering a warning
	    uint8_t alarmThreshold;							// Threshold for triggering an alarm
	    uint32_t config;								// The use of the configuration depends on the specific subclass
	    uint8_t flags;									// Sensor flags (see above)
	    uint8_t lowThresholds[FANS_COUNT];				// The low temperature threshold for each fan
	    uint8_t highThresholds[FANS_COUNT];				// The high temperature threshold for each fan
	    uint8_t fanImpacts[FANS_COUNT];					// Max/unavailable sensor impact (see above)

} TSensor_Config;	// 1 + (8 + 1) + 8 + 1 + 1 + 4 + 1 + 6 + 6 + 6 + 1 (padding) = 44 bytes

*/


/// <summary>
/// The structure of the TSensor configuration
/// </summary>
public ref struct SensorConfigStructure
{
    /// <summary>
    /// Structure size: 25 fixed bytes + 3 bytes per fan, aligned to 4 bytes
    /// </summary>
    public static int SizeForFanCount(int fanCount) => ((25 + fanCount * 3) + 3) & ~3;

    /// <summary>
    /// Packed structure size
    /// </summary>
    public readonly int Size;

    // Fan count
    public readonly int FanCount;

    // Sensor name field size (including the terminating '\0')
    public const int NameSize = 9;

    // Sensor Serial Number size
    public const int SerialNumberSize = 8;

    // Packed structure data
    private Span<byte> _dataSpan;


    /// <summary>
    /// Wraps the raw structure data for <paramref name="fanCount"/> fans.
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public SensorConfigStructure(Span<byte> data, int fanCount)
    {
        FanCount = fanCount;
        Size = SizeForFanCount(fanCount);
        PackedStructureMethods.EnsureSize(data, Size, nameof(SensorConfigStructure));
        _dataSpan = data;
    }


    /// <summary>
    /// Sensor type (see <see cref="TSensor_Type"/>)
    /// </summary>
    public byte Type
    {
        get => _dataSpan[0];
        set => _dataSpan[0] = value;
    }


    /// <summary>
    /// Sensor name (raw field)
    /// </summary>
    public Span<byte> Name
    {
        get => _dataSpan.Slice(1, NameSize);
        set => value.CopyTo(_dataSpan.Slice(1, NameSize));
    }


    /// <summary>
    /// Sensor name (up to NameSize - 1 ASCII characters)
    /// </summary>
    public string NameString
    {
        get => PackedStructureMethods.ReadAsciiString(Name);
        set => PackedStructureMethods.WriteAsciiString(value, Name);
    }


    /// <summary>
    /// The unique identifier of the sensor
    /// </summary>
    public Span<byte> SerialNumber
    {
        get => _dataSpan.Slice(10, SerialNumberSize);
        set => value.CopyTo(_dataSpan.Slice(10, SerialNumberSize));
    }


    /// <summary>
    /// Threshold for triggering a warning (°C)
    /// </summary>
    public byte WarningThreshold
    {
        get => _dataSpan[18];
        set => _dataSpan[18] = value;
    }


    /// <summary>
    /// Threshold for triggering an alarm (°C)
    /// </summary>
    public byte AlarmThreshold
    {
        get => _dataSpan[19];
        set => _dataSpan[19] = value;
    }


    /// <summary>
    /// The use of the configuration depends on the specific subclass
    /// </summary>
    public UInt32 Config
    {
        get => PackedStructureMethods.ReadUInt32FromSpan(_dataSpan, 20);
        set => PackedStructureMethods.WriteUInt32IntoSpan(value, _dataSpan, 20);
    }


    /// <summary>
    /// Sensor configuration flags (see <see cref="TSensor_Flags"/>)
    /// </summary>
    public byte Flags
    {
        get => _dataSpan[24];
        set => _dataSpan[24] = value;
    }


    /// <summary>
    /// The low temperature threshold for each fan (°C)
    /// </summary>
    public Span<byte> LowThresholds
    {
        get => _dataSpan.Slice(25, FanCount);
        set => value.CopyTo(_dataSpan.Slice(25, FanCount));
    }


    /// <summary>
    /// The high temperature threshold for each fan (°C)
    /// </summary>
    public Span<byte> HighThresholds
    {
        get => _dataSpan.Slice(25 + FanCount, FanCount);
        set => value.CopyTo(_dataSpan.Slice(25 + FanCount, FanCount));
    }


    /// <summary>
    /// Max/unavailable sensor impact for each fan (see <see cref="TSensor_Fan_Impact"/>)
    /// </summary>
    public Span<byte> FanImpacts
    {
        get => _dataSpan.Slice(25 + FanCount * 2, FanCount);
        set => value.CopyTo(_dataSpan.Slice(25 + FanCount * 2, FanCount));
    }

}

// END-OF-FILE
