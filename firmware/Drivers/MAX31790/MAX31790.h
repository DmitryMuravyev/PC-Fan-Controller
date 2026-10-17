////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// MAX31790.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	MAX31790.h
 * @brief	Minimalistic class for managing the MAX31790 fan controller.
 * @note	We will not use the concept of RPM (Revolutions Per Minute) here,
 * 			since it depends on the number of pulses per revolution for each
 * 			specific fan (usually 2 pulses). Instead, we will use PPM
 * 			(Pulses Per Minute), which is much more universal.
 */


#ifndef _MAX31790_
#define _MAX31790_


// Includes
#include "stm32f0xx_hal.h"
#include <stdbool.h>


// Class-level definitions
#define MAX31790_BASE_ADDR					0x40		///< MAX31790 base I2C address (8-bit form)

// MAX31790 Registers addresses
#define MAX31790_GLOBAL_CONFIG_REG			0x00		///< Global Configuration
#define MAX31790_PWM_FREQ_REG				0x01		///< PWM Frequency
#define MAX31790_FAN_CONFIG_BASE			0x02		///< Fan Configuration (fans 1 - 6)
#define MAX31790_FAN_DYNAMICS_BASE			0x08		///< Fan Dynamics (fans 1 - 6)
#define MAX31790_FAN_STATUS_REG				0x10		///< Fan Fault Status (2 bytes)
#define MAX31790_FAN_MASK_REG				0x12		///< Fan Fault Mask (2 bytes)
#define MAX31790_FAN_OPTIONS_REG			0x14		///< Failed Fan Options / Sequential Start
#define MAX31790_FAN_TACH_BASE				0x18		///< TACH Count (2 bytes per tachometer)
#define MAX31790_PWM_OUT_BASE				0x30		///< PWMOUT Duty Cycle (2 bytes per fan)
#define MAX31790_PWM_TARGET_BASE			0x40		///< PWMOUT Target Duty Cycle (2 bytes per fan)
#define MAX31790_TACH_TARGET_BASE			0x50		///< TACH Target Count (2 bytes per fan)
#define MAX31790_WINDOW_BASE				0x60		///< Window (fans 1 - 6)

// Hardware constants
#define MAX31790_FANS_COUNT					6			///< Number of fan channels
#define MAX31790_STARTUP_TIME				45			///< Start-up time after reset in milliseconds
#define MAX31790_OSCILLATOR_STARTUP_TIME	1000		///< External oscillator start-up time in milliseconds
#define MAX31790_TACH_MEASUREMENT_TIME		1000		///< TACH measurement period in milliseconds
#define MAX31790_SR_APPLICATION_MARGIN		50			///< Additional time to apply the new Speed Range in milliseconds
#define MAX31790_PWM_BITS					9			///< PWM bit width
#define MAX31790_TACH_BITS					11			///< TACH count bit width
#define MAX31790_TACH_FULL_RANGE			(1 << MAX31790_TACH_BITS)		///< Full range of the TACH count
#define MAX31790_TACH_QUARTER_RANGE			(MAX31790_TACH_FULL_RANGE >> 2)	///< Quarter of the TACH count range
#define MAX31790_TACH_TOP					(MAX31790_TACH_FULL_RANGE - 1)	///< Maximum TACH count (the fan is stopped)
#define MAX31790_MAX_SPEED_RANGE			5			///< Maximum Speed Range value (SR = 32)

// Runtime constants
#define MAX31790_MAX_DYNAMICS_CORRECTIONS	MAX31790_MAX_SPEED_RANGE		///< Maximum number of Speed Range corrections per request
#define MAX31790_TACH_REQUEST_DELAY			10			///< Delay between the TACH requests in milliseconds
#define MAX31790_INDEFINITE_PPM				0xFFFFFFFF	///< Impossible PPM value (TACH = 0)


/**
 * @brief	Global Configuration bit numbers (default = 0010 0XX0).
 */
