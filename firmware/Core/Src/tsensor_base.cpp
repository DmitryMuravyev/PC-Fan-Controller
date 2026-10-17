////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// tsensor_base.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include <tsensor_base.h>


// Constructor
TSensor_base::TSensor_base(uint8_t type) : sensorType(type) {
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Configuration procedures

// Apply new or saved configuration
HAL_StatusTypeDef TSensor_base::applyConfig(TSensor_Config * newConfig) {
	if (newConfig == nullptr) return  (HAL_ERROR);

	configuration = newConfig;

	// Clean the Sensor Status and (re)initialize the sensor
	return (resetState());
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Sensor handling procedures

// Reset sensor errors and (re)initialize the sensor
HAL_StatusTypeDef TSensor_base::resetState() {
	state.status = 0;
	state.overcooledByFanMap = 0;
	state.totalErrorsCount = 0;
	hwErrorCounter = 0;
	return (reInit());
}


// Check and clear/set status flags
void TSensor_base::updateStatus() {
	if (configuration == nullptr) return;

	uint8_t currentSensorState = state.status;

	if (state.temperature >= ((int16_t)configuration->warningThreshold << TSENSOR_FRACTION_BITS)) {
		currentSensorState |= TSensor_Status_Warning;
		state.totalErrorsCount++;
	} else {
		currentSensorState &= ~TSensor_Status_Warning;
	}

	if (state.temperature >= ((int16_t)configuration->alarmThreshold << TSENSOR_FRACTION_BITS)) {
		currentSensorState |= TSensor_Status_Alarm;
		state.totalErrorsCount++;
	} else {
		currentSensorState &= ~TSensor_Status_Alarm;
	}

	if (hwErrorCounter >= TSENSOR_MAX_HW_ERRORS_COUNT) {
		if ((configuration->flags & TSensor_Flag_Ignore_If_Unavailable) == 0) currentSensorState |= TSensor_Status_Error;
	} else {
		currentSensorState &= ~TSensor_Status_Error;
	}

	state.status = currentSensorState;
}


// Calculate the control value for a specified fan (0x0000 - 0xFFFF ~ 0-100%)
uint16_t TSensor_base::getControlForFan(uint8_t fan, bool * overcool) {
	if ((configuration != nullptr) && (fan < FANS_COUNT)) {
		uint32_t maxControl = TSENSOR_FAN_CONTROL_TOP *
				(configuration->fanImpacts[fan] & TSensor_Fan_Impact_Mask) / TSensor_Fan_Impact_Mask;
		// if (maxControl > TSENSOR_FAN_CONTROL_TOP) maxControl = TSENSOR_FAN_CONTROL_TOP;

		// Check if sensor readings outdated
		bool sensorReadingsOutdated = (state.status & TSensor_Status_Valid) == 0;
		if (sensorReadingsOutdated) {
			// Check if have to ignore the unavailable sensor
			if ((configuration->flags & TSensor_Flag_Ignore_If_Unavailable) != 0) {
				if (overcool) *overcool = true;
				return (0);
			} else {
				if (overcool) *overcool = false;
				uint32_t unavailableSensorControl = TSENSOR_FAN_CONTROL_TOP *
					((configuration->fanImpacts[fan] >> TSensor_Fan_Impact_Sensor_Unavailable_Shift) & TSensor_Fan_Impact_Mask) / TSensor_Fan_Impact_Mask;
				return (uint16_t)unavailableSensorControl;
			}
		}

		// A warm sensor with 0% impact on fan must
		// not prevent the fan from stopping.
		if (maxControl == 0) {
			if (overcool) *overcool = true;
			return (0);
		}

		uint8_t savedOC = (state.overcooledByFanMap >> fan) & 1;
		int16_t lt = (int16_t)configuration->lowThresholds[fan] << TSENSOR_FRACTION_BITS;
		int16_t ht = (int16_t)configuration->highThresholds[fan] << TSENSOR_FRACTION_BITS;
		int16_t temp = state.temperature;

		if (lt < ht) {
			if (temp < (lt - (TSENSOR_OVERCOOL_MARGIN << TSENSOR_FRACTION_BITS))) {
				savedOC = 1;
			} else if (temp >= lt) {
				savedOC = 0;
			}

			if (overcool) *overcool = (bool)savedOC;
			if (savedOC) {
				state.overcooledByFanMap |= (1 << fan);
			} else {
				state.overcooledByFanMap &= ~(1 << fan);
			}

			if (temp < lt) return (0);
			if (temp > ht) return (maxControl);

			// Example:
			// lowerThreshold = 20, upperThresholds = 60, t = 40, fanImpact = 10
			// lt = 320, ht = 960, temp = 640, maxControl = 43690
			// control = (640 - 320) * 43690 / (960 - 320) = 13980800 / 640 = 21845
			uint32_t control = (uint32_t)(temp - lt) * maxControl / (ht - lt);
			return ((uint16_t)control);
		} else {
			// Degenerate thresholds (lt >= ht): simple on/off control at the low threshold
			if (overcool) *overcool = (temp < lt);
			if (temp >= lt) return (maxControl);
			return (0);
		}
	}

	// Without a configuration, the sensor doesn't prevent the fan from being halted
	if (overcool) *overcool = true;
	return (0);
}

// END-OF-FILE
