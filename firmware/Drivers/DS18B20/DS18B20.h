////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// DS18B20.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	DS18B20.h
 * @brief	Minimalistic class for managing the DS18B20 temperature sensors on a 1-Wire line.
 */



#ifndef _DS18B20_
#define _DS18B20_


// Includes
#include "onewire.h"
#include <stdbool.h>


// Types

/**
 * @brief	Sensor resolution (bit depth) configuration.
 */
typedef enum
{
	DS18B20_Config_9bit					= 0b00000000,		///< 9-bit resolution (93.75 ms)
	DS18B20_Config_10bit				= 0b00000001,		///< 10-bit resolution (187.5 ms)
	DS18B20_Config_11bit				= 0b00000010,		///< 11-bit resolution (375 ms)
	DS18B20_Config_12bit				= 0b00000011,		///< 12-bit resolution (750 ms)
	DS18B20_Config_Base					= 0b00011111		///< Reserved bits of the configuration register (always 1)
} DS18B20_Config;


// Class-level definitions
#define DS18B20_DATA_SIZE				9			///< Scratchpad size
#define DS18B20_TEMPERATURE_LSB			0			///< Lower temperature byte
#define DS18B20_TEMPERATURE_MSB			1			///< Upper temperature byte
#define DS18B20_USER_BYTE_1				2			///< User byte 1 / High Alarm Level
#define DS18B20_USER_BYTE_2				3			///< User byte 2 / Low Alarm Level
#define DS18B20_CONFIG_BYTE				4			///< Config byte
#define DS18B20_CONFIG_SHIFT			5			///< Configuration bits offset inside a configuration byte
#define DS18B20_CONFIG_MASK				0b01100000	///< Configuration bits mask
#define DS18B20_CONVERSION_TIME_BASE	94			///< tconv / 8 (milliseconds, 9-bit conversion time)
#define DS18B20_DEFAULT_CONFIGURATION	DS18B20_Config_12bit	///< Default resolution
#define DS18B20_EEPROM_FIRST_BYTE		DS18B20_USER_BYTE_1		///< There are only 3 EEPROM memory bytes in the scratchpad
#define DS18B20_EEPROM_SIZE				3			///< DS18B20 EEPROM size
#define DS18B20_EEPROM_TIMEOUT			10			///< EEPROM write timeout
#define DS18B20_DEVICE_FAMILY			0x28		///< DS18B20 family device code
#define DS18B20_CONVERT_COMMAND			0x44		///< Start temperature conversion command
#define DS18B20_READ_SCRATCH_COMMAND	0xBE		///< Read Scratchpad command
#define DS18B20_WRITE_SCRATCH_COMMAND	0x4E		///< Write Scratchpad command
#define DS18B20_LOAD_EEPROM_COMMAND		0xB8		///< Copy EEPROM to Scratchpad
#define DS18B20_SAVE_EEPROM_COMMAND		0x48		///< Copy Scratchpad to EEPROM


/**
 * @brief	1-Wire line with DS18B20 sensors.
 * @note	The temperature conversion can be started either for a single sensor (startConversion()),
 * 			or for all sensors of the line at once (startConversionAll()). The two approaches
 * 			must not be mixed on the same line, since they share the conversion timestamp.
 */
class DS18B20 : public OneWire {

private:

protected:

	uint32_t conversionStartTick = 0;				///< Timestamp of the last successfully started conversion
													///< on this line (0 - no conversion has been started yet)

	/**
	 * @brief	Save the timestamp of the started conversion (0 is reserved for "none").
	 */
	void setConversionStartTick();

public:

	/**
	 * @brief	Constructor.
	 * @param	port				Line GPIO port.
	 * @param	pin					Line GPIO pin.
	 * @param	timer				Timer counting microseconds (1 MHz).
	 * @param	allowedIrqs			Interrupts allowed between the bits (optional).
	 * @param	allowedIrqsCount	Number of the allowed interrupts.
	 */
	DS18B20(GPIO_TypeDef * port, uint16_t pin, TIM_HandleTypeDef * timer,
		const IRQn_Type * allowedIrqs = nullptr, size_t allowedIrqsCount = 0);

	virtual ~DS18B20() = default;