typedef enum
{
	MAX31790_Config_Standby					= 7,			///< 0 = Run, 1 = Standby
	MAX31790_Config_Reset					= 6,			///< 0 = Normal, 1 = Reset
	MAX31790_Config_BusTimeout				= 5,			///< 0 = Enabled
	MAX31790_Config_OSC						= 3,			///< 0 = Internal oscillator, 1 = External crystal
	MAX31790_Config_WatchdogStatus			= 0				///< 1 = Watchdog fault detected
} MAX31790_Config_Bits;


/**
 * @brief	Global Configuration Watchdog options.
 */
typedef enum
{
	MAX31790_Config_Watchdog_Disabled		= 0b000,		///< Watchdog disabled
	MAX31790_Config_Watchdog_5s				= 0b010,		///< 5 seconds
	MAX31790_Config_Watchdog_10s			= 0b100,		///< 10 seconds
	MAX31790_Config_Watchdog_30s			= 0b110			///< 30 seconds
} MAX31790_Config_Watchdog_Options;


/**
 * @brief	PWM Frequency options.
 */
typedef enum
{
	MAX31790_PWM_Frequency_25Hz				= 0b0000,		///< 25 Hz
	MAX31790_PWM_Frequency_30Hz				= 0b0001,		///< 30 Hz
	MAX31790_PWM_Frequency_35Hz				= 0b0010,		///< 35 Hz
	MAX31790_PWM_Frequency_100Hz			= 0b0011,		///< 100 Hz
	MAX31790_PWM_Frequency_125Hz			= 0b0100,		///< 125 Hz
	MAX31790_PWM_Frequency_145Hz			= 0b0101,		///< 145 Hz
	MAX31790_PWM_Frequency_1250Hz			= 0b0110,		///< 1.25 kHz
	MAX31790_PWM_Frequency_1470Hz			= 0b0111,		///< 1.47 kHz
	MAX31790_PWM_Frequency_3570Hz			= 0b1000,		///< 3.57 kHz
	MAX31790_PWM_Frequency_5000Hz			= 0b1001,		///< 5 kHz
	MAX31790_PWM_Frequency_12500Hz			= 0b1010,		///< 12.5 kHz
	MAX31790_PWM_Frequency_25000Hz			= 0b1011		///< 25 kHz
} MAX31790_PWM_Frequency_Options;


/**
 * @brief	Fan Configuration bit numbers (default = 0XX0 0000).
 */
typedef enum
{
	MAX31790_Fan_Config_Mode				= 7,			///< 0 = PWM, 1 = RPM
	MAX31790_Fan_Config_Control				= 4,			///< 0 = Control, 1 = Monitor only
	MAX31790_Fan_Config_TACH_Input			= 3,			///< 1 = Enabled
	MAX31790_Fan_Config_TACH_Mode			= 2,			///< 0 = TACH, 1 = Locked Rotor
	MAX31790_Fan_Config_LR_Polarity			= 1,			///< 0 = Low, 1 = High
	MAX31790_Fan_Config_PWM_TACH			= 0				///< 0 = PWM, 1 = TACH
} MAX31790_Fan_Config_Bits;


/**
 * @brief	Fan Configuration Spin-up options.
 */
typedef enum
{
	MAX31790_Fan_Config_Spinup_Disabled		= 0b0000000,	///< No Spin-up
	MAX31790_Fan_Config_Spinup_0_5s			= 0b0100000,	///< 2 TACH counts or 0.5s
	MAX31790_Fan_Config_Spinup_1s			= 0b1000000,	///< 2 TACH counts or 1s
	MAX31790_Fan_Config_Spinup_2s			= 0b1100000		///< 2 TACH counts or 2s
} MAX31790_Fan_Config_Spinup_Options;


/**
 * @brief	Fan Dynamics bit numbers (default = 0100 1100).
 */
typedef enum
{
	MAX31790_Fan_Dynamics_Asymmetric_RoC	= 1				///< 1 = Enabled
} MAX31790_Fan_Dynamics_Bits;


/**
 * @brief	Fan Dynamics Speed Ranges (default = 0b010 = 4).
 */
