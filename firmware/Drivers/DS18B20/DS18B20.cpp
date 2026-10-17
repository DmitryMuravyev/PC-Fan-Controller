////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// DS18B20.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include "DS18B20.h"


// Implementation-level definitions
// Call procedure and check the result
#define CALL_AND_CHECK(f)			{ HAL_StatusTypeDef status = (f); if (status != HAL_OK) return (status); }


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

// Constructor
DS18B20::DS18B20(GPIO_TypeDef * port, uint16_t pin, TIM_HandleTypeDef * timer,
		const IRQn_Type * allowedIrqs, size_t allowedIrqsCount)
		: OneWire(port, pin, timer, allowedIrqs, allowedIrqsCount) {
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

// Send command to the sensor with specified ROM
// (or to all sensors of the line, if the ROM isn't specified).
HAL_StatusTypeDef DS18B20::sendCommand(uint8_t * rom, uint8_t command) {
	// No presence pulse - there are no devices on the line
	CALL_AND_CHECK(reset());

	if (rom) {
		selectROM(rom);
	} else {
		skipROM();
	}
	writeByte(command);

	return (HAL_OK);
}


// Read the entire scratchpad from the specified sensor
HAL_StatusTypeDef DS18B20::readScratchpad(uint8_t * rom, uint8_t * addr) {
	HAL_StatusTypeDef result = HAL_ERROR;

	if (addr != nullptr) {
		result = sendCommand(rom, DS18B20_READ_SCRATCH_COMMAND);
		if (result == HAL_OK) {
			readBytes(addr, DS18B20_DATA_SIZE);
			if ((calcCRC(addr, DS18B20_DATA_SIZE) != 0) || (addr[DS18B20_CONFIG_BYTE] == 0)) result = HAL_ERROR;
		}
	}
	return (result);
}


// Write 3 configuration bytes to the device's EEPROM
HAL_StatusTypeDef DS18B20::writeEEPROM(uint8_t * rom, uint8_t * eeprom) {
	HAL_StatusTypeDef result = HAL_ERROR;

	if (eeprom != nullptr) {
		result = sendCommand(rom, DS18B20_WRITE_SCRATCH_COMMAND);
		if (result == HAL_OK) {
			writeBytes(eeprom, DS18B20_EEPROM_SIZE);
		}

		if (result == HAL_OK) {
			result = sendCommand(rom, DS18B20_SAVE_EEPROM_COMMAND);
			HAL_Delay(DS18B20_EEPROM_TIMEOUT);
		}
	}
	return (result);
}

////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Check the device configuration and if it differs from the specified,
// then save the new one into the EEPROM.
HAL_StatusTypeDef DS18B20::checkAndSetConfig(uint8_t * rom, uint8_t config) {
	uint8_t scratchpad[DS18B20_DATA_SIZE];
	CALL_AND_CHECK(readScratchpad(rom, scratchpad));

	uint8_t currentConfig = (scratchpad[DS18B20_CONFIG_BYTE] & DS18B20_CONFIG_MASK) >> DS18B20_CONFIG_SHIFT;
	if (currentConfig != config) {
		scratchpad[DS18B20_CONFIG_BYTE] = ((config << DS18B20_CONFIG_SHIFT) & DS18B20_CONFIG_MASK) | DS18B20_Config_Base;
		CALL_AND_CHECK(writeEEPROM(rom, &scratchpad[DS18B20_EEPROM_FIRST_BYTE]));
		CALL_AND_CHECK(sendCommand(rom, DS18B20_LOAD_EEPROM_COMMAND));
		// Check the result
		CALL_AND_CHECK(readScratchpad(rom, scratchpad));
		currentConfig = (scratchpad[DS18B20_CONFIG_BYTE] & DS18B20_CONFIG_MASK) >> DS18B20_CONFIG_SHIFT;
		if (currentConfig != config) return (HAL_ERROR);
	}

	return (HAL_OK);
}


// Restart the sensor search on the OneWire line
void DS18B20::restartSearch(OneWire_Search_Context & searchContext, uint8_t * searchRom) {
	if (searchRom != nullptr) {
		startSearch(searchContext, searchRom, ONEWIRE_ROM_SIZE);
	} else {
		uint8_t deviceFamily = DS18B20_DEVICE_FAMILY;
		startSearch(searchContext, &deviceFamily, 1);
	}
}


// Find next sensor
HAL_StatusTypeDef DS18B20::findNext(OneWire_Search_Context & searchContext, bool alarmed) {
	// The previous sensor was the last one or CRC error detected:
	if (searchContext.lastDeviceFlag || searchContext.crcErrorFlag) {
		return (HAL_ERROR);
	}

	// Search ROM and check if there's no CRC error
	if ((searchNext(searchContext, alarmed) == nullptr) || (searchContext.crcErrorFlag)) return (HAL_ERROR);

	return (HAL_OK);
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Temperature conversion

// Save the timestamp of the started conversion.
// The value 0 is reserved for "no conversion has been started yet".
void DS18B20::setConversionStartTick() {
	conversionStartTick = HAL_GetTick();
	if (conversionStartTick == 0) conversionStartTick = 1;
}


// Start temperature conversion of the specified sensor
// (if only one sensor is connected to the line).
HAL_StatusTypeDef DS18B20::startConversion(uint8_t * rom) {
	CALL_AND_CHECK(sendCommand(rom, DS18B20_CONVERT_COMMAND));
	// The conversion starts after the command has been received,
	// so we take the timestamp after sending it.
	setConversionStartTick();
	return (HAL_OK);
}


// Check if the last conversion started on the line has been completed
// (enough time has passed for the specified resolution).
bool DS18B20::checkLastConversionDone(uint8_t config) {
	return ((conversionStartTick != 0) &&
			((HAL_GetTick() - conversionStartTick) > ((uint32_t)DS18B20_CONVERSION_TIME_BASE << config)));
}


// Start temperature conversion on all sensors of the line at once (Skip ROM + Convert T).
// The conversion that is still in progress isn't restarted.
HAL_StatusTypeDef DS18B20::startConversionAll() {
	// Sensors with different resolutions may be connected to the line,
	// so let's wait for the maximum (12-bit) conversion time.
	if ((conversionStartTick != 0) && !checkLastConversionDone(DS18B20_Config_12bit)) return (HAL_BUSY);

	CALL_AND_CHECK(sendCommand(nullptr, DS18B20_CONVERT_COMMAND));
	setConversionStartTick();
	return (HAL_OK);
}


// Get the start timestamp of the last completed line-wide conversion.
// The timestamp identifies the conversion, so each sensor can check
// whether it has already read its result.
// Returns 0 if there is no completed conversion at the moment.
uint32_t DS18B20::getCompletedConversionTick() {
	return (checkLastConversionDone(DS18B20_Config_12bit) ? conversionStartTick : 0);
}


// Read the temperature from the sensor
HAL_StatusTypeDef DS18B20::readTemperature(uint8_t * rom, int16_t & temperature) {
	uint8_t scratchpad[DS18B20_DATA_SIZE];
	CALL_AND_CHECK(readScratchpad(rom, scratchpad));
	temperature = (scratchpad[DS18B20_TEMPERATURE_MSB] << 8) | (scratchpad[DS18B20_TEMPERATURE_LSB]);

	// The least significant bits are undefined for 9-11 bit resolutions
	uint8_t config = (scratchpad[DS18B20_CONFIG_BYTE] & DS18B20_CONFIG_MASK) >> DS18B20_CONFIG_SHIFT;
	temperature &= ~((1 << (DS18B20_Config_12bit - config)) - 1);

	return (HAL_OK);
}

// END-OF-FILE
