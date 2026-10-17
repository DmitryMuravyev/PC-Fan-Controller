////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// fan_MAX31790_DS1841.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include "fan_MAX31790_DS1841.h"


// Implementation-level definitions
// Call procedure and check the result
#define CALL_AND_CHECK(f)					{ HAL_StatusTypeDef status = (f); if (status != HAL_OK) return (status); }
// Call procedure and break on error (instead of goto)
#define CALL_AND_BREAK_ON_ERROR(f, v)		{ v = (f); if (v != HAL_OK) break; }
// Standard Bit Value macro
#ifndef _BV
#define _BV(x)								(1 << (x))
#endif


// Constructor
Fan_MAX31790_DS1841::Fan_MAX31790_DS1841(MAX31790 * theControllerDriver, uint8_t controllerIndex, DS1841 * thePotDriver, uint8_t potLine, VMeter * theVMeterDriver, GPIO_TypeDef * onOffPort, uint16_t onOffPin) :
Fan_base(), controllerDriver(theControllerDriver), controllerFanIndex(controllerIndex), potDriver(thePotDriver), potLineIndex(potLine), vMeterDriver(theVMeterDriver), mosfetPort(onOffPort), mosfetPin(onOffPin) {
}


// MAX31790 basic initialization
HAL_StatusTypeDef Fan_MAX31790_DS1841::initMAX31790(MAX31790 * driver) {
	if (driver == nullptr) return (HAL_ERROR);

	uint8_t config = 0;
	driver->getGlobalConfiguration(config);
	config |= _BV(MAX31790_Config_Reset);	// Reset chip
	HAL_StatusTypeDef result = driver->setGlobalConfiguration(config);
	if (result == HAL_OK) {
		HAL_Delay(MAX31790_STARTUP_TIME);
	}

	// We will perform only the minimum necessary configuration.
	// Only what needs to be changed after powering-on or resetting.

	// Activate external oscillator
	CALL_AND_CHECK(driver->getGlobalConfiguration(config));
	config |= _BV(MAX31790_Config_OSC);
	CALL_AND_CHECK(driver->setGlobalConfiguration(config));
	HAL_Delay(MAX31790_OSCILLATOR_STARTUP_TIME);

	return (result);
}


// Init corresponding MAX31790 fan and DS1841 pot
HAL_StatusTypeDef Fan_MAX31790_DS1841::initChannel() {
	if (controllerDriver != nullptr) {
		// Configure fan
		// We will implement all the features programmatically,
		// so let's turn off spinning-up, smooth PWM changing and everything else.
		uint8_t config = _BV(MAX31790_Fan_Config_TACH_Input) |
				 _BV(MAX31790_Fan_Config_LR_Polarity); /* (just in case) */
		CALL_AND_CHECK(controllerDriver->setFanConfiguration(controllerFanIndex, config));
		uint8_t fanDynamics;
		CALL_AND_CHECK(controllerDriver->getFanDynamics(controllerFanIndex, fanDynamics));
		fanDynamics &= MAX31790_Fan_Dynamics_Mask;		// Keep dynamics value
		CALL_AND_CHECK(controllerDriver->setFanDynamics(controllerFanIndex, fanDynamics));
		uint16_t currentPWM;
		CALL_AND_CHECK(controllerDriver->getFanPWM(controllerFanIndex, currentPWM));
		lastPWMControl = currentPWM;
	}

	if (potDriver) {
		uint8_t regValue;
		// Check IVR default value (DC-DC output power at startup)
		CALL_AND_CHECK(potDriver->readRegister(potLineIndex, DS1841_IVR_REG, regValue));
		if (regValue != FAN_DEFAULT_DCDC_VALUE) {
			CALL_AND_CHECK(potDriver->configureIVR(potLineIndex, FAN_DEFAULT_DCDC_VALUE));
		}

		// Check Adder/Update default value (DC-DC output power at startup)
		CALL_AND_CHECK(potDriver->readRegister(potLineIndex, DS1841_CR1_REG, regValue));
		if (regValue != DS1841_EnableUpdates) {
			CALL_AND_CHECK(potDriver->configureAdderUpdateMode(potLineIndex, DS1841_EnableUpdates));
		}

		// TODO We won't use LUT auto-mode in this version, but let's keep this possibility in mind.
		//CALL_AND_CHECK(potDriver->configureLUT(dcdcIndex, 20, 40));

		CALL_AND_CHECK(potDriver->setVolatileMode(potLineIndex));
		CALL_AND_CHECK(potDriver->setOperationMode(potLineIndex, DS1841_ManualMode | DS1841_LutarMode));

		CALL_AND_CHECK(potDriver->getWR(potLineIndex, regValue));
		lastWRControl = regValue;
	}

	return(HAL_OK);
}


