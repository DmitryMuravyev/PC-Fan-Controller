////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// vmeter.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include <vmeter.h>


// Constructor 1
VMeter::VMeter(uint16_t * adcValueCell, ADC_Channel * refChannel,
		uint16_t upperResistorValue, uint16_t lowerResistorValue) : ADC_Channel(adcValueCell) {
	// assert_param((refChannel != nullptr) && (upperResistorValue > 0) && (lowerResistorValue > 0));

	referenceChannel = refChannel;
	upperResistor = upperResistorValue;
	lowerResistor = lowerResistorValue;
}

// Constructor 2
VMeter::VMeter(uint16_t * adcValueCell, uint8_t smoothingFactor, ADC_Channel * refChannel,
		uint16_t upperResistorValue, uint16_t lowerResistorValue) : VMeter(adcValueCell, refChannel, upperResistorValue, lowerResistorValue) {

	emaSmoothingFactor = smoothingFactor;
}


// Calculate the voltage across the divider
uint16_t VMeter::calculateDividerVoltage(uint32_t adcValue) {
	//
	// Vadc = (ADCval/4096) * Vcc
	// MCU Vcc = (Vcal/Vref) * 3.3
	//
	// We getting the voltage from the middle point of the resistive divider
	// so the voltage on the divider is:
	//
	// V = (Vadc * (R1 + R2)) / R2, where R1 - upper resistor in divider, R2 - lower one.
	// V = (ADCval * Vcal * 3.3 * (R1 + R2)) / (4096 * Vref * R2)
	//
	// Now we have to get rid of floating point calculations:
	//
	// 3.3 * 256 = 844.8; 845 / 256 = 3.30078125
	// V = (ADCval * Vcal * 845 * R1) / (4096 * 256 * Vref * R2)
	//
	// All adcFiltered values (adcVal, refVal) can be shifted left by 4
	// or more bits (equivalent to multiplying by 16),
	// but we can neglect this because they are divided into each other.
	// Also we have to multiply result by 16 more, because of our data format:
	// SIIIIIIIIIIIFFFF, where S - sign, I - integer part and F - fractional part.
	// The sign can be ignored, because we have to deal with positive values only.
	//
	// V = (ADCval * Vcal * 845 * (R1 + R2) * 16) / (4096 * 256 * Vref * R2)
	// V = (ADCval * Vcal * 845 * (R1 + R2)) / (65536 * Vref * R2)
	//
	// ...phew, not bad, right?
	// Be careful, the order of the operation may affect the overflow.
	//

	if ((referenceChannel == nullptr) || (referenceChannel->filteredValue == 0)) return (0);
	uint32_t tmp = adcValue * 845 / referenceChannel->filteredValue;
	tmp *= (upperResistor + lowerResistor);
	tmp *= (int32_t)*ADC_VREFINT_CAL_ADDR;
	tmp /= lowerResistor;
	return (tmp >> (ADC_CHANNEL_BIT_WIDTH + 8 - ADC_CHANNEL_FRACTION_BITS));
}


// Calculate and return filtered voltage.
uint16_t VMeter::getVoltage() {
	return (calculateDividerVoltage(filteredValue));
}


// Return filtered ADC value adjusted to the specified Q-format.
uint16_t VMeter::getADCValue() {
	return (filteredValue);
}

// END-OF-FILE
