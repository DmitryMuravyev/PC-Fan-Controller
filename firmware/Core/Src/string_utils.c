////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// string_utils.cpp
////////////////////////////////////////////////////////////////////////////////////////

#include <string_utils.h>
#include <stdbool.h>
#include <string.h>


// Update non-empty string with the new string
uint8_t SU_updateString(char * destinationString, const char * sourceString, uint8_t maxLength) {
	memset(destinationString, 0, maxLength);
	uint8_t i = 0;
	while ((i < maxLength) && ((destinationString[i] = sourceString[i]) != 0)) i++;
	return (i);
}


// Copy string into the buffer and append it with some index
void SU_appendIndexToString(char * destinationString, const char * sourceString, uint8_t index, uint8_t maxLength) {
	uint8_t tens = index / 10;
	uint8_t pos = SU_updateString(destinationString, sourceString, maxLength);
	uint8_t lastPos = maxLength - 1;
	if (tens != 0) lastPos--;
	if (pos < lastPos) {
		if (pos != 0) destinationString[pos++] = '_';
		lastPos = pos;
	}

	if (tens != 0) {
		if (tens < 10) {
			// Up to index = 99 we will write it as usual 2-digits number
			destinationString[lastPos++] = '0' + tens;
		} else {
			// For index > 100 we will use the letters of the alphabet.
			// For example: 100 = A0,
			//				101 = A1,
			//				110 = B0 and so on.
			destinationString[lastPos++] = 'A' - 10 + tens;
		}
	}
	destinationString[lastPos++] = '0' + (index - tens * 10);
	if (lastPos < maxLength) destinationString[lastPos] = '\0';
}


// Convert uint16_t into a readable integer string
void SU_getStringFromInt16(char * destinationString, uint16_t value, uint8_t *lsPos) {
	uint8_t digits[STRING_UTILS_MAX_DIGITS];
	uint16_t divider = STRING_UTILS_MAX_TEN_POWER_FOR_MAX_DIGITS;
	uint8_t firstSignificantDigit = 0;
	int8_t i = STRING_UTILS_MAX_DIGITS;

	// Determine digits
	do {
		i--;
		digits[i] = value / divider;
		if ((firstSignificantDigit == 0) && (digits[i] != 0)) firstSignificantDigit = i;
		value -= digits[i] * divider;
		divider /= 10;
	} while (i);

	// Output of an integer centered relative to the maximum number of digits
	for (int8_t i = 0; i < STRING_UTILS_MAX_DIGITS; i++) {
		//if ((i == 0) || (digits[i] != 0) || (i < firstSignificantDigit)) destinationString[(*lsPos)--] = '0' + digits[i];
		if ((i == 0) || (i <= firstSignificantDigit)) destinationString[(*lsPos)--] = '0' + digits[i];

	}
}


// Convert uint16_t into a readable integer string and append it with some suffix (one letter)
void SU_getSuffixedStringFromInt16(char * destinationString, uint16_t value, uint8_t length, char suffix) {
	memset(destinationString, ' ', length);
	destinationString[length] = 0;
	uint8_t lsPos = length - 1;
	//if (value < 10000) lsPos--;
	if (value < 1000) lsPos--;
	if (value < 100) lsPos--;
	if (suffix) destinationString[lsPos] = suffix;
	lsPos--;
	SU_getStringFromInt16(destinationString, value, &lsPos);
}


// Convert signed Q-format value into a readable real-number string
void SU_getStringFromQFormat(char * destinationString, int16_t value, uint8_t fractionBits, uint8_t maxLength) {
	memset(destinationString, ' ', maxLength);
	destinationString[maxLength] = 0;

	uint8_t lsPos = maxLength - 1;
	bool negative = 0;

	if (value < 0) {
		value = -value;
		negative = 1;
	}
	uint16_t whole = value >> fractionBits;
	uint16_t fractional = (((value & ((1 << fractionBits) - 1)) * 1000) >> fractionBits);
	uint8_t tenth = fractional / 100;
	fractional -= tenth * 100;
	uint8_t hundredth = fractional / 10;

	if ((whole < 100) && !negative) lsPos--;
	if (hundredth > 0) destinationString[lsPos] = '0' + hundredth;
	lsPos--;
	destinationString[lsPos--] = '0' + tenth;
	destinationString[lsPos--] = '.';

	SU_getStringFromInt16(destinationString, whole, &lsPos);
	if (negative) destinationString[lsPos] = '-';
}

// END-OF-FILE