	/**
	 * @brief	Send a command to the sensor with the specified ROM code, or to all sensors of the line.
	 * @param	rom					Sensor ROM code, nullptr - all sensors (Skip ROM).
	 * @param	command				Command.
	 * @retval	HAL status, HAL_ERROR if there are no devices on the line (no presence pulse).
	 */
	HAL_StatusTypeDef sendCommand(uint8_t * rom, uint8_t command);

	/**
	 * @brief	Read the entire scratchpad and check its CRC.
	 * @param	rom					Sensor ROM code, nullptr - the only sensor of the line.
	 * @param	addr				[out] Buffer for the scratchpad (DS18B20_DATA_SIZE bytes).
	 * @retval	HAL status, HAL_ERROR if the CRC is wrong or the data is invalid.
	 */
	HAL_StatusTypeDef readScratchpad(uint8_t * rom, uint8_t * addr);

	/**
	 * @brief	Write the 3 configuration bytes (TH, TL, configuration) to the sensor's EEPROM.
	 * @param	rom					Sensor ROM code, nullptr - the only sensor of the line.
	 * @param	eeprom				Data (DS18B20_EEPROM_SIZE bytes).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writeEEPROM(uint8_t * rom, uint8_t * eeprom);

	/**
	 * @brief	Check the sensor resolution and, if it differs from the specified one, save the new one into the EEPROM.
	 * @param	rom					Sensor ROM code, nullptr - the only sensor of the line.
	 * @param	config				Resolution (see DS18B20_Config).
	 * @retval	HAL status, HAL_ERROR if the resolution couldn't be set.
	 */
	HAL_StatusTypeDef checkAndSetConfig(uint8_t * rom, uint8_t config);

	/**
	 * @brief	Restart the sensor search on the line.
	 * @param	searchContext		Search context.
	 * @param	searchRom			ROM code of a specific sensor to find, nullptr - all DS18B20 sensors.
	 */
	void restartSearch(OneWire_Search_Context & searchContext, uint8_t * searchRom = nullptr);

	/**
	 * @brief	Find the next sensor.
	 * @param	searchContext		Search context (the ROM code of the found sensor is stored in it).
	 * @param	alarmed				Search only for the sensors with an alarm condition.
	 * @retval	HAL_OK if a sensor is found, HAL_ERROR if there are no more sensors or a CRC error occurred.
	 */
	HAL_StatusTypeDef findNext(OneWire_Search_Context & searchContext, bool alarmed);

	/**
	 * @brief	Start the temperature conversion of a single sensor
	 * 			(if only one sensor is connected to the line).
	 * @param	rom					Sensor ROM code, nullptr - the only sensor of the line.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef startConversion(uint8_t * rom);

	/**
	 * @brief	Check if the last conversion started on the line has been completed.
	 * @param	config				Resolution used to determine the conversion time (see DS18B20_Config).
	 * @retval	true if the conversion has been completed, false if it is in progress or hasn't been started.
	 */
	bool checkLastConversionDone(uint8_t config);

	/**
	 * @brief	Start the temperature conversion on all sensors of the line at once (Skip ROM + Convert T).
	 * @note	The conversion that is still in progress isn't restarted.
	 * @retval	HAL status, HAL_BUSY if the previous conversion is still in progress,
	 * 			HAL_ERROR if there are no devices on the line.
	 */
	HAL_StatusTypeDef startConversionAll();

	/**
	 * @brief	Get the start timestamp of the last completed line-wide conversion.
	 * @note	The timestamp identifies the conversion, so each sensor can check
	 * 			whether it has already read its result. The maximum (12-bit) conversion
	 * 			time is used, since sensors with different resolutions may be connected to the line.
	 * @retval	Timestamp, 0 if there is no completed conversion at the moment.
	 */
	uint32_t getCompletedConversionTick();

	/**
	 * @brief	Read the temperature (the result of the last conversion).
	 * @param	rom					Sensor ROM code, nullptr - the only sensor of the line.
	 * @param	temperature			[out] Temperature in 1/16 C (the undefined low bits are cleared for 9-11 bit resolutions).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef readTemperature(uint8_t * rom, int16_t & temperature);

};

#endif /* _DS18B20_ */

// END-OF-FILE
