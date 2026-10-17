////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// vmeter_mcu.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	vmeter_mcu.h
 * @brief	ADC-based MCU Vdd voltmeter class (based on the internal reference voltage VREFINT).
 *
 * @attention	CORRECT VOLTAGE MEASUREMENT IS ONLY POSSIBLE IF
 * 				THE POWER SUPPLY OF THE MCU IS STABLE ENOUGH!
 */

#ifndef _VMETER_MCU_
#define _VMETER_MCU_


// Includes
#include <adc_channel.h>


/**
 * @brief	MCU Vdd voltmeter. Measures the VREFINT channel, so it is also used
 * 			as the reference channel for the other voltmeters and the MCU temperature sensor.
 */
class VMeter_MCU: public ADC_Channel {

public:

	/**
	 * @brief	Constructor (with the default smoothing factor).
	 * @param	adcValueCell		Pointer to the VREFINT cell of the ADC DMA buffer.
	 */
	VMeter_MCU(uint16_t * adcValueCell);

	/**
	 * @brief	Constructor.
	 * @param	adcValueCell		Pointer to the VREFINT cell of the ADC DMA buffer.
	 * @param	smoothingFactor		EMA smoothing factor (bit shift).
	 */
	VMeter_MCU(uint16_t * adcValueCell, uint8_t smoothingFactor);

	virtual ~VMeter_MCU() = default;

	/**
	 * @brief	Get the MCU supply voltage.
	 * @retval	Voltage in Q-format (see ADC_CHANNEL_FRACTION_BITS), 0 if the value isn't available yet.
	 */
	uint16_t getVoltage();

	/**
	 * @brief	Get the filtered VREFINT ADC value.
	 * @retval	ADC value in Q-format (see ADC_CHANNEL_FRACTION_BITS).
	 */
	uint16_t getADCValue();

};

#endif /* _VMETER_MCU_ */

// END-OF-FILE
