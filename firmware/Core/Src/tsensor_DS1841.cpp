////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// tsensor_DS1841.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include "tsensor_DS1841.h"


// Constructor
TSensor_DS1841::TSensor_DS1841(DS1841 * driver, uint8_t index) : TSensor_base(TSensor_Type_DS1841), potDriver(driver), potIndex(index) {
}


// Initialization or re-initialization (after changing the settings) of the sensor
HAL_StatusTypeDef TSensor_DS1841::reInit() {
	HAL_StatusTypeDef result = HAL_ERROR;
	if ((configuration != nullptr) && (potDriver != nullptr)) {
		uint8_t configRegisterValue;
		result = potDriver->readRegister(potIndex, DS1841_CR1_REG, configRegisterValue);
		if (result == HAL_OK) {
			if ((configRegisterValue & DS1841_EnableUpdates) == 0) {
				configRegisterValue |= DS1841_EnableUpdates;
				result = potDriver->configureAdderUpdateMode(potIndex, configRegisterValue);
			}
		}
	}
	if (result == HAL_OK) {
		state.status |= (TSensor_Status_Connected | TSensor_Status_Configured);
	} else {
		state.status &= ~(uint8_t)TSensor_Status_Connected;
	}

	return (result);
}


// Request the temperature from the sensor and check the alarms
bool TSensor_DS1841::pollAndCheck() {
	int8_t temp;
	if ((configuration != nullptr) && (potDriver != nullptr)) {
		if (potDriver->getTemp(potIndex, temp) == HAL_OK) {
			// We can't replace multiplication with a shift, because we have to preserve the sign.
			state.temperature = temp * (1 << TSENSOR_FRACTION_BITS);
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
