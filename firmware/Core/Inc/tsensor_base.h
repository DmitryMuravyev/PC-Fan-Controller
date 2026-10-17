////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// tsensor_base.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	tsensor_base.h
 * @brief	Base class for inheritance by classes of supported temperature sensors:
 * 			configuration, status handling and calculation of the fan Control Values.
 */

#ifndef _TSENSOR_BASE_
#define _TSENSOR_BASE_


// Includes
#include "board_config.h"
#include "stm32f0xx_hal.h"
#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>
#include <string.h>


// Class-level definitions
// Most of these constants are intended for internal use,
// but I'll still leave them in the header file for convenience.

// Basic constants
#define TSENSOR_FRACTION_BITS				4			///< Q-format bits for fractional part

#define TSENSOR_NAME_SIZE					8			///< Sensor Name string length (without the terminating zero)
#define TSENSOR_SN_SIZE						8			///< SN string length

#define TSENSOR_FIRST_SW_SENSOR_TYPE		32			///< First type of software sensors
#define TSENSOR_LAST_SW_SENSOR_TYPE			32			///< Last type of software sensors (we will not use different types yet)

// Temperature constants
#define TSENSOR_OVERCOOL_MARGIN				2			///< If the temperature is below the lower limit for this margin, the fan can be turned off.
#define TSENSOR_LOW_TEMP					20			///< 20C - below this temperature the Fan is Off or runs at minimum speed (0%)
#define TSENSOR_HIGH_TEMP					50			///< 50C - above this temperature the Fan is On or runs at maximum speed (100%)
#define TSENSOR_WARNING_THRESHOLD			60			///< 60C - warning temperature
#define TSENSOR_ALARM_THRESHOLD				70			///< 70C - alarm temperature

// Error handling
#define TSENSOR_MAX_HW_ERRORS_COUNT			10			///< Max consecutive HW errors count before Error status issued

// Fan control parameters
#define TSENSOR_FAN_CONTROL_BIT_WIDTH		16			///< Bit width of the fan speed Control Value
#define TSENSOR_FAN_CONTROL_FULL_RANGE		(1 << TSENSOR_FAN_CONTROL_BIT_WIDTH)		///< Full range of Control Value
#define TSENSOR_FAN_CONTROL_TOP				(TSENSOR_FAN_CONTROL_FULL_RANGE - 1)		///< 0xFFFF


// Types

/**
 * @brief	Supported temperature sensor types.
 */
typedef enum
{
	TSensor_Type_Unknown					= 0,		///< The value will be used for the search
	// On-board sensors: 0 - 15
	TSensor_Type_MCU						= 1,		///< MCU internal temperature sensor
	TSensor_Type_DS1841						= 2,		///< DS1841 digiPOT internal sensor

	// Connectable sensors: 16 - 31
	TSensor_Type_DS18B20					= 16,		///< DS18B20 digital sensor (oneWire)

	// Software sensors: 32 - 127
	TSensor_Type_SW							= 32,		///< Some sensor, the readings of which are transmitted
														///< by the control application via USB.
/*
 	// There is no need to make specific types for each software sensor...
	TSensor_Type_SW_CPU						= 32,		// Host CPU sensor
	TSensor_Type_SW_GPU						= 33,		// Host GPU sensor
	TSensor_Type_SW_MB						= 34,		// Host motherboard sensor
	TSensor_Type_SW_HDD						= 35,		// Host HDD sensor
*/
	// Other sensors: 128 - ...
	TSensor_Type_Other						= 128		///< For future use...
} TSensor_Type;


/**
 * @brief	Sensor configuration flags.
 */
typedef enum
{
	TSensor_Flag_Unknown					= 0b00000000,		///< Dummy value
	TSensor_Flag_Visible					= 0b00000001,		///< The sensor must be displayed on the screen
	TSensor_Flag_Active						= 0b00000010,		///< The sensor readings should affect the fans
	TSensor_Flag_Deletable					= 0b00000100,		///< The sensor can be removed (not available for controller hardware sensors)
	TSensor_Flag_Permanent					= 0b00001000,		///< The sensor configuration is saved and it will be restored after reset
	TSensor_Flag_Ignore_If_Unavailable		= 0b00010000		///< Ignore the sensor (disable alarm and impact on fans) if it is not available
} TSensor_Flags;


