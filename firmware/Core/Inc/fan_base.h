////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// fan_base.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	fan_base.h
 * @brief	An abstract class that implements the basic functions for fan operation:
 * 			hardware and fan detection, smooth control, on/off logic, spin-up,
 * 			PID speed control and error handling.
 *
 * @attention	THE DETECTION FUNCTIONS OF THE EQUIPMENT AND THE FAN MODELS WORK NORMALLY
 * 				ONLY IF THE POWER SUPPLY OF THE MICROCONTROLLER IS STABLE ENOUGH!
 */

#ifndef _FAN_BASE_
#define _FAN_BASE_


// Includes
#include "stm32f0xx_hal.h"
#include <stdint.h>
#include <stdbool.h>
#include <limits.h>
#include <string.h>
#include <string_utils.h>



// Class-level definitions
// Most of these constants are intended for internal use,
// but I'll still leave them in the header file for convenience.

// Basic constants
#define FAN_NAME_SIZE							8			///< Fan Name string length (without the terminating zero)
#define FAN_CONTROL_BIT_WIDTH					16			///< Bit width of the fan speed Control Value
#define FAN_CONTROL_FULL_RANGE					(1 << FAN_CONTROL_BIT_WIDTH)		///< Full range of Control Value
#define FAN_CONTROL_HALF_RANGE					(FAN_CONTROL_FULL_RANGE >> 1)		///< 0x8000
#define FAN_CONTROL_QUARTER_RANGE				(FAN_CONTROL_FULL_RANGE >> 2)		///< 0x4000
#define FAN_CONTROL_TOP							(FAN_CONTROL_FULL_RANGE - 1)		///< 0xFFFF

#define FAN_ROC_UPDATE_TIME_INTERVAL			10			///< Control Value updating procedure calling interval in milliseconds.
															///< For normal operation, you must call doIncrements() procedure 10-1000 times per second.
#define FAN_PPM_FRACTION_BITS					8			///< Q-format bits for fractional part in EMA calculations
#define FAN_INDEFINITE_PPM						0xFFFFFFFF	///< Just a constant to indicate an impossible PPM value
#define FAN_INDEFINITE_VOLTAGE					0xFFFF		///< And the same for voltage
#define FAN_MOSFET_MODE_HYSTERESIS				1024		///< For on/off mode, we'll add a small hysteresis
															///< so that switching doesn't happen too often.
#define FAN_BASIC_OPERATIONS_PERIOD				pulseMeasurementDelay()		///< A constant based on which the doBasicOperations() function call period can be set.

// Hardware and fan scanning constants
#define FAN_SCAN_MIN_VOLTAGE					4			///< Minimum power supply voltage (used to check the voltmeter operation)
#define FAN_SCAN_MAX_VOLTAGE					32			///< Maximum power supply voltage (used to check the voltmeter operation)
#define FAN_SCAN_MIN_VOLTAGE_RANGE				2			///< Minimum DC-DC voltage range
#define FAN_SCAN_MIN_MOSFET_DETECTION_VOLTAGE	1			///< Minimum voltage to detect the presence of a MOSFET

#define FAN_SCAN_MAX_STEPS						4			///< The maximum number of steps by which the control signal can be changed
															///< when searching for the boundaries of the DC-DC control range.
#define FAN_SCAN_VOLTAGE_UNIFORM_CHANGES		3			///< The number of similar changes of voltage sufficient to trigger algorithm
#define FAN_SCAN_VOLTAGE_TIMEOUT				10000		///< DC-DC output voltage scanning timeout (10 sec)
#define FAN_SCAN_VOLTAGE_TIME_CONSTANT			100			///< Time enough to change the DC-DC voltage by 1 step.
															///< ATTENTION!!! Increase this constant by a factor of two or more
															///< in case if the MCU is powered by an unstable power source!


#define FAN_SCAN_PWM_DETECTION_RANGE_SHIFT		1			///< Used to detect the possibility of PWM control. The maximum fan speed
															///< at 100% PWM should be twice as high as the minimum (0% PWM).
#define FAN_SCAN_PWM_EXCESSIVE_FS_STEP			128			///< Control step (~1%, equal to one 9-bit PWM step) used to detect
															///< a significant speed increase between 99% and 100% PWM
#define FAN_SCAN_PWM_EXCESSIVE_FS_PERCENT		5			///< Speed increase between 99% and 100% PWM (in percent of the operating range)
															///< that is considered significant

#define FAN_SCAN_PPM_MAX_VALUE_SHIFT			1			///< Maximum PPM value calculation by shift
#define FAN_SCAN_PPM_MAX_DELTA_SHIFT			2			///< Maximum Delta value calculation by shift
#define FAN_SCAN_PPM_TIMEOUT					180000		///< PPM scanning timeout (3 min)
#define FAN_SCAN_PPM_POINTS						12			///< The number of points should be from 2 to 32
#define FAN_SCAN_PPM_MIN_POINTS					10			///< The minimum number of points from which it is possible to draw a conclusion
															///< about the stabilization of the revolutions (2 - FAN_SCAN_PPM_POINTS).
#define FAN_SCAN_PPM_DELTA_THRESHOLD_FACTOR 	100			///< Calculation of the delta threshold using the coefficient
#define FAN_SCAN_PPM_DELTA_THRESHOLD_SHIFT		6			///< Calculation of the delta threshold via the shift
#define FAN_SCAN_PPM_GROWTH_GRUDGE				16			///< How vindictive the algorithm should be about growth or reducing of the rotation speed
#define FAN_SCAN_PPM_REDUCTION_GRUDGE			(FAN_SCAN_PPM_GROWTH_GRUDGE + (FAN_SCAN_PPM_GROWTH_GRUDGE >> 1))	///< The same for the speed reduction
#define FAN_SCAN_PPM_GRUDGE_STEP				2			///< The quantum of grudge
#define FAN_SCAN_PPM_MERCY_STEP					1			///< The quantum of mercy
#define FAN_SCAN_PPM_UNIFORM_CHANGES			3			///< The number of similar changes of PPM/Delta sufficient to trigger algorithm
#define FAN_SCAN_PPM_JITTER						1			///< Acceptable jitter of the speed change (grudge) vector

#define FAN_SCAN_PPM_MAX_START_ATTEMPTS			3			///< Maximum number of fan start attempts
#define FAN_SCAN_PPM_MAX_WRONG_PPM_COUNT		3			///< Maximum number of incorrect PPM values
#define FAN_SCAN_PPM_MAX_WRONG_DELTA_COUNT		3			///< Maximum number of incorrect PPM Delta values
#define FAN_SCAN_PPM_SPEED_DIRECTION_CHANGES_COUNT	5		///< The number of acceleration/deceleration cycles, sufficient to be sure
															///< that the fan will continue to work this way.
#define FAN_SCAN_PPM_ACCURACY_LIMIT_STEPS		128			///< We have to limit the accuracy of determining the minimum
															///< rotation speed so that it doesn't take too long.

#define FAN_SCAN_SPIN_UP_TIMEOUT				12500		///< Fan spin-up timeout
#define FAN_SCAN_STOP_TIMEOUT					25000		///< Fan stop delay
#define FAN_SCAN_SLOW_DOWN_TIMEOUT				2000		///< The time it takes for the fan to noticeably slow down
															///< when the control signal is reduced from 100% to 50%
#define FAN_SCAN_SHORT_WARM_UP_TIME				60000		///< Short warm-up time - if we have already warmed up the fan on 50% power (1 min).
#define FAN_SCAN_ADDITIONAL_WARM_UP_TIME		120000		///< Additional warm-up time if the fan is cold (2 min).
#define FAN_SCAN_PREWARMING_TIME_SHIFT			1			///< Let's assume that warming up at 50% power should last 2 times longer than at 100%.

