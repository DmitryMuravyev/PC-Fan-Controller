////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// DtoDefaults.cs
////////////////////////////////////////////////////////////////////////////////////////

namespace FanController.Contracts;

/// <summary>
/// Single wrapper object for all defaults
/// </summary>
public static class DtoDefaults
{
    /// <summary>
    /// Controller (system) defaults
    /// </summary>
    public static class Controller
    {
        // Default values, shared by DTOs and any
        // field initializers that need it without constructing a full DTO.
        public const BuzzerWaveform DefaultBuzzerWaveform = BuzzerWaveform.Cricket;
        public const AlarmProfile DefaultAlarmProfile = AlarmProfile.Noisy;
        public const double DefaultDisplayBrightness = 0.5;
        public const DisplayRotation DefaultDisplayRotation = DisplayRotation.Rotate0;
        public const OperationResult DefaultLastHostRequestErrorCode = OperationResult.OK;


        /// <summary>
        /// Default System Behavior
        /// </summary>
        public static SystemBehaviorDto NewBehavior() => new()
        {
            ConfigBuzzerWaveform = DefaultBuzzerWaveform,
            DisplayBrightness = DefaultDisplayBrightness,
            DisplayRotation = DefaultDisplayRotation
        };

        /// <summary>
        /// Default System State
        /// </summary>
        public static SystemStateDto NewState() => new()
        {
            LastHostRequestErrorCode = DefaultLastHostRequestErrorCode,
            SystemBehavior = NewBehavior()
        };

        /// <summary>
        /// Default System Config
        /// </summary>
        public static SystemConfigDto NewConfig() => new()
        {
            SystemBehavior = NewBehavior()
        };
    }


    /// <summary>
    /// Substitution matrix defaults
    /// </summary>
    public static class SubstitutionMatrix
    {
        /// <summary>
        /// Default (empty) Substitution Matrix
        /// </summary>
        public static SubstitutionMatrixDto NewMatrix() => new();
    }


    /// <summary>
    /// Fan defaults
    /// </summary>
    public static class Fan
    {
        // Default values, shared by DTOs and any
        // field initializers that need it without constructing a full DTO.
        public const FanModelSize DefaultModelSize = FanModelSize.SizeUnknown;
        public const FanModelPower DefaultModelPower = FanModelPower.PowerUnknown;
        public const FanModelClass DefaultModelClass = FanModelClass.ClassUnknown;
        public const int DefaultOptionPpr = 2;
        public const FanOptionSpinPower DefaultOptionSpinPower = FanOptionSpinPower.SpinPower100;
        public const FanOptionRoc DefaultOptionRoc = FanOptionRoc.Roc512;          // Firmware default (1.28 s)
        public const double UnknownVoltageSetting = -1.0;
        public const FanControlMode DefaultControlMode = FanControlMode.ControlDisabled;
        public const FanOperationMode DefaultOperationMode = FanOperationMode.OperationNormal;
        public const double DefaultControlBasis = 0.5;


        /// <summary>
        /// Default Fan State
        /// </summary>
        public static FanStateDto NewState() => new()
        {
            ControlMode = DefaultControlMode,
            OperationMode = DefaultOperationMode
        };

        /// <summary>
        /// Default Fan Config
        /// </summary>
        public static FanConfigDto NewConfig() => new()
        {
            ModelSize = DefaultModelSize,
            ModelPower = DefaultModelPower,
            ModelClass = DefaultModelClass,
            OptionPpr = DefaultOptionPpr,
            OptionSpinPowerPwm = DefaultOptionSpinPower,
            OptionSpinPowerPwr = DefaultOptionSpinPower,
            OptionRoc = DefaultOptionRoc,
            MaxVoltage = UnknownVoltageSetting,
            MinVoltage = UnknownVoltageSetting,
            ControlMode = DefaultControlMode,
            OperationMode = DefaultOperationMode,
            ControlBasis = DefaultControlBasis
        };
    }


    /// <summary>
    /// Sensor defaults
    /// </summary>
    public static class Sensor
    {
        // Default values, shared by DTOs and any
        // field initializers that need it without constructing a full DTO.
        public const TSensorType DefaultType = TSensorType.Unknown;
        public const int DefaultLowTemperature = 20;            // 20 °C - below this temperature the Fan is Off or runs at minimum speed (0%)
        public const int DefaultHighTemperature = 50;           // 50 °C - above this temperature the Fan is On or runs at maximum speed (100%)
        public const int DefaultWarningTemperature = 60;        // 60 °C - warning temperature
        public const int DefaultAlarmTemperature = 70;          // 70 °C - alarm temperature
        public const double DefaultImpact = 1.0;                // 100% - default fan impact
        public const double DefaultUnavailableImpact = 0.6;     // 60% - default fan impact if the sensor is unavailable
        public const UInt32 DefaultSWSetting = 60;              // 60 seconds - the time for the system to boot up and run the control software
                                                                // before the alarm will be fired.

        /// <summary>
        /// Default Sensor State
        /// </summary>
        public static SensorStateDto NewState() => new();

        /// <summary>
        /// Default Sensor Config
        /// </summary>
        public static SensorConfigDto NewConfig() => new()
        {
            Type = DefaultType,
            WarningThreshold = DefaultWarningTemperature,
            AlarmThreshold = DefaultAlarmTemperature
        };
    }

}

// END-OF-FILE
