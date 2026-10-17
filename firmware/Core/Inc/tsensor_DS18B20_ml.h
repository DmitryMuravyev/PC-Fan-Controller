////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// tsensor_DS18B20_ml.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	tsensor_DS18B20_ml.h
 * @brief	Subclass serving the DS18B20 temperature sensor with multiple lines support.
 */

#ifndef _TSENSOR_DS18B20_ML_
#define _TSENSOR_DS18B20_ML_


// Includes
#include "tsensor_base.h"
#include "DS18B20.h"


// Class-level definitions
#define TSENSOR_REINIT_HW_ERRORS_COUNT		3		///< The number of errors before trying to find the sensor on another line
#define TSENSOR_LINE_UNDEFINED				-1		///< Sensor's line unknown marker
#define TSENSOR_STALE_DATA_TIMEOUT			2000	///< The readings are considered outdated if no fresh conversion results
													///< have been received for this time (in milliseconds).


/**
 * @brief	DS18B20 temperature sensor that can be connected to any of several OneWire lines.
 * @note	The line is detected automatically by the sensor ROM code, and is searched again
 * 			if the sensor stops responding. The temperature conversion is started for the whole
 * 			line at once (see DS18B20::startConversionAll()), the sensor only reads the results.
 */
class TSensor_DS18B20_ML : public TSensor_base {

private:

	/**
	 * @brief	Count a reading error, and drop the line on persistent errors
	 * 			(the sensor will be searched again on all lines).
	 */
	void countReadingError();

protected:

	int8_t currentLine = TSENSOR_LINE_UNDEFINED;	///< Index of the line the sensor is connected to.
	uint32_t lastReadTick = 0;						///< Start timestamp of the line-wide conversion the sensor has read last
													///< (or the timestamp of the sensor initialization). Only the conversions
													///< started after this moment are read, and the readings become outdated
													///< if this moment is older than TSENSOR_STALE_DATA_TIMEOUT.


public:

	DS18B20 * const sensorDriverPool;				///< Array of OneWire lines (DS18B20 driver objects)
	const uint8_t linesCount;						///< Number of OneWire lines


	/**
	 * @brief	Prepare the default sensor configuration.
	 * @param	newConfig			Configuration to fill.
	 * @param	name				Sensor name, nullptr - use the base name.
	 * @param	serialNumber		Sensor ROM code.
	 */
	static void prepareConfig(TSensor_Config * newConfig, char * name, uint8_t * serialNumber) {
		if (newConfig == nullptr) return;

		// Prefill the default configuration
		TSensor_base::prepareConfig(newConfig, name, serialNumber);

		// Append default configuration
		newConfig->type = TSensor_Type_DS18B20;
		// TODO: In future firmware versions, the sensor may be temporary.
		newConfig->flags = TSensor_Flag_Visible | TSensor_Flag_Active | TSensor_Flag_Deletable | TSensor_Flag_Permanent;
		newConfig->config = DS18B20_DEFAULT_CONFIGURATION;
	}

	/**
	 * @brief	Constructor.
	 * @param	drivers				Array of OneWire lines (DS18B20 driver objects).
	 * @param	count				Number of OneWire lines.
	 */
	TSensor_DS18B20_ML(DS18B20 * drivers, uint8_t count);

	virtual ~TSensor_DS18B20_ML() = default;

	/**
	 * @copydoc TSensor_base::reInit()
	 * @note	Searches for the sensor on all lines, and checks and (if necessary) writes
	 * 			the sensor resolution. Only the line-wide conversions started after
	 * 			the initialization will be read.
	 */
	HAL_StatusTypeDef reInit() override;

	/**
	 * @copydoc TSensor_base::pollAndCheck()
	 * @note	Reads the result of the last completed line-wide conversion (if it hasn't been read yet).
	 * 			The readings become invalid if no fresh results are received for TSENSOR_STALE_DATA_TIMEOUT.
	 * 			If the sensor stops responding, it is searched again on all lines.
	 */
	bool pollAndCheck() override;

};

#endif /* _TSENSOR_DS18B20_ML_ */

// END-OF-FILE