// Experimental part of fan detection
#define FAN_SCAN_MINCONTROL_SUPPLEMENT_STEPS	2			///< A small supplement value for more stable operation at minimum speed
#define FAN_SCAN_VELOCITY_RATIO_SHIFT			16			///< Just some math for primitive calculations
#define FAN_SCAN_SIZE_SPIN_THRESHOLD_PWM		80 			///< An attempt to guess the size of the fan based on the relative speed
#define FAN_SCAN_SIZE_STOP_THRESHOLD_PWM		45			///< of its acceleration/deceleration in PWM Control Mode.
#define FAN_SCAN_SIZE_SPIN_THRESHOLD_DCDC		50			///< The same, but for the DC-DC Control Mode (acceleration)
#define FAN_SCAN_SIZE_STOP_THRESHOLD_DCDC		30			///< The same, but for the DC-DC Control Mode (deceleration)
#define FAN_SCAN_CLASS_RPM_THRESHOLD			8000		///< This is a naive assumption that if a fan gives two pulses per revolution
															///< and rotates at a speed of more than 8000 revolutions per minute, then
															///< this is a server fan (of course, this is not always the case).

// PID coefficients in 1/256 fractions
#define FAN_PID_PROPORTIONAL_K					16			///< 16 / 256 = 0.0625
#define FAN_PID_INTEGRAL_K						80			///< 80 / 256 = 0.3125
#define FAN_PID_DERIVATIVE_K					48			///< 48 / 256 = 0.1875
#define FAN_PID_SLOW_PROPORTIONAL_K				0			///< No proportional part
#define FAN_PID_SLOW_INTEGRAL_K					64			///< 64 / 256 = 0.25
#define FAN_PID_SLOW_DERIVATIVE_K				16			///< 16 / 256 = 0.0625
#define FAN_PID_OPERATING_RANGE_SHIFT			2			///< Used to determine whether the PID can handle a significant change in the Control Value
#define FAN_PID_SUSPEND_TIMEOUT					15000		///< Sufficient time to spin-up or slow down any fans to fit the operating range of the PID

// Error correction constants
#define FAN_ERR_CONTROL_NEAR_RANGE_SHIFT		4			///< Used to check if current Control Value is close to the Target one
#define FAN_ERR_LOW_PPM_THRESHOLD_SHIFT			1			///< The fan speed is considered low if it is 2 times less than the Target one
#define FAN_ERR_MAX_SUPPLEMENT_RANGE_SHIFT		2			///< Used to determine the maximum possible low-speed correction supplement
#define FAN_ERR_PWR_ALARM_THRESHOLD_SHIFT		2			///< If the voltage differs from the expected by 1/4, then this is enough for an alarm.
#define FAN_ERR_PWR_WARNING_ADDITIONAL_SHIFT	1			///< If the voltage differs from the expected by 1/8, then this is enough for a warning.

#define FAN_ERR_COUNTER_BITS_PER_SLOT			4			///< Bits per one counter
#define FAN_ERR_COUNTER_MASK					((1 << FAN_ERR_COUNTER_BITS_PER_SLOT) - 1)	///< Single counter mask
#define FAN_ERR_COUNTER_MAX						FAN_ERR_COUNTER_MASK						///< Error counter max value
#define FAN_ERR_COUNTER_INCREMENT				5			///< Counter increment in case of error
#define FAN_ERR_COUNTER_DECREMENT				1			///< Counter decrement if there are no errors

#define FAN_ERR_COUNTER_PWM_SHIFT				0			///< PWM HW errors counter bit position
#define FAN_ERR_COUNTER_TACH_SHIFT				4			///< Tachometer HW errors counter bit position
#define FAN_ERR_COUNTER_VMETER_SHIFT			8			///< Voltmeter HW errors counter bit position
#define FAN_ERR_COUNTER_DCDC_SHIFT				12			///< DC-DC (POT/DAC) HW errors counter bit position
#define FAN_ERR_COUNTER_MOSFET_SHIFT			16			///< MOSFET HW errors counter bit position

#define FAN_ERR_COUNTER_PWR_WARNING_SHIFT		0			///< Power warning counter bit position
#define FAN_ERR_COUNTER_PWR_ALARM_SHIFT			4			///< Power alarm counter bit position
#define FAN_ERR_COUNTER_PPM_WARNING_SHIFT		8			///< PPM warning counter bit position
#define FAN_ERR_COUNTER_PPM_ALARM_SHIFT			12			///< PPM alarm counter bit position
#define FAN_ERR_COUNTER_FAN_FAILED_SHIFT		16			///< Fan failed counter bit position
#define FAN_ERR_COUNTER_FAN_ERRORS_MASK			((FAN_ERR_COUNTER_MASK << FAN_ERR_COUNTER_PWR_ALARM_SHIFT) | (FAN_ERR_COUNTER_MASK << FAN_ERR_COUNTER_PPM_ALARM_SHIFT))
															///< Used to check if there are any errors associated with the fan
#define FAN_ERR_COUNTER_PPM_ERRORS_MASK			((FAN_ERR_COUNTER_MASK << FAN_ERR_COUNTER_PPM_WARNING_SHIFT) | (FAN_ERR_COUNTER_MASK << FAN_ERR_COUNTER_PPM_ALARM_SHIFT))
															///< Used to check if there are any errors related to PPM

#define FAN_ERR_PRESSURE_FAILURE_THRESHOLD		900000		///< 15 minutes of total error pressure: 15m * 60s * 1000ms
#define FAN_ERR_PRESSURE_STEP					150000		///< 1 error gives us 2.5 minutes of pressure: 2.5m * 60 * 1000ms.
															///< So, we will be tolerant to errors only if
															///< there are less than 6 of them in 15 minutes.
#define FAN_ERR_PRESSURE_MAX					3600000		///< Max pressure = 1 hour


// Types

/**
 * @brief	Available hardware capabilities of the fan channel (bit flags).
 */
typedef enum
{
	Fan_Hardware_Undefined				= 0b00000000,		///< No hardware installed/detected
	Fan_Hardware_PWM_Available			= 0b00000001,		///< PWM Control available via MAX31790 or internal Timer
	Fan_Hardware_RPM_Available			= 0b00000010,		///< RPM measurement available via MAX31790 or internal Timer
	Fan_Hardware_Voltmeter_Available	= 0b00000100,		///< Voltage monitoring available (ADC or INA or smth)
	Fan_Hardware_MOSFET_Available		= 0b00001000,		///< Fan power is controlled by MOSFET or DC-DC Enable pin
	Fan_Hardware_DCDC_Available			= 0b00010000,		///< Digipot or DAC available for voltage control via DC-DC
} Fan_Hardware;


/**
 * @brief	Connected fan's parameters (bit flags).
 */
