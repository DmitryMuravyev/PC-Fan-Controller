////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// adc_channel.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include "adc_channel.h"


// Initialize static variable
uint32_t ADC_Channel::samplingTimeInterval = ADC_CHANNEL_TIME_INTERVAL;


// Constructor 1
ADC_Channel::ADC_Channel(uint16_t * adcValueCell) : rawADCValue(adcValueCell) {
}


// Constructor 2
ADC_Channel::ADC_Channel(uint16_t * adcValueCell, uint8_t smoothingFactor) : rawADCValue(adcValueCell), emaSmoothingFactor(smoothingFactor) {
}



// Reset EMA filtered value to get the correct average as quickly as possible.
void ADC_Channel::reset() {
	if (rawADCValue != nullptr) filteredValue = *rawADCValue << ADC_CHANNEL_FRACTION_BITS;
}


// Update EMA filtered value
void ADC_Channel::update() {
	if (rawADCValue != nullptr) {
		filteredValue += (((*rawADCValue << ADC_CHANNEL_FRACTION_BITS) - filteredValue) >> emaSmoothingFactor);
	}
}


// Get EMA Time Constant
// The approximate time it takes for the average value to reach 63% of input value level.
uint32_t ADC_Channel::getTimeConstant() {
	return (ADC_Channel::samplingTimeInterval * (1 << emaSmoothingFactor));
}

// END-OF-FILE
