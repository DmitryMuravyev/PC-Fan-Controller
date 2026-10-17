////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// vmeter.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	vmeter.h
 * @brief	Super simple class serving an ADC-based voltmeter (with a resistive divider).
 *
 * @attention	CORRECT VOLTAGE MEASUREMENT IS ONLY POSSIBLE IF
 * 				THE POWER SUPPLY OF THE MCU IS STABLE ENOUGH!
 */

#ifndef _VMETER_
#define _VMETER_


// Includes
#include "adc_channel.h"
#include <stdint.h>
#include <stddef.h>


/**
 * @brief	ADC-based voltmeter measuring the voltage through a resistive divider.
 * 			The result is corrected by the MCU supply voltage measured via the reference channel (VREFINT).
 */
class VMeter : public ADC_Channel {

private:

	/**
	 * @brief	Calculate the voltage across the divider.
	 * @param	adcValue			Filtered ADC value (Q-format).
	 * @retval	Voltage in Q-format (see ADC_CHANNEL_FRACTION_BITS), 0 if the reference value isn't available yet.
	 */
	uint16_t calculateDividerVoltage(uint32_t adcValue);

public:

	ADC_Channel * referenceChannel = nullptr;		///< Reference (VREFINT) channel
	uint16_t upperResistor = 1;						///< Upper resistor of the divider. The resistance can be in any order
													///< of magnitude (ohms, tens of ohms, kilohms, etc.)...
	uint16_t lowerResistor = 1;						///< ...lower resistor of the divider, but in the same units.

	/**
	 * @brief	Constructor (with the default smoothing factor).
	 * @param	adcValueCell		Pointer to the channel cell of the ADC DMA buffer.
	 * @param	refChannel			Reference (VREFINT) channel.
	 * @param	upperResistorValue	Upper resistor of the divider.
	 * @param	lowerResistorValue	Lower resistor of the divider (in the same units).
	 */
	VMeter(uint16_t * adcValueCell, ADC_Channel * refChannel,
			uint16_t upperResistorValue, uint16_t lowerResistorValue);

	/**
	 * @brief	Constructor.
	 * @param	adcValueCell		Pointer to the channel cell of the ADC DMA buffer.
	 * @param	smoothingFactor		EMA smoothing factor (bit shift).
	 * @param	refChannel			Reference (VREFINT) channel.
	 * @param	upperResistorValue	Upper resistor of the divider.
	 * @param	lowerResistorValue	Lower resistor of the divider (in the same units).
	 */
	VMeter(uint16_t * adcValueCell, uint8_t smoothingFactor, ADC_Channel * refChannel,
			uint16_t upperResistorValue, uint16_t lowerResistorValue);

	virtual ~VMeter() = default;

	/**
	 * @brief	Get the filtered voltage.
	 * @retval	Voltage in Q-format (see ADC_CHANNEL_FRACTION_BITS).
	 */
	uint16_t getVoltage();

	/**
	 * @brief	Get the filtered ADC value.
	 * @retval	ADC value in Q-format (see ADC_CHANNEL_FRACTION_BITS).
	 */
	uint16_t getADCValue();

};

#endif /* _VMETER_ */

// END-OF-FILE