typedef enum
{
	Fan_Model_Unconnected				= 0b0000000000000000,		///< No fan connected
	Fan_Model_Connected					= 0b0000000000000001,		///< 2-wire fan connected (no tacho, no PWM, fan connected)
	Fan_Model_Tachometer				= 0b0000000000000010,		///< 3-wire fan connected (tacho enabled)
	Fan_Model_PWM						= 0b0000000000000100,		///< 4-wire fan connected (PWM enabled)

	Fan_Model_DCDC_Range_Detected		= 0b0000000000010000,		///< The DC-DC control range is detected
	Fan_Model_PWM_Range_Detected		= 0b0000000000100000,		///< The PWM control range is detected

	Fan_Model_PWM_Freewheel_Tachometer	= 0b0000000010000000,		///< The tachometer is functional at 0% PWM
	Fan_Model_PWM_Excessive_Full_Speed	= 0b0000000100000000,		///< Significant speed increase between 99% and 100% PWM

	Fan_Model_Size_Detected				= 0b0000010000000000,		///< Fan size detected
	Fan_Model_Size_Large				= 0b0000100000000000,		///< Fan size:	1 = Large fan size (strong inertia),
																	///<			0 = Small fan size (low inertia).
	Fan_Model_Power_Detected			= 0b0001000000000000,		///< Fan power detected
	Fan_Model_Power_High				= 0b0010000000000000,		///< Fan power:	1 = Powerful fan (spins up quickly to high speeds),
																	///<			0 = Low-power fan (spins up slowly).
	Fan_Model_Class_Detected			= 0b0100000000000000,		///< Fan family (class) detected
	Fan_Model_Class_Server				= 0b1000000000000000,		///< Fan family:	1 = Server family (high RPM, 4 pins),
																	///<				0 = Desktop family (low RPM, 2/3/4 pins).

	Fan_Model_Connectivity_Mask			= 0b0000000000000111,		///< Fan wiring
	Fan_Model_Capabilities_Mask			= 0b0000000110110000,		///< Fan hardware capabilities mask
	Fan_Model_Features_Mask				= 0b1111110000000000		///< Fan model features mask

} Fan_Model;


/**
 * @brief	Fan Control Mode (the hardware used to control the fan speed).
 */
typedef enum
{
	Fan_Control_Disabled				= 0b00000000,		///< No fan connected or it is disabled by user
	Fan_Control_PWM						= 0b00000001,		///< PWM Control Mode
	Fan_Control_DCDC					= 0b00000010,		///< DC-DC Control Mode
	Fan_Control_MOSFET					= 0b00000011,		///< ON/OFF (MOSFET or DC-DC Enable pin) Control Mode
	Fan_Control_Mask					= 0b00000011,		///< Mask for fan Control Mode
} Fan_Control_Mode;


/**
 * @brief	Fan Operation Mode (how the Control Value is interpreted).
 */
typedef enum
{
	Fan_Operation_Normal				= 0b00000000,		///< Recalculation of the Control Value in accordance with the basis level of fan's power
	Fan_Operation_Manual				= 0b00000001,		///< Only correction of the Control Value to the operating range
	Fan_Operation_Silent				= 0b00000010,		///< Minimum rotation speed
	Fan_Operation_Turbo					= 0b00000011,		///< 100%
	Fan_Operation_Mask					= 0b00000011		///< Operation parameters mask
} Fan_Operation_Mode;


/**
 * @brief	Fan start/stop/spin-up/scan Service Modes.
 * @note	The Service Modes take precedence over the normal Control Mode.
 */
typedef enum
{
	Fan_Service_Mode_None				= 0b00000000,		///< Service mode off
	Fan_Service_Mode_PWM				= Fan_Control_PWM,	///< PWM Service Mode
	Fan_Service_Mode_DCDC				= Fan_Control_DCDC,	///< DC-DC Service Mode
	Fan_Service_Mode_MOSFET				= Fan_Control_MOSFET	///< ON/OFF (MOSFET or DC-DC Enable pin) Service Mode
} Fan_Service_Modes;


/**
 * @brief	Fan options (start/spin options, Pulses Per Revolution and so on),
 * 			packed into the Fan_Config::options field.
 */
typedef enum
{
	// Fan Pulses Per Revolution options
	// RPM = PPM / PPR = PPM >> Fan_Options_PPR
	Fan_Options_PPR_Default				= 0b01,				///< Pulses Per Revolution = 2
	Fan_Options_PPR_1					= 0b00,				///< RPM = PPM = PPM >> 0
	Fan_Options_PPR_2					= 0b01,				///< RPM = PPM / 2 = PPM >> 1 (default value)
	Fan_Options_PPR_4					= 0b10,				///< RPM = PPM / 4 = PPM >> 2
	Fan_Options_PPR_Mask				= 0b11,				///< PPR option mask

	// Spin-up control options
	// Value is calculated as a quarter of the Control Range multiplied by some coefficient.
	Fan_Options_Spin_Default			= 0b100,			///< Default spin-up Control = 100%
	Fan_Options_Spin_0					= 0b000,			///< The fan does not need to be spinned-up and rotates even when the Control Value is zero.
	Fan_Options_Spin_25					= 0b001,			///< Spin-up value = 1/4 of full Control Range * 1 = 25%
	Fan_Options_Spin_50					= 0b010,			///< Spin-up value = 1/4 of full Control Range * 2 = 50%
	Fan_Options_Spin_75					= 0b011,			///< Spin-up value = 1/4 of full Control Range * 3 = 75%
	Fan_Options_Spin_100				= 0b100,			///< Spin-up value = 1/4 of full Control Range * 4 = 100% (default)
	Fan_Options_Spin_Mask				= 0b111,			///< Spin-up option mask
	Fan_Options_Spin_PWM_Bit_Shift		= 2,				///< PWM spin-up option first bit address
	Fan_Options_Spin_PWR_Bit_Shift		= 5,				///< PWR spin-up option first bit address

	// Same for Rate-of-Change
	// Increment or decrement of the Control Value per one step
	// Examples are given for the case when calls to the Control Value update procedure are performed 100 times per second:
	Fan_Options_ROC_Default				= 0b1001,			///< Default value = 1.28s
	Fan_Options_ROC_1					= 0b0000,			///< 655 seconds for the full range (100 calls per second)
	Fan_Options_ROC_2					= 0b0001,			///< 328 seconds for the full range (100 calls per second)
	Fan_Options_ROC_4					= 0b0010,			///< 164 seconds
	Fan_Options_ROC_8					= 0b0011,			///< 82 seconds
	Fan_Options_ROC_16					= 0b0100,			///< 41s
	Fan_Options_ROC_32					= 0b0101,			///< 20.5s
	Fan_Options_ROC_64					= 0b0110,			///< 10.2s
	Fan_Options_ROC_128					= 0b0111,			///< 5.1s
	Fan_Options_ROC_256					= 0b1000,			///< 2.56s
	Fan_Options_ROC_512					= 0b1001,			///< 1.28s
	Fan_Options_ROC_1024				= 0b1010,			///< 0.64s
	Fan_Options_ROC_2048				= 0b1011,			///< 0.32s
	Fan_Options_ROC_4096				= 0b1100,			///< 0.16s
	Fan_Options_ROC_8192				= 0b1101,			///< 0.08s
	Fan_Options_ROC_16384				= 0b1110,			///< 0.04s
	Fan_Options_ROC_Disabled			= 0b1111,			///< 0s (instantaneous change)
	Fan_Options_ROC_Mask				= 0b1111,			///< ROC option mask
	Fan_Options_ROC_Bit_Shift			= 8,				///< ROC option first bit address

	// Fan rotation speed PID controller options:
	Fan_Options_PID_Enabled_PWM_Bit_Shift		= 16,		///< Enable PID for PWM Control Mode
	Fan_Options_PID_Enabled_PWR_Bit_Shift		= 17,		///< Enable PID for DC-DC Control Mode
	Fan_Options_PID_Slow_PWM_Bit_Shift	= 18,				///< Use "slow" PID coefficients for PWM Control Mode (may be useful for server fans)
	Fan_Options_PID_Slow_PWR_Bit_Shift	= 19,				///< Use "slow" PID coefficients for DC-DC Control Mode (may be useful for large high-inertia fans)

	// Other fan options:
	Fan_Options_Always_On_Shift			= 20				///< The fan will not be powered off when it is halted (e.g. the sensors are overcooled)

} Fan_Options;


