////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// onewire.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	onewire.h
 * @brief	1-Wire bus driver for STM32 (bit-banging with a microsecond timer).
 * @note	The time-critical parts of the bit slots are executed with all interrupts disabled.
 * 			Between the bits, only the allowed interrupts (see the constructor) are enabled.
 */


#ifndef _ONEWIRE_
#define _ONEWIRE_


// Includes
#include "stm32f0xx_hal.h"
#include "irq_lock.h"
#include <stdbool.h>


// Class-level definitions
#define ONEWIRE_ROM_SIZE						8			///< Device ROM code size

#define ONEWIRE_READ_ROM_COMMAND				0x33		///< Read ROM (single device on the line)
#define ONEWIRE_MATCH_ROM_COMMAND				0x55		///< Select the device by its ROM code
#define ONEWIRE_SKIP_ROM_COMMAND				0xCC		///< Address all devices of the line
#define ONEWIRE_SEARCH_ROM_COMMAND				0xF0		///< Search for devices
#define ONEWIRE_CONDITIONAL_SEARCH_ROM_COMMAND	0xEC		///< Search for devices with an alarm condition


// Types

/**
 * @brief	Device search context.
 */
typedef struct
{
	uint8_t tempROM[8];						///< ROM code of the found device (also the search filter)
	uint8_t lastDiscrepancy;				///< Bit position of the last discrepancy
	uint8_t deviceFilterBits;				///< Number of fixed (filtered) bits at the beginning of the ROM code
    bool lastDeviceFlag;					///< The last device has been found
    bool crcErrorFlag;						///< CRC error of the found ROM code
} OneWire_Search_Context;


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

/**
 * @brief	1-Wire line (master).
 * @note	The pin must be configured as an open-drain output.
 */
class OneWire {

private:

    SelectiveIrqLock busLock;				///< Keeps only the allowed interrupts enabled during the transfers


protected:

	/**
	 * @brief	Delay for the specified number of microseconds (using the delay timer).
	 * @param	duration			Delay in microseconds.
	 */
	void delayMicros(uint16_t duration);

	/**
	 * @brief	Pull the line down.
	 */
	void pinLow();

	/**
	 * @brief	Release the line (open-drain high).
	 */
	void pinRelease();

	/**
	 * @brief	Read the line state.
	 * @retval	Line state (0 or 1).
	 */
	uint8_t pinRead();

	/**
	 * @brief	Pull the line down and release it (with all interrupts disabled).
	 * @param	low					Low phase duration in microseconds.
	 * @param	high				High phase duration in microseconds.
	 */
	void pinLowRelease(uint16_t low, uint16_t high);

	/**
	 * @brief	Write one bit (time slot).
	 * @param	data				Bit value.
	 */
	void writeBit(uint8_t data);

	/**
	 * @brief	Read one bit (time slot).
	 * @retval	Bit value.
	 */
	uint8_t readBit();


public:

	GPIO_TypeDef * const owPort;			///< Line GPIO port
	const uint16_t owPin;					///< Line GPIO pin
	TIM_HandleTypeDef * const delayTimer;	///< Timer counting microseconds (1 MHz)

	/**
	 * @brief	Constructor.
	 * @param	port				Line GPIO port.
	 * @param	pin					Line GPIO pin.
	 * @param	timer				Timer counting microseconds (1 MHz, at least 16-bit period).
	 * @param	allowedIrqs			Interrupts allowed between the bits (optional).
	 * @param	allowedIrqsCount	Number of the allowed interrupts.
	 */
	OneWire(GPIO_TypeDef * port, uint16_t pin, TIM_HandleTypeDef * timer,
		const IRQn_Type * allowedIrqs = nullptr, size_t allowedIrqsCount = 0);

	virtual ~OneWire() = default;

	/**
	 * @brief	Reset the line and detect the presence pulse.
	 * @retval	HAL_OK if at least one device is present, HAL_ERROR otherwise.
	 */
	HAL_StatusTypeDef reset();

	/**
	 * @brief	Write a byte (LSB first).
	 * @param	data				Byte to write.
	 */
	void writeByte(uint8_t data);

	/**
	 * @brief	Write several bytes.
	 * @param	addr				Data.
	 * @param	len					Number of bytes.
	 */
	void writeBytes(uint8_t * addr, uint8_t len);

	/**
	 * @brief	Read a byte (LSB first).
	 * @retval	Byte read.
	 */
	uint8_t readByte();

	/**
	 * @brief	Read several bytes.
	 * @param	addr				[out] Buffer for the data.
	 * @param	len					Number of bytes.
	 */
	void readBytes(uint8_t * addr, uint8_t len);

	/**
	 * @brief	Read the ROM code (if there is only one device on the line).
	 * @param	addr				[out] Buffer for the ROM code (ONEWIRE_ROM_SIZE bytes).
	 */
	void readROM(uint8_t * addr);

	/**
	 * @brief	Select the device by its ROM code (Match ROM).
	 * @param	addr				ROM code (ONEWIRE_ROM_SIZE bytes).
	 */
	void selectROM(uint8_t * addr);

	/**
	 * @brief	Address all devices of the line (Skip ROM).
	 */
	void skipROM();

	/**
	 * @brief	Prepare (reset) the search context.
	 * @note	The search can be limited to the devices whose ROM codes start with the specified bytes
	 * 			(e.g. 1 byte - the device family, 8 bytes - a specific device).
	 * @param	searchContext		Search context.
	 * @param	deviceFilter		First bytes of the ROM code to search for (optional).
	 * @param	filterBytesCount	Number of the filter bytes (up to ONEWIRE_ROM_SIZE).
	 */
	void startSearch(OneWire_Search_Context & searchContext, uint8_t * deviceFilter = nullptr, uint8_t filterBytesCount = 0);

	/**
	 * @brief	Search for the next device.
	 * @param	searchContext		Search context.
	 * @param	conditionalSearch	Search only for the devices with an alarm condition.
	 * @retval	ROM code of the found device (in the search context), nullptr if there are no more devices.
	 * 			The CRC error is reported in the search context.
	 */
	uint8_t * searchNext(OneWire_Search_Context & searchContext, bool conditionalSearch = false);

	/**
	 * @brief	Calculate the Dallas/Maxim CRC-8 over the buffer.
	 * @param	addr				Data.
	 * @param	len					Number of bytes.
	 * @retval	CRC (0 if the data includes a correct CRC byte at the end).
	 */
	uint8_t calcCRC(const uint8_t * addr, uint8_t len);

	/**
	 * @brief	Generate a pulse (for oscillator calibration).
	 * @param	duration			Low and high phase duration in microseconds.
	 */
	void pulse(uint16_t duration);

};


#endif // end _ONEWIRE_

// END-OF-FILE
