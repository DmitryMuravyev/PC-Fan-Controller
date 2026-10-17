////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// MAX31790.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include "MAX31790.h"
#include "i2c_utils.h"


// Implementation-level definitions
// Fan Controller address macro. Index = 0 ~ 15
#define CONTROLLER_ADDRESS			(MAX31790_BASE_ADDR | (controllerIndex << 1))
// Call procedure and check the result
#define CALL_AND_CHECK(f)			{ HAL_StatusTypeDef status = (f); if (status != HAL_OK) return (status); }
// Standard Bit Value macro
#define _BV(x)						(1 << (x))
// Speed Range changed flag
#define SPEED_RANGE_CHANGED_FLAG	(uint16_t)(_BV(15))


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

// Constructor
MAX31790::MAX31790(I2C_HandleTypeDef * handle, uint8_t index) : i2cHandle(handle), controllerIndex(index) {
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

// Read 8 bit register value from the controller
HAL_StatusTypeDef MAX31790::readRegister(uint8_t regAddr, uint8_t * outValue, uint8_t count) {
	if ((i2cHandle == nullptr) || (outValue == nullptr)) return (HAL_ERROR);
	return (I2C_utils::readRegister(i2cHandle, CONTROLLER_ADDRESS, regAddr, outValue, count, i2cRobustConnection));
}


// Write 8 bit register value to the controller
HAL_StatusTypeDef MAX31790::writeRegister(uint8_t regAddr, uint8_t * value, bool word) {
	if (i2cHandle == nullptr) return (HAL_ERROR);
	return (I2C_utils::writeRegister(i2cHandle, CONTROLLER_ADDRESS, regAddr, value, word, i2cRobustConnection));
}


// Read 2x8 bit register value from the controller
HAL_StatusTypeDef MAX31790::read2bytesRegister(uint8_t regAddr, uint8_t * outValueHi, uint8_t * outValueLow) {
	uint8_t buffer[2];
	HAL_StatusTypeDef result = readRegister(regAddr, buffer, 2);
	if (outValueHi != nullptr) *outValueHi = buffer[0];
	if (outValueLow != nullptr) *outValueLow = buffer[1];
	return (result);
}


// Write 2x8 bit register value to the controller
HAL_StatusTypeDef MAX31790::write2bytesRegister(uint8_t regAddr, uint8_t valueHi, uint8_t valueLow) {
	uint8_t buffer[2] = { valueHi, valueLow };
	return (writeRegister(regAddr, buffer, true));
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

// Read Global Configuration register value
HAL_StatusTypeDef MAX31790::getGlobalConfiguration(uint8_t & globalConfiguration) {
	return (readRegister(MAX31790_GLOBAL_CONFIG_REG, &globalConfiguration, 1));
}


// Write Global Configuration register value
HAL_StatusTypeDef MAX31790::setGlobalConfiguration(uint8_t globalConfiguration) {
	return (writeRegister(MAX31790_GLOBAL_CONFIG_REG, &globalConfiguration, false));
}


// Read PWM Frequency register value
HAL_StatusTypeDef MAX31790::getPWMFrequency(uint8_t * frequency1_3, uint8_t * frequency4_6) {
	uint8_t buffer;
	HAL_StatusTypeDef result = readRegister(MAX31790_PWM_FREQ_REG, &buffer, 1);
	if (frequency1_3 != nullptr) *frequency1_3 = buffer & 0b1111;
	if (frequency4_6 != nullptr) *frequency4_6 = buffer >> 4;
	return (result);
}


// Write PWM Frequency register value
HAL_StatusTypeDef MAX31790::setPWMFrequency(uint8_t frequency1_3, uint8_t frequency4_6) {
	uint8_t buffer = (frequency1_3 & 0b1111) | (frequency4_6 << 4);
	return (writeRegister(MAX31790_PWM_FREQ_REG, &buffer, false));
}


// Read Fan Configuration register value
HAL_StatusTypeDef MAX31790::getFanConfiguration(uint8_t fanIndex, uint8_t & fanConfiguration) {
	return (readRegister(MAX31790_FAN_CONFIG_BASE + fanIndex, &fanConfiguration, 1));
}


// Write Fan Configuration register value
HAL_StatusTypeDef MAX31790::setFanConfiguration(uint8_t fanIndex, uint8_t fanConfiguration) {
	// Check if we need to switch from PWM to RPM mode.
	if (fanConfiguration & _BV(MAX31790_Fan_Config_Mode)) {
		uint8_t oldConfiguration;
		CALL_AND_CHECK(getFanConfiguration(fanIndex, oldConfiguration));
		if ((oldConfiguration & _BV(MAX31790_Fan_Config_Mode)) == 0) {
			uint8_t buffer[2];
			CALL_AND_CHECK(readRegister(MAX31790_FAN_TACH_BASE + (fanIndex << 1), buffer, 2));

			// We have to set target TACH to the current value before switching to the RPM mode.
			CALL_AND_CHECK(writeRegister(MAX31790_TACH_TARGET_BASE + (fanIndex << 1), buffer, true));
		}
	}

	return (writeRegister(MAX31790_FAN_CONFIG_BASE + fanIndex, &fanConfiguration, false));
}


// Read Fan Dynamics register value
HAL_StatusTypeDef MAX31790::getFanDynamics(uint8_t fanIndex, uint8_t & fanDynamics) {
	return (readRegister(MAX31790_FAN_DYNAMICS_BASE + fanIndex, &fanDynamics, 1));
}


// Write Fan Dynamics register value
HAL_StatusTypeDef MAX31790::setFanDynamics(uint8_t fanIndex, uint8_t fanDynamics) {
	return (writeRegister(MAX31790_FAN_DYNAMICS_BASE + fanIndex, &fanDynamics, false));
}


// Read Fan Fault Status register
HAL_StatusTypeDef MAX31790::getFanFault(uint8_t * fanFault1_6, uint8_t * fanFault7_12) {
	return (read2bytesRegister(MAX31790_FAN_STATUS_REG, fanFault7_12, fanFault1_6));
}


// Write Fan Fault Status register
HAL_StatusTypeDef MAX31790::setFanFault(uint8_t fanFault1_6, uint8_t fanFault7_12) {
	return (write2bytesRegister(MAX31790_FAN_STATUS_REG, fanFault7_12, fanFault1_6));
}


// Read Fan Fault Mask register
HAL_StatusTypeDef MAX31790::getFanFaultMask(uint8_t * fanFaultMask1_6, uint8_t * fanFaultMask7_12) {
	return (read2bytesRegister(MAX31790_FAN_MASK_REG, fanFaultMask7_12, fanFaultMask1_6));
}


// Write Fan Fault Mask register
HAL_StatusTypeDef MAX31790::setFanFaultMask(uint8_t fanFaultMask1_6, uint8_t fanFaultMask7_12) {
	return (write2bytesRegister(MAX31790_FAN_MASK_REG, fanFaultMask7_12, fanFaultMask1_6));
}


// Read Sequential Start Delay / Failed Fan Options / Fan Faults count register
HAL_StatusTypeDef MAX31790::getFanStartAndFaultOptions(uint8_t & fanStartAndFaultOptions) {
	return (readRegister(MAX31790_FAN_OPTIONS_REG, &fanStartAndFaultOptions, 1));
}


// Write Sequential Start Delay / Failed Fan Options / Fan Faults count register
HAL_StatusTypeDef MAX31790::setFanStartAndFaultOptions(uint8_t fanStartAndFaultOptions) {
	return (writeRegister(MAX31790_FAN_OPTIONS_REG, &fanStartAndFaultOptions, false));
}


// Read Fan's Speed Range
HAL_StatusTypeDef MAX31790::getFanSpeedRange(uint8_t fanIndex, uint8_t & speedRange, uint8_t * dynamics) {
	uint8_t fanDynamics;
	HAL_StatusTypeDef result = getFanDynamics(fanIndex, fanDynamics);
	if (dynamics) *dynamics = fanDynamics;
	fanDynamics >>= 5;
	if (fanDynamics > MAX31790_MAX_SPEED_RANGE) fanDynamics = MAX31790_MAX_SPEED_RANGE;
	speedRange = fanDynamics;
	return (result);
}


// Read Fan's TACH counter
HAL_StatusTypeDef MAX31790::getFanTach(uint8_t fanIndex, uint16_t & tach) {
	uint8_t buffer[2];
	HAL_StatusTypeDef result = readRegister(MAX31790_FAN_TACH_BASE + (fanIndex << 1), buffer, 2);
	tach = ((uint16_t)buffer[0] << 3) | (buffer[1] >> 5);
	return (result);
}


// Read Fan's TACH Target register
HAL_StatusTypeDef MAX31790::getFanTachTarget(uint8_t fanIndex, uint16_t & tachTarget) {
	uint8_t buffer[2];
	HAL_StatusTypeDef result = readRegister(MAX31790_TACH_TARGET_BASE + (fanIndex << 1), buffer, 2);
	tachTarget = ((uint16_t)buffer[0] << 3) | (buffer[1] >> 5);
	return (result);
}


// Get Fan's PPM
// We have to calculate the PPM relative to the current Speed Range value,
// as well as adjust the Speed Range for accurate measurement at different rotation speeds.
// Since the rotation speed is measured in the MAX31790 only once per second,
// we may have different options for obtaining measurement results.
HAL_StatusTypeDef MAX31790::getFanPPM(uint8_t fanIndex, uint32_t & ppm, uint8_t ppmRequestMode) {
	// Get Dynamics and Speed Range
	uint8_t fanDynamics, speedRange;
	CALL_AND_CHECK(getFanSpeedRange(fanIndex, speedRange, &fanDynamics));

	uint8_t dynamicsCorrections = MAX31790_MAX_DYNAMICS_CORRECTIONS;
	while (dynamicsCorrections) {

		uint16_t tach;
		bool timeIsUp;
		bool speedRangeChanged = (lastMeasurement[fanIndex] & SPEED_RANGE_CHANGED_FLAG) != 0;
		uint16_t lastTach = lastMeasurement[fanIndex] & 0b11111111111;
		uint32_t currentTick;
		uint32_t measurementDuration = MAX31790_TACH_MEASUREMENT_TIME;
		// We have to wait at least a second after changing the Speed Range
		// (and in fact, it's better to wait a little longer than a second)
		// to get a guaranteed correct result.
		if (speedRangeChanged) measurementDuration += MAX31790_SR_APPLICATION_MARGIN;

		// Get fresh TACH value
		do {
			// Get TACH
			CALL_AND_CHECK(getFanTach(fanIndex, tach));

			currentTick = HAL_GetTick();
			timeIsUp = (currentTick - lastMeasurementTick[fanIndex]) >= measurementDuration;

			// Break the loop if we have received a fresh TACH value

			if (timeIsUp || (!speedRangeChanged && (tach != lastTach))) {
				break;
			}

			// If there's no fresh TACH and we use a Quick Request Mode,
			// then return the previous PPM value...
			if (ppmRequestMode == MAX31790_PPM_Mode_Fast) {
				speedRange = (lastMeasurement[fanIndex] >> 11) & 0b111;
				ppm = calculatePPM(speedRange, lastTach);
				return (HAL_OK);
			}

			// ...or wait and continue the cycle.
			HAL_Delay(MAX31790_TACH_REQUEST_DELAY);

		} while (1);

		lastMeasurementTick[fanIndex] = currentTick;
		lastMeasurement[fanIndex] = tach;
		lastMeasurement[fanIndex] |= (speedRange << 11);

		ppm = calculatePPM(speedRange, tach);

		uint8_t newSpeedRange = speedRange;

		// Let's adjust the Speed Range
		while ((tach < MAX31790_TACH_QUARTER_RANGE) && (newSpeedRange < MAX31790_MAX_SPEED_RANGE)) {
			tach <<= 1; // multiply by 2
			newSpeedRange++;
		}

		while ((tach >= (MAX31790_TACH_FULL_RANGE - MAX31790_TACH_QUARTER_RANGE)) && (newSpeedRange > 0)) {
			tach >>= 1; // divide by 2
			newSpeedRange--;
		}

		if (newSpeedRange != speedRange) {

			// Update Speed Range
			fanDynamics = (fanDynamics & 0b00011111) | (newSpeedRange << 5);
			CALL_AND_CHECK(setFanDynamics(fanIndex, fanDynamics));
			speedRange = newSpeedRange;

			// Update TACH target according to the new Speed Range value.
			CALL_AND_CHECK(updateFanTachTarget(fanIndex));
			lastMeasurement[fanIndex] |= SPEED_RANGE_CHANGED_FLAG;

			// If the fresh PPM value is enough for us, then we can end the cycle.
			if (ppmRequestMode == MAX31790_PPM_Mode_Fresh) break;

			// Otherwise, continue until the maximum number of Speed Range corrections is reached.
			dynamicsCorrections--;
		} else {
			lastMeasurement[fanIndex] &= ~SPEED_RANGE_CHANGED_FLAG;
			break;
		}
	}

	return (HAL_OK);
}


// Read Fan's current PWM
HAL_StatusTypeDef MAX31790::getFanPWM(uint8_t fanIndex, uint16_t & pwm, bool * fullSpeed) {
	uint8_t buffer[2];
	HAL_StatusTypeDef result = readRegister(MAX31790_PWM_OUT_BASE + (fanIndex << 1), buffer, 2);
	pwm = ((uint16_t)buffer[0] << 1) | (buffer[1] >> 7);
	if (fullSpeed) *fullSpeed = (buffer[1] & 0b00000001) != 0;
	return (result);
}


// Get current target PWM duty cycle
HAL_StatusTypeDef MAX31790::getFanPWMTarget(uint8_t fanIndex, uint16_t & pwmTarget) {
	uint8_t buffer[2];
	HAL_StatusTypeDef result = readRegister(MAX31790_PWM_TARGET_BASE + (fanIndex << 1), buffer, 2);
	pwmTarget = ((uint16_t)buffer[0] << 1) | (buffer[1] >> 7);
	return (result);
}



// Set target PWM duty cycle and (if necessary) the minimum PPM value.
HAL_StatusTypeDef MAX31790::setFanPWMTarget(uint8_t fanIndex, uint16_t pwmTarget, uint32_t * minPPM) {
	uint8_t buffer[2];
	buffer[0] = (uint8_t)(pwmTarget >> 1);
	buffer[1] = (uint8_t)(pwmTarget << 7);
	CALL_AND_CHECK(writeRegister(MAX31790_PWM_TARGET_BASE + (fanIndex << 1), buffer, true));

	if (minPPM != nullptr) {
		targetPPM[fanIndex] = *minPPM;
		CALL_AND_CHECK(updateFanTachTarget(fanIndex));
	}

	return (HAL_OK);
}


// Get current target PPM for RPM control mode
HAL_StatusTypeDef MAX31790::getFanPPMTarget(uint8_t fanIndex, uint32_t & ppmTarget) {
	ppmTarget = targetPPM[fanIndex];
	return (HAL_OK);
}


// Set target PPM for RPM control mode
HAL_StatusTypeDef MAX31790::setFanPPMTarget(uint8_t fanIndex, uint32_t ppmTarget) {
	// Get current fan's configuration
	uint8_t configuration;
	CALL_AND_CHECK(getFanConfiguration(fanIndex, configuration));

	targetPPM[fanIndex] = ppmTarget;
	if (ppmTarget == 0) {
		// Check if fan is controlled
		if ((configuration & _BV(MAX31790_Fan_Config_Control)) == 0) {
			// Switch to monitor only (duty cycle will be set to 0% automatically)
			configuration |= _BV(MAX31790_Fan_Config_Control);
			CALL_AND_CHECK(setFanConfiguration(fanIndex, configuration));
		}
	} else {
		CALL_AND_CHECK(updateFanTachTarget(fanIndex));

		// Check if fan is monitored only
		if (configuration & _BV(MAX31790_Fan_Config_Control)) {
			// Switch to control mode
			configuration &= ~(uint8_t)_BV(MAX31790_Fan_Config_Control);
			CALL_AND_CHECK(setFanConfiguration(fanIndex, configuration));
		}
	}

	return (HAL_OK);
}


// Update TACH target according to the target PPM.
// RPM (closed-loop) mode isn’t used by the firmware and hasn’t been tested.
HAL_StatusTypeDef MAX31790::updateFanTachTarget(uint8_t fanIndex) {
	// Get current TACH target
	uint8_t buffer[2];
	CALL_AND_CHECK(readRegister(MAX31790_TACH_TARGET_BASE + (fanIndex << 1), buffer, 2));
	uint16_t tachTarget = ((uint16_t)buffer[0] << 3) | (buffer[1] >> 5);

	// Get current Speed Range
	uint8_t speedRange;
	CALL_AND_CHECK(getFanSpeedRange(fanIndex, speedRange));

	uint32_t newTachTarget;
	uint32_t ppmTarget = targetPPM[fanIndex];
	if (ppmTarget == 0) {
		newTachTarget = MAX31790_TACH_TOP;
	} else {
		newTachTarget = (uint32_t)60 * 8192 * (1 << speedRange) / ppmTarget;
		if (newTachTarget > MAX31790_TACH_TOP) newTachTarget = MAX31790_TACH_TOP;
	}

	// Check and set new target TACH.
	// We will adjust the Speed Range in the PPM reading function.
	// Therefore, at the first attempt, the target may be set roughly,
	// but it should be OK.
	if ((uint16_t)newTachTarget != tachTarget) {
		buffer[0] = (uint8_t)(newTachTarget >> 3);
		buffer[1] = (uint8_t)(newTachTarget << 5);
		CALL_AND_CHECK(writeRegister(MAX31790_TACH_TARGET_BASE + (fanIndex << 1), buffer, true));
	}

	return (HAL_OK);
}


// Set Control Loop "Window" for the RPM mode
HAL_StatusTypeDef MAX31790::setFanWindow(uint8_t fanIndex, uint8_t window) {
	return (writeRegister(MAX31790_WINDOW_BASE + fanIndex, &window, false));
}


// Read User byte
HAL_StatusTypeDef MAX31790::getUserByte(uint8_t userByteAddress, uint8_t & userByte) {
	return (readRegister(userByteAddress, &userByte, 1));
}


// Write User byte
HAL_StatusTypeDef MAX31790::setUserByte(uint8_t userByteAddress, uint8_t userByte) {
	return (writeRegister(userByteAddress, &userByte, false));
}


// Calculate PPM from the values of TACH and Speed Range
uint32_t MAX31790::calculatePPM(uint8_t speedRange, uint16_t tach) {
	if (tach == 0) {
		return (MAX31790_INDEFINITE_PPM); // Some kind of infinite value...
	} else if ((tach == MAX31790_TACH_TOP) && (speedRange == 0)) {
		return (0);
	}

	uint8_t readableSpeedRange = 1 << speedRange;
	return (((uint32_t)60 * readableSpeedRange * 8192) / tach);
}


// END-OF-FILE
