////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// tsensor_mcu.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include <tsensor_mcu.h>
#include <string.h>


// Constructor
TSensor_MCU::TSensor_MCU(uint16_t * adcValueCell, ADC_Channel * refChannel) : TSensor_base(TSensor_Type_MCU), ADC_Channel(adcValueCell), referenceChannel(refChannel) {
}


// Get ADC values, calculate temperature and check alarms
bool TSensor_MCU::pollAndCheck() {
	if (configuration != nullptr) state.status |= TSensor_Status_Configured;
	if ((rawADCValue != nullptr) &&
		(referenceChannel != nullptr) &&
		(referenceChannel->filteredValue != 0)) {
		// This is based on a standard example of temperature measurement,
		// adjusted by MCU supply voltage and taking into account all applicable coefficients.
		// Calculations are performed with an x16 coefficient
		// to match the temperature format of the DS18B20 sensor.
		int32_t mcuTemp = ((int32_t)filteredValue * (int32_t)*ADC_VREFINT_CAL_ADDR << ADC_CHANNEL_FRACTION_BITS) /
				(int32_t)referenceChannel->filteredValue - ((int32_t)*TSENSOR_TEMP30_CAL_ADDR << ADC_CHANNEL_FRACTION_BITS);
		mcuTemp = mcuTemp * (int32_t)(110 - 30);
		mcuTemp = mcuTemp / (int32_t)(*TSENSOR_TEMP110_CAL_ADDR - *TSENSOR_TEMP30_CAL_ADDR);
		mcuTemp = mcuTemp + (30 << ADC_CHANNEL_FRACTION_BITS);

		if (state.temperature != mcuTemp) {
			state.temperature = (int16_t)mcuTemp;
			state.status |= (TSensor_Status_Connected | TSensor_Status_Valid);
			identicalValuesCounter = 0;
			if (hwErrorCounter > 0) hwErrorCounter--;
		} else {
			if (identicalValuesCounter < 0xFF) identicalValuesCounter++;
			if (identicalValuesCounter > TSENSOR_MAX_IDENTICAL_VALUES_COUNT) {
				state.status &= ~(uint8_t)TSensor_Status_Valid;
				state.totalErrorsCount++;
				if (hwErrorCounter < 0xFF) hwErrorCounter++;
			}
		}

		updateStatus();
	}

	return ((state.status & TSensor_Status_Any_Problems_Mask) != 0);
}

// END-OF-FILE
