////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// tsensor_DS1841.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	tsensor_DS1841.h
 * @brief	Subclass serving the DS1841 integrated digipot temperature sensor.
 */

#ifndef _TSENSOR_DS1841_
#define _TSENSOR_DS1841_


// Includes
#include "tsensor_base.h"
#include "DS1841.h"


/**
 * @brief	Temperature sensor built into the DS1841 digiPOT (DC-DC converter temperature).
 * @note	The sensor resolution is 1 C.
 */
class TSensor_DS1841 : public TSensor_base {

private:

protected:

public:

	DS1841 * const potDriver;						///< DS1841 driver object
	const uint8_t potIndex;							///< Corresponding Digipot address (0 - 3)


	/**
	 * @brief	Get the base name of the sensor (we need to know it before we create an object of the class).
	 * 			The fan number is appended to it.
	 * @retval	Base name.
	 */
	static const char * baseName() { return ("DC/DC F"); }

	/**
	 * @brief	Prepare the default sensor configuration.
	 * @param	newConfig			Configuration to fill.
	 * @param	name				Sensor name, nullptr - use the base name.
	 * @param	serialNumber		Sensor serial number (optional).
	 */
	static void prepareConfig(TSensor_Config * newConfig, char * name, uint8_t * serialNumber = nullptr) {
		if (newConfig == nullptr) return;

		// Prefill the default configuration
		TSensor_base::prepareConfig(newConfig, (name == nullptr) ? TSensor_DS1841::baseName() : name, serialNumber);

		// Append default configuration
		newConfig->type = TSensor_Type_DS1841;
		newConfig->flags = TSensor_Flag_Visible | TSensor_Flag_Permanent;
	}


	/**
	 * @brief	Constructor.
	 * @param	driver				DS1841 driver object.
	 * @param	index				Corresponding Digipot address (0 - 3).
	 */
	TSensor_DS1841(DS1841 * driver, uint8_t index);

	virtual ~TSensor_DS1841() = default;

	/**
	 * @copydoc TSensor_base::reInit()
	 * @note	Enables the automatic updates of the DS1841 (required for the temperature readings).
	 */
	HAL_StatusTypeDef reInit() override;

	bool pollAndCheck() override;

};

#endif /* _TSENSOR_DS1841_ */

// END-OF-FILE
