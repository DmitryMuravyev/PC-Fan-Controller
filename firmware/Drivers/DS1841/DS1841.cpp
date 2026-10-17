////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// DS1841.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include "DS1841.h"
#include "i2c_utils.h"


// Implementation-level definitions
// DigiPOT address macro. x = 0 ~ 3
#define POT_ADDRESS(x)						(DS1841_BASE_ADDR | ((x) << 1))
// Check the previous result and call procedure if there's no error
#define CHECK_AND_CALL(f, v)				{ if (v == HAL_OK) v = (f); }
// Call procedure and get the worse result
#define CALL_AND_GET_WORSE_RESULT(f, v)		{ HAL_StatusTypeDef firstStatus = v; \
											  v = (f); \
											  if (firstStatus != HAL_OK) v = firstStatus; }


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

DS1841::DS1841(I2C_HandleTypeDef * handle) : i2cHandle(handle) {
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

// Read 8 bit register value from the POT
HAL_StatusTypeDef DS1841::readRegister(uint8_t potIndex, uint8_t regAddr, uint8_t & outValue) {
	if (i2cHandle == nullptr) return (HAL_ERROR);
	return (I2C_utils::readRegister(i2cHandle, POT_ADDRESS(potIndex), regAddr, &outValue, 1, i2cRobustConnection));
}


// Write 8 bit register value to the POT
HAL_StatusTypeDef DS1841::writeRegister(uint8_t potIndex, uint8_t regAddr, uint8_t value) {
	if (i2cHandle == nullptr) return (HAL_ERROR);
	return (I2C_utils::writeRegister(i2cHandle, POT_ADDRESS(potIndex), regAddr, &value, false, i2cRobustConnection));
}


// Write nonvolatile register value
HAL_StatusTypeDef DS1841::configureRegister(uint8_t potIndex, uint8_t regAddr, uint8_t value) {
	// Checking whether the register has NV memory
	if ((regAddr != DS1841_IVR_REG) && (regAddr != DS1841_CR1_REG)) return (HAL_ERROR);

	HAL_StatusTypeDef result = writeRegister(potIndex, DS1841_CR0_REG, DS1841_NonVolatileMode);
	CHECK_AND_CALL(writeRegister(potIndex, regAddr, value), result);
	CALL_AND_GET_WORSE_RESULT(writeRegister(potIndex, DS1841_CR0_REG, DS1841_VolatileMode), result);

	return (result);
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

// LUT programming for automatic POT operation in a selected temperature range
// lowTemp >= -128, highTemp <= 127, highTemp - lowTemp <= 127
HAL_StatusTypeDef DS1841::configureLUT(uint8_t potIndex, int8_t lowTemp, int8_t highTemp) {
	// Checking the temperature range correctness
	if (lowTemp >= highTemp) return (HAL_ERROR);

	HAL_StatusTypeDef result = HAL_OK;
	uint8_t regValue;
	uint8_t regAddr = DS1841_LUT_BASE;
	uint8_t tempRange = highTemp - lowTemp;

	for (int8_t i = DS1841_LUT_FIRST_TEMP;
			(i < (DS1841_LUT_FIRST_TEMP + DS1841_LUT_REGISTERS_COUNT * DS1841_LUT_TEMP_STEP)) && (result == HAL_OK);
			i += DS1841_LUT_TEMP_STEP) {
		if (i < lowTemp) {
			regValue = 0;
		} else
		if (i <= highTemp) {
			regValue = (int16_t)DS1841_MAX_WIPER_VALUE * (i - lowTemp) / tempRange;
		} else {
			regValue = DS1841_MAX_WIPER_VALUE;
		}
		result = writeRegister(potIndex, regAddr, regValue);
		HAL_Delay(DS1841_NV_TIMEOUT);
		regAddr++;
	}
	return (result);
}


// Write nonvolatile Initial Value Register
HAL_StatusTypeDef DS1841::configureIVR(uint8_t potIndex, uint8_t value) {
	return (configureRegister(potIndex, DS1841_IVR_REG, value));
}


// Write nonvolatile CR1 register value
HAL_StatusTypeDef DS1841::configureAdderUpdateMode(uint8_t potIndex, uint8_t adderUpdateMode) {
	return (configureRegister(potIndex, DS1841_CR1_REG, adderUpdateMode));
}


// Set the write to volatile memory mode for IVR and CR1 registers.
HAL_StatusTypeDef DS1841::setVolatileMode(uint8_t potIndex) {
	return (writeRegister(potIndex, DS1841_CR0_REG, DS1841_VolatileMode));
}


// Write volatile CR1 register value
HAL_StatusTypeDef DS1841::setAdderUpdateMode(uint8_t potIndex, uint8_t adderUpdateMode) {
	return (writeRegister(potIndex, DS1841_CR1_REG, adderUpdateMode));
}


// Set CR2 register value
HAL_StatusTypeDef DS1841::setOperationMode(uint8_t potIndex, uint8_t operationMode) {
	return (writeRegister(potIndex, DS1841_CR2_REG, operationMode));
}


// Read Wiper Register value
HAL_StatusTypeDef DS1841::getWR(uint8_t potIndex, uint8_t & wr) {
	return (readRegister(potIndex, DS1841_WR_REG, wr));
}


// Set Wiper Register value
HAL_StatusTypeDef DS1841::setWR(uint8_t potIndex, uint8_t wr) {
	return (writeRegister(potIndex, DS1841_WR_REG, wr));
}


// Set LUT Address Register value
HAL_StatusTypeDef DS1841::setLUTAR(uint8_t potIndex, uint8_t lutar) {
	return (writeRegister(potIndex, DS1841_LUTAR_REG, lutar));
}


// Read Temperature Result register value
HAL_StatusTypeDef DS1841::getTemp(uint8_t potIndex, int8_t & temp) {
	return (readRegister(potIndex, DS1841_TEMP_REG, (uint8_t &)temp));
}


// Read Supply Voltage Result register value
// (native format LSB = 25.6mV = 0.0256V).
HAL_StatusTypeDef DS1841::getVcc(uint8_t potIndex, uint8_t & vcc) {
	return (readRegister(potIndex, DS1841_VOLTAGE_REG, vcc));
}

// END-OF-FILE
