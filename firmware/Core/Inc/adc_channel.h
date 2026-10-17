////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// adc_channel.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	adc_channel.h
 * @brief	A class serving the EMA (Exponential Moving Average) filtering for the ADC channel.
 *
 * @attention	CORRECT ADC MEASUREMENT IS ONLY POSSIBLE IF
 * 				THE POWER SUPPLY OF THE MCU IS STABLE ENOUGH!
 */

#ifndef _ADC_CHANNEL_
#define _ADC_CHANNEL_


// Includes
#include <stdint.h>
#include <stddef.h>


// Class-level definitions
#define ADC_VREFINT_CAL_ADDR				((uint16_t*) ((uint32_t)0x1FFFF7BA))	///< VREFINT calibration value (VDDA = 3.3 V), relevant for STM32F070
#define ADC_CHANNEL_BIT_WIDTH				12		///< ADC bit width
#define ADC_CHANNEL_FRACTION_BITS			4		///< Q-format bits for fractional part
#define ADC_CHANNEL_TIME_INTERVAL			10		///< Sampling time interval in milliseconds
#define ADC_CHANNEL_EMA_SMOOTHING_FACTOR	3		///< Exponential moving average smoothing factor in the form of bit shift.
													///< 3 ~ a=1/8 i.e right shift by 3 bits.
													///< See https://en.wikipedia.org/wiki/Exponential_smoothing
													///< ATTENTION!!! Increase this constant by 1 (or more)
													///< in case if the MCU is powered by an unstable power source!


/**
 * @brief	ADC channel with EMA filtering.
 * @note	update() is called from the ADC conversion complete interrupt,
 * 			so filteredValue may change at any moment.
 */
class ADC_Channel {

private:

protected:

public:

	uint16_t * const rawADCValue = nullptr;					///< Pointer to the channel cell of the ADC DMA buffer

	static uint32_t samplingTimeInterval;					///< Interval between the update() calls in milliseconds

	uint8_t emaSmoothingFactor = ADC_CHANNEL_EMA_SMOOTHING_FACTOR;	///< EMA smoothing factor (bit shift)
	volatile uint16_t filteredValue = 0;					///< Filtered ADC value (Q-format, see ADC_CHANNEL_FRACTION_BITS)

	/**
	 * @brief	Constructor (with the default smoothing factor).
	 * @param	adcValueCell		Pointer to the channel cell of the ADC DMA buffer.
	 */
	ADC_Channel(uint16_t * adcValueCell);

	/**
	 * @brief	Constructor.
	 * @param	adcValueCell		Pointer to the channel cell of the ADC DMA buffer.
	 * @param	smoothingFactor		EMA smoothing factor (bit shift).
	 */
	ADC_Channel(uint16_t * adcValueCell, uint8_t smoothingFactor);

	virtual ~ADC_Channel() = default;

	/**
	 * @brief	Reset the filtered value to the current raw ADC value
	 * 			to get the correct average as quickly as possible.
	 */
	void reset();

	/**
	 * @brief	Update the filtered value with the current raw ADC value.
	 * @note	Must be called after each ADC conversion (every samplingTimeInterval).
	 */
	void update();

	/**
	 * @brief	Get the EMA Time Constant: the approximate time it takes
	 * 			for the average value to reach 63% of the input value level.
	 * @retval	Time constant in milliseconds.
	 */
	uint32_t getTimeConstant();

};

#endif /* _ADC_CHANNEL_ */

// END-OF-FILE
