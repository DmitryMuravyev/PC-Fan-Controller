////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// tsensor_sw.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	tsensor_sw.h
 * @brief	A subclass serving software temperature sensors received via USB.
 */

#ifndef _TSENSOR_SW_
#define _TSENSOR_SW_


// includes
#include "tsensor_base.h"


// Class-level definitions
#define TSENSOR_BOOT_CYCLES					60		///< Give the system time to boot up and run the control software
													///< that transmits the readings of the software sensors (polls).
#define TSENSOR_MAX_NO_UPDATES_COUNT		5		///< To check for non-updating sensor data (polls)


/**
 * @brief	Software (virtual) temperature sensor, the readings of which are transmitted
 * 			by the control application via USB.
 * @note	TSensor_Config::config holds the number of polls to wait for the first reading
 * 			after the start (see TSENSOR_BOOT_CYCLES). updateTemperature() is called
 * 			from the USB request handler (PendSV).
 */
class TSensor_SW : public TSensor_base {

private:

protected:

	uint32_t noUpdatesCounter = 0;					///< Number of polls without new readings
	bool updated = false;							///< At least one reading has been received since the start


public:

	/**
	 * @brief	Prepare the default sensor configuration.
	 * @param	newConfig			Configuration to fill.
	 * @param	name				Sensor name, nullptr - use the base name.
	 * @param	serialNumber		Sensor serial number (assigned by the host).
	 */
	static void prepareConfig(TSensor_Config * newConfig, char * name, uint8_t * serialNumber) {
		if (newConfig == nullptr) return;

		// Prefill the default configuration
		TSensor_base::prepareConfig(newConfig, name, serialNumber);

		// Append default configuration
		newConfig->type = TSensor_Type_SW;
		newConfig->flags = TSensor_Flag_Visible;
		newConfig->config = TSENSOR_BOOT_CYCLES;
	}


	/**
	 * @brief	Constructor.
	 */
	TSensor_SW();

	virtual ~TSensor_SW() = default;

	/**
	 * @brief	Update the sensor temperature according to the data received via USB.
	 * @param	temp				Temperature (Q-format, see TSENSOR_FRACTION_BITS).
	 */
	void updateTemperature(int16_t temp);

	/**
	 * @copydoc TSensor_base::prepareToHostShutdown()
	 * @note	Restarts the waiting for the first reading (the host will boot again).
	 */
	void prepareToHostShutdown() override;

	/**
	 * @copydoc TSensor_base::pollAndCheck()
	 * @note	The readings become invalid, and the error is raised, if no new readings
	 * 			are received for TSENSOR_MAX_NO_UPDATES_COUNT polls (or for the boot period
	 * 			before the first reading).
	 */
	bool pollAndCheck() override;

};

#endif /* _TSENSOR_SW_ */

// END-OF-FILE