typedef enum
{
	MAX31790_Fan_Dynamics_SR_1				= 0b000,		///< SR = 1
	MAX31790_Fan_Dynamics_SR_2				= 0b001,		///< SR = 2
	MAX31790_Fan_Dynamics_SR_4				= 0b010,		///< SR = 4 (default)
	MAX31790_Fan_Dynamics_SR_8				= 0b011,		///< SR = 8
	MAX31790_Fan_Dynamics_SR_16				= 0b100,		///< SR = 16
	MAX31790_Fan_Dynamics_SR_32				= 0b101,		///< SR = 32
	MAX31790_Fan_Dynamics_SR_32_2			= 0b110,		///< SR = 32
	MAX31790_Fan_Dynamics_SR_32_3			= 0b111,		///< SR = 32
	MAX31790_Fan_Dynamics_Mask				= 0b11100000	///< Speed Range mask in the Fan Dynamics register
} MAX31790_Fan_Dynamics_Speed_Ranges;


/**
 * @brief	Fan Dynamics PWM Rate-of-Change (default = 0b011 = 7.8125ms per LSB).
 */
typedef enum
{
	MAX31790_Fan_Dynamics_PWM_RoC_0ms		= 0b000,		///< 0ms per LSB (PWM), 0.9765ms per LSB (RPM)
	MAX31790_Fan_Dynamics_PWM_RoC_1_9ms		= 0b001,		///< 1.953125ms per LSB
	MAX31790_Fan_Dynamics_PWM_RoC_3_9ms		= 0b010,		///< 3.90625ms per LSB
	MAX31790_Fan_Dynamics_PWM_RoC_7_8ms		= 0b011,		///< 7.8125ms per LSB (default)
	MAX31790_Fan_Dynamics_PWM_RoC_15_6ms	= 0b100,		///< 15.625ms per LSB
	MAX31790_Fan_Dynamics_PWM_RoC_31_2ms	= 0b101,		///< 31.25ms per LSB
	MAX31790_Fan_Dynamics_PWM_RoC_62_5ms	= 0b110,		///< 62.5ms per LSB
	MAX31790_Fan_Dynamics_PWM_RoC_125ms		= 0b111			///< 125ms per LSB
} MAX31790_Fan_PWM_RoC;


/**
 * @brief	Fans Sequential Start delays (default = 0b010 = 500ms).
 */
typedef enum
{
	MAX31790_Sequential_Start_Delay_0ms		= 0b000,		///< 0s
	MAX31790_Sequential_Start_Delay_250ms	= 0b001,		///< 250ms
	MAX31790_Sequential_Start_Delay_500ms	= 0b010,		///< 500ms (default)
	MAX31790_Sequential_Start_Delay_1s		= 0b011,		///< 1s
	MAX31790_Sequential_Start_Delay_2s		= 0b100,		///< 2s
	MAX31790_Sequential_Start_Delay_4s		= 0b101,		///< 4s
	MAX31790_Sequential_Start_Delay_4s_2	= 0b110,		///< 4s
	MAX31790_Sequential_Start_Delay_4s_3	= 0b111			///< 4s
} MAX31790_Sequential_Start_Delay_Options;


/**
 * @brief	Failed Fans options (default = 0b01 = Continue on fail).
 */
typedef enum
{
	MAX31790_Stop_On_Fail					= 0b00,			///< Duty cycle = 0% on fail
	MAX31790_Continue_On_Fail				= 0b01,			///< Continue PWM or RPM mode operation on fail (default)
	MAX31790_Max_On_Fail					= 0b10,			///< Duty cycle = 100% on fail
	MAX31790_All_Max_On_Fail				= 0b11			///< All fans to 100% on any unmasked fan failure.
} MAX31790_Failed_Fans_Options;


/**
 * @brief	Fan Faults count, required to decide that the fan has failed (default = 0b01 = 2 faults).
 */
typedef enum
{
	MAX31790_Faults_Count_1					= 0b00,			///< 1 fault
	MAX31790_Faults_Count_2					= 0b01,			///< 2 faults (default)
	MAX31790_Faults_Count_4					= 0b10,			///< 4 faults
	MAX31790_Faults_Count_6					= 0b11			///< 6 faults
} MAX31790_Fault_Count_Options;


/**
 * @brief	User Bytes addresses.
 */