/**
 * @brief	Fan impact options (packed into the TSensor_Config::fanImpacts elements).
 * @details	The maximum sensor impact (lower 4 bits) and the impact of the unavailable sensor
 * 			(upper 4 bits) are calculated as 1/15 of the full fan Control range multiplied
 * 			by the option value:
 * @verbatim
	0	=	0
	1	=	6.66%
	2	=	13.33%
	3	=	20%
	4	=	26.66%
	5	=	33.33%
	6	=	40%
	7	=	46.66%
	8	=	53.33%
	9	=	60%
	10	=	66.66%
	11	=	73.33%
	12	=	80%
	13	=	86.66%
	14	=	93.33%
	15	=	100%
   @endverbatim
 */
typedef enum
{
	TSensor_Fan_Impact_Default				= 0b00001111,		///< Default impact = 100%
	TSensor_Fan_Impact_Mask					= 0b00001111,		///< Impact option mask
	TSensor_Fan_Impact_Sensor_Unavailable_Default	= 0b00001001,		///< Set fan speed to 60% if the sensor is unavailable
	TSensor_Fan_Impact_Sensor_Unavailable_Shift		= 4					///< Unavailable sensor fan impact shift
} TSensor_Fan_Impact;


/**
 * @brief	Sensor status flags (TSensor_Info::status).
 */
typedef enum
{
	TSensor_Status_Connected				= 0b00000001,		///< The sensor is connected
	TSensor_Status_Configured				= 0b00000010,		///< The sensor is configured correctly
	TSensor_Status_Valid					= 0b00000100,		///< Sensor readings are relevant
	TSensor_Status_Error					= 0b00010000,		///< Sensor hardware error
	TSensor_Status_Warning					= 0b00100000,		///< Temperature warning
	TSensor_Status_Alarm					= 0b01000000,		///< Temperature error
	TSensor_Status_Any_Problems_Mask		= TSensor_Status_Alarm | TSensor_Status_Warning | TSensor_Status_Error	///< Any problems mask
} TSensor_Status;


/**
 * @brief	The structure defining the sensor and its operation parameters.
 * @note	The structure is stored in Flash and transferred to the host as is.
 * 			1 + (8 + 1) + 8 + 1 + 1 + 4 + 1 + 6 * 3 + 1 (padding) = 25 + 18 + 1 (padding) = 44 bytes.
 */
typedef struct __attribute__((packed, aligned(4)))
{
	uint8_t type;									///< Sensor type (see TSensor_Type)
	char name[TSENSOR_NAME_SIZE + 1];				///< The displayed name of the sensor. Let's reserve
													///< one last byte for the end-of-line character '\0', and after
													///< initializing the configuration, we won't change it anymore.
	uint8_t serialNumber[TSENSOR_SN_SIZE];			///< The unique identifier of the sensor
	uint8_t warningThreshold;						///< Threshold for triggering a warning (C)
	uint8_t alarmThreshold;							///< Threshold for triggering an alarm (C)
	uint32_t config;								///< The use of the configuration depends on the specific subclass
	uint8_t flags;									///< Sensor flags (see TSensor_Flags)
	uint8_t lowThresholds[FANS_COUNT];				///< The low temperature threshold for each fan (C)
	uint8_t highThresholds[FANS_COUNT];				///< The high temperature threshold for each fan (C)
	uint8_t fanImpacts[FANS_COUNT];					///< Max/unavailable sensor impact (see TSensor_Fan_Impact)

} TSensor_Config;


/**
 * @brief	Current sensor state.
 * @note	The structure is transferred to the host as is.
 * 			4 + 2 + 1 + 1 = 8 bytes.
 */
typedef struct __attribute__((packed, aligned(4)))
{
	uint32_t totalErrorsCount;						///< Total errors (HW errors, temperature alarms and warnings) count
	int16_t temperature;							///< Measured sensor's temperature (Q-format, see TSENSOR_FRACTION_BITS)
	uint8_t status;									///< Status bits (see TSensor_Status)
	//
	// ATTENTION!!!
	// Replace overcooledByFanMap var with uint16_t or uint32_t
	// in case if the controller supports more than 8 fans!
	uint8_t overcooledByFanMap;						///< A map of fans for which the temperature of this sensor is below
													///< the lower limit. Used to determine whether the fan can be turned off.
} TSensor_Info;



/**
 * @brief	Base class of a temperature sensor.
 */
class TSensor_base {

private:

protected:

	uint32_t hwErrorCounter = 0;					///< Hardware Errors counter

	/**
	 * @brief	Check the temperature thresholds and the hardware errors, and set/clear the status flags.
	 */
	void updateStatus();


public:

