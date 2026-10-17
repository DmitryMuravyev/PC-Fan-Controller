////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// tsensor_DS18B20.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include "tsensor_DS18B20.h"


// Constructor
TSensor_DS18B20::TSensor_DS18B20(DS18B20 * driver) : TSensor_base(TSensor_Type_DS18B20), sensorDriver(driver) {

}


// Initialization or re-initialization (after changing the settings) of the sensor
HAL_StatusTypeDef TSensor_DS18B20::reInit() {
	HAL_StatusTypeDef result = HAL_ERROR;
	if ((configuration != nullptr) && (sensorDriver != nullptr)) {
		result = sensorDriver->checkAndSetConfig(configuration->serialNumber, (uint8_t)configuration->config);
		// Start the first conversion right away, otherwise the next reading
		// will return the power-on value of the scratchpad (85 °C).
		if (result == HAL_OK) startConversion();
	}

	if (result == HAL_OK) {
		state.status |= (TSensor_Status_Connected | TSensor_Status_Configured);
	} else {
		state.status &= ~(uint8_t)TSensor_Status_Connected;
	}

	return (result);
}


// Start DS18B20 temperature conversion
void TSensor_DS18B20::startConversion() {
	if ((configuration != nullptr) && (sensorDriver != nullptr)) {
		if (sensorDriver->startConversion(configuration->serialNumber) == HAL_OK) {
			if (hwErrorCounter > 0) hwErrorCounter--;
		} else {
			state.totalErrorsCount++;
			if (hwErrorCounter < 0xFF) hwErrorCounter++;
		}
	}
}


// Request the temperature from the sensor and check the alarms
bool TSensor_DS18B20::pollAndCheck() {
	int16_t temp;
	if ((configuration != nullptr) && (sensorDriver != nullptr)) {
		// Skip the reading if the conversion hasn't been completed yet
		// (e.g. immediately after the sensor initialization).
		if (!sensorDriver->checkLastConversionDone((uint8_t)configuration->config)) {
			updateStatus();
			return ((state.status & TSensor_Status_Any_Problems_Mask) != 0);
		}

		if (sensorDriver->readTemperature(configuration->serialNumber, temp) == HAL_OK) {

#if defined(TSENSOR_AUTOSTART_CONVERSION)
			startConversion();
#endif
			state.temperature = temp;
			state.status |= TSensor_Status_Valid;
			if (hwErrorCounter > 0) hwErrorCounter--;
		} else {
			state.status &= ~(uint8_t)TSensor_Status_Valid;
			state.totalErrorsCount++;
			if (hwErrorCounter < 0xFF) hwErrorCounter++;
		}

		updateStatus();
	}

	return ((state.status & TSensor_Status_Any_Problems_Mask) != 0);
}

// END-OF-FILE