// A specific function for this subclass that returns the fan index
// in the MAX31790 controller. In some cases, we can equate this index
// to the fan index in the entire controller.
uint8_t Fan_MAX31790_DS1841::getMAX31790FanIndex() {
	return (controllerFanIndex);
}


// To more accurately measure the duration of fan acceleration/deceleration,
// we must monitor the update of the tacho pulse period value.
// In the next version, we can try to do this using interrupts.
HAL_StatusTypeDef Fan_MAX31790_DS1841::waitForTachChange(uint16_t * tachIn, uint16_t * tachOut, uint16_t timeout) {
	uint16_t firstValue, nextValue;
	uint32_t tickStart = HAL_GetTick();
	HAL_StatusTypeDef result;

	if (tachIn) {
		firstValue = *tachIn;
	} else {
		result = controllerDriver->getFanTach(controllerFanIndex, firstValue);
		if (result != HAL_OK) return (result);
	}

	// Waiting for the period to change or for the timeout
	while (((result = controllerDriver->getFanTach(controllerFanIndex, nextValue)) == HAL_OK) &&
			(firstValue == nextValue) &&
			((HAL_GetTick() - tickStart) < timeout)) {
		HAL_Delay(MAX31790_TACH_REQUEST_DELAY);
	}

	if (tachOut) *tachOut = nextValue;
	return (result);
}


// Get MOSFET current state
HAL_StatusTypeDef Fan_MAX31790_DS1841::getMOSFET(uint16_t & control) {
	if (mosfetPort) {
		GPIO_PinState pinState = HAL_GPIO_ReadPin(mosfetPort, mosfetPin);
		control = (pinState == GPIO_PIN_SET) ? FAN_CONTROL_TOP : 0;
		return (HAL_OK);
	}
	return (HAL_ERROR);
}


// Set MOSFET state (GPIO).
// The priority of the operation is ignored in this case.
HAL_StatusTypeDef Fan_MAX31790_DS1841::setMOSFET(uint16_t control, bool highPriority) {
	if (mosfetPort) {
		if (control > (FAN_CONTROL_HALF_RANGE + FAN_MOSFET_MODE_HYSTERESIS)) {
			HAL_GPIO_WritePin(mosfetPort, mosfetPin, GPIO_PIN_SET);
		} else if (control < (FAN_CONTROL_HALF_RANGE - FAN_MOSFET_MODE_HYSTERESIS)) {
			HAL_GPIO_WritePin(mosfetPort, mosfetPin, GPIO_PIN_RESET);
		}
		return (HAL_OK);
	}
	return (HAL_ERROR);
}


// Check whether the Control value corresponds to the current status of the MOSFET.
// In fact, anything less than FAN_CONTROL_HALF_RANGE corresponds to GPIO = 0,
// and anything higher means GPIO = 1 (but also taking into account hysteresis).
HAL_StatusTypeDef Fan_MAX31790_DS1841::checkControlEqualsForMOSFET(uint16_t control, bool & equivalence) {
	if (mosfetPort == nullptr) {
		equivalence = false;
		return (HAL_ERROR);
	} else {
		GPIO_PinState pinState = HAL_GPIO_ReadPin(mosfetPort, mosfetPin);

		if (control > (FAN_CONTROL_HALF_RANGE + FAN_MOSFET_MODE_HYSTERESIS)) {
			equivalence = (pinState == GPIO_PIN_SET);
		} else if (control < (FAN_CONTROL_HALF_RANGE - FAN_MOSFET_MODE_HYSTERESIS)) {
			equivalence = (pinState == GPIO_PIN_RESET);
		} else {
			equivalence = true;
		}
	}
	return (HAL_OK);
}


