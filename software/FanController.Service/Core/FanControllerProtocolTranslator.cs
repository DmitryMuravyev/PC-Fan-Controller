////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// FanControllerProtocolTranslator.cs
////////////////////////////////////////////////////////////////////////////////////////

using FanController.Contracts;
using FanController.Service.Core.Parsers;


namespace FanController.Service.Core;

/// <summary>
/// Mapping of the hardware data exchange format to Contracts and vice versa.
/// </summary>
public static class FanControllerProtocolTranslator
{
    ////////////////////////////////////////////////////////////////////////////////////////
    // Bitwise operations
    // BitSet* only set the bits (the values are built starting from zero).

    private static bool BitTest8(byte value, byte mask) => (value & mask) != 0;

    private static void BitSet8(ref byte value, byte mask, bool isSet)
    {
        if (isSet) value |= mask;
    }


    private static bool BitTest16(UInt16 value, UInt16 mask) => (value & mask) != 0;

    private static void BitSet16(ref UInt16 value, UInt16 mask, bool isSet)
    {
        if (isSet) value |= mask;
    }


    private static bool BitTest32(UInt32 value, UInt32 mask) => (value & mask) != 0;

    private static void BitSet32(ref UInt32 value, UInt32 mask, bool isSet)
    {
        if (isSet) value |= mask;
    }


    // One flag per fan (bit i = fan i)
    private static bool[] Bit8ToBooleanArray(byte value, int fanCount)
    {
        var resultArray = new bool[fanCount];
        for (int i = 0; i < fanCount; i++)
            resultArray[i] = (value & (1 << i)) != 0;
        return resultArray;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Simple conversion operations

    // Voltage: 1/16 V
    private static double VoltageHWToDouble(UInt16 value) =>
        value / 16.0;

    private static UInt16 VoltageDoubleToHW(double value) =>
        (UInt16)Math.Round(Math.Clamp(value * 16.0, 0.0, UInt16.MaxValue));


    // 16-bit Control Value: 0..65535 = 0..1
    private static double ControlHW16ToDouble(UInt16 value) =>
        Math.Round((double)value / UInt16.MaxValue, 5);

    private static UInt16 ControlDoubleToHW16(double value) =>
        (UInt16)Math.Round(Math.Clamp(value, 0.0, 1.0) * UInt16.MaxValue);


    // 8-bit Control Value: 0..255 = 0..1
    private static double ControlHW8ToDouble(byte value) =>
        Math.Round((double)value / byte.MaxValue, 3);

    private static byte ControlDoubleToHW8(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0.0, 1.0) * byte.MaxValue);


    // 16-bit temperature: 1/16 °C
    private static double TemperatureHW16ToDouble(Int16 value) =>
        value / 16.0;

    private static Int16 TemperatureDoubleToHW16(double value) =>
        (Int16)Math.Round(Math.Clamp(value * 16.0, Int16.MinValue, Int16.MaxValue));


    /// <summary>
    /// Operation Result from HW to Contract
    /// </summary>
    public static OperationResult OperationResultHWToContract(byte result)
    {
        return result switch
        {
            (byte)Operation_Results.Response_OK => OperationResult.OK,
            (byte)Operation_Results.Response_NACK => OperationResult.NACK,

            (byte)Operation_Results.Response_Busy => OperationResult.Busy,
            (byte)Operation_Results.Response_Request_Scheduled => OperationResult.RequestScheduled,
            (byte)Operation_Results.Response_Request_Processing => OperationResult.RequestProcessing,
            (byte)Operation_Results.Response_Request_Done => OperationResult.RequestDone,
            (byte)Operation_Results.Response_Request_Error => OperationResult.RequestError,
            (byte)Operation_Results.Response_Request_Unknown_Result => OperationResult.RequestUnknownResult,

            (byte)Operation_Results.Response_System_Not_Configured => OperationResult.SystemNotConfigured,

            (byte)Operation_Results.Response_Fan_Not_Found => OperationResult.FanNotFound,
            (byte)Operation_Results.Response_Fan_Not_Configured => OperationResult.FanNotConfigured,

            (byte)Operation_Results.Response_Sensor_Not_Found => OperationResult.SensorNotFound,
            (byte)Operation_Results.Response_Sensor_Not_Configured => OperationResult.SensorNotConfigured,
            (byte)Operation_Results.Response_Sensor_Already_Exists_Error => OperationResult.SensorAlreadyExistsError,

            (byte)Operation_Results.Response_Configuration_Update_Error => OperationResult.ConfigurationUpdateError,
            (byte)Operation_Results.Response_Flash_Page_Already_Started => OperationResult.FlashPageAlreadyStarted,
            (byte)Operation_Results.Response_Flash_No_Page_Started => OperationResult.FlashNoPageStarted,

            _ => OperationResult.NACK    // Safe fallback for unknown firmware values
        };
    }


    private static DisplayRotation DisplayRotationHWToContract(byte rotation)
    {
        return rotation switch
        {
            (byte)Display_Rotation.Rotate0 => DisplayRotation.Rotate0,
            (byte)Display_Rotation.Rotate90 => DisplayRotation.Rotate90,
            (byte)Display_Rotation.Rotate180 => DisplayRotation.Rotate180,
            (byte)Display_Rotation.Rotate270 => DisplayRotation.Rotate270,
            _ => DisplayRotation.Rotate0    // Safe fallback for unknown firmware values
        };
    }


    private static byte DisplayRotationContractToHW(DisplayRotation rotation)
    {
        return rotation switch
        {
            DisplayRotation.Rotate0 => (byte)Display_Rotation.Rotate0,
            DisplayRotation.Rotate90 => (byte)Display_Rotation.Rotate90,
            DisplayRotation.Rotate180 => (byte)Display_Rotation.Rotate180,
            DisplayRotation.Rotate270 => (byte)Display_Rotation.Rotate270,
            _ => (byte)Display_Rotation.Rotate0    // Safe fallback for unknown values
        };
    }


    private static FanControlMode ControlModeHWToContract(byte mode)
    {
        return mode switch
        {
            (byte)Fan_Control_Mode.Fan_Control_Disabled => FanControlMode.ControlDisabled,
            (byte)Fan_Control_Mode.Fan_Control_PWM => FanControlMode.ControlPWM,
            (byte)Fan_Control_Mode.Fan_Control_DCDC => FanControlMode.ControlDCDC,
            (byte)Fan_Control_Mode.Fan_Control_MOSFET => FanControlMode.ControlONOFF,
            _ => FanControlMode.ControlDisabled    // Safe fallback for unknown firmware values
        };
    }


    public static byte ControlModeContractToHW(FanControlMode mode)
    {
        return mode switch
        {
            FanControlMode.ControlDisabled => (byte)Fan_Control_Mode.Fan_Control_Disabled,
            FanControlMode.ControlPWM => (byte)Fan_Control_Mode.Fan_Control_PWM,
            FanControlMode.ControlDCDC => (byte)Fan_Control_Mode.Fan_Control_DCDC,
            FanControlMode.ControlONOFF => (byte)Fan_Control_Mode.Fan_Control_MOSFET,
            _ => (byte)Fan_Control_Mode.Fan_Control_Disabled    // Safe fallback for unknown values
        };
    }


