////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// onewire.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include "onewire.h"
#include <string.h>


// Constructor
OneWire::OneWire(GPIO_TypeDef * port, uint16_t pin, TIM_HandleTypeDef * timer,
                  const IRQn_Type * allowedIrqs, size_t allowedIrqsCount)
    : busLock(SelectiveIrqLock::ComputeMask(allowedIrqs, allowedIrqsCount)),
      owPort(port), owPin(pin), delayTimer(timer) {
}


///////////////////////////////////////////////////////////////////////////////////////////////////////////
// OneWire internal functions (based on this project: http://www.technoblogy.com/show?2G8A )
///////////////////////////////////////////////////////////////////////////////////////////////////////////

// Delay for microseconds
void OneWire::delayMicros(uint16_t duration) {
	if (delayTimer != nullptr) {
		__HAL_TIM_SET_COUNTER(delayTimer, 0);						// set the counter value a 0
		while (__HAL_TIM_GET_COUNTER(delayTimer) < duration) {};	// wait for the counter to reach
	}
}


// Pull down
void OneWire::pinLow() {
	HAL_GPIO_WritePin(owPort, owPin, GPIO_PIN_RESET);
}


// Set to Open Drain
void OneWire::pinRelease() {
	HAL_GPIO_WritePin(owPort, owPin, GPIO_PIN_SET);
}


// Read OneWire pin
uint8_t OneWire::pinRead() {
	return (HAL_GPIO_ReadPin(owPort, owPin) == GPIO_PIN_SET);
}


// Transmit inverted 1-0 sequence
void OneWire::pinLowRelease(uint16_t low, uint16_t high) {
    IrqLock lock;
    LockGuard guard(lock);
	pinLow();
	delayMicros(low);
	pinRelease();
	delayMicros(high);
}


// Write bit
void OneWire::writeBit(uint8_t data) {
    uint16_t low, high;
    if (data == 0) {
		low = 65;
		high = 5;
    } else {
		low = 10;
		high = 55;
    }
    pinLowRelease(low, high);
}


// Read bit
uint8_t OneWire::readBit() {
	uint8_t data = 0;
    {
        IrqLock lock;
        LockGuard guard(lock);
		pinLowRelease(3, 10);
		data = pinRead();
    }
	delayMicros(53);
    return data;
}


///////////////////////////////////////////////////////////////////////////////////////////////////////////
// OneWire interface functions (based on this project: https://github.com/PaulStoffregen/OneWire )
///////////////////////////////////////////////////////////////////////////////////////////////////////////

// Initialize OneWire
HAL_StatusTypeDef OneWire::reset() {
    LockGuard guard(busLock);	// busLock is the member
    // Pull low 480us (tolerant - device just sees low line)
    pinLow();

    // No timing sensitivity,
    // the device just needs to see >= 480us low
    delayMicros(480);

    // Release and presence detect (CRITICAL - 70us window)
    uint8_t result;
    {
        IrqLock lock;
        LockGuard pulseGuard(lock);
		pinRelease();
		delayMicros(70);
    	result = pinRead();
	}
    delayMicros(410);

	return ((HAL_StatusTypeDef)result); // 0 = HAL_OK, 1 = HAL_ERROR
}


// Write byte
void OneWire::writeByte(uint8_t data) {
    LockGuard guard(busLock);
	for (uint8_t i = 0; i < 8; i++) {
		writeBit(data & 1);
		data = data >> 1;
		// Some IRQ can fire here (~70µs gap between bits is fine for 1-Wire)
	}
}


// Write bytes
void OneWire::writeBytes(uint8_t * addr, uint8_t len) {
	if (addr != nullptr) {
        LockGuard guard(busLock);
		for (uint8_t i = 0; i < len; i++) writeByte(addr[i]);
	}
}


// Read byte
uint8_t OneWire::readByte() {
    LockGuard guard(busLock);
	uint8_t data = 0;
	for (uint8_t i = 0; i < 8; i++) {
		data = data | (readBit() << i);
		// Some IRQ can fire here
	}
	return data;
}


// Read bytes into the buffer
void OneWire::readBytes(uint8_t * addr, uint8_t len) {
	if (addr != nullptr) {
	    LockGuard guard(busLock);
		for (uint8_t i = 0; i < len; i++) {
			addr[i] = readByte();
		}
	}
}


// Read ROM (if there is only one device on the line)
void OneWire::readROM(uint8_t * addr) {
	if (addr != nullptr) {
	    LockGuard guard(busLock);
		writeByte(ONEWIRE_READ_ROM_COMMAND);
		readBytes(addr, ONEWIRE_ROM_SIZE);
	}
}


// Select device by its ROM
void OneWire::selectROM(uint8_t * addr) {
	if (addr != nullptr) {
	    LockGuard guard(busLock);
		writeByte(ONEWIRE_MATCH_ROM_COMMAND);
		for (uint8_t i = 0; i < ONEWIRE_ROM_SIZE; i++) writeByte(addr[i]);
	}
}


// Skip ROM
void OneWire::skipROM() {
    LockGuard guard(busLock);
	writeByte(ONEWIRE_SKIP_ROM_COMMAND);
}


///////////////////////////////////////////////////////////////////////////////////////////////////////////
// Search functions (based on this project: https://github.com/PaulStoffregen/OneWire )
///////////////////////////////////////////////////////////////////////////////////////////////////////////

