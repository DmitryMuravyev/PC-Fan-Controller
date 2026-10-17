////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// DS1841.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	DS1841.h
 * @brief	Minimalistic class for managing the DS1841 digipots (up to 4 on one I2C bus).
 */


#ifndef _DS1841_
#define _DS1841_


// Includes
#include "stm32f0xx_hal.h"


// Class-level definitions
// DS1841 base I2C address
#define DS1841_BASE_ADDR				0x50			///< Base I2C address (8-bit form)

// DS1841 Registers addresses
#define DS1841_IVR_REG					0x00			///< Initial Value Register
#define DS1841_CR0_REG					0x02			///< Control Register 0 (volatile/nonvolatile access)
#define DS1841_CR1_REG					0x03			///< Control Register 1 (adder/updates)
#define DS1841_LUTAR_REG				0x08			///< LUT Address Register
#define DS1841_WR_REG					0x09			///< Wiper Register
#define DS1841_CR2_REG					0x0A			///< Control Register 2 (operation mode)
#define DS1841_TEMP_REG					0x0C			///< Temperature Result register
#define DS1841_VOLTAGE_REG				0x0E			///< Supply Voltage Result register
#define DS1841_LUT_BASE					0x80			///< First LUT register

// LUT parameters
#define DS1841_LUT_REGISTERS_COUNT		72				///< Number of LUT registers
#define DS1841_LUT_FIRST_TEMP			-40				///< Temperature of the first LUT register (C)
#define DS1841_LUT_TEMP_STEP			2				///< Temperature step of the LUT registers (C)

// Common POT parameters
#define DS1841_WIPER_BITS				7				///< Wiper bit width
#define DS1841_MAX_WIPER_VALUE			127				///< Maximum wiper value ((1 << DS1841_WIPER_BITS) - 1)
#define DS1841_NV_TIMEOUT				10				///< Nonvolatile memory write time in milliseconds


// Types

/**
 * @brief	CR0 modes (access to the IVR/CR1 registers).
 */
typedef enum
{
	DS1841_NonVolatileMode				= 0b00000000,	///< Reading/writing IVR/CR1 NV value.
	DS1841_VolatileMode					= 0b10000000,	///< Reading/writing WR/CR1 volatile value.
} DS1841_WriteModes;


/**
 * @brief	CR1 modes (adder and updates).
 */
typedef enum
{
	DS1841_NoUpdatesNoAdder				= 0b00000000,	///< ADC off, LUTVAL or IVR value is directly loaded to WR.
	DS1841_EnableUpdates				= 0b00000001,	///< ADC on (temperature and voltage measurements).
	DS1841_EnableAdder					= 0b00000010	///< The LUTVAL is summed with the IVR value and is directly loaded to WR.
} DS1841_AdderUpdateModes;


/**
 * @brief	CR2 modes (operation mode).
 */
typedef enum
{
	DS1841_FullAutomaticMode			= 0b00000000,	///< WR is loaded with the value from IVR summed with LUTVAL,
														///< or just the value of LUTVAL. The LUTAR value, located in at address 08h,
														///< is calculated following each temperature conversion.
	DS1841_LutarMode					= 0b00000010,	///< Automatic updates of LUTAR are disabled, allowing the LUTAR register
														///< to be programmed using I2C commands.
	DS1841_ManualMode					= 0b00000100	///< The DS1841 is placed in a manual mode.
} DS1841_OperationModes;


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

/**
 * @brief	DS1841 digipots on one I2C bus (addressed by the index 0 - 3).
 */
class DS1841 {

private:

protected:

public:

	I2C_HandleTypeDef * const i2cHandle;			///< I2C Handle

	bool i2cRobustConnection = true;				///< Repeat attempts to send/receive data in case of errors

	/**
	 * @brief	Constructor.
	 * @param	handle				I2C Handle.
	 */
	DS1841(I2C_HandleTypeDef * handle);

	virtual ~DS1841() = default;

