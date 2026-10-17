////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// tsensor_DS18B20_ml.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include <tsensor_DS18B20_ml.h>


// Constructor
TSensor_DS18B20_ML::TSensor_DS18B20_ML(DS18B20 * drivers, uint8_t count) : TSensor_base(TSensor_Type_DS18B20), sensorDriverPool(drivers), linesCount(count) {

}


// Initialization or re-initialization (after changing the settings) of the sensor
HAL_StatusTypeDef TSensor_DS18B20_ML::reInit() {
	HAL_StatusTypeDef result = HAL_ERROR;
	currentLine = TSENSOR_LINE_UNDEFINED;
	if ((configuration != nullptr) && (sensorDriverPool != nullptr)) {
		OneWire_Search_Context searchContext;
		for (uint8_t i = 0; i < linesCount; i++) {
			DS18B20 * currentDriver = &sensorDriverPool[i];
			currentDriver->restartSearch(searchContext, configuration->serialNumber);

			if (currentDriver->findNext(searchContext, false) == HAL_OK) {
				currentLine = i;
				result = currentDriver->checkAndSetConfig(configuration->serialNumber, (uint8_t)configuration->config);

				// Only the line-wide conversions started after this moment will be read,
				// otherwise we could get the power-on value of the scratchpad (85 C).
				lastReadTick = HAL_GetTick();

				// The driver (line) currently servicing this sensor has been found.
				break;
			}
		}
	}

	if (currentLine >= 0) {
		state.status |= TSensor_Status_Connected;
		if (result == HAL_OK) state.status |= TSensor_Status_Configured;
	} else {
		if (hwErrorCounter < 0xFF) hwErrorCounter++;
		state.status &= ~(uint8_t)TSensor_Status_Connected;
	}

	return (result);
}


// Count a reading error and drop the line on persistent errors
void TSensor_DS18B20_ML::countReadingError() {
	state.status &= ~(uint8_t)TSensor_Status_Valid;
	state.totalErrorsCount++;
	if (hwErrorCounter < 0xFF) hwErrorCounter++;
	if (hwErrorCounter >= TSENSOR_REINIT_HW_ERRORS_COUNT) {
		currentLine = TSENSOR_LINE_UNDEFINED;
	}
}


// Read the temperature from the sensor and check the alarms
bool TSensor_DS18B20_ML::pollAndCheck() {
	if ((configuration != nullptr) && (sensorDriverPool != nullptr)) {
		if (currentLine >= 0) {
			DS18B20 * currentDriver = &sensorDriverPool[currentLine];

			// Start timestamp of the last completed line-wide conversion (0 - none).
			// We read only the conversion that has been started after the previous
			// reading (or after the sensor initialization).
			uint32_t conversionTick = currentDriver->getCompletedConversionTick();

			if ((conversionTick != 0) && ((int32_t)(conversionTick - lastReadTick) > 0)) {
				int16_t temp;
				lastReadTick = conversionTick;

				if (currentDriver->readTemperature(configuration->serialNumber, temp) == HAL_OK) {
					state.temperature = temp;
					state.status |= TSensor_Status_Valid;
					if (hwErrorCounter > 0) hwErrorCounter--;
				} else {
					countReadingError();
				}

			} else if ((HAL_GetTick() - lastReadTick) > TSENSOR_STALE_DATA_TIMEOUT) {
				// There are no fresh conversion results for too long
				// (e.g. the line-wide conversion can't be started).
				countReadingError();
			}

		} else {
			TSensor_DS18B20_ML::reInit();
		}

		updateStatus();
	}

	return ((state.status & TSensor_Status_Any_Problems_Mask) != 0);
}

// END-OF-FILE
