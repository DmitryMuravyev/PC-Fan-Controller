////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// fan_MAX31790_DS1841.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	fan_MAX31790_DS1841.h
 * @brief	The middleware between the fan logic and the hardware drivers:
 * 			PWM and tachometer - MAX31790, DC-DC control - DS1841 digiPOT,
 * 			power on/off - MCU GPIO (MOSFET or DC-DC Enable pin),
 * 			voltage measurement - MCU ADC.
 */

#ifndef _FAN_MAX31790_DS1841_
#define _FAN_MAX31790_DS1841_


// Includes
#include "fan_base.h"
#include "MAX31790.h"
#include "DS1841.h"
#include "vmeter.h"


// Class-level definitions
#define FAN_TACH_CHANGE_TIMEOUT			10000		///< Timeout for waiting for a change in the tacho pulses period.
													///< It's pretty big here, but I hope it can be done much better
													///< in the next board version.
#define FAN_DEFAULT_DCDC_VALUE			0			///< DC-DC voltage value immediately after powering on


/**
 * @brief	Fan channel based on the MAX31790 fan controller, the DS1841 digiPOT (DC-DC control),
 * 			a GPIO-controlled MOSFET (or DC-DC Enable pin) and an ADC voltmeter.
 * @note	Any of the DS1841, MOSFET or voltmeter may be absent for a channel (nullptr in the constructor).
 */
class Fan_MAX31790_DS1841 : public Fan_base {

private:

	/**
	 * @brief	Wait for a change in the tacho pulse period measured by the MAX31790.
	 * @param	tachIn			Initial tacho value (optional, nullptr - read the current one).
	 * @param	tachOut			[out] New tacho value (optional).
	 * @param	timeout			Timeout in milliseconds.
	 * @retval	HAL status of the I2C communication (the timeout isn't considered an error).
	 */
	HAL_StatusTypeDef waitForTachChange(uint16_t * tachIn = nullptr, uint16_t * tachOut = nullptr, uint16_t timeout = FAN_TACH_CHANGE_TIMEOUT);


protected:

	volatile uint16_t lastPWMControl;					///< Last PWM value written to the MAX31790.
	volatile uint8_t lastWRControl;						///< Last wiper value written to the DS1841.
														///< Used to avoid setting the same value several times
														///< and to free up the I2C line a bit (for GPIO, we can
														///< neglect this optimization). Also written from the
														///< ADC interrupt (see Fan_base::doIncrements()).

	// Hardware functions (the descriptions are inherited from Fan_base)

	HAL_StatusTypeDef getMOSFET(uint16_t & control) override;

	/**
	 * @copydoc Fan_base::setMOSFET()
	 * @note	The priority is ignored, since the MOSFET is controlled by the GPIO.
	 */
	HAL_StatusTypeDef setMOSFET(uint16_t control, bool highPriority = true) override;

	HAL_StatusTypeDef checkControlEqualsForMOSFET(uint16_t control, bool & equivalence) override;
	HAL_StatusTypeDef getPWM(uint16_t & control) override;

	/**
	 * @copydoc Fan_base::setPWM()
	 * @note	The value is written only if it differs from the last written one (in terms of the PWM bit width).
	 */
	HAL_StatusTypeDef setPWM(uint16_t control, bool highPriority = true) override;

	HAL_StatusTypeDef checkControlEqualsForPWM(uint16_t control, bool & equivalence) override;
	HAL_StatusTypeDef getDCDC(uint16_t & control) override;

	/**
	 * @copydoc Fan_base::setDCDC()
	 * @note	The value is written only if it differs from the last written one (in terms of the wiper bit width).
	 */
	HAL_StatusTypeDef setDCDC(uint16_t control, bool highPriority = true) override;

	HAL_StatusTypeDef checkControlEqualsForDCDC(uint16_t control, bool & equivalence) override;
	HAL_StatusTypeDef prepareVoltageMeasurement() override;
	HAL_StatusTypeDef getVoltageADC(uint16_t & voltageADC) override;
	HAL_StatusTypeDef getVoltage(uint16_t & voltage) override;
	HAL_StatusTypeDef getCredibleVoltageADC(uint16_t & voltageADC) override;
	HAL_StatusTypeDef getCredibleVoltage(uint16_t & voltage) override;

	/**
	 * @copydoc Fan_base::preparePulseMeasurement()
	 * @note	If the expected PPM is specified, the MAX31790 Speed Range is adjusted to it,
	 * 			and the function waits for the tacho period to be updated twice.
	 */
	HAL_StatusTypeDef preparePulseMeasurement(uint16_t * period = nullptr, uint32_t * expectedPPM = nullptr, uint16_t * expectedPeriod = nullptr) override;