	/**
	 * @brief	Read an 8-bit register.
	 * @param	potIndex			POT index (0 - 3).
	 * @param	regAddr				Register address.
	 * @param	outValue			[out] Register value.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef readRegister(uint8_t potIndex, uint8_t regAddr, uint8_t & outValue);

	/**
	 * @brief	Write an 8-bit register.
	 * @param	potIndex			POT index (0 - 3).
	 * @param	regAddr				Register address.
	 * @param	value				Register value.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writeRegister(uint8_t potIndex, uint8_t regAddr, uint8_t value);

	/**
	 * @brief	Write a nonvolatile register (IVR or CR1).
	 * @param	potIndex			POT index (0 - 3).
	 * @param	regAddr				Register address (DS1841_IVR_REG or DS1841_CR1_REG).
	 * @param	value				Register value.
	 * @retval	HAL status, HAL_ERROR if the register has no nonvolatile memory.
	 */
	HAL_StatusTypeDef configureRegister(uint8_t potIndex, uint8_t regAddr, uint8_t value);

	/**
	 * @brief	Program the LUT for the automatic POT operation in the selected temperature range
	 * 			(the wiper changes linearly from 0 to maximum within the range).
	 * @param	potIndex			POT index (0 - 3).
	 * @param	lowTemp				Low temperature of the range (C).
	 * @param	highTemp			High temperature of the range (C), highTemp - lowTemp <= 127.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef configureLUT(uint8_t potIndex, int8_t lowTemp, int8_t highTemp);

	/**
	 * @brief	Write the nonvolatile Initial Value Register (wiper value at power-up).
	 * @param	potIndex			POT index (0 - 3).
	 * @param	value				Initial wiper value.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef configureIVR(uint8_t potIndex, uint8_t value);

	/**
	 * @brief	Write the nonvolatile CR1 register.
	 * @param	potIndex			POT index (0 - 3).
	 * @param	adderUpdateMode		Mode (see DS1841_AdderUpdateModes).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef configureAdderUpdateMode(uint8_t potIndex, uint8_t adderUpdateMode);

	/**
	 * @brief	Set the volatile access mode for the IVR/CR1 registers (WR is accessed instead of IVR).
	 * @param	potIndex			POT index (0 - 3).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setVolatileMode(uint8_t potIndex);

	/**
	 * @brief	Write the volatile CR1 register.
	 * @param	potIndex			POT index (0 - 3).
	 * @param	adderUpdateMode		Mode (see DS1841_AdderUpdateModes).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setAdderUpdateMode(uint8_t potIndex, uint8_t adderUpdateMode);

	/**
	 * @brief	Write the CR2 register.
	 * @param	potIndex			POT index (0 - 3).
	 * @param	operationMode		Mode (see DS1841_OperationModes).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setOperationMode(uint8_t potIndex, uint8_t operationMode);

	/**
	 * @brief	Read the Wiper Register.
	 * @param	potIndex			POT index (0 - 3).
	 * @param	wr					[out] Wiper value (0 - DS1841_MAX_WIPER_VALUE).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getWR(uint8_t potIndex, uint8_t & wr);

	/**
	 * @brief	Write the Wiper Register.
	 * @param	potIndex			POT index (0 - 3).
	 * @param	wr					Wiper value (0 - DS1841_MAX_WIPER_VALUE).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setWR(uint8_t potIndex, uint8_t wr);

	/**
	 * @brief	Write the LUT Address Register.
	 * @param	potIndex			POT index (0 - 3).
	 * @param	lutar				LUT address.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setLUTAR(uint8_t potIndex, uint8_t lutar);

	/**
	 * @brief	Read the temperature (requires the updates to be enabled, see DS1841_EnableUpdates).
	 * @param	potIndex			POT index (0 - 3).
	 * @param	temp				[out] Temperature in C.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getTemp(uint8_t potIndex, int8_t & temp);

	/**
	 * @brief	Read the supply voltage (requires the updates to be enabled, see DS1841_EnableUpdates).
	 * @param	potIndex			POT index (0 - 3).
	 * @param	vcc					[out] Voltage in the native format (LSB = 25.6 mV).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef getVcc(uint8_t potIndex, uint8_t & vcc);

};


#endif /* _DS1841_ */

// END-OF-FILE