/**
 * @brief	Global static configuration flags (common for all fans, see Fan_base::configFlags).
 */
typedef enum
{
	Fan_Config_Flag_Empty				= 0b00000000,		///< Dummy flag
	Fan_Config_Flag_Professional_Mode	= 0b00000001,		///< The Pro mode implies the ability to switch between PWM and DC-DC
															///< Control Modes in case of problems with one of them, and also prohibits
															///< the fan from leaving the FAILED status if it has got it once.
	Fan_Config_Flag_Fan_Model_Detection	= 0b00000010,		///< Try to detect the features of the fan (size, power, class).
	Fan_Config_Flag_Mask				= 0b00000011		///< Mask of all global flags
} Fan_Global_Config_Flags;


/**
 * @brief	Fan status (bit flags of the Fan_Info::status field).
 */
typedef enum
{
	// Hardware errors
	Fan_Status_PWM_HW_Error				= 0b00000000000000000000000000000001,		///< PWM operation/connection error
	Fan_Status_TACH_HW_Error			= 0b00000000000000000000000000000010,		///< PPM measurement operation/connection error
	Fan_Status_Voltmeter_HW_Error		= 0b00000000000000000000000000000100,		///< Voltage measurement operation/connection error
	Fan_Status_MOSFET_HW_Error			= 0b00000000000000000000000000001000,		///< Fan power On/Off GPIO error
	Fan_Status_DCDC_HW_Error			= 0b00000000000000000000000000010000,		///< DigiPOT or DAC connection error

	// Runtime errors
	Fan_Status_PPM_Warning				= 0b00000000000000000000010000000000,		///< Fan PPM is persistently below 1/2 of the expected one
	Fan_Status_PPM_Alarm				= 0b00000000000000000000100000000000,		///< ...and the fan cannot be recovered by spinning-up
	Fan_Status_PWR_Warning				= 0b00000000000000000001000000000000,		///< Fan power voltage differs from the expected by more than 1/8
	Fan_Status_PWR_Alarm				= 0b00000000000000000010000000000000,		///< Fan power voltage differs from the expected by more than 1/4
	Fan_Status_Failed					= 0b00000000000000000100000000000000,		///< The fan could not be started or it stopped completely

	// Service flags
	Fan_Status_Halted					= 0b00000000000001000000000000000000,		///< Temporary halt the fan (e.g. if the sensors are overcooled)
	Fan_Status_Switched_Off				= 0b00000000000010000000000000000000,		///< The Control signals are set to minimum values (used
																					///< to turn off the power of an unplugged fan).
	Fan_Status_Running					= 0b00000000000100000000000000000000,		///< Fan isn't disabled or stopped
	Fan_Status_Spinning_Up				= 0b00000000001000000000000000000000,		///< Fan spinning up is in progress
	Fan_Status_Spin_Up_Failed			= 0b00000000010000000000000000000000,		///< Spin-up attempt failed
	Fan_Status_PID_Suspended			= 0b00000000100000000000000000000000,		///< The PID controller is suspended due to significant change in Control Value
	Fan_Status_PID_Action_Required		= 0b00000001000000000000000000000000,		///< The Target values have changed and we need a PID reaction
																					///< to start moving towards the target PPM.
	Fan_Status_PreWarming				= 0b00000010000000000000000000000000,		///< Fan pre-warming (at 50% power) is in progress
	Fan_Status_Warming					= 0b00000100000000000000000000000000,		///< Fan warming-up before scanning is in progress
	Fan_Status_Scanning					= 0b00001000000000000000000000000000,		///< Fan scanning (hardware or model) is in progress
	Fan_Status_Fatal_Failure			= 0b00010000000000000000000000000000,		///< Indicates that the fan failure cannot be fixed except by
																					///< resetting the environment, restarting the device, or re-scanning the fan.
	Fan_Status_HW_Errors_Mask			= 0b00000000000000000000000000011111,		///< Hardware errors mask
	Fan_Status_Warnings_Mask			= 0b00000000000000000001010000000000,		///< Warnings mask
	Fan_Status_Alarms_Mask				= 0b00000000000000000110100000000000,		///< Alarms mask
	Fan_Status_Any_Problems_Mask		= Fan_Status_HW_Errors_Mask | Fan_Status_Warnings_Mask | Fan_Status_Alarms_Mask | Fan_Status_Fatal_Failure	///< Any problems mask
} Fan_Status;


/**
 * @brief	Expected fan speed change (used when waiting for the speed to stabilize).
 */
typedef enum
{
	Fan_Speed_Deceleration				= -1,				///< The fan is expected to slow down
	Fan_Speed_No_Change					= 0,				///< Stable fan speed is expected
	Fan_Speed_Acceleration				= 1					///< Fan acceleration is expected
} Fan_Expected_Speed_Change;


/**
 * @brief	Types of scanning tasks (reported to the UpdateScanningDelegate).
 */
typedef enum
{
	Fan_Scan_Task_None					= 0,				///< Dummy value
	Fan_Scan_Task_Hardware				= 1,				///< Hardware scanning
	Fan_Scan_Task_PreWarm				= 2,				///< Pre-warm fan
	Fan_Scan_Task_WarmUp				= 3,				///< Warming up fan
	Fan_Scan_Task_Fan					= 4					///< Fan scanning
} Fan_Scan_Tasks;


/**
 * @brief	The structure defining the Hardware and Fan configuration.
 * @note	The structure is stored in Flash and transferred to the host as is.
 * 			1 + 1 + 2 + (8 + 1) + 2 + 4 + 1 + 2 + 2 + 2 + 2 + 4 + 2 + 4 + 4 + 2 + 4 = 48 bytes.
 */
typedef struct __attribute__((packed, aligned(4)))
{
	/* Parameters that imply the possibility of user configuration */
	/* Initial values of runtime parameters */
	uint8_t controlMode;				///< Hardware Control mode: PWM, DC-DC, or power MOSFET ON/OFF (see Fan_Control_Mode)
	uint8_t operationMode;				///< Fan operation mode: Sensor controlled, Silent, Turbo or Manual (see Fan_Operation_Mode)
	uint16_t controlBasis;				///< The basis level of fan's power. If Control Basis = FAN_CONTROL_HALF_RANGE,
										///< then the fan will operate over the entire range of available speeds.
										///< By shifting the Basis down, we limit the maximum RPM, and by shifting it up,
										///< we force the fan to work in the upper part of its operating range.

	char name[FAN_NAME_SIZE + 1];		///< The displayed name of the fan
	uint16_t model;						///< Connected fan's capabilities (see Fan_Model)
	uint32_t options;					///< Rate-Of-Change, Pulses Per Revolution and so on (see Fan_Options)

	/* Parameters that can only be determined automatically */
	/* Hardware capabilities */
	uint8_t hardware;					///< Hardware capabilities (see Fan_Hardware)

	/* Power HW metrics */
	uint16_t maxVoltage;				///< Maximum DC-DC voltage
	uint16_t minVoltage;				///< Minimum DC-DC voltage
	uint16_t maxVoltageControl;			///< The Control Value corresponding to the maximum DC-DC voltage.
	uint16_t minVoltageControl;			///< The Control Value corresponding to the minimum DC-DC voltage.

	/* Common Fan metrics */
	uint32_t maxPPM;					///< Fan's maximum Pulses Per Minute.

	/* PWM-controlled metrics */
	uint16_t minPWMControl;				///< The minimum Control value for the PWM Control mode at which the fan still will rotate.
	uint32_t pwmControlledMinPPM;		///< Minimum PPM for the PWM Control Mode.
	uint32_t pwmPenultimatePPM;			///< Fan's PPM at the 99% PWM

	/* DC-DC-controlled metrics */
	uint16_t minPWRControl;				///< The minimum Control value for the DC-DC Control mode at which the fan still will rotate.
	uint32_t pwrControlledMinPPM;		///< Minimum PPM for the DC-DC Control Mode.

} Fan_Config;