// Prepare/reset search environment
void OneWire::startSearch(OneWire_Search_Context & searchContext, uint8_t * deviceFilter, uint8_t filterBytesCount) {
	// Clear search context
	memset(&searchContext, 0, sizeof(OneWire_Search_Context));

	// Here we can filter the search by a specified number of bits (bytes).
	// For example, we can search only for devices of a certain family (1 byte)
	// or a specific device (all 8 bytes).
	if (deviceFilter != nullptr) {
		if (filterBytesCount > ONEWIRE_ROM_SIZE) filterBytesCount = ONEWIRE_ROM_SIZE;
		memcpy(searchContext.tempROM, deviceFilter, filterBytesCount);
		searchContext.deviceFilterBits = filterBytesCount << 3;
	}
}


// Search for next device
uint8_t * OneWire::searchNext(OneWire_Search_Context & searchContext, bool conditionalSearch) {
	bool searchResult = false;
	if (!searchContext.lastDeviceFlag) {

		uint8_t romBitIndex = 1, romByteIndex = 0, romByteMask = 1;
		uint8_t bitA, bitB;
		uint8_t discrepancyMarker = 0, chosenRomBit;

		{
		    LockGuard guard(busLock);

			// 1-Wire reset
			if (reset() == HAL_OK) {
				// Send the search command
				writeByte(conditionalSearch ? ONEWIRE_CONDITIONAL_SEARCH_ROM_COMMAND : ONEWIRE_SEARCH_ROM_COMMAND);

				do {
					// Read a bit and its negative value
					bitA = readBit();
					bitB = readBit();

					// Check if there's no devices on 1-wire
					if ((bitA == 1) && (bitB == 1)) {

						break;

					} else {
						// If we are searching for a specific device or device family,
						// then select only the code of this device/family (first 8-64 bits).
						bool deviceFilter = (romBitIndex <= searchContext.deviceFilterBits);
						uint8_t currentROMBit = ((searchContext.tempROM[romByteIndex] & romByteMask) != 0);

						// All devices have the same bit value
						if (bitA != bitB) {
							chosenRomBit = bitA;  // Choose it and continue
						} else {
							// If this discrepancy if before the Last Discrepancy on a previous step,
							// then pick the same bit value as last time.
							if ((romBitIndex < searchContext.lastDiscrepancy) || deviceFilter) {
								chosenRomBit = currentROMBit;
							} else {
								// If bit number is equal to the Last Discrepancy then choose 1, if not then choose 0.
								chosenRomBit = ((romBitIndex == searchContext.lastDiscrepancy) != 0);
							}

							// if 0 chosen then save its position in Discrepancy Marker
							if ((chosenRomBit == 0) && !deviceFilter) {
								discrepancyMarker = romBitIndex;
							}
						}

						if (deviceFilter) {
							// Check the filter match
							if (chosenRomBit != currentROMBit) {
								searchContext.lastDeviceFlag = true;
								break;
							}
						} else {
							// Set or clear the bit in the ROM
							if (chosenRomBit) {
								searchContext.tempROM[romByteIndex] |= romByteMask;
							} else {
								searchContext.tempROM[romByteIndex] &= ~romByteMask;
							}
						}
						// Write chosen ROM search direction bit
						writeBit(chosenRomBit);

						// Increment the bit counter and shift the byte mask
						romBitIndex++;
						romByteMask <<= 1;

						// If the current byte is done, then go to the next one and reset the mask
						if (romByteMask == 0) {
							romByteIndex++;
							romByteMask = 1;
						}
					}

				} while (romByteIndex < ONEWIRE_ROM_SIZE);  // Loop over all 64 bits of ROM
			}
		}

		// If the search was successful then...
		if (romBitIndex > (ONEWIRE_ROM_SIZE << 3)) {
			// ...set Last Discrepancy to the Discrepancy Marker...
			searchContext.lastDiscrepancy = discrepancyMarker;

			// ...check for the last device...
			if (searchContext.lastDiscrepancy == 0) {
				searchContext.lastDeviceFlag = true;
			}
			searchResult = true;
			// ...check CRC...
			searchContext.crcErrorFlag = (calcCRC(searchContext.tempROM, ONEWIRE_ROM_SIZE) != 0);
		}

	}

	// ...and finally check the result
	if (searchResult && (searchContext.tempROM[0] != 0)) return (searchContext.tempROM);

	return (nullptr);
}


///////////////////////////////////////////////////////////////////////////////////////////////////////////
// Auxiliary functions
///////////////////////////////////////////////////////////////////////////////////////////////////////////

// Calculate CRC over the buffer - 0x00 is correct
uint8_t OneWire::calcCRC(const uint8_t * addr, uint8_t len) {
	uint8_t crc = 0;
	for (uint8_t j = 0; j < len; j++) {
		crc = crc ^ addr[j];
		for (uint8_t i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) ? 0x8c : 0);
	}

// An alternative variant from the OneWire library ( http://www.pjrc.com/teensy/td_libs_OneWire.html ).
/*
	uint8_t addr = 0;
	while (len--) {
		uint8_t inbyte = *addr++;
		for (uint8_t i = 8; i; i--) {
			uint8_t mix = (crc ^ inbyte) & 0x01;
			crc >>= 1;
			if (mix) crc ^= 0x8C;
			inbyte >>= 1;
		}
	}
*/

	return crc;
}


// Pulse generation for oscillator calibration
void OneWire::pulse(uint16_t duration) {
    IrqLock lock;
    LockGuard guard(lock);
    pinLow();
	delayMicros(duration);
	pinRelease();
	delayMicros(duration);
}

// END-OF-FILE