typedef enum
{
	MAX31790_User_Byte_1					= 0x15,			///< User byte 1
	MAX31790_User_Byte_2					= 0x16,			///< User byte 2
	MAX31790_User_Byte_3					= 0x17,			///< User byte 3

	MAX31790_User_Byte_4					= 0x4C,			///< User byte 4
	MAX31790_User_Byte_5					= 0x4D,			///< User byte 5
	MAX31790_User_Byte_6					= 0x4E,			///< User byte 6
	MAX31790_User_Byte_7					= 0x4F,			///< User byte 7

	MAX31790_User_Byte_8					= 0x5C,			///< User byte 8
	MAX31790_User_Byte_9					= 0x5D,			///< User byte 9
	MAX31790_User_Byte_10					= 0x5E,			///< User byte 10
	MAX31790_User_Byte_11					= 0x5F,			///< User byte 11

	MAX31790_User_Byte_12					= 0x66,			///< User byte 12
	MAX31790_User_Byte_13					= 0x67			///< User byte 13
} MAX31790_User_Bytes_Addresses;


/**
 * @brief	PPM Request Mode.
 * @note	Since the rotation speed is measured in the chip once per second,
 * 			we may have different options for obtaining measurement results.
 */
typedef enum
{
	MAX31790_PPM_Mode_Fast					= 0x00,			///< If the fresh TACH value is not available yet,
															///< then return the previous PPM value (fastest).
	MAX31790_PPM_Mode_Fresh					= 0x01,			///< If the fresh TACH value is not yet available,
															///< then wait for it and return the actual PPM value (up to 1 second).
	MAX31790_PPM_Mode_Accurate				= 0x02			///< Wait until the Speed Range stops changing and return
															///< stable PPM value. (up to 5-6 seconds).
} MAX31790_PPM_Modes;


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

/**
 * @brief	MAX31790 fan controller (6 PWM outputs and 6 tachometer inputs).
 * @note	The Speed Range of each tachometer is adjusted automatically
 * 			to keep the TACH count in the accurate part of its range (see getFanPPM()).
 */
class MAX31790 {

private:

protected:

	uint32_t targetPPM[MAX31790_FANS_COUNT] = { 0 };			///< Target PPM for each of 6 fans
	uint32_t lastMeasurementTick[MAX31790_FANS_COUNT] = { 0 };	///< We need to store the time of the last
																///< dynamic change in order to understand
																///< whether the TACH values have been updated.
	uint16_t lastMeasurement[MAX31790_FANS_COUNT] = { 0 };		///< To save RAM, we will store in this variable
																///< the values of TACH, Speed Range, and a flag
																///< indicating that SR has been changed.

	/**
	 * @brief	Update the TACH target according to the target PPM and the current Speed Range.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef updateFanTachTarget(uint8_t fanIndex);


public:

	I2C_HandleTypeDef * const i2cHandle;						///< I2C Handle
	const uint8_t controllerIndex;								///< MAX31790 address index: 0x0 - 0xF (up to 16 ICs)

	bool i2cRobustConnection = true;							///< Repeat attempts to send/receive data in case of errors.

	/**
	 * @brief	Constructor.
	 * @param	handle				I2C Handle.
	 * @param	index				Address index: 0x0 - 0xF.
	 */
	MAX31790(I2C_HandleTypeDef * handle, uint8_t index = 0);

	virtual ~MAX31790() = default;