	/**
	 * @brief	Get the base name for new sensors (we need to know it before we create an object of the class).
	 * @retval	Base name.
	 */
	static const char * baseName() { return ("SENS"); }

	/**
	 * @brief	Prepare the default sensor configuration.
	 * @param	newConfig			Configuration to fill.
	 * @param	name				Sensor name, nullptr - use the base name.
	 * @param	serialNumber		Sensor serial number (optional).
	 */
	static void prepareConfig(TSensor_Config * newConfig, const char * name, const uint8_t * serialNumber = nullptr) {
		if (newConfig == nullptr) return;

		// Clean structure
		memset(newConfig, 0, sizeof(TSensor_Config));

		// Use the provided name or the default one
		if (name != nullptr) {
			strncpy(newConfig->name, name, TSENSOR_NAME_SIZE);
		} else {
			strncpy(newConfig->name, TSensor_base::baseName(), TSENSOR_NAME_SIZE);
		}

		// Use the provided SN if any
		if (serialNumber != nullptr) {
			memcpy(newConfig->serialNumber, serialNumber, TSENSOR_SN_SIZE);
		}

		// Initialization of basic parameters
		for(uint8_t i = 0; i < FANS_COUNT; i++) {
			newConfig->lowThresholds[i] = TSENSOR_LOW_TEMP;
			newConfig->highThresholds[i] = TSENSOR_HIGH_TEMP;
			newConfig->fanImpacts[i] = TSensor_Fan_Impact_Default |
					(TSensor_Fan_Impact_Sensor_Unavailable_Default << TSensor_Fan_Impact_Sensor_Unavailable_Shift);
		}

		newConfig->warningThreshold = TSENSOR_WARNING_THRESHOLD;
		newConfig->alarmThreshold = TSENSOR_ALARM_THRESHOLD;
	}

	/**
	 * @brief	Check if the sensor is "virtual" and its readings
	 * 			are transmitted by the control application via USB.
	 * @param	config				Sensor configuration.
	 * @retval	true for a software sensor.
	 */
	static bool isSoftwareSensor(const TSensor_Config * config) {
		return ((config != nullptr) && (config->type >= TSENSOR_FIRST_SW_SENSOR_TYPE) && (config->type <= TSENSOR_LAST_SW_SENSOR_TYPE));
	}


	const uint8_t sensorType;						///< Type of the sensor object (see TSensor_Type)
	TSensor_Config * configuration = nullptr;		///< Active sensor configuration (nullptr - the sensor isn't configured)
	TSensor_Info state = { 0 };						///< Current sensor state

	/**
	 * @brief	Constructor.
	 * @param	type				Sensor type (see TSensor_Type).
	 */
	TSensor_base(uint8_t type);

	virtual ~TSensor_base() = default;

	/**
	 * @brief	Apply a new or saved configuration, reset the sensor state and (re)initialize the sensor.
	 * @param	newConfig			Configuration to apply.
	 * @retval	HAL status of the sensor initialization.
	 */
	HAL_StatusTypeDef applyConfig(TSensor_Config * newConfig);

	/**
	 * @brief	Reset the sensor errors and status, and (re)initialize the sensor.
	 * @retval	HAL status of the sensor initialization.
	 */
	HAL_StatusTypeDef resetState();

	/**
	 * @brief	Initialize or re-initialize (after changing the settings) the sensor.
	 * @retval	HAL status.
	 */
	virtual HAL_StatusTypeDef reInit() { return (HAL_OK); }

	/**
	 * @brief	Prepare the sensor to the host shutdown (reboot or power off).
	 */
	virtual void prepareToHostShutdown() {}

	/**
	 * @brief	Poll the sensor, update the temperature and the status flags.
	 * @note	Called once per second from the main loop.
	 * @retval	true if the status contains any problems (warning, alarm or error).
	 */
	virtual bool pollAndCheck() {
		updateStatus();
		return ((state.status & TSensor_Status_Any_Problems_Mask) != 0);
	}

	/**
	 * @brief	Calculate the Control Value for the specified fan according to the sensor readings.
	 * @param	fan					Fan index.
	 * @param	overcool			[out] true if the sensor allows the fan to be halted
	 * 								(the temperature is below the low threshold, with hysteresis).
	 * @retval	Control Value (0x0000 - 0xFFFF ~ 0-100%).
	 */
	virtual uint16_t getControlForFan(uint8_t fan, bool * overcool);

};

#endif /* _TSENSOR_BASE_ */

// END-OF-FILE