// Get current PWM duty cycle
HAL_StatusTypeDef Fan_MAX31790_DS1841::getPWM(uint16_t & control) {
	HAL_StatusTypeDef result = HAL_ERROR;
	if (controllerDriver) {
		uint16_t pwm;
		bool fullSpeed = false;
		result = controllerDriver->getFanPWM(controllerFanIndex, pwm, &fullSpeed);
		if (fullSpeed) {
			control = FAN_CONTROL_TOP;
		} else {
			control = pwm << (FAN_CONTROL_BIT_WIDTH - MAX31790_PWM_BITS);
		}
		countErrors(result != HAL_OK, &hwErrorCounters, FAN_ERR_COUNTER_PWM_SHIFT, Fan_Status_PWM_HW_Error);
	}
	return (result);
}


// Set PWM duty cycle
// If the operation priority is set to low, only one attempt will be performed,
// after which the priority is set back to high.
HAL_StatusTypeDef Fan_MAX31790_DS1841::setPWM(uint16_t control, bool highPriority) {
	HAL_StatusTypeDef result = HAL_ERROR;
	if (controllerDriver) {
		uint16_t pwm = control >> (FAN_CONTROL_BIT_WIDTH - MAX31790_PWM_BITS);
		if (pwm != lastPWMControl) {
			controllerDriver->i2cRobustConnection = highPriority;
			result = controllerDriver->setFanPWMTarget(controllerFanIndex, pwm, nullptr);
			controllerDriver->i2cRobustConnection = true;

			bool halOK = (result == HAL_OK);
			if (halOK) lastPWMControl = pwm;
			if (highPriority) countErrors(!halOK, &hwErrorCounters, FAN_ERR_COUNTER_PWM_SHIFT, Fan_Status_PWM_HW_Error);
		} else result = HAL_OK;
	}
	return (result);
}


// Check whether the Control value corresponds to the current PWM duty cycle.
// The difference in bit widths is taken into account.
HAL_StatusTypeDef Fan_MAX31790_DS1841::checkControlEqualsForPWM(uint16_t control, bool & equivalence) {
	HAL_StatusTypeDef result = HAL_ERROR;
	uint16_t pwm;
	if (controllerDriver) {
		result = controllerDriver->getFanPWM(controllerFanIndex, pwm);
		countErrors(result != HAL_OK, &hwErrorCounters, FAN_ERR_COUNTER_PWM_SHIFT, Fan_Status_PWM_HW_Error);
	}
	equivalence = (result != HAL_OK) ? false : (pwm == (control >> (FAN_CONTROL_BIT_WIDTH - MAX31790_PWM_BITS)));
	return (result);
}


// Get the DC-DC output voltage setting
HAL_StatusTypeDef Fan_MAX31790_DS1841::getDCDC(uint16_t & control) {
	HAL_StatusTypeDef result = HAL_ERROR;
	if (potDriver) {
		uint8_t wr;
		result = potDriver->getWR(potLineIndex, wr);
		control = wr << (FAN_CONTROL_BIT_WIDTH - DS1841_WIPER_BITS);
		countErrors(result != HAL_OK, &hwErrorCounters, FAN_ERR_COUNTER_DCDC_SHIFT, Fan_Status_DCDC_HW_Error);
	}
	return (result);
}


// Set DC-DC output voltage.
// If the operation priority is set to low, only one attempt will be performed,
// after which the priority is set back to high.
HAL_StatusTypeDef Fan_MAX31790_DS1841::setDCDC(uint16_t control, bool highPriority) {
	HAL_StatusTypeDef result = HAL_ERROR;
	if (potDriver) {
		uint8_t wr = control >> (FAN_CONTROL_BIT_WIDTH - DS1841_WIPER_BITS);
		if (wr != lastWRControl) {
			potDriver->i2cRobustConnection = highPriority;
			result = potDriver->setWR(potLineIndex, wr);
			potDriver->i2cRobustConnection = true;

			bool halOK = (result == HAL_OK);
			if (halOK) lastWRControl = wr;
			if (highPriority) countErrors(!halOK, &hwErrorCounters, FAN_ERR_COUNTER_DCDC_SHIFT, Fan_Status_DCDC_HW_Error);
		} else result = HAL_OK;
	}
	return (result);
}


// Check whether the Control value corresponds
// to the current DC-DC output voltage setting.
// The difference in bit widths is taken into account.
HAL_StatusTypeDef Fan_MAX31790_DS1841::checkControlEqualsForDCDC(uint16_t control, bool & equivalence) {
	HAL_StatusTypeDef result = HAL_ERROR;
	uint8_t wr;
	if (potDriver) {
		result = potDriver->getWR(potLineIndex, wr);
		countErrors(result != HAL_OK, &hwErrorCounters, FAN_ERR_COUNTER_DCDC_SHIFT, Fan_Status_DCDC_HW_Error);
	}
	equivalence = (result != HAL_OK) ? false : (wr == (control >> (FAN_CONTROL_BIT_WIDTH - DS1841_WIPER_BITS)));
	return (result);
}