	/**
	 * @brief	Read register(s).
	 * @param	regAddr				Register address.
	 * @param	outValue			[out] Buffer for the data.
	 * @param	count				Number of bytes.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef readRegister(uint8_t regAddr, uint8_t * outValue, uint8_t count);

	/**
	 * @brief	Write 1 or 2 bytes to the register(s).
	 * @param	regAddr				Register address.
	 * @param	value				Data.
	 * @param	word				true - write 2 bytes, false - write 1 byte.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writeRegister(uint8_t regAddr, uint8_t * value, bool word);

	/**
	 * @brief	Read a 2-byte register.
	 * @param	regAddr				Register address.
	 * @param	outValueHi			[out] High byte (optional).
	 * @param	outValueLow			[out] Low byte (optional).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef read2bytesRegister(uint8_t regAddr, uint8_t * outValueHi, uint8_t * outValueLow);

	/**
	 * @brief	Write a 2-byte register.
	 * @param	regAddr				Register address.
	 * @param	valueHi				High byte.
	 * @param	valueLow			Low byte.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef write2bytesRegister(uint8_t regAddr, uint8_t valueHi, uint8_t valueLow);

	/**
	 * @brief	Read the Global Configuration register.
	 * @param	globalConfiguration	[out] Register value (see MAX31790_Config_Bits).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getGlobalConfiguration(uint8_t & globalConfiguration);

	/**
	 * @brief	Write the Global Configuration register.
	 * @param	globalConfiguration	Register value (see MAX31790_Config_Bits).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setGlobalConfiguration(uint8_t globalConfiguration);

	/**
	 * @brief	Read the PWM Frequency register.
	 * @param	frequency1_3		[out] Frequency of PWM outputs 1-3 (optional, see MAX31790_PWM_Frequency_Options).
	 * @param	frequency4_6		[out] Frequency of PWM outputs 4-6 (optional).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getPWMFrequency(uint8_t * frequency1_3, uint8_t * frequency4_6);

	/**
	 * @brief	Write the PWM Frequency register.
	 * @param	frequency1_3		Frequency of PWM outputs 1-3 (see MAX31790_PWM_Frequency_Options).
	 * @param	frequency4_6		Frequency of PWM outputs 4-6.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setPWMFrequency(uint8_t frequency1_3, uint8_t frequency4_6);

	/**
	 * @brief	Read the Fan Configuration register.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	fanConfiguration	[out] Register value (see MAX31790_Fan_Config_Bits).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getFanConfiguration(uint8_t fanIndex, uint8_t & fanConfiguration);

	/**
	 * @brief	Write the Fan Configuration register.
	 * @note	When switching to the RPM mode, the TACH target is set to the current TACH value first.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	fanConfiguration	Register value (see MAX31790_Fan_Config_Bits).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setFanConfiguration(uint8_t fanIndex, uint8_t fanConfiguration);

	/**
	 * @brief	Read the Fan Dynamics register.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	fanDynamics			[out] Register value.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getFanDynamics(uint8_t fanIndex, uint8_t & fanDynamics);

	/**
	 * @brief	Write the Fan Dynamics register.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	fanDynamics			Register value.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setFanDynamics(uint8_t fanIndex, uint8_t fanDynamics);

	/**
	 * @brief	Read the Fan Fault Status registers.
	 * @param	fanFault1_6			[out] Faults of tachometers 1-6 (optional).
	 * @param	fanFault7_12		[out] Faults of tachometers 7-12 (optional).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getFanFault(uint8_t * fanFault1_6, uint8_t * fanFault7_12);

	/**
	 * @brief	Write the Fan Fault Status registers.
	 * @param	fanFault1_6			Faults of tachometers 1-6.
	 * @param	fanFault7_12		Faults of tachometers 7-12.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setFanFault(uint8_t fanFault1_6, uint8_t fanFault7_12);

	/**
	 * @brief	Read the Fan Fault Mask registers.
	 * @param	fanFaultMask1_6		[out] Mask of tachometers 1-6 (optional).
	 * @param	fanFaultMask7_12	[out] Mask of tachometers 7-12 (optional).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getFanFaultMask(uint8_t * fanFaultMask1_6, uint8_t * fanFaultMask7_12);

	/**
	 * @brief	Write the Fan Fault Mask registers.
	 * @param	fanFaultMask1_6		Mask of tachometers 1-6.
	 * @param	fanFaultMask7_12	Mask of tachometers 7-12.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setFanFaultMask(uint8_t fanFaultMask1_6, uint8_t fanFaultMask7_12);

	/**
	 * @brief	Read the Sequential Start Delay / Failed Fan Options / Fan Faults count register.
	 * @param	fanStartAndFaultOptions	[out] Register value.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getFanStartAndFaultOptions(uint8_t & fanStartAndFaultOptions);

	/**
	 * @brief	Write the Sequential Start Delay / Failed Fan Options / Fan Faults count register.
	 * @param	fanStartAndFaultOptions	Register value.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setFanStartAndFaultOptions(uint8_t fanStartAndFaultOptions);

	/**
	 * @brief	Read the fan's Speed Range.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	speedRange			[out] Speed Range (0 - MAX31790_MAX_SPEED_RANGE).
	 * @param	dynamics			[out] Fan Dynamics register value (optional).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getFanSpeedRange(uint8_t fanIndex, uint8_t & speedRange, uint8_t * dynamics = nullptr);

	/**
	 * @brief	Read the fan's TACH count (tachometer pulse period).
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	tach				[out] TACH count (11 bits).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getFanTach(uint8_t fanIndex, uint16_t & tach);

	/**
	 * @brief	Read the fan's TACH Target count.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	tachTarget			[out] TACH Target count (11 bits).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getFanTachTarget(uint8_t fanIndex, uint16_t & tachTarget);

	/**
	 * @brief	Get the fan's Pulses Per Minute and adjust the Speed Range if necessary.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	ppm					[out] Pulses Per Minute (MAX31790_INDEFINITE_PPM if TACH = 0).
	 * @param	ppmRequestMode		Request mode (see MAX31790_PPM_Modes).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getFanPPM(uint8_t fanIndex, uint32_t & ppm, uint8_t ppmRequestMode = MAX31790_PPM_Mode_Fast);

	/**
	 * @brief	Read the fan's current PWM duty cycle.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	pwm					[out] Duty cycle (9 bits).
	 * @param	fullSpeed			[out] Full speed flag (optional).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getFanPWM(uint8_t fanIndex, uint16_t & pwm, bool * fullSpeed = nullptr);

	/**
	 * @brief	Read the fan's target PWM duty cycle.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	pwmTarget			[out] Target duty cycle (9 bits).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getFanPWMTarget(uint8_t fanIndex, uint16_t & pwmTarget);

	/**
	 * @brief	Set the fan's target PWM duty cycle and (if necessary) the minimum PPM value.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	pwmTarget			Target duty cycle (9 bits).
	 * @param	minPPM				Minimum PPM (optional, used to update the TACH target).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setFanPWMTarget(uint8_t fanIndex, uint16_t pwmTarget, uint32_t * minPPM);

	/**
	 * @brief	Get the fan's target PPM for the RPM control mode.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	ppmTarget			[out] Target PPM.
	 * @retval	HAL_OK.
	 */
	HAL_StatusTypeDef getFanPPMTarget(uint8_t fanIndex, uint32_t & ppmTarget);

