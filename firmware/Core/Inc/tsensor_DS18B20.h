////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// tsensor_DS18B20.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	tsensor_DS18B20.h
 * @brief	Subclass serving the DS18B20 temperature sensor (single OneWire line).
 * @note	Not used in this project (see tsensor_DS18B20_ml.h), kept as an example.
 */

#ifndef _TSENSOR_DS18B20_
#define _TSENSOR_DS18B20_


// Includes
#include "tsensor_base.h"
#include "DS18B20.h"


// Class-level definitions
#ifndef TSENSOR_AUTOSTART_CONVERSION
#define TSENSOR_AUTOSTART_CONVERSION				///< Autostart the conversion after each temperature reading
#endif


/**
 * @brief	DS18B20 temperature sensor connected to a single OneWire line.
 */
class TSensor_DS18B20 : public TSensor_base {

private:

protected:

public:

	DS18B20 * const sensorDriver;					///< OneWire line (DS18B20 driver object)


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
	 * @param	driver				OneWire line (DS18B20 driver object).
	 */
	TSensor_DS18B20(DS18B20 * driver);

	virtual ~TSensor_DS18B20() = default;

	/**
	 * @copydoc TSensor_base::reInit()
	 * @note	Checks and (if necessary) writes the sensor resolution, and starts the first conversion.
	 */
	HAL_StatusTypeDef reInit() override;

	/**
	 * @brief	Start the temperature conversion.
	 */
	void startConversion();

	/**
	 * @copydoc TSensor_base::pollAndCheck()
	 * @note	The result of the previous conversion is read, and the next one is started.
	 */
	bool pollAndCheck() override;

};

#endif /* _TSENSOR_DS18B20_ */

// END-OF-FILE