/**
 * @brief	Current Fan state.
 * @note	The structure is transferred to the host as is.
 * 			4 + 4 + 4 + 2 + 2 + 1 + 1 + 2 + 2 + 2 (padding) = 24 bytes.
 */
typedef struct __attribute__((packed, aligned(4)))
{
	/* Status */
	uint32_t status;					///< Status bits (see Fan_Status)
	uint32_t totalErrorsCount;			///< Total errors (HW errors, fan alarms and fan warnings) count
	uint32_t currentPPM;				///< Measured fan's Pulses Per Minute
	uint16_t currentVoltage;			///< Measured fan's power voltage

	/* Control parameters initialized from the configuration */
	uint16_t controlBasis;				///< The basis level of fan's power.
	uint8_t controlMode;				///< Hardware Control mode: PWM, DC-DC, or power MOSFET ON/OFF
	uint8_t operationMode;				///< Fan operation mode: Sensor controlled, Silent, Turbo or Manual.

	/* Runtime parameters */
	uint16_t controlValue;				///< The basis on which more accurate Control Values will be calculated.

	uint16_t scaledControl;				///< A Control Value scaled to the current fan Control basis
										///< (actually to the selected fan power range).

} Fan_Info;


/**
 * @brief	Scanning progress callback.
 * @param	scanTask		Current scanning task (see Fan_Scan_Tasks).
 * @param	percentDone		Task progress 0-100%, or 0xFF in case of an error.
 * @param	sender			The fan object (Fan_base subclass) performing the task.
 * @param	attribute		The configuration being filled during the scanning (Fan_Config *).
 */
typedef void (*UpdateScanningDelegate)(uint8_t scanTask, uint8_t percentDone, void * sender, void * attribute);



////////////////////////////////////////////////////////////////////////////////////////
// CLASS
////////////////////////////////////////////////////////////////////////////////////////

/**
 * @brief	Abstract base class of a fan channel.
 *
 * 			Implements the hardware-independent fan logic. Access to the hardware
 * 			(PWM, DC-DC, MOSFET, tachometer and voltmeter) must be implemented
 * 			in the subclasses through the pure virtual functions.
 */
class Fan_base {

private:

protected:

	uint16_t lastVoltage = FAN_INDEFINITE_VOLTAGE;			///< Previous fan's power voltage
	uint32_t lastPPM = FAN_INDEFINITE_PPM;					///< Previous fan's Pulses Per Minute

	uint8_t serviceMode = Fan_Service_Mode_None;			///< The Control Mode used for service needs (see Fan_Service_Modes)

	uint32_t targetPPM = 0;									///< Target PPM calculated from the scaledControl value relative to the
															///< minimum and maximum rotation speed supported by the connected fan.
	uint16_t targetControl = 0;								///< The same for Target Control value.
	volatile uint16_t currentControl = 0;					///< The current Control Value to be set for the PWM, DC/DC or MOSFET. If there is a
															///< tachometer feedback, this parameter will be automatically adjusted via PID.
	uint8_t minControlSupplementSteps = 0;					///< A supplement to the minimum control signal, in case the fan is cold
															///< or its performance has degraded over time.
															///< @todo A slow return to the zero value for the moment when the fan warms up.
															///< For example, one step per hour. Or better to use some kind of EMA
															///< to continuously update the lower limits of the operating
															///< Control Range and RPM Range.
															///< @todo The same for the upper limits. It would be better to update them
															///< continuously.

	uint16_t extraSupplement = 0;							///< An extra Control Value supplement to compensate possible failures of other fans.

	int32_t pidPrevErr = 0;									///< The previous error value for calculating the derivative component of the PID
	int32_t pidIntegralComponent = INT32_MIN; 				///< Integral component of the PID (LONG_MIN = PID is not started)

	uint32_t serviceTick = 0;								///< Service timestamp for monitoring all kinds of timeouts
	uint32_t lastUpdateTick = 0;							///< Timestamp of the last call to the doBasicOperations() function
															///< (used to calculate dt for PID and error handling)
	uint32_t hwErrorCounters = 0;							///< Hardware Error counters
	uint32_t fanErrorCounters = 0;							///< Fan Error counters
	uint32_t errorPressureCounter = 0;						///< An indicator of the "pressure" of errors. Measured in milliseconds.
															///< When an error occurs (the transition from a normal state to an erroneous one),
															///< the corresponding "weight" in milliseconds is added to the indicator.
															///< When this indicator exceeds the threshold value, the fan status is set
															///< to Failed. If no errors have occurred during the time between checks,
															///< then the "pressure" indicator decreases for this time (dt).


	/**
	 * @brief	Update one of the error counters and set/clear the corresponding status flag.
	 * @param	error			true if an error occurred during the current check.
	 * @param	counter			Pointer to the packed counters variable (hwErrorCounters or fanErrorCounters).
	 * @param	counterShift	Bit position of the counter (FAN_ERR_COUNTER_xxx_SHIFT).
	 * @param	errorFlag		Status flag to be set or cleared (see Fan_Status).
	 * @param	severe			Set or clear the flag immediately, without accumulating the counter.
	 * @retval	true if the counter has reached its limit and the flag is set (error = true)
	 * 			or cleared (error = false).
	 */
	bool countErrors(bool error, uint32_t * counter, uint8_t counterShift, uint32_t errorFlag, bool severe = false);

	/**
	 * @brief	Check whether the PID can be used in the current Control and Operation Modes.
	 * @note	The configuration must be available.
	 * @retval	true if the PID is enabled for the current Control Mode, the control range
	 * 			is detected and the fan isn't in the Turbo Operation Mode.
	 */
	bool pidEnabled();

	/**
	 * @brief	Check whether the PID can handle the difference between the current and the target speed.
	 * @note	The configuration must be available.
	 * @param	currentPPM		Current fan PPM.
	 * @param	targetPPM		Target fan PPM.
	 * @param	minPPM			Minimum PPM for the current Control Mode.
	 * @retval	true if the difference fits the PID operating range (no need to bypass the PID).
	 */
	bool pidCanHandleDelta(uint32_t currentPPM, uint32_t targetPPM, uint32_t minPPM);

	/**
	 * @brief	Suspend the PID (if it is used) and save the timestamp for subsequent timeout checks.
	 * @param	pid				true if the PID is enabled for the fan.
	 */
	void suspendPID(bool pid);

	/**
	 * @brief	Reset the PID and error handling runtime parameters.
	 */
	void resetRuntimeParameters();

	/**
	 * @brief	Calculate the target PPM and the target Control Value for the current Control Mode.
	 * @note	The configuration must be available.
	 * @param	control			Scaled Control Value (0 - FAN_CONTROL_TOP).
	 * @param	isPWM			true for the PWM Control Mode, false for the DC-DC one.
	 * @param	minPPM			[in,out] Minimum PPM for the Control Mode (corrected by the accumulated supplement).
	 * @param	minControl		Minimum Control Value of the fan operating range.
	 * @param	maxControl		Maximum Control Value of the fan operating range.
	 * @param	targetPPM		[out] Target PPM.
	 * @param	targetControl	[out] Target Control Value.
	 */
	void calculateTargets(uint16_t control, bool isPWM, uint32_t & minPPM, uint16_t minControl, uint16_t maxControl, uint32_t & targetPPM, uint16_t & targetControl);