	/**
	 * @brief	Set the fan's target PPM for the RPM control mode.
	 * @note	The RPM (closed-loop) mode isn't used by the firmware and hasn't been tested.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	ppmTarget			Target PPM (0 - switch the fan to the monitor-only mode).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setFanPPMTarget(uint8_t fanIndex, uint32_t ppmTarget);

	/**
	 * @brief	Set the control loop "Window" for the RPM mode.
	 * @param	fanIndex			Fan index (0 - 5).
	 * @param	window				Window register value.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setFanWindow(uint8_t fanIndex, uint8_t window);

	/**
	 * @brief	Read a User byte.
	 * @param	userByteAddress		User byte address (see MAX31790_User_Bytes_Addresses).
	 * @param	userByte			[out] Value.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getUserByte(uint8_t userByteAddress, uint8_t & userByte);

	/**
	 * @brief	Write a User byte.
	 * @param	userByteAddress		User byte address (see MAX31790_User_Bytes_Addresses).
	 * @param	userByte			Value.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setUserByte(uint8_t userByteAddress, uint8_t userByte);

	/**
	 * @brief	Calculate the PPM from the TACH count and the Speed Range.
	 * @param	speedRange			Speed Range (0 - MAX31790_MAX_SPEED_RANGE).
	 * @param	tach				TACH count.
	 * @retval	Pulses Per Minute (0 - stopped, MAX31790_INDEFINITE_PPM if TACH = 0).
	 */
	uint32_t calculatePPM(uint8_t speedRange, uint16_t tach);

};


#endif /* _MAX31790_ */

// END-OF-FILE
