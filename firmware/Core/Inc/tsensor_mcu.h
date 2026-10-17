////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// tsensor_mcu.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	tsensor_mcu.h
 * @brief	Subclass serving the temperature sensor built into the MCU.
 */

#ifndef _TSENSOR_MCU_
#define _TSENSOR_MCU_


// includes
#include "tsensor_base.h"
#include "adc_channel.h"


// Class-level definitions
#define TSENSOR_TEMP30_CAL_ADDR					((uint16_t*) ((uint32_t)0x1FFFF7B8))	///< TS_CAL1: ADC value at 30 C and VDDA = 3.3 V (factory calibration)
#define TSENSOR_TEMP110_CAL_ADDR				((uint16_t*) ((uint32_t)0x1FFFF7C2))	///< TS_CAL2: ADC value at 110 C and VDDA = 3.3 V (factory calibration)
#define TSENSOR_MAX_IDENTICAL_VALUES_COUNT		30		///< To check for non-updating ADC readings


/**
 * @brief	MCU internal temperature sensor.
 * @note	The sensor is also an ADC channel, its update() is called from the ADC
 * 			conversion complete interrupt. The readings are corrected by the MCU
 * 			supply voltage measured via the reference channel (VREFINT).
 */
class TSensor_MCU : public TSensor_base, public ADC_Channel {

private:

protected:

	int16_t identicalValuesCounter = 0;				///< Counter of identical consecutive readings (to detect a stuck ADC)


public:

	ADC_Channel * const referenceChannel;			///< Reference (VREFINT) channel


	/**
	 * @brief	Get the base name of the sensor (we need to know it before we create an object of the class).
	 * @retval	Base name.
	 */
	static const char * baseName() { return ("MCU"); }

	/**
	 * @brief	Prepare the default sensor configuration.
	 * @param	newConfig			Configuration to fill.
	 * @param	name				Sensor name, nullptr - use the base name.
	 * @param	serialNumber		Sensor serial number (optional).
	 */
	static void prepareConfig(TSensor_Config * newConfig, char * name = nullptr, uint8_t * serialNumber = nullptr) {
		if (newConfig == nullptr) return;

		// Prefill the default configuration
		TSensor_base::prepareConfig(newConfig, (name == nullptr) ? TSensor_MCU::baseName() : name, serialNumber);

		// Append default configuration
		newConfig->type = TSensor_Type_MCU;
		newConfig->flags = TSensor_Flag_Visible | TSensor_Flag_Permanent;
	}


	/**
	 * @brief	Constructor.
	 * @param	adcValueCell		Pointer to the temperature sensor cell of the ADC DMA buffer.
	 * @param	refChannel			Reference (VREFINT) channel.
	 */
	TSensor_MCU(uint16_t * adcValueCell, ADC_Channel * refChannel);

	virtual ~TSensor_MCU() = default;

	/**
	 * @copydoc TSensor_base::pollAndCheck()
	 * @note	The readings are considered invalid if the temperature doesn't change
	 * 			for more than TSENSOR_MAX_IDENTICAL_VALUES_COUNT polls.
	 */
	bool pollAndCheck() override;

};

#endif /* _TSENSOR_MCU_ */

// END-OF-FILE