	/**
	 * @brief	Check whether the available hardware and the connected fan allow the specified Control Mode.
	 * @param	mode			Control Mode (see Fan_Control_Mode).
	 * @param	config			Configuration to check.
	 * @retval	true if the Control Mode is available.
	 */
	bool controlModeAvailable(uint8_t mode, Fan_Config * config);

	/**
	 * @brief	Determine the best Control Mode, taking into account the available hardware and accumulated errors.
	 * @param	config			Configuration to analyze.
	 * @retval	The best Control Mode (see Fan_Control_Mode), Fan_Control_Disabled if none is available.
	 */
	uint8_t getBestControlMode(Fan_Config * config);

	/**
	 * @brief	Get the spin-up Control Value from the spin-up option.
	 * @param	option			Spin-up option (see Fan_Options), or the whole options field.
	 * @param	shift			Bit position of the spin-up option in the options field (0 if option is already extracted).
	 * @retval	Spin-up Control Value.
	 */
	uint16_t getSpinUpControlFromOption(uint16_t option, uint8_t shift = 0);

	/**
	 * @brief	Set a temporary Control Mode for service operations, or return to the normal Control Mode.
	 * @note	The Current and Target Control Values are synchronized with the actual hardware state.
	 * @param	mode			Service Mode (see Fan_Service_Modes), Fan_Service_Mode_None to return to the normal Control Mode.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setServiceMode(uint8_t mode);

	/**
	 * @brief	Write the Control Value to the hardware of the current Service Mode (or Control Mode if no Service Mode is set).
	 * @param	control			Control Value.
	 * @param	highPriority	false - a single attempt without error counting (used from the interrupt).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef driverSetControl(uint16_t control, bool highPriority = true);

	/**
	 * @brief	Get the actual Control Value from the hardware.
	 * @param	mode			Control Mode (see Fan_Control_Mode).
	 * @param	control			[out] Actual Control Value.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getRealControlForMode(uint8_t mode, uint16_t & control);

	/**
	 * @brief	Set the Target Control Value. The Control Value is changed smoothly by doIncrements(), or immediately.
	 * @param	control			Target Control Value.
	 * @param	immediately		Set the value to the hardware right now.
	 * @param	wait			Wait until the smooth change reaches the target (with a double time margin).
	 * @retval	HAL status, HAL_TIMEOUT if the target wasn't reached in time.
	 */
	HAL_StatusTypeDef setControlTarget(uint16_t control, bool immediately, bool wait);

	/**
	 * @brief	Recalculate the scaled Control Value and the targets according to the Control and Operation Modes.
	 * @note	The configuration must be available.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef updateControlValues();

	/**
	 * @brief	Make sure that the PWM and DC-DC are set to 0, and then turn on the MOSFET.
	 * @param	dcdcHardwareAvailable	DC-DC hardware is available.
	 * @param	pwmHardwareAvailable	PWM hardware is available.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef safelyTurnOnMOSFET(bool dcdcHardwareAvailable, bool pwmHardwareAvailable);

	/**
	 * @brief	Turn on all the available hardware to the maximum.
	 * @param	mosfetHardwareAvailable	MOSFET hardware is available.
	 * @param	dcdcHardwareAvailable	DC-DC hardware is available.
	 * @param	pwmHardwareAvailable	PWM hardware is available.
	 * @param	panic					Set the values immediately, without smooth changing and waiting.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef everythingOn100percent(bool mosfetHardwareAvailable, bool dcdcHardwareAvailable, bool pwmHardwareAvailable, bool panic = false);

	/**
	 * @brief	Spin-up the fan to the specified Control Value and wait until the PPM is consistently greater than 0.
	 * @param	spinUpControl	Spin-up Control Value (it is increased on subsequent attempts).
	 * @param	outPPM			[out] Measured PPM (optional).
	 * @retval	HAL status, HAL_TIMEOUT if the fan didn't start.
	 */
	HAL_StatusTypeDef spinUpNow(uint16_t spinUpControl, uint32_t * outPPM = nullptr);

	/**
	 * @brief	Warm up the fan for the specified time while monitoring the PPM.
	 * 			The warming-up is interrupted if the fan stops.
	 * @param	warmUpDuration	Warm-up duration in milliseconds.
	 * @param	ppm				[out] The last measured PPM (0 - the fan isn't rotating).
	 * @retval	HAL status, HAL_ERROR if more than half of the measurements failed.
	 */
	HAL_StatusTypeDef warmUpAndGetPPM(uint32_t warmUpDuration, uint32_t & ppm);

	/**
	 * @brief	Set the DC-DC Control Value and wait for the voltage to stabilize (steps = 0),
	 * 			or search for the point where the voltage starts to change (steps != 0).
	 * @param	startPoint		[in,out] Starting Control Value, updated with the found point (may be nullptr if steps = 0).
	 * @param	steps			Search step in DC-DC control steps (the sign defines the direction), 0 - just stabilize.
	 * @retval	HAL status, HAL_TIMEOUT if the voltage didn't stabilize, HAL_ERROR if the point wasn't found.
	 */
	HAL_StatusTypeDef refinePWRControlPoint(uint16_t * startPoint, int8_t steps);

	/**
	 * @brief	Wait for the fan speed to stabilize.
	 * @param	ppm				[out] Stabilized PPM (0 - the fan has stopped).
	 * @param	maxPPM			Maximum detected PPM used to discard erroneous readings (0 - unknown).
	 * @param	expectedChange	Expected speed change (see Fan_Expected_Speed_Change). The readings are ignored
	 * 							while the speed is changing in the expected direction.
	 * @param	changeDirection	[out] Speed change vector at the end of the measurement (optional): 0 - stable.
	 * @param	timeout			Timeout in milliseconds.
	 * @retval	HAL status, HAL_TIMEOUT if the speed didn't stabilize or the readings are unreliable.
	 */
	HAL_StatusTypeDef waitRotationToStabilize(uint32_t & ppm, uint32_t maxPPM, int8_t expectedChange = Fan_Speed_No_Change, int8_t * changeDirection = nullptr, uint32_t timeout = FAN_SCAN_PPM_TIMEOUT);

	/**
	 * @brief	Find the minimum stable rotation speed using the binary search method.
	 * @param	mode			Service Mode (Fan_Service_Mode_PWM or Fan_Service_Mode_DCDC).
	 * @param	topPPM			Maximum fan PPM.
	 * @param	spinUpOption	[out] Spin-up option required to start the fan (see Fan_Options).
	 * @param	minPPM			[out] Minimum stable PPM.
	 * @param	minControl		[out] Minimum Control Value corresponding to the minimum stable PPM.
	 * @retval	HAL status, HAL_TIMEOUT if the stable point wasn't found.
	 */
	HAL_StatusTypeDef detectLowSpeedCapabilities(uint8_t mode, uint32_t topPPM, uint8_t & spinUpOption, uint32_t & minPPM, uint16_t & minControl);

