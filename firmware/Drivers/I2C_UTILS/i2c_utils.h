////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// i2c_utils.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	i2c_utils.h
 * @brief	A common class required for sharing access to the I2C bus,
 * 			as well as to minimize problems on important requests.
 * @note	If functions are called from an interrupt, then the robustConnection = false
 * 			parameter must be used to avoid problems with delays.
 */

#ifndef _I2C_UTILS_
#define _I2C_UTILS_

#include "stm32f0xx_hal.h"
#include <stdint.h>


#define I2C_TIMEOUT						10			///< Transfer timeout in milliseconds
#define I2C_MAX_ATTEMPTS				3			///< Number of attempts for the robust connection
#define I2C_ATTEMPTS_DELAY				10			///< Minimum delay between the attempts in milliseconds
#define I2C_DELAY_SUPPLEMENT_BASE		0x100		///< Range of the random supplement to the delay in milliseconds


/**
 * @brief	Register access helpers for 8-bit register addresses.
 * @note	The register address and the data are transferred in a single transaction
 * 			(with a repeated start for reading), so the access is atomic on the bus level.
 */
class I2C_utils {

private:

protected:

public:

	/**
	 * @brief	Read data from the device's register(s).
	 * @param	i2cHandle			I2C handle.
	 * @param	deviceAddr			Device address (8-bit form, i.e. 7-bit address shifted left).
	 * @param	regAddr				Register address.
	 * @param	outValue			[out] Buffer for the data.
	 * @param	count				Number of bytes to read.
	 * @param	robustConnection	Repeat the attempt (with a random delay) in case of an error.
	 * @retval	HAL status.
	 */
	static HAL_StatusTypeDef readRegister(I2C_HandleTypeDef * i2cHandle, uint8_t deviceAddr, uint8_t regAddr, uint8_t * outValue, uint8_t count, bool robustConnection);

	/**
	 * @brief	Write 1 or 2 bytes of data to the device's register.
	 * @param	i2cHandle			I2C handle.
	 * @param	deviceAddr			Device address (8-bit form, i.e. 7-bit address shifted left).
	 * @param	regAddr				Register address.
	 * @param	value				Data (1 or 2 bytes).
	 * @param	word				true - write 2 bytes, false - write 1 byte.
	 * @param	robustConnection	Repeat the attempt (with a random delay) in case of an error.
	 * @retval	HAL status.
	 */
	static HAL_StatusTypeDef writeRegister(I2C_HandleTypeDef * i2cHandle, uint8_t deviceAddr, uint8_t regAddr, uint8_t * value, bool word, bool robustConnection);

};

#endif /* _I2C_UTILS_ */

// END-OF-FILE