// Assign the current ADC value to the EMA counter
// so that you don't have to wait for the EMA to reach
// the real value after a voltage change.
HAL_StatusTypeDef Fan_MAX31790_DS1841::prepareVoltageMeasurement() {
	if (vMeterDriver) {
		vMeterDriver->reset();
		return (HAL_OK);
	}
	return (HAL_ERROR);
}


// Get the raw ADC value (fan power supply) without conversion to voltage
// value (faster, easier and more accurate for some calculations kinds).
HAL_StatusTypeDef Fan_MAX31790_DS1841::getVoltageADC(uint16_t & voltageADC) {
	if (vMeterDriver) {
		voltageADC = vMeterDriver->getADCValue();
		return (HAL_OK);
	}
	return (HAL_ERROR);
}


// Get current fan supply voltage
HAL_StatusTypeDef Fan_MAX31790_DS1841::getVoltage(uint16_t & voltage) {
	if (vMeterDriver) {
		voltage = vMeterDriver->getVoltage();
		return (HAL_OK);
	}
	return (HAL_ERROR);
}


// Get credible fan supply raw ADC value (for example, immediately after
// changing DC-DC output setting). The procedure resets the EMA counter
// and waits for some time while the voltage value is refined.
HAL_StatusTypeDef Fan_MAX31790_DS1841::getCredibleVoltageADC(uint16_t & voltageADC) {
	if (vMeterDriver) {
		vMeterDriver->reset();
		HAL_Delay(vMeterDriver->getTimeConstant());
		voltageADC = vMeterDriver->getADCValue();
		return (HAL_OK);
	}
	return (HAL_ERROR);
}


// Get credible fan supply voltage value (for example,
// immediately after changing it).
HAL_StatusTypeDef Fan_MAX31790_DS1841::getCredibleVoltage(uint16_t & voltage) {
	if (vMeterDriver) {
		vMeterDriver->reset();
		HAL_Delay(vMeterDriver->getTimeConstant());
		voltage = vMeterDriver->getVoltage();
		return (HAL_OK);
	}
	return (HAL_ERROR);
}


// To more accurately measure the duration of fan acceleration/deceleration,
// we need to synchronize with the moments of measuring the period
// of the tacho pulses. In the next version of the board,
// we can try to do this using interrupts.
HAL_StatusTypeDef Fan_MAX31790_DS1841::preparePulseMeasurement(uint16_t * period, uint32_t * expectedPPM, uint16_t * expectedPeriod) {
	HAL_StatusTypeDef result = HAL_ERROR;
	if (controllerDriver) {
		do { // Multiple "goto" replacement

			if (expectedPPM != nullptr) {
				// Get Dynamics
				uint8_t fanDynamics, speedRange;
				uint8_t newSpeedRange = 0;
				uint32_t expectedTach = MAX31790_TACH_TOP;

				CALL_AND_BREAK_ON_ERROR(controllerDriver->getFanSpeedRange(controllerFanIndex, speedRange, &fanDynamics), result);

				// Calculate expected tacho pulses period and corresponding Speed Range
				if (*expectedPPM > 0) {
					while (((expectedTach = (uint32_t)60 * (1 << newSpeedRange) * 8192 / *expectedPPM) < MAX31790_TACH_QUARTER_RANGE) &&
							(newSpeedRange < MAX31790_MAX_SPEED_RANGE) ) {
						newSpeedRange++;
					}
				}

				if (expectedPeriod) *expectedPeriod = expectedTach;

				// Set new Speed Range and wait for tachometer period to change.
				if (newSpeedRange != speedRange) {
					fanDynamics = (fanDynamics & 0b00011111) | (newSpeedRange << 5);
					CALL_AND_BREAK_ON_ERROR(controllerDriver->setFanDynamics(controllerFanIndex, fanDynamics), result);

					// Wait until the TACH changes or the timeout runs out
					CALL_AND_BREAK_ON_ERROR(waitForTachChange(nullptr, period, MAX31790_TACH_MEASUREMENT_TIME), result);
					// Let's do it once again to eliminate errors
					CALL_AND_BREAK_ON_ERROR(waitForTachChange(period, period, MAX31790_TACH_MEASUREMENT_TIME), result);
					break;
				}
			}

			// Wait until the TACH changes or the timeout runs out
			result = waitForTachChange(nullptr, period);

		} while (0); // "goto" replacement

		countErrors(result != HAL_OK, &hwErrorCounters, FAN_ERR_COUNTER_TACH_SHIFT, Fan_Status_TACH_HW_Error);
	}

	return (result);
}


