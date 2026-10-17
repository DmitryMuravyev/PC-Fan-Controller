////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// i2c_utils.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include <i2c_utils.h>
#include <cstdlib>


// Implementation-level definitions
// Check the previous result and call procedure if there's no error
#define CHECK_AND_CALL(f, v)		{ if (v == HAL_OK) v = (f); }


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

// Read data from the device
HAL_StatusTypeDef I2C_utils::readRegister(I2C_HandleTypeDef * i2cHandle, uint8_t deviceAddr, uint8_t regAddr, uint8_t * outValue, uint8_t count, bool robustConnection) {
	// If the operation is not very important for normal operation,
	// or if we perform it from the interrupt routine (in this case,
	// we most likely will not be able to perform HAL_Delay due to the fact
	// that the millisecond counter will not be updated), then we will
	// limit ourselves to one attempt. If the operation is important,
	// then we will try several times with a pause of random duration.

	uint8_t i = robustConnection ? I2C_MAX_ATTEMPTS : 1;
	HAL_StatusTypeDef result;

	do {
		// The register address and the data are transferred in a single transaction
		// (with a repeated start), so no other transfer (e.g. from an interrupt)
		// can change the device's register pointer in between.
		result = HAL_I2C_Mem_Read(i2cHandle, deviceAddr, regAddr, I2C_MEMADD_SIZE_8BIT, outValue, count, I2C_TIMEOUT);
		if ((result != HAL_OK) && robustConnection) {
			// I'll use the outdated and unreliable <cstdlib> library
			// and the srand/rand functions. If necessary, you can switch
			// to the C++11 <random> library.
			// But here, the old one is quite enough for my tasks.
			uint32_t delay = I2C_ATTEMPTS_DELAY + (rand() % I2C_DELAY_SUPPLEMENT_BASE);
			HAL_Delay(delay);
		}
	} while ((result != HAL_OK) && (--i != 0));

	return (result);
}


// Write 1 or 2 bytes of data to the device's register
HAL_StatusTypeDef I2C_utils::writeRegister(I2C_HandleTypeDef * i2cHandle, uint8_t deviceAddr, uint8_t regAddr, uint8_t * value, bool word, bool robustConnection) {
	// The same here...

	uint8_t i = robustConnection ? I2C_MAX_ATTEMPTS : 1;
	HAL_StatusTypeDef result;

	do {
		result = HAL_I2C_Mem_Write(i2cHandle, deviceAddr, regAddr, I2C_MEMADD_SIZE_8BIT, value, word ? 2 : 1, I2C_TIMEOUT);
		if ((result != HAL_OK) && robustConnection) {
			// The same with <cstdlib> library here
			uint32_t delay = I2C_ATTEMPTS_DELAY + (rand() % I2C_DELAY_SUPPLEMENT_BASE);
			HAL_Delay(delay);
		}
	} while ((result != HAL_OK) && (--i != 0));

	return (result);
}

// END-OF-FILE