	/**
	 * @brief	Try to guess the fan model features (size and class) by its acceleration and deceleration.
	 * @note	Experimental. The Service Mode must already be set to the specified mode.
	 * @param	mode			Service Mode (Fan_Service_Mode_PWM or Fan_Service_Mode_DCDC).
	 * @param	spinUpOption	Spin-up option (see Fan_Options).
	 * @param	topPPM			Maximum fan PPM.
	 * @param	minPPM			Minimum stable PPM.
	 * @param	minControl		Minimum Control Value.
	 * @param	modelFeatures	[in,out] Fan model flags (see Fan_Model); the detected features are added.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef detectModelFeatures(uint8_t mode, uint8_t spinUpOption, uint32_t topPPM, uint32_t minPPM, uint16_t minControl, uint16_t & modelFeatures);


	// Hardware functions that must be implemented in subclasses,
	// so that the base class methods refer to the overridden methods.

	/**
	 * @brief	Get the MOSFET state.
	 * @param	control			[out] 0 - off, FAN_CONTROL_TOP - on.
	 * @retval	HAL status, HAL_ERROR if the MOSFET isn't available.
	 */
	virtual HAL_StatusTypeDef getMOSFET(uint16_t & control) = 0;

	/**
	 * @brief	Set the MOSFET state (the half of the Control range with hysteresis is a threshold).
	 * @param	control			Control Value.
	 * @param	highPriority	false - a single attempt without error counting.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef setMOSFET(uint16_t control, bool highPriority = true) = 0;

	/**
	 * @brief	Check whether the Control Value corresponds to the current MOSFET state.
	 * @param	control			Control Value.
	 * @param	equivalence		[out] true if the hardware state corresponds to the Control Value.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef checkControlEqualsForMOSFET(uint16_t control, bool & equivalence) = 0;

	/**
	 * @brief	Get the current PWM duty cycle.
	 * @param	control			[out] Control Value.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef getPWM(uint16_t & control) = 0;

	/**
	 * @brief	Set the PWM duty cycle.
	 * @param	control			Control Value.
	 * @param	highPriority	false - a single attempt without error counting.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef setPWM(uint16_t control, bool highPriority = true) = 0;

	/**
	 * @brief	Check whether the Control Value corresponds to the current PWM duty cycle.
	 * @param	control			Control Value.
	 * @param	equivalence		[out] true if the hardware state corresponds to the Control Value.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef checkControlEqualsForPWM(uint16_t control, bool & equivalence) = 0;

	/**
	 * @brief	Get the DC-DC output voltage setting.
	 * @param	control			[out] Control Value.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef getDCDC(uint16_t & control) = 0;

	/**
	 * @brief	Set the DC-DC output voltage.
	 * @param	control			Control Value.
	 * @param	highPriority	false - a single attempt without error counting.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef setDCDC(uint16_t control, bool highPriority = true) = 0;

	/**
	 * @brief	Check whether the Control Value corresponds to the current DC-DC output voltage setting.
	 * @param	control			Control Value.
	 * @param	equivalence		[out] true if the hardware state corresponds to the Control Value.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef checkControlEqualsForDCDC(uint16_t control, bool & equivalence) = 0;

	/**
	 * @brief	Reset the voltage averaging, so that the next readings reflect the current voltage.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef prepareVoltageMeasurement() = 0;

	/**
	 * @brief	Get the raw (averaged) ADC value of the fan power supply voltage.
	 * @param	voltageADC		[out] ADC value.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef getVoltageADC(uint16_t & voltageADC) = 0;

	/**
	 * @brief	Get the current fan supply voltage.
	 * @param	voltage			[out] Voltage in Q-format (see voltageFractionBits()).
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef getVoltage(uint16_t & voltage) = 0;

	/**
	 * @brief	Get a credible raw ADC value of the fan power supply voltage
	 * 			(resets the averaging and waits for its time constant).
	 * @param	voltageADC		[out] ADC value.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef getCredibleVoltageADC(uint16_t & voltageADC) = 0;

	/**
	 * @brief	Get a credible fan supply voltage (resets the averaging and waits for its time constant).
	 * @param	voltage			[out] Voltage in Q-format (see voltageFractionBits()).
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef getCredibleVoltage(uint16_t & voltage) = 0;

	/**
	 * @brief	Prepare the tachometer for accurate pulse period measurement.
	 * @param	period			[out] Current tacho pulse period (optional).
	 * @param	expectedPPM		Expected PPM used to select the measurement range (optional).
	 * @param	expectedPeriod	[out] Pulse period corresponding to the expected PPM (optional).
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef preparePulseMeasurement(uint16_t * period = nullptr, uint32_t * expectedPPM = nullptr, uint16_t * expectedPeriod = nullptr) = 0;

	/**
	 * @brief	Get the current tacho pulse period.
	 * @param	period			[out] Pulse period.
	 * @param	previousPeriod	Previous pulse period (optional). If specified, the function waits for a new value.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef getPulsePeriod(uint16_t & period, uint16_t * previousPeriod = nullptr) = 0;

	/**
	 * @brief	Calculate the PPM from the tacho pulse period.
	 * @param	period			Pulse period.
	 * @param	ppm				[out] Pulses Per Minute.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef getPPMFromPulsePeriod(uint16_t period, uint32_t & ppm) = 0;

	/**
	 * @brief	Get the current Pulses Per Minute value (fast, the last measured value may be returned).
	 * @param	ppm				[out] Pulses Per Minute.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef getPPM(uint32_t & ppm) = 0;

	/**
	 * @brief	Get an accurate Pulses Per Minute value (the measurement range is adjusted if necessary).
	 * @param	ppm				[out] Pulses Per Minute.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef getCrediblePPM(uint32_t & ppm) = 0;
	//virtual HAL_StatusTypeDef isStopped(bool & stopped) = 0;

	/**
	 * @brief	Get the number of fraction bits of the voltage values (Q-format).
	 * @retval	Number of fraction bits.
	 */
	virtual uint8_t voltageFractionBits() = 0;

	/**
	 * @brief	Get the bit width of the PWM hardware.
	 * @retval	Bit width.
	 */
	virtual uint8_t pwmControlBitWidth() = 0;

	/**
	 * @brief	Get the bit width of the DC-DC control hardware (POT/DAC).
	 * @retval	Bit width.
	 */
	virtual uint8_t dcdcControlBitWidth() = 0;

	/**
	 * @brief	Get the expected change of the raw ADC voltage value for one DC-DC control step.
	 * @retval	ADC value (Q-format).
	 */
	virtual uint16_t dcdcControlVoltageStep() = 0;

	/**
	 * @brief	Get the tacho pulse measurement period.
	 * @retval	Period in milliseconds.
	 */
	virtual uint16_t pulseMeasurementDelay() = 0;

public:

	static uint8_t configFlags;								///< Global configuration flags (see Fan_Global_Config_Flags)

	/**
	 * @brief	Get the base name for new fans (we need to know it before we create an object of the class).
	 * @retval	Base name.
	 */
	static const char * baseName() { return ("FAN"); }

	/**
	 * @brief	Prepare the default fan configuration.
	 * @param	newConfig		Configuration to fill.
	 * @param	name			Fan name, nullptr - use the base name.
	 */
	static void prepareConfig(Fan_Config * newConfig, const char * name) {
		if (newConfig == nullptr) return;

		// Initialize default configuration
		memset(newConfig, 0, sizeof(Fan_Config));

		// Use the provided name or the default one
		if (name != nullptr) {
			strncpy(newConfig->name, name, FAN_NAME_SIZE);
		} else {
			strncpy(newConfig->name, Fan_base::baseName(), FAN_NAME_SIZE);
		}

		newConfig->controlBasis = FAN_CONTROL_HALF_RANGE;
		//newConfig->model = Fan_Model_Class_Unknown;
		newConfig->options = Fan_Options_PPR_Default |
			(Fan_Options_Spin_Default << Fan_Options_Spin_PWM_Bit_Shift) |
			(Fan_Options_Spin_Default << Fan_Options_Spin_PWR_Bit_Shift) |
			(Fan_Options_ROC_Default << Fan_Options_ROC_Bit_Shift);
	}


