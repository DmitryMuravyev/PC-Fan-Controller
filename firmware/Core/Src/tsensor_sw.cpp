////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// tsensor_sw.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include "tsensor_sw.h"
#include <string.h>


// Constructor
TSensor_SW::TSensor_SW() : TSensor_base(TSensor_Type_SW) {
}


// Update the software sensor temperature according to the data received via USB
void TSensor_SW::updateTemperature(int16_t temp) {
	state.temperature = temp;
	state.status |= (TSensor_Status_Connected | TSensor_Status_Valid);
	updated = true;
	noUpdatesCounter = 0;
	if (hwErrorCounter > 0) hwErrorCounter--;
}

// Prepare sensor to Host shutdown (reboot or power off)
void TSensor_SW::prepareToHostShutdown() {
	updated = false;
	noUpdatesCounter = 0;
}


// Check the data freshness and alarms
bool TSensor_SW::pollAndCheck() {
	if (configuration != nullptr) {
		state.status |= TSensor_Status_Configured;

		// Set the sensor outdated flag
		if (noUpdatesCounter > TSENSOR_MAX_NO_UPDATES_COUNT) {
			state.status &= ~(uint8_t)TSensor_Status_Valid;
		}

		// Check if we have to alarm the sensor
		if ((updated && (noUpdatesCounter > TSENSOR_MAX_NO_UPDATES_COUNT)) ||
			(!updated && (noUpdatesCounter > configuration->config))) {
			state.totalErrorsCount++;
			hwErrorCounter = TSENSOR_MAX_HW_ERRORS_COUNT;
		}

		updateStatus();
		if (configuration->flags & TSensor_Flag_Active) noUpdatesCounter++;
	}

	return ((state.status & TSensor_Status_Any_Problems_Mask) != 0);
}

// END-OF-FILE