    private static FanOperationMode OperationModeHWToContract(byte mode)
    {
        return mode switch
        {
            (byte)Fan_Operation_Mode.Fan_Operation_Normal => FanOperationMode.OperationNormal,
            (byte)Fan_Operation_Mode.Fan_Operation_Silent => FanOperationMode.OperationSilent,
            (byte)Fan_Operation_Mode.Fan_Operation_Turbo => FanOperationMode.OperationTurbo,
            (byte)Fan_Operation_Mode.Fan_Operation_Manual => FanOperationMode.OperationManual,
            _ => FanOperationMode.OperationNormal    // Safe fallback for unknown firmware values
        };
    }


    public static byte OperationModeContractToHW(FanOperationMode mode)
    {
        return mode switch
        {
            FanOperationMode.OperationNormal => (byte)Fan_Operation_Mode.Fan_Operation_Normal,
            FanOperationMode.OperationSilent => (byte)Fan_Operation_Mode.Fan_Operation_Silent,
            FanOperationMode.OperationTurbo => (byte)Fan_Operation_Mode.Fan_Operation_Turbo,
            FanOperationMode.OperationManual => (byte)Fan_Operation_Mode.Fan_Operation_Manual,
            _ => (byte)Fan_Operation_Mode.Fan_Operation_Normal    // Safe fallback for unknown values
        };
    }


    private static FanOptionSpinPower SpinPowerHWToContract(byte mode)
    {
        return mode switch
        {
            (byte)Fan_Options.Fan_Options_Spin_0 => FanOptionSpinPower.SpinPower0,
            (byte)Fan_Options.Fan_Options_Spin_25 => FanOptionSpinPower.SpinPower25,
            (byte)Fan_Options.Fan_Options_Spin_50 => FanOptionSpinPower.SpinPower50,
            (byte)Fan_Options.Fan_Options_Spin_75 => FanOptionSpinPower.SpinPower75,
            (byte)Fan_Options.Fan_Options_Spin_100 => FanOptionSpinPower.SpinPower100,
            _ => FanOptionSpinPower.SpinPower100    // Safe fallback for unknown firmware values
        };
    }


    private static byte SpinPowerContractToHW(FanOptionSpinPower spinOption)
    {
        return spinOption switch
        {
            FanOptionSpinPower.SpinPower0 => (byte)Fan_Options.Fan_Options_Spin_0,
            FanOptionSpinPower.SpinPower25 => (byte)Fan_Options.Fan_Options_Spin_25,
            FanOptionSpinPower.SpinPower50 => (byte)Fan_Options.Fan_Options_Spin_50,
            FanOptionSpinPower.SpinPower75 => (byte)Fan_Options.Fan_Options_Spin_75,
            FanOptionSpinPower.SpinPower100 => (byte)Fan_Options.Fan_Options_Spin_100,
            _ => (byte)Fan_Options.Fan_Options_Spin_100    // Safe fallback for unknown values
        };
    }


    // Fan Pulses-Per-Revolution
    private static int PprHWToContract(byte ppr)
    {
        return ppr switch
        {
            (byte)Fan_Options.Fan_Options_PPR_1 => 1,
            (byte)Fan_Options.Fan_Options_PPR_2 => 2,
            (byte)Fan_Options.Fan_Options_PPR_4 => 4,
            _ => 2    // Safe fallback for unknown firmware values
        };
    }


    private static byte PprContractToHW(int ppr)
    {
        return ppr switch
        {
            1 => (byte)Fan_Options.Fan_Options_PPR_1,
            2 => (byte)Fan_Options.Fan_Options_PPR_2,
            4 => (byte)Fan_Options.Fan_Options_PPR_4,
            _ => (byte)Fan_Options.Fan_Options_PPR_2    // Safe fallback for unknown values
        };
    }


    // Fan Control Rate-of-Change
    private static FanOptionRoc RocHWToContract(byte roc)
    {
        return roc switch
        {
            (byte)Fan_Options.Fan_Options_ROC_1 => FanOptionRoc.Roc1,
            (byte)Fan_Options.Fan_Options_ROC_2 => FanOptionRoc.Roc2,
            (byte)Fan_Options.Fan_Options_ROC_4 => FanOptionRoc.Roc4,
            (byte)Fan_Options.Fan_Options_ROC_8 => FanOptionRoc.Roc8,
            (byte)Fan_Options.Fan_Options_ROC_16 => FanOptionRoc.Roc16,
            (byte)Fan_Options.Fan_Options_ROC_32 => FanOptionRoc.Roc32,
            (byte)Fan_Options.Fan_Options_ROC_64 => FanOptionRoc.Roc64,
            (byte)Fan_Options.Fan_Options_ROC_128 => FanOptionRoc.Roc128,
            (byte)Fan_Options.Fan_Options_ROC_256 => FanOptionRoc.Roc256,
            (byte)Fan_Options.Fan_Options_ROC_512 => FanOptionRoc.Roc512,
            (byte)Fan_Options.Fan_Options_ROC_1024 => FanOptionRoc.Roc1024,
            (byte)Fan_Options.Fan_Options_ROC_2048 => FanOptionRoc.Roc2048,
            (byte)Fan_Options.Fan_Options_ROC_4096 => FanOptionRoc.Roc4096,
            (byte)Fan_Options.Fan_Options_ROC_8192 => FanOptionRoc.Roc8192,
            (byte)Fan_Options.Fan_Options_ROC_16384 => FanOptionRoc.Roc16384,
            (byte)Fan_Options.Fan_Options_ROC_Disabled => FanOptionRoc.RocDisabled,
            _ => FanOptionRoc.Roc512    // Safe fallback (firmware default) for unknown firmware 
        };
    }


    private static byte RocContractToHW(FanOptionRoc roc)
    {
        return roc switch
        {
            FanOptionRoc.Roc1 => (byte)Fan_Options.Fan_Options_ROC_1,
            FanOptionRoc.Roc2 => (byte)Fan_Options.Fan_Options_ROC_2,
            FanOptionRoc.Roc4 => (byte)Fan_Options.Fan_Options_ROC_4,
            FanOptionRoc.Roc8 => (byte)Fan_Options.Fan_Options_ROC_8,
            FanOptionRoc.Roc16 => (byte)Fan_Options.Fan_Options_ROC_16,
            FanOptionRoc.Roc32 => (byte)Fan_Options.Fan_Options_ROC_32,
            FanOptionRoc.Roc64 => (byte)Fan_Options.Fan_Options_ROC_64,
            FanOptionRoc.Roc128 => (byte)Fan_Options.Fan_Options_ROC_128,
            FanOptionRoc.Roc256 => (byte)Fan_Options.Fan_Options_ROC_256,
            FanOptionRoc.Roc512 => (byte)Fan_Options.Fan_Options_ROC_512,
            FanOptionRoc.Roc1024 => (byte)Fan_Options.Fan_Options_ROC_1024,
            FanOptionRoc.Roc2048 => (byte)Fan_Options.Fan_Options_ROC_2048,
            FanOptionRoc.Roc4096 => (byte)Fan_Options.Fan_Options_ROC_4096,
            FanOptionRoc.Roc8192 => (byte)Fan_Options.Fan_Options_ROC_8192,
            FanOptionRoc.Roc16384 => (byte)Fan_Options.Fan_Options_ROC_16384,
            FanOptionRoc.RocDisabled => (byte)Fan_Options.Fan_Options_ROC_Disabled,
            _ => (byte)Fan_Options.Fan_Options_ROC_Default    // Safe fallback for unknown values
        };
    }