	/**
	 * @brief	Map the value from one range to another (linear interpolation).
	 * @note	The value must be within the source range, and the source range must not be empty.
	 * @param	value			Value to map.
	 * @param	fromLow			Lower bound of the source range.
	 * @param	fromHigh		Upper bound of the source range.
	 * @param	toLow			Lower bound of the target range.
	 * @param	toHigh			Upper bound of the target range.
	 * @retval	Mapped value.
	 */
	static uint32_t mapUInt(uint32_t value, uint32_t fromLow, uint32_t fromHigh, uint32_t toLow, uint32_t toHigh) {
		return ((toLow +
				(toHigh - toLow) *
				(value - fromLow) /
				(fromHigh - fromLow)));
	}


	/**
	 * @brief	Constrain the value to the specified range.
	 * @param	value			Value to constrain.
	 * @param	low				Lower bound.
	 * @param	high			Upper bound.
	 * @retval	Constrained value.
	 */
	static uint32_t constrainUInt(int32_t value, uint32_t low, uint32_t high) {
		if (value < 0) return (low);
		return ((uint32_t)value < low ? low : ((uint32_t)value > high ? high : (uint32_t)value));
	}


	Fan_Config * configuration = nullptr;					///< Active fan configuration (nullptr - the fan isn't configured)
	Fan_Info state = { 0 };									///< Current fan state

	Fan_base();
	virtual ~Fan_base() = default;

	/**
	 * @brief	Convert the PPM to the RPM according to the fan Pulses Per Revolution option.
	 * @param	ppm				Pulses Per Minute.
	 * @retval	Revolutions Per Minute.
	 */
	uint16_t getRPMFromPPM(uint32_t ppm);

	/**
	 * @brief	Get the latest measured RPM.
	 * @retval	Revolutions Per Minute.
	 */
	uint16_t getRPM();

	/**
	 * @brief	Get the latest measured fan supply voltage.
	 * @retval	Voltage in Q-format (see voltageFractionBits()).
	 */
	uint16_t getVoltage();

	/**
	 * @brief	Get the current Control Value scaled to the current fan Control basis.
	 * @retval	Scaled Control Value.
	 */
	uint16_t getActualControl();

	/**
	 * @brief	Set the Control Mode.
	 * @param	mode			Control Mode (see Fan_Control_Mode).
	 * @retval	HAL status, HAL_ERROR if the fan isn't configured, has a fatal failure,
	 * 			or the Control Mode isn't available.
	 */
	HAL_StatusTypeDef setControlMode(uint8_t mode);

	/**
	 * @brief	Set the Operation Mode.
	 * @param	mode			Operation Mode (see Fan_Operation_Mode).
	 * @retval	HAL status, HAL_ERROR if the fan isn't configured or has a fatal failure.
	 */
	HAL_StatusTypeDef setOperationMode(uint8_t mode);

	/**
	 * @brief	Set the basis level of fan's power. If Control Basis = FAN_CONTROL_HALF_RANGE,
	 * 			then the fan will operate over the entire range of available speeds.
	 * @param	controlBasis	Control Basis.
	 * @retval	HAL status, HAL_ERROR if the fan isn't configured or has a fatal failure.
	 */
	HAL_StatusTypeDef setControlBasis(uint16_t controlBasis);

	/**
	 * @brief	Set the current Control Value (based on sensor readings, or manual setting in the control application).
	 * @param	control			Control Value.
	 * @param	supplement		An extra Control Value supplement to compensate possible failures of other fans.
	 * @retval	HAL status, HAL_ERROR if the fan isn't configured or has a fatal failure.
	 */
	HAL_StatusTypeDef setControl(uint16_t control, uint16_t supplement = 0);

	/**
	 * @brief	Temporary halt the fan (e.g. if the sensors are overcooled) or resume it.
	 * @param	halted			true - halt the fan.
	 * @retval	HAL_OK.
	 */
	HAL_StatusTypeDef setHalted(bool halted);

	/**
	 * @brief	Reset the fan errors, status, PID parameters and timestamps, and exit the Service Mode.
	 */
	void resetState();

	/**
	 * @brief	A critical error happened in the main program: detach the configuration
	 * 			and turn all the available hardware on to the maximum immediately.
	 * @note	May be called with the interrupts disabled.
	 */
	void kernelPanic();

	/**
	 * @brief	Determine the best Control Mode and store it in the new configuration.
	 * @param	newConfig		Configuration to update.
	 */
	void chooseBestControlModeForConfig(Fan_Config * newConfig);

	/**
	 * @brief	Apply a new or saved configuration.
	 * @param	newConfig					Configuration to apply.
	 * @param	updateControlParameters		Apply the Control/Operation Modes and the Control Basis from the configuration
	 * 										(they are always applied if the fan had no configuration before).
	 * @retval	HAL status, HAL_ERROR if the Control Mode of the configuration isn't available
	 * 			(the fan is set to the Fatal Failure state).
	 */
	HAL_StatusTypeDef applyConfig(Fan_Config * newConfig, bool updateControlParameters);

	/**
	 * @brief	Detect the available hardware (PWM, tachometer, voltmeter, DC-DC range, MOSFET).
	 * @param	newConfig			[in,out] Configuration to store the results.
	 * @param	progressDelegate	Scanning progress callback (optional).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef scanHardware(Fan_Config * newConfig, UpdateScanningDelegate progressDelegate);

	/**
	 * @brief	Start warming up the fan at 50% power in advance, to shorten the subsequent fan scanning.
	 * @param	newConfig			Configuration with the detected hardware.
	 * @param	progressDelegate	Scanning progress callback (optional).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef prewarmFan(Fan_Config * newConfig, UpdateScanningDelegate progressDelegate);

	/**
	 * @brief	Detect the connected fan features: connection, tachometer, PWM support,
	 * 			minimum speeds and spin-up options for the PWM and DC-DC Control Modes,
	 * 			and (optionally) the model features.
	 * @param	newConfig			[in,out] Configuration with the detected hardware to store the results.
	 * @param	progressDelegate	Scanning progress callback (optional).
	 * @param	prewarmingDuration	Time in milliseconds the fan has already been warmed up (reduces the warm-up time).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef scanFan(Fan_Config * newConfig, UpdateScanningDelegate progressDelegate, uint32_t prewarmingDuration = 0);

	/**
	 * @brief	Set the fan to 50% power while scanning others or for some other long tasks.
	 * @param	newConfig			Configuration of the scanned fan.
	 * @retval	HAL status, HAL_ERROR if the fan is disabled.
	 */
	HAL_StatusTypeDef leaveAt50Percent(Fan_Config * newConfig);

	/**
	 * @brief	Perform a step of the smooth Control Value change.
	 * @note	For normal operation, you must call this procedure 10-1000 times per second.
	 * 			In this project it is called from the ADC conversion complete interrupt.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef doIncrements();

	/**
	 * @brief	On/off operations, Control Mode changes, voltage and PPM checks, PID control and error checking.
	 * @note	For more reliable work, it is advisable to perform all changes to the Operation Mode,
	 * 			Control Value and Control Basis immediately AFTER calling this procedure, and not before it.
	 * @retval	true if the status flags contain any errors or warnings.
	 */
	bool doBasicOperations();

};

#endif /* _FAN_BASE_ */

// END-OF-FILE