// Get current tachometer pulses period
HAL_StatusTypeDef Fan_MAX31790_DS1841::getPulsePeriod(uint16_t & period, uint16_t * previousPeriod) {
	HAL_StatusTypeDef result = HAL_ERROR;
	if (controllerDriver) {
		if (previousPeriod) {
			result = waitForTachChange(previousPeriod, &period, MAX31790_TACH_MEASUREMENT_TIME + MAX31790_SR_APPLICATION_MARGIN);
		} else {
			result = controllerDriver->getFanTach(controllerFanIndex, period);
		}
		countErrors(result != HAL_OK, &hwErrorCounters, FAN_ERR_COUNTER_TACH_SHIFT, Fan_Status_TACH_HW_Error);
	}
	return (result);
}


// Calculate PPM from tacho pulses period
HAL_StatusTypeDef Fan_MAX31790_DS1841::getPPMFromPulsePeriod(uint16_t period, uint32_t & ppm) {
	HAL_StatusTypeDef result = HAL_ERROR;
	if (controllerDriver) {
		uint8_t speedRange;
		result = controllerDriver->getFanSpeedRange(controllerFanIndex, speedRange);
		ppm = controllerDriver->calculatePPM(speedRange, period);
		countErrors(result != HAL_OK, &hwErrorCounters, FAN_ERR_COUNTER_TACH_SHIFT, Fan_Status_TACH_HW_Error);
	}
	return (result);
}


// Get current Pulses Per Minute value.
// If a fresh value is not available, the last measured value will be returned.
HAL_StatusTypeDef Fan_MAX31790_DS1841::getPPM(uint32_t & ppm) {
	HAL_StatusTypeDef result = HAL_ERROR;
	if (controllerDriver) {
		result = controllerDriver->getFanPPM(controllerFanIndex, ppm); // Get PPM in Fast mode
		countErrors(result != HAL_OK, &hwErrorCounters, FAN_ERR_COUNTER_TACH_SHIFT, Fan_Status_TACH_HW_Error);
	}
	return (result);
}


// Unfortunately, in the MAX31790 we have to constantly adjust the fan dynamics
// to get an accurate PPM/RPM value. If we haven't done this for a long time,
// we'll have to spend time adjusting the Speed Range value to the most appropriate one.
HAL_StatusTypeDef Fan_MAX31790_DS1841::getCrediblePPM(uint32_t & ppm) {
	HAL_StatusTypeDef result = HAL_ERROR;
	if (controllerDriver) {
		result = controllerDriver->getFanPPM(controllerFanIndex, ppm, MAX31790_PPM_Mode_Accurate); // Get PPM in Accurate mode
		countErrors(result != HAL_OK, &hwErrorCounters, FAN_ERR_COUNTER_TACH_SHIFT, Fan_Status_TACH_HW_Error);
	}
	return (result);
}


/*
HAL_StatusTypeDef Fan_MAX31790_DS1841::isStopped(bool & stopped) {
	HAL_StatusTypeDef result = HAL_ERROR;
	if (controllerDriver) {
		do { // Multiple "goto" replacement
			// Get Speed Range and TACH
			uint8_t speedRange;
			uint16_t tach;
			CALL_AND_BREAK_ON_ERROR(controllerDriver->getFanSpeedRange(controllerFanIndex, speedRange), result);
			CALL_AND_BREAK_ON_ERROR(controllerDriver->getFanTach(controllerFanIndex, tach), result);

			stopped = (speedRange == 0) && (tach == MAX31790_TACH_TOP);

		} while (0); // "goto" replacement

		countErrors(result != HAL_OK, &hwErrorCounters, FAN_ERR_COUNTER_TACH_SHIFT, Fan_Status_TACH_HW_Error);
	}
	return (result);
}
*/


// END-OF-FILE