    private static TSensorType SensorTypeHWToContract(byte type)
    {
        return type switch
        {
            (byte)TSensor_Type.TSensor_Type_Unknown => TSensorType.Unknown,
            (byte)TSensor_Type.TSensor_Type_MCU => TSensorType.MCU,
            (byte)TSensor_Type.TSensor_Type_DS1841 => TSensorType.DS1841,
            (byte)TSensor_Type.TSensor_Type_DS18B20 => TSensorType.DS18B20,
            (byte)TSensor_Type.TSensor_Type_SW => TSensorType.SW,
            (byte)TSensor_Type.TSensor_Type_Other => TSensorType.Other,
            _ => TSensorType.Unknown    // Safe fallback for unknown firmware values
        };
    }


    private static byte SensorTypeContractToHW(TSensorType type)
    {
        return type switch
        {
            TSensorType.Unknown => (byte)TSensor_Type.TSensor_Type_Unknown,
            TSensorType.MCU => (byte)TSensor_Type.TSensor_Type_MCU,
            TSensorType.DS1841 => (byte)TSensor_Type.TSensor_Type_DS1841,
            TSensorType.DS18B20 => (byte)TSensor_Type.TSensor_Type_DS18B20,
            TSensorType.SW => (byte)TSensor_Type.TSensor_Type_SW,
            TSensorType.Other => (byte)TSensor_Type.TSensor_Type_Other,
            _ => (byte)TSensor_Type.TSensor_Type_Unknown    // Safe fallback for unknown values
        };
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Algorithmic conversion operations

    // Temperature thresholds: unsigned 8-bit °C
    private static int[] TemperatureArrayHWToContract(byte[] array) =>
        Array.ConvertAll(array, t => (int)t);

    private static byte[] TemperatureArrayContractToHW(int[] array) =>
        Array.ConvertAll(array, t => (byte)Math.Clamp(t, byte.MinValue, byte.MaxValue));


    // Sensor's impact on a fan: 0..15 = 0..1
    private static double FanImpactHWToContract(byte fanImpact) =>
        (double)fanImpact / (double)TSensor_Fan_Impact.TSensor_Fan_Impact_Mask;

    private static byte FanImpactContractToHW(double fanImpact) =>
        (byte)Math.Round(Math.Clamp(fanImpact, 0.0, 1.0) * (double)TSensor_Fan_Impact.TSensor_Fan_Impact_Mask);


    // One byte per fan: the impact in the low 4 bits, the unavailable-sensor impact in the high 4 bits
    private static double[] FanImpactArrayHWToContract(byte[] fanImpacts, bool isUnavailableSensorFanImpact = false)
    {
        int valueShift = isUnavailableSensorFanImpact ?
            (int)TSensor_Fan_Impact.TSensor_Fan_Impact_Sensor_Unavailable_Shift : 0;

        var resultArray = new double[fanImpacts.Length];
        for (int i = 0; i < fanImpacts.Length; i++)
        {
            resultArray[i] = FanImpactHWToContract((byte)((fanImpacts[i] >> valueShift) &
                (byte)TSensor_Fan_Impact.TSensor_Fan_Impact_Mask));
        }
        return resultArray;
    }


    private static byte[] FanImpactArrayContractToHW(double[] fanImpacts, double[] unavailableSensorFanImpacts)
    {
        int size = Math.Min(fanImpacts.Length, unavailableSensorFanImpacts.Length);
        var resultArray = new byte[size];
        for (int i = 0; i < size; i++)
        {
            resultArray[i] = (byte)(
                (FanImpactContractToHW(unavailableSensorFanImpacts[i]) << (int)TSensor_Fan_Impact.TSensor_Fan_Impact_Sensor_Unavailable_Shift) |
                FanImpactContractToHW(fanImpacts[i]));
        }
        return resultArray;
    }


    // Hardware SystemBehaviorStructure to SystemBehaviorDto
    private static SystemBehaviorDto SystemBehaviorHWToContract(SystemBehaviorStructure hwBehavior)
    {
        UInt32 buzzerMask = hwBehavior.BuzzerAlarmsMask;
        byte configFlags = hwBehavior.ConfigFlags;

        return new SystemBehaviorDto
        {
            // Buzzer alarms/warnings mask
            BuzzerFlagWarningBadSensor = BitTest32(buzzerMask, (UInt32)System_Status.Status_Warning_Bad_Sensor),
            BuzzerFlagWarningFanRPM = BitTest32(buzzerMask, (UInt32)System_Status.Status_Warning_Fan_RPM),
            BuzzerFlagWarningFanPower = BitTest32(buzzerMask, (UInt32)System_Status.Status_Warning_Fan_Power),
            BuzzerFlagWarningTemperature = BitTest32(buzzerMask, (UInt32)System_Status.Status_Warning_Temperature),
            BuzzerFlagAlarmBadSensor = BitTest32(buzzerMask, (UInt32)System_Status.Status_Alarm_Bad_Sensor),
            BuzzerFlagAlarmFanRPM = BitTest32(buzzerMask, (UInt32)System_Status.Status_Alarm_Fan_RPM),
            BuzzerFlagAlarmFanPower = BitTest32(buzzerMask, (UInt32)System_Status.Status_Alarm_Fan_Power),
            BuzzerFlagAlarmPOTs = BitTest32(buzzerMask, (UInt32)System_Status.Status_Alarm_POTs),
            BuzzerFlagAlarmMAX = BitTest32(buzzerMask, (UInt32)System_Status.Status_Alarm_MAX),
            BuzzerFlagAlarmFanFailed = BitTest32(buzzerMask, (UInt32)System_Status.Status_Alarm_Fan_Failed),
            BuzzerFlagAlarmLowRAM = BitTest32(buzzerMask, (UInt32)System_Status.Status_Alarm_Low_RAM),
            BuzzerFlagAlarmPower = BitTest32(buzzerMask, (UInt32)System_Status.Status_Alarm_Power),
            BuzzerFlagAlarmTemperature = BitTest32(buzzerMask, (UInt32)System_Status.Status_Alarm_Temperature),
            BuzzerFlagAlarmGeneralFailure = BitTest32(buzzerMask, (UInt32)System_Status.Status_Alarm_General_Failure),

            // Config flags
            ConfigBuzzerWaveform = BitTest8(configFlags, (byte)Config_Flags.Config_Flag_Buzzer_Loud_Mode)
                ? BuzzerWaveform.Loud
                : BuzzerWaveform.Cricket,
            ConfigFlagProfessionalMode = BitTest8(configFlags, (byte)Config_Flags.Config_Flag_Professional_Mode),
            ConfigFlagFanModelDetection = BitTest8(configFlags, (byte)Config_Flags.Config_Flag_Fan_Model_Detection),
            ConfigFlagLockSubstitutionMatrix = BitTest8(configFlags, (byte)Config_Flags.Config_Flag_Lock_Substitution_Matrix),

            // Display parameters
            DisplayBrightness = ControlHW8ToDouble(hwBehavior.DisplayBrightness),
            DisplayRotation = DisplayRotationHWToContract(hwBehavior.DisplayRotation)
        };
    }


    // SystemBehaviorDto onto the hardware SystemBehaviorStructure
    private static void MapSystemBehaviorContractOnHW(SystemBehaviorDto contractBehavior, SystemBehaviorStructure hwBehavior)
    {
        // Buzzer alarms/warnings mask
        UInt32 buzzerAlarmsMask = 0;
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Warning_Bad_Sensor, contractBehavior.BuzzerFlagWarningBadSensor);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Warning_Fan_RPM, contractBehavior.BuzzerFlagWarningFanRPM);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Warning_Fan_Power, contractBehavior.BuzzerFlagWarningFanPower);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Warning_Temperature, contractBehavior.BuzzerFlagWarningTemperature);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Alarm_Bad_Sensor, contractBehavior.BuzzerFlagAlarmBadSensor);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Alarm_Fan_RPM, contractBehavior.BuzzerFlagAlarmFanRPM);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Alarm_Fan_Power, contractBehavior.BuzzerFlagAlarmFanPower);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Alarm_POTs, contractBehavior.BuzzerFlagAlarmPOTs);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Alarm_MAX, contractBehavior.BuzzerFlagAlarmMAX);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Alarm_Fan_Failed, contractBehavior.BuzzerFlagAlarmFanFailed);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Alarm_Low_RAM, contractBehavior.BuzzerFlagAlarmLowRAM);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Alarm_Power, contractBehavior.BuzzerFlagAlarmPower);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Alarm_Temperature, contractBehavior.BuzzerFlagAlarmTemperature);
        BitSet32(ref buzzerAlarmsMask, (UInt32)System_Status.Status_Alarm_General_Failure, contractBehavior.BuzzerFlagAlarmGeneralFailure);
        hwBehavior.BuzzerAlarmsMask = buzzerAlarmsMask;

        // Display parameters
        hwBehavior.DisplayBrightness = ControlDoubleToHW8(contractBehavior.DisplayBrightness);
        hwBehavior.DisplayRotation = DisplayRotationContractToHW(contractBehavior.DisplayRotation);

        // Config flags
        byte configFlags = 0;
        BitSet8(ref configFlags, (byte)Config_Flags.Config_Flag_Buzzer_Loud_Mode, contractBehavior.ConfigBuzzerWaveform == BuzzerWaveform.Loud);
        BitSet8(ref configFlags, (byte)Config_Flags.Config_Flag_Professional_Mode, contractBehavior.ConfigFlagProfessionalMode);
        BitSet8(ref configFlags, (byte)Config_Flags.Config_Flag_Fan_Model_Detection, contractBehavior.ConfigFlagFanModelDetection);
        BitSet8(ref configFlags, (byte)Config_Flags.Config_Flag_Lock_Substitution_Matrix, contractBehavior.ConfigFlagLockSubstitutionMatrix);
        hwBehavior.ConfigFlags = configFlags;
    }


    /// <summary>
    /// Compares two states of a status flag set (single-bit enum members only):
    /// returns the risen, gone and currently active flags, and the log level
    /// (Error if any alarm flag is active, Warning if any warning flag is active).
    /// </summary>
    private static (LogLevel Level,
                    IReadOnlyList<TFlag> Risen,
                    IReadOnlyList<TFlag> Gone,
                    IReadOnlyList<TFlag> Current)
        FlagsCompare<TFlag, TState>(TState previousState, TState currentState, TState alarmMask, TState warningMask)
            where TFlag : struct, Enum
            where TState : unmanaged
    {
        var logLevel = LogLevel.Information;
        var risen = new List<TFlag>();
        var gone = new List<TFlag>();
        var active = new List<TFlag>();

        ulong previous = Convert.ToUInt64(previousState);
        ulong current = Convert.ToUInt64(currentState);
        ulong alarm = Convert.ToUInt64(alarmMask);
        ulong warning = Convert.ToUInt64(warningMask);

        foreach (TFlag f in Enum.GetValues<TFlag>())
        {
            ulong v = Convert.ToUInt64(f);

            if (v == 0) continue;                // skip "Dummy/None"
            if ((v & (v - 1)) != 0) continue;    // skip combined/multi-bit values (masks)

            bool inPrevious = (previous & v) != 0;
            bool inCurrent = (current & v) != 0;

            if (inCurrent)
            {
                active.Add(f);
                if (!inPrevious) risen.Add(f);
            }
            else if (inPrevious)
            {
                gone.Add(f);
            }
        }

        if ((current & alarm) != 0) logLevel = LogLevel.Error;
        else if ((current & warning) != 0) logLevel = LogLevel.Warning;

        return (logLevel, risen, gone, active);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Public API
    ////////////////////////////////////////////////////////////////////////////////////////
    // System section

    /// <summary>
    /// Hardware data (SystemInfoStructure) to SystemStateDto
    /// </summary>
    public static SystemStateDto SystemStateHWToContract(Span<byte> data, out uint statusFlags)
    {
        var sysInfo = new SystemInfoStructure(data);
        statusFlags = sysInfo.Status;

        return new SystemStateDto()
        {
            // System status flags
            StatusNormal = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Normal),
            StatusWarningBadSensor = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Warning_Bad_Sensor),
            StatusWarningFanRPM = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Warning_Fan_RPM),
            StatusWarningFanPower = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Warning_Fan_Power),
            StatusWarningTemperature = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Warning_Temperature),
            StatusAlarmBadSensor = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Alarm_Bad_Sensor),
            StatusAlarmFanRPM = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Alarm_Fan_RPM),
            StatusAlarmFanPower = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Alarm_Fan_Power),
            StatusAlarmPOTs = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Alarm_POTs),
            StatusAlarmMAX = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Alarm_MAX),
            StatusAlarmFanFailed = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Alarm_Fan_Failed),
            StatusAlarmLowRam = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Alarm_Low_RAM),
            StatusAlarmPower = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Alarm_Power),
            StatusAlarmTemperature = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Alarm_Temperature),
            StatusAlarmGeneralFailure = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Alarm_General_Failure),
            StatusCategoryWarnings = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Category_Warnings),
            StatusCategoryAlarms = BitTest32(sysInfo.Status, (UInt32)System_Status.Status_Category_Alarms),

            // Configuration intervention flags
            ConfigFlagNoConfig = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_No_Config),
            ConfigFlagHWFailed = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_HW_Failed),
            ConfigFlagPrewarmFailed = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_Prewarm_Failed),
            ConfigFlagFanFailed = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_Fan_Failed),
            ConfigFlagSensorFailed = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_Sensor_Failed),
            ConfigFlagTooManySensorsDetected = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_Too_Many_Sensors_Detected),
            ConfigFlagTooManySensorsStored = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_Too_Many_Sensors_Stored),
            ConfigFlagSensorNotFound = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_Sensor_Not_Found),
            ConfigFlagSensorConfigError = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_Sensor_Config_Error),
            ConfigFlagFanConfigError = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_Fan_Config_Error),
            ConfigFlagSystemConfigError = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_System_Config_Error),
            ConfigFlagEEPROMFailure = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_EEPROM_Failure),
            ConfigFlagAllFanChannelsInvolved = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_All_Fan_Channels_Involved),
            ConfigFlagInitializationInProgress = !BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_Initialization_Complete),
            ConfigFlagHWInProgress = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_HW_In_Progress),
            ConfigFlagFanInProgress = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_Fan_In_Progress),
            ConfigFlagSensorInProgress = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_Sensor_In_Progress),
            ConfigFlagAnyScanInProgress = BitTest32(sysInfo.ConfigurationChangeFlags, (UInt32)Configuration_Change_Flags.CC_Flag_Progress_Mask),

            // Hardware metrics
            FreeRAM = (int)sysInfo.FreeRAM,
            HighLoadPowerBusVoltage = VoltageHWToDouble(sysInfo.HighLoadPowerBusVoltage),
            DigitalPowerBusVoltage = VoltageHWToDouble(sysInfo.DigitalPowerBusVoltage),
            McuPowerBusVoltage = VoltageHWToDouble(sysInfo.McuPowerBusVoltage),

            // User actions
            UserActionRequest = sysInfo.UserActionRequest,

            ActionFlagLastConfigRequestDone = BitTest8(sysInfo.UserActionFlags, (byte)User_Action_Flags.UA_Flag_Last_Config_Request_Done),
            ActionFlagLastConfigRequestError = BitTest8(sysInfo.UserActionFlags, (byte)User_Action_Flags.UA_Flag_Last_Config_Request_Error),
            ActionFlagConfigPageStarted = BitTest8(sysInfo.UserActionFlags, (byte)User_Action_Flags.UA_Flag_Config_Page_Started),
            ActionFlagSystemConfigChanged = BitTest8(sysInfo.UserActionFlags, (byte)User_Action_Flags.UA_Flag_System_Config_Changed),
            ActionFlagFanConfigChanged = BitTest8(sysInfo.UserActionFlags, (byte)User_Action_Flags.UA_Flag_Fan_Config_Changed),
            ActionFlagSubstitutionMatrixChanged = BitTest8(sysInfo.UserActionFlags, (byte)User_Action_Flags.UA_Flag_Substitution_Matrix_Changed),
            ActionFlagSensorConfigChanged = BitTest8(sysInfo.UserActionFlags, (byte)User_Action_Flags.UA_Flag_Sensor_Config_Changed),
            ActionFlagConfigChanged = BitTest8(sysInfo.UserActionFlags, (byte)User_Action_Flags.UA_Flag_Config_Changes_Mask),

            ScanProgress = sysInfo.ScanProgress,
            LastHostRequestErrorCode = OperationResultHWToContract(sysInfo.LastHostRequestErrorCode),

            SystemBehavior = SystemBehaviorHWToContract(sysInfo.SystemBehavior)
        };
    }


    /// <summary>
    /// Analyze System Status flags changes
    /// </summary>
    public static (LogLevel Level,
                   IReadOnlyList<System_Status> Risen,
                   IReadOnlyList<System_Status> Gone,
                   IReadOnlyList<System_Status> Current)
        SystemStateFlagsCompare(UInt32 previousState, UInt32 currentState)
        => FlagsCompare<System_Status, UInt32>(
            previousState,
            currentState,
            (UInt32)System_Status.Status_Category_Alarms,
            (UInt32)System_Status.Status_Category_Warnings);


    /// <summary>
    /// Hardware data (SystemConfigStructure) to SystemConfigDto
    /// </summary>
    public static SystemConfigDto SystemConfigHWToContract(Span<byte> data)
    {
        var sysConfig = new SystemConfigStructure(data);

        return new SystemConfigDto
        {
            Version = new SystemConfigDto.FirmwareVersion(
                (ushort)(sysConfig.FirmwareVersion >> 16),
                (ushort)(sysConfig.FirmwareVersion & 0xFFFF)),

            NormalHLPBVoltage = VoltageHWToDouble(sysConfig.NormalHLPBVoltage),

            FirmwareFlagDisplayEnabled = BitTest8(sysConfig.FirmwareFlags, (byte)Firmware_Flags.Firmware_Flag_Display_Enabled),
            FirmwareFlagUARTLoggerEnabled = BitTest8(sysConfig.FirmwareFlags, (byte)Firmware_Flags.Firmware_Flag_UART_Logger_Enabled),

            SystemBehavior = SystemBehaviorHWToContract(sysConfig.SystemBehavior)
        };
    }


    /// <summary>
    /// SystemConfigDto to HW payload
    /// </summary>
    public static byte[] SystemConfigContractToHW(SystemConfigDto config)
    {
        byte[] payloadArray = new byte[SystemConfigStructure.Size];
        var systemConfig = new SystemConfigStructure(payloadArray.AsSpan());

        // Original firmware parameters (overwritten by the firmware anyway)
        systemConfig.FirmwareVersion = ((UInt32)config.Version.Major << 16) | config.Version.Minor;
        systemConfig.NormalHLPBVoltage = VoltageDoubleToHW(config.NormalHLPBVoltage);

        // Firmware flags
        byte firmwareFlags = 0;
        BitSet8(ref firmwareFlags, (byte)Firmware_Flags.Firmware_Flag_Display_Enabled, config.FirmwareFlagDisplayEnabled);
        BitSet8(ref firmwareFlags, (byte)Firmware_Flags.Firmware_Flag_UART_Logger_Enabled, config.FirmwareFlagUARTLoggerEnabled);
        systemConfig.FirmwareFlags = firmwareFlags;

        MapSystemBehaviorContractOnHW(config.SystemBehavior, systemConfig.SystemBehavior);

        return payloadArray;
    }


    /// <summary>
    /// SystemBehaviorDto to HW payload
    /// </summary>
    public static byte[] SystemBehaviorContractToHW(SystemBehaviorDto contractBehavior)
    {
        byte[] payloadArray = new byte[SystemBehaviorStructure.Size];
        MapSystemBehaviorContractOnHW(contractBehavior, new SystemBehaviorStructure(payloadArray.AsSpan()));
        return payloadArray;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Fan section

    /// <summary>
    /// Hardware data (FanInfoStructure) to FanStateDto
    /// </summary>
    public static FanStateDto FanStateHWToContract(Span<byte> data, int index, out uint statusFlags)
    {
        var fanInfo = new FanInfoStructure(data);
        statusFlags = fanInfo.Status;

        return new FanStateDto()
        {
            Index = index,

            // Hardware errors
            StatusHWErrorPWM = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_PWM_HW_Error),
            StatusHWErrorTACH = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_TACH_HW_Error),
            StatusHWErrorVoltmeter = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Voltmeter_HW_Error),
            StatusHWErrorMOSFET = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_MOSFET_HW_Error),
            StatusHWErrorDCDC = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_DCDC_HW_Error),

            // Runtime errors
            StatusWarningPPM = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_PPM_Warning),
            StatusAlarmPPM = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_PPM_Alarm),
            StatusWarningPWR = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_PWR_Warning),
            StatusAlarmPWR = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_PWR_Alarm),
            StatusFailed = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Failed),

            // Service flags
            StatusHalted = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Halted),
            StatusSwitchedOff = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Switched_Off),

            StatusRunning = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Running),
            StatusSpinningUp = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Spinning_Up),
            StatusSpinUpFailed = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Spin_Up_Failed),
            StatusPIDSuspended = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_PID_Suspended),
            StatusPIDActionRequired = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_PID_Action_Required),

            StatusPreWarming = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_PreWarming),
            StatusWarming = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Warming),
            StatusScanning = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Scanning),
            StatusFatalFailure = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Fatal_Failure),

            StatusHWErrors = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_HW_Errors_Mask),
            StatusWarnings = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Warnings_Mask),
            StatusAlarms = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Alarms_Mask),
            StatusAnyProblems = BitTest32(fanInfo.Status, (UInt32)Fan_Status.Fan_Status_Any_Problems_Mask),

            // Runtime parameters
            TotalErrorsCount = fanInfo.TotalErrorsCount,
            CurrentPPM = fanInfo.CurrentPPM,
            CurrentVoltage = VoltageHWToDouble(fanInfo.CurrentVoltage),

            // Control parameters
            ControlMode = ControlModeHWToContract(fanInfo.ControlMode),
            OperationMode = OperationModeHWToContract(fanInfo.OperationMode),
            ControlBasis = ControlHW16ToDouble(fanInfo.ControlBasis),
            ControlValue = ControlHW16ToDouble(fanInfo.ControlValue),
            ScaledControl = ControlHW16ToDouble(fanInfo.ScaledControl)
        };
    }


    /// <summary>
    /// Analyze Fan Status flags changes
    /// </summary>
    public static (LogLevel Level,
                   IReadOnlyList<Fan_Status> Risen,
                   IReadOnlyList<Fan_Status> Gone,
                   IReadOnlyList<Fan_Status> Current)
        FanStateFlagsCompare(UInt32 previousState, UInt32 currentState)
        => FlagsCompare<Fan_Status, UInt32>(
            previousState,
            currentState,
            (UInt32)Fan_Status.Fan_Status_Alarms_Mask,
            (UInt32)Fan_Status.Fan_Status_Warnings_Mask);


    /// <summary>
    /// Hardware data (FanConfigStructure) to FanConfigDto
    /// </summary>
    public static FanConfigDto FanConfigHWToContract(Span<byte> data, int index)
    {
        var fanConfig = new FanConfigStructure(data);
        return new FanConfigDto()
        {
            Index = index,

            // Fan name
            Name = fanConfig.NameString,

            // Hardware capabilities
            HardwarePwmAvailable = BitTest8(fanConfig.Hardware, (byte)Fan_Hardware.Fan_Hardware_PWM_Available),
            HardwareRpmAvailable = BitTest8(fanConfig.Hardware, (byte)Fan_Hardware.Fan_Hardware_RPM_Available),
            HardwareVoltmeterAvailable = BitTest8(fanConfig.Hardware, (byte)Fan_Hardware.Fan_Hardware_Voltmeter_Available),
            HardwareMosfetAvailable = BitTest8(fanConfig.Hardware, (byte)Fan_Hardware.Fan_Hardware_MOSFET_Available),
            HardwareDcdcAvailable = BitTest8(fanConfig.Hardware, (byte)Fan_Hardware.Fan_Hardware_DCDC_Available),

            // Connected fan's capabilities
            ModelConnected = BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_Connected),
            ModelTachometer = BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_Tachometer),
            ModelPwm = BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_PWM),
            ModelDcdcRangeDetected = BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_DCDC_Range_Detected),
            ModelPwmRangeDetected = BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_PWM_Range_Detected),
            ModelPwmFreewheelTachometer = BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_PWM_Freewheel_Tachometer),
            ModelPwmExcessiveFullSpeed = BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_PWM_Excessive_Full_Speed),

            ModelSize = !BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_Size_Detected) ? FanModelSize.SizeUnknown :
                (BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_Size_Large) ? FanModelSize.SizeLarge : FanModelSize.SizeSmall),

            ModelPower = !BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_Power_Detected) ? FanModelPower.PowerUnknown :
                (BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_Power_High) ? FanModelPower.PowerLarge : FanModelPower.PowerLow),

            ModelClass = !BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_Class_Detected) ? FanModelClass.ClassUnknown :
                (BitTest16(fanConfig.Model, (UInt16)Fan_Model.Fan_Model_Class_Server) ? FanModelClass.ClassServer : FanModelClass.ClassDesktop),

            // Fan options
            OptionPpr = PprHWToContract((byte)(fanConfig.Options & (UInt32)Fan_Options.Fan_Options_PPR_Mask)),
            OptionSpinPowerPwm = SpinPowerHWToContract((byte)((fanConfig.Options >> (int)Fan_Options.Fan_Options_Spin_PWM_Bit_Shift) & (UInt32)Fan_Options.Fan_Options_Spin_Mask)),
            OptionSpinPowerPwr = SpinPowerHWToContract((byte)((fanConfig.Options >> (int)Fan_Options.Fan_Options_Spin_PWR_Bit_Shift) & (UInt32)Fan_Options.Fan_Options_Spin_Mask)),
            OptionRoc = RocHWToContract((byte)((fanConfig.Options >> (int)Fan_Options.Fan_Options_ROC_Bit_Shift) & (UInt32)Fan_Options.Fan_Options_ROC_Mask)),

            OptionPidEnabledPwm = BitTest32(fanConfig.Options, (UInt32)1 << (int)Fan_Options.Fan_Options_PID_Enabled_PWM_Bit_Shift),
            OptionPidEnabledPwr = BitTest32(fanConfig.Options, (UInt32)1 << (int)Fan_Options.Fan_Options_PID_Enabled_PWR_Bit_Shift),
            OptionPidSlowPwm = BitTest32(fanConfig.Options, (UInt32)1 << (int)Fan_Options.Fan_Options_PID_Slow_PWM_Bit_Shift),
            OptionPidSlowPwr = BitTest32(fanConfig.Options, (UInt32)1 << (int)Fan_Options.Fan_Options_PID_Slow_PWR_Bit_Shift),
            OptionAlwaysOn = BitTest32(fanConfig.Options, (UInt32)1 << (int)Fan_Options.Fan_Options_Always_On_Shift),

            // Power HW metrics
            MaxVoltage = VoltageHWToDouble(fanConfig.MaxVoltage),
            MinVoltage = VoltageHWToDouble(fanConfig.MinVoltage),
            MaxVoltageControl = ControlHW16ToDouble(fanConfig.MaxVoltageControl),
            MinVoltageControl = ControlHW16ToDouble(fanConfig.MinVoltageControl),

            // Common Fan metrics
            MaxPPM = fanConfig.MaxPPM,

            // PWM-controlled metrics
            MinPwmControl = ControlHW16ToDouble(fanConfig.MinPWMControl),
            PwmControlledMinPpm = fanConfig.PwmControlledMinPPM,
            PwmPenultimatePpm = fanConfig.PwmPenultimatePPM,

            // DC-DC-controlled metrics
            MinPwrControl = ControlHW16ToDouble(fanConfig.MinPWRControl),
            PwrControlledMinPpm = fanConfig.PwrControlledMinPPM,

            // Control settings
            ControlMode = ControlModeHWToContract(fanConfig.ControlMode),
            OperationMode = OperationModeHWToContract(fanConfig.OperationMode),
            ControlBasis = ControlHW16ToDouble(fanConfig.ControlBasis)
        };
    }


    /// <summary>
    /// FanConfigDto to HW payload (fan index + configuration)
    /// </summary>
    public static byte[] FanConfigContractToHW(FanConfigDto config, byte index)
    {
        byte[] payloadArray = new byte[FanConfigStructure.Size + 1];
        payloadArray[0] = index;
        var fanConfig = new FanConfigStructure(payloadArray.AsSpan(1));

        // Fan name
        fanConfig.NameString = config.Name;

        // Hardware capabilities
        byte hardware = 0;
        BitSet8(ref hardware, (byte)Fan_Hardware.Fan_Hardware_PWM_Available, config.HardwarePwmAvailable);
        BitSet8(ref hardware, (byte)Fan_Hardware.Fan_Hardware_RPM_Available, config.HardwareRpmAvailable);
        BitSet8(ref hardware, (byte)Fan_Hardware.Fan_Hardware_Voltmeter_Available, config.HardwareVoltmeterAvailable);
        BitSet8(ref hardware, (byte)Fan_Hardware.Fan_Hardware_MOSFET_Available, config.HardwareMosfetAvailable);
        BitSet8(ref hardware, (byte)Fan_Hardware.Fan_Hardware_DCDC_Available, config.HardwareDcdcAvailable);
        fanConfig.Hardware = hardware;

        // Connected fan's capabilities
        UInt16 model = 0;
        BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_Connected, config.ModelConnected);
        BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_Tachometer, config.ModelTachometer);
        BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_PWM, config.ModelPwm);
        BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_DCDC_Range_Detected, config.ModelDcdcRangeDetected);
        BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_PWM_Range_Detected, config.ModelPwmRangeDetected);
        BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_PWM_Freewheel_Tachometer, config.ModelPwmFreewheelTachometer);
        BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_PWM_Excessive_Full_Speed, config.ModelPwmExcessiveFullSpeed);

        if (config.ModelSize != FanModelSize.SizeUnknown)
        {
            BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_Size_Detected, true);
            BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_Size_Large, config.ModelSize == FanModelSize.SizeLarge);
        }

        if (config.ModelPower != FanModelPower.PowerUnknown)
        {
            BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_Power_Detected, true);
            BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_Power_High, config.ModelPower == FanModelPower.PowerLarge);
        }

        if (config.ModelClass != FanModelClass.ClassUnknown)
        {
            BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_Class_Detected, true);
            BitSet16(ref model, (UInt16)Fan_Model.Fan_Model_Class_Server, config.ModelClass == FanModelClass.ClassServer);
        }

        fanConfig.Model = model;

        // Fan options
        UInt32 options = PprContractToHW(config.OptionPpr);
        options |= (UInt32)SpinPowerContractToHW(config.OptionSpinPowerPwm) << (int)Fan_Options.Fan_Options_Spin_PWM_Bit_Shift;
        options |= (UInt32)SpinPowerContractToHW(config.OptionSpinPowerPwr) << (int)Fan_Options.Fan_Options_Spin_PWR_Bit_Shift;
        options |= (UInt32)RocContractToHW(config.OptionRoc) << (int)Fan_Options.Fan_Options_ROC_Bit_Shift;

        BitSet32(ref options, (UInt32)1 << (int)Fan_Options.Fan_Options_PID_Enabled_PWM_Bit_Shift, config.OptionPidEnabledPwm);
        BitSet32(ref options, (UInt32)1 << (int)Fan_Options.Fan_Options_PID_Enabled_PWR_Bit_Shift, config.OptionPidEnabledPwr);
        BitSet32(ref options, (UInt32)1 << (int)Fan_Options.Fan_Options_PID_Slow_PWM_Bit_Shift, config.OptionPidSlowPwm);
        BitSet32(ref options, (UInt32)1 << (int)Fan_Options.Fan_Options_PID_Slow_PWR_Bit_Shift, config.OptionPidSlowPwr);
        BitSet32(ref options, (UInt32)1 << (int)Fan_Options.Fan_Options_Always_On_Shift, config.OptionAlwaysOn);

        fanConfig.Options = options;

        // Power HW metrics
        fanConfig.MaxVoltage = VoltageDoubleToHW(config.MaxVoltage);
        fanConfig.MinVoltage = VoltageDoubleToHW(config.MinVoltage);
        fanConfig.MaxVoltageControl = ControlDoubleToHW16(config.MaxVoltageControl);
        fanConfig.MinVoltageControl = ControlDoubleToHW16(config.MinVoltageControl);

        // Common Fan metrics
        fanConfig.MaxPPM = config.MaxPPM;

        // PWM-controlled metrics
        fanConfig.MinPWMControl = ControlDoubleToHW16(config.MinPwmControl);
        fanConfig.PwmControlledMinPPM = config.PwmControlledMinPpm;
        fanConfig.PwmPenultimatePPM = config.PwmPenultimatePpm;

        // DC-DC-controlled metrics
        fanConfig.MinPWRControl = ControlDoubleToHW16(config.MinPwrControl);
        fanConfig.PwrControlledMinPPM = config.PwrControlledMinPpm;

        // Control settings
        fanConfig.ControlMode = ControlModeContractToHW(config.ControlMode);
        fanConfig.OperationMode = OperationModeContractToHW(config.OperationMode);
        fanConfig.ControlBasis = ControlDoubleToHW16(config.ControlBasis);

        return payloadArray;
    }


    /// <summary>
    /// Set Fan Control request payload (fan index + 16-bit control value)
    /// </summary>
    public static byte[] CreateHWSetControlRequest(double control, byte index)
    {
        byte[] payloadArray = new byte[3];
        payloadArray[0] = index;
        PackedStructureMethods.WriteUInt16IntoSpan(ControlDoubleToHW16(control), payloadArray, 1);
        return payloadArray;
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Substitution matrix section
    // Packing: row by row, the diagonal (a fan with itself) skipped,
    // two 4-bit values per byte (high bits first).

    /// <summary>
    /// Hardware data (SubstitutionMatrixStructure) to SubstitutionMatrixDto
    /// </summary>
    public static SubstitutionMatrixDto SubstitutionMatrixHWToContract(Span<byte> data, int fanCount)
    {
        var matrix = new SubstitutionMatrixStructure(data, fanCount);
        var matrixData = matrix.FanSubstitutionMatrix;

        var rows = new SubstitutionMatrixRow[fanCount];
        int byteIndex = 0;
        bool hiBits = true;

        for (int i = 0; i < fanCount; i++)
        {
            var coefficients = new SubstitutionCoefficient[fanCount];

            for (int j = 0; j < fanCount; j++)
            {
                if (i == j)
                {
                    coefficients[j] = SubstitutionCoefficient.Unavailable;
                }
                else
                {
                    byte value = matrixData[byteIndex];
                    if (hiBits)
                    {
                        value >>= 4;
                    }
                    else
                    {
                        value &= 0b1111;
                        byteIndex++;
                    }
                    hiBits = !hiBits;
                    coefficients[j] = (SubstitutionCoefficient)value;     // 0..15 = Coefficient0..Coefficient150
                }
            }

            rows[i] = new SubstitutionMatrixRow { Coefficients = coefficients };
        }

        return new SubstitutionMatrixDto { Rows = rows };
    }


    /// <summary>
    /// SubstitutionMatrixDto to HW payload
    /// </summary>
    /// <exception cref="ArgumentException">The matrix is smaller than fanCount × fanCount.</exception>
    public static byte[] SubstitutionMatrixContractToHW(SubstitutionMatrixDto matrix, int fanCount)
    {
        if ((matrix.Rows.Length < fanCount) ||
            matrix.Rows.Take(fanCount).Any(row => row.Coefficients.Length < fanCount))
            throw new ArgumentException(
                $"The substitution matrix must be at least {fanCount}x{fanCount}.", nameof(matrix));

        byte[] payloadArray = new byte[SubstitutionMatrixStructure.SizeForFanCount(fanCount)];
        var substitutionMatrix = new SubstitutionMatrixStructure(payloadArray.AsSpan(), fanCount);
        var resultArray = substitutionMatrix.FanSubstitutionMatrix;

        int byteIndex = 0;
        bool hiBits = true;

        for (int i = 0; i < fanCount; i++)
        {
            for (int j = 0; j < fanCount; j++)
            {
                if (i == j) continue;   // The diagonal is not transmitted

                // Unavailable (expected only on the diagonal) is sent as 0%
                SubstitutionCoefficient coefficient = matrix.Rows[i].Coefficients[j];
                byte value = (coefficient is >= SubstitutionCoefficient.Coefficient0 and <= SubstitutionCoefficient.Coefficient150)
                    ? (byte)coefficient
                    : (byte)0;

                if (hiBits)
                {
                    resultArray[byteIndex] = (byte)(value << 4);
                }
                else
                {
                    resultArray[byteIndex] |= value;
                    byteIndex++;
                }
                hiBits = !hiBits;
            }
        }

        return (payloadArray);
    }


    ////////////////////////////////////////////////////////////////////////////////////////
    // Sensor section

    /// <summary>
    /// Hardware data (SensorInfoStructure) to SensorStateDto
    /// </summary>
    public static SensorStateDto SensorStateHWToContract(Span<byte> data, int index, int fanCount, out byte statusFlags)
    {
        var sensorInfo = new SensorInfoStructure(data);
        statusFlags = sensorInfo.Status;

        return new SensorStateDto()
        {
            Index = index,

            // Sensor status flags
            StatusConnected = BitTest8(sensorInfo.Status, (byte)TSensor_Status.TSensor_Status_Connected),
            StatusConfigured = BitTest8(sensorInfo.Status, (byte)TSensor_Status.TSensor_Status_Configured),
            StatusValid = BitTest8(sensorInfo.Status, (byte)TSensor_Status.TSensor_Status_Valid),
            StatusError = BitTest8(sensorInfo.Status, (byte)TSensor_Status.TSensor_Status_Error),
            StatusWarning = BitTest8(sensorInfo.Status, (byte)TSensor_Status.TSensor_Status_Warning),
            StatusAlarm = BitTest8(sensorInfo.Status, (byte)TSensor_Status.TSensor_Status_Alarm),
            StatusAnyProblems = BitTest8(sensorInfo.Status, (byte)TSensor_Status.TSensor_Status_Any_Problems_Mask),

            // Sensor Temperature
            Temperature = TemperatureHW16ToDouble(sensorInfo.Temperature),

            // Other Runtime parameters
            TotalErrorsCount = sensorInfo.TotalErrorsCount,
            OvercooledByFanMap = Bit8ToBooleanArray(sensorInfo.OvercooledByFanMap, fanCount)
        };
    }


    /// <summary>
    /// Analyze Sensor Status flags changes
    /// </summary>
    public static (LogLevel Level,
                   IReadOnlyList<TSensor_Status> Risen,
                   IReadOnlyList<TSensor_Status> Gone,
                   IReadOnlyList<TSensor_Status> Current)
        SensorStateFlagsCompare(byte previousState, byte currentState)
        => FlagsCompare<TSensor_Status, byte>(
            previousState,
            currentState,
            (byte)(TSensor_Status.TSensor_Status_Error | TSensor_Status.TSensor_Status_Alarm),
            (byte)TSensor_Status.TSensor_Status_Any_Problems_Mask);


    /// <summary>
    /// Hardware data (SensorConfigStructure) to SensorConfigDto
    /// </summary>
    public static SensorConfigDto SensorConfigHWToContract(Span<byte> data, int index, int fanCount)
    {
        var sensorConfig = new SensorConfigStructure(data, fanCount);
        var fanImpactsArray = sensorConfig.FanImpacts.ToArray();

        return new SensorConfigDto()
        {
            Index = index,

            SerialNumber = sensorConfig.SerialNumber.ToArray(),
            Name = sensorConfig.NameString,
            Type = SensorTypeHWToContract(sensorConfig.Type),

            // Configuration
            Setting = sensorConfig.Config,
            WarningThreshold = sensorConfig.WarningThreshold,
            AlarmThreshold = sensorConfig.AlarmThreshold,

            IsVisible = BitTest8(sensorConfig.Flags, (byte)TSensor_Flags.TSensor_Flag_Visible),
            IsActive = BitTest8(sensorConfig.Flags, (byte)TSensor_Flags.TSensor_Flag_Active),
            IsDeletable = BitTest8(sensorConfig.Flags, (byte)TSensor_Flags.TSensor_Flag_Deletable),
            IsPermanent = BitTest8(sensorConfig.Flags, (byte)TSensor_Flags.TSensor_Flag_Permanent),
            IsIgnorable = BitTest8(sensorConfig.Flags, (byte)TSensor_Flags.TSensor_Flag_Ignore_If_Unavailable),

            LowThresholds = TemperatureArrayHWToContract(sensorConfig.LowThresholds.ToArray()),
            HighThresholds = TemperatureArrayHWToContract(sensorConfig.HighThresholds.ToArray()),

            FanImpact = FanImpactArrayHWToContract(fanImpactsArray),
            UnavailableSensorFanImpact = FanImpactArrayHWToContract(fanImpactsArray, true)
        };
    }


    /// <summary>
    /// SensorConfigDto to HW payload
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The serial number is not 8 bytes long, or a per-fan array doesn't have fanCount elements.
    /// </exception>
    public static byte[] SensorConfigContractToHW(SensorConfigDto config, int fanCount)
    {
        if (config.SerialNumber.Length != SensorConfigStructure.SerialNumberSize)
            throw new ArgumentException(
                $"The sensor serial number must be {SensorConfigStructure.SerialNumberSize} bytes long.",
                nameof(config));

        if ((config.LowThresholds.Length != fanCount) ||
            (config.HighThresholds.Length != fanCount) ||
            (config.FanImpact.Length != fanCount) ||
            (config.UnavailableSensorFanImpact.Length != fanCount))
            throw new ArgumentException(
                $"The sensor configuration must contain {fanCount} value(s) per fan parameter.",
                nameof(config));

        byte[] payloadArray = new byte[SensorConfigStructure.SizeForFanCount(fanCount)];
        var sensorConfig = new SensorConfigStructure(payloadArray.AsSpan(), fanCount);

        // Identity
        sensorConfig.SerialNumber = config.SerialNumber.AsSpan();
        sensorConfig.NameString = config.Name;
        sensorConfig.Type = SensorTypeContractToHW(config.Type);

        // Configuration
        sensorConfig.Config = config.Setting;
        sensorConfig.WarningThreshold = (byte)Math.Clamp(config.WarningThreshold, byte.MinValue, byte.MaxValue);
        sensorConfig.AlarmThreshold = (byte)Math.Clamp(config.AlarmThreshold, byte.MinValue, byte.MaxValue);

        byte flags = 0;
        BitSet8(ref flags, (byte)TSensor_Flags.TSensor_Flag_Visible, config.IsVisible);
        BitSet8(ref flags, (byte)TSensor_Flags.TSensor_Flag_Active, config.IsActive);
        BitSet8(ref flags, (byte)TSensor_Flags.TSensor_Flag_Deletable, config.IsDeletable);
        BitSet8(ref flags, (byte)TSensor_Flags.TSensor_Flag_Permanent, config.IsPermanent);
        BitSet8(ref flags, (byte)TSensor_Flags.TSensor_Flag_Ignore_If_Unavailable, config.IsIgnorable);
        sensorConfig.Flags = flags;

        sensorConfig.LowThresholds = TemperatureArrayContractToHW(config.LowThresholds);
        sensorConfig.HighThresholds = TemperatureArrayContractToHW(config.HighThresholds);
        sensorConfig.FanImpacts = FanImpactArrayContractToHW(config.FanImpact, config.UnavailableSensorFanImpact);

        return payloadArray;
    }


    /// <summary>
    /// Host sensor temperature update payload (serial number + 16-bit temperature in 1/16 °C)
    /// </summary>
    public static byte[] CreateHWUpdateSensorTemperatureRequest(byte[] serialNumber, double temperature)
    {
        byte[] payloadArray = new byte[serialNumber.Length + 2];
        serialNumber.CopyTo(payloadArray, 0);
        PackedStructureMethods.WriteInt16IntoSpan(TemperatureDoubleToHW16(temperature), payloadArray, serialNumber.Length);
        return payloadArray;
    }

}

// END-OF-FILE
