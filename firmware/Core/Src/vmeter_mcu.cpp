////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// vmeter_mcu.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include <vmeter_mcu.h>


VMeter_MCU::VMeter_MCU(uint16_t * adcValueCell) : ADC_Channel(adcValueCell) {
}

VMeter_MCU::VMeter_MCU(uint16_t * adcValueCell, uint8_t smoothingFactor) : ADC_Channel(adcValueCell, smoothingFactor) {
}



// Calculate and return filtered voltage
uint16_t VMeter_MCU::getVoltage() {
	// MCU power voltage
	//
	// MCU Vcc = (Vcal/Vref) * 3.3
	//
	// All adcFiltered values are shifted left by 4 bits (equivalent to multiplying by 16).
	// So, we have to multiply result by 16 to compensate adcFiltered.data.vref_voltage coefficient
	// Also we have to multiply result by 16 more, because of our data format: SIIIIIIIIIIIFFFF, where S - sign, I - integer part and F - fractional part.
	// The sign can be ignored, because we have to deal with positive values only.
	//
	// 3.3 * 256 = 844.8; 845 / 256 = 3.30078125
	// Vcc = (Vcal * 16 * 16 * 845) / (adcFiltered.data.vref_voltage * 256) = (Vcal * 845) / adcFiltered.data.vref_voltage
	//

	if (filteredValue == 0) return (0);
	return ((uint16_t)((int32_t)*ADC_VREFINT_CAL_ADDR * 845 / filteredValue));
}


// Return filtered ADC value adjusted to the specified Q-format.
uint16_t VMeter_MCU::getADCValue() {
	return (filteredValue);
}


// END-OF-FILE