	HAL_StatusTypeDef getPulsePeriod(uint16_t & period, uint16_t * previousPeriod = nullptr) override;
	HAL_StatusTypeDef getPPMFromPulsePeriod(uint16_t period, uint32_t & ppm) override;
	HAL_StatusTypeDef getPPM(uint32_t & ppm) override;
	HAL_StatusTypeDef getCrediblePPM(uint32_t & ppm) override;

	//HAL_StatusTypeDef isStopped(bool & stopped) override;

	uint8_t voltageFractionBits() override { return (ADC_CHANNEL_FRACTION_BITS); }
	uint8_t pwmControlBitWidth() override { return MAX31790_PWM_BITS; }
	uint8_t dcdcControlBitWidth() override { return DS1841_WIPER_BITS; }

	/**
	 * @copydoc Fan_base::dcdcControlVoltageStep()
	 * @details	In the next board version, it makes sense to perform all calculations in float,
	 * 			especially if the FPU is available. In the meantime, let's calculate the step
	 * 			approximately so that it's definitely enough for normal operation:
	 * @verbatim
	   float:
	   ADC voltage step: 18.3 / 4096 = 0.0044677734375v
	   DC-DC/POT voltage step: (12 - 2.7) / 128 = 0.07265625v
	   Threshold (steps): (0.07265625 / 0.0044677734375) / 2 (for sure) = 8.1311475409836065573770491803279

	   int:
	   ADC top: 4096
	   POT top: 128
	   DC-DC/POT voltage step: 4096 / 128 / 2 (for the smaller range) = 16 (0.071484375)
	   Threshold: (4096 / 128) / 2 (for the smaller range) / 2 (for sure) = 8 (0.0357421875)

	   shifts:
	   ADC bit width: 12
	   POT bit width: 7
	   DC-DC/POT voltage step: 1 << (12 - 7 - 1) = 1 << 4 = 16
	   Threshold: 1 << (12 - 7 - 2) = 1 << 3 = 8

	   With fraction bits = 4:
	   DC-DC/POT voltage step: 1 << (12 - 7 - 1 + 4) = 1 << 8 = 256
	   Threshold: 1 << (12 - 7 - 2 + 4) = 1 << 7 = 128
	   @endverbatim
	 */
	uint16_t dcdcControlVoltageStep() override { return (1 << (ADC_CHANNEL_BIT_WIDTH + ADC_CHANNEL_FRACTION_BITS - DS1841_WIPER_BITS - 1)); }

	uint16_t pulseMeasurementDelay() override { return MAX31790_TACH_MEASUREMENT_TIME; }


public:

	MAX31790 * const controllerDriver;					///< MAX31790 driver object
	const uint8_t controllerFanIndex;					///< Corresponding MAX31790 fan number
	DS1841 * const potDriver;							///< DS1841 driver object (nullptr - no DC-DC control)
	const uint8_t potLineIndex;							///< Corresponding Digipot address (0 - 3)
	VMeter * const vMeterDriver;						///< Voltmeter object (nullptr - no voltmeter)
	GPIO_TypeDef * const mosfetPort;					///< MOSFET control port (nullptr - no MOSFET)
	const uint16_t mosfetPin;							///< MOSFET control pin


	/**
	 * @brief	MAX31790 basic initialization: reset the chip and activate the external oscillator.
	 * @note	Must be called once for the driver, before the initialization of the fan channels.
	 * @param	driver			MAX31790 driver object.
	 * @retval	HAL status.
	 */
	static HAL_StatusTypeDef initMAX31790(MAX31790 * driver);

	/**
	 * @brief	Constructor.
	 * @param	theControllerDriver		MAX31790 driver object.
	 * @param	controllerIndex			Corresponding MAX31790 fan number.
	 * @param	thePotDriver			DS1841 driver object (nullptr - no DC-DC control).
	 * @param	potLine					Corresponding Digipot address (0 - 3).
	 * @param	theVMeterDriver			Voltmeter object (nullptr - no voltmeter).
	 * @param	onOffPort				MOSFET control port (nullptr - no MOSFET).
	 * @param	onOffPin				MOSFET control pin.
	 */
	Fan_MAX31790_DS1841(MAX31790 * theControllerDriver, uint8_t controllerIndex, DS1841 * thePotDriver, uint8_t potLine, VMeter * theVMeterDriver, GPIO_TypeDef * onOffPort, uint16_t onOffPin);

	virtual ~Fan_MAX31790_DS1841() = default;

	/**
	 * @brief	Initialize the corresponding MAX31790 fan channel and DS1841 wiper
	 * 			(tacho input, PWM mode, DC-DC voltage at startup, manual wiper control),
	 * 			and read their current state.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef initChannel();

	/**
	 * @brief	Get the fan index in the MAX31790 controller
	 * 			(in this board it is equal to the fan index in the entire controller).
	 * @retval	MAX31790 fan number.
	 */
	uint8_t getMAX31790FanIndex();

};

#endif /* _FAN_MAX31790_DS1841_ */

// END-OF-FILE
