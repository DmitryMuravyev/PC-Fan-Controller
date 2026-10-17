////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// string_utils.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	string_utils.h
 * @brief	Some plain-C functions for working with strings.
 * @note	The number formatting functions fill a field of the specified length
 * 			and write the terminating zero after it, so the destination buffer
 * 			must be at least (length + 1) bytes.
 */

#ifndef _STRING_UTILS_
#define _STRING_UTILS_


#ifdef __cplusplus
extern "C" {
#endif


// Includes
#include <stdint.h>


// Some common definitions
#define STRING_UTILS_MAX_DIGITS						5			///< Maximum # of digits in the numeric value (uint16_t ~ 0xFFFF ~ 65535)
#define STRING_UTILS_MAX_TEN_POWER_FOR_MAX_DIGITS	10000		///< 10 in the power of (MAX_DIGITS - 1)


/**
 * @brief	Clean the string buffer and copy the new string into it.
 * @param	destinationString	Destination buffer (at least maxLength bytes).
 * @param	sourceString		Source string.
 * @param	maxLength			Maximum number of characters to copy.
 * 								The terminating zero isn't written if the source string is longer.
 * @retval	Number of copied characters.
 */
uint8_t SU_updateString(char * destinationString, const char * sourceString, uint8_t maxLength);

/**
 * @brief	Copy the string into the buffer and append it with an index ("NAME_1", "NAME_12", ...).
 * 			If the string is too long, it is truncated to fit the index. Indexes from 100
 * 			are written using letters for the tens: 100 = A0, 101 = A1, 110 = B0 and so on.
 * @param	destinationString	Destination buffer (at least maxLength bytes).
 * @param	sourceString		Source string.
 * @param	index				Index to append.
 * @param	maxLength			Maximum number of characters.
 */
void SU_appendIndexToString(char * destinationString, const char * sourceString, uint8_t index, uint8_t maxLength);

/**
 * @brief	Write the decimal digits of the value into the string from right to left.
 * @note	Used by the other formatting functions. The caller must ensure
 * 			there is enough space to the left of the starting position.
 * @param	destinationString	Destination buffer.
 * @param	value				Value to convert.
 * @param	lsPos				[in,out] Position of the least significant digit,
 * 								updated to the position to the left of the most significant one.
 */
void SU_getStringFromInt16(char * destinationString, uint16_t value, uint8_t *lsPos);

/**
 * @brief	Convert the value into a readable integer string (right-aligned in the field)
 * 			and append it with some suffix (one character).
 * @param	destinationString	Destination buffer (at least length + 1 bytes).
 * @param	value				Value to convert.
 * @param	length				Field length (at least STRING_UTILS_MAX_DIGITS + 1).
 * @param	suffix				Suffix character (0 - no suffix).
 */
void SU_getSuffixedStringFromInt16(char * destinationString, uint16_t value, uint8_t length, char suffix);

/**
 * @brief	Convert a signed Q-format value into a readable real-number string with two fractional digits.
 * @note	A field of STRING_UTILS_MAX_DIGITS + 3 characters is enough for any int16_t value
 * 			with 4 fraction bits. Shorter fields limit the range (e.g. 7 characters: -999.99 to 2047.93).
 * @param	destinationString	Destination buffer (at least maxLength + 1 bytes).
 * @param	value				Q-format value.
 * @param	fractionBits		Number of fraction bits.
 * @param	maxLength			Field length.
 */
void SU_getStringFromQFormat(char * destinationString, int16_t value, uint8_t fractionBits, uint8_t maxLength);


#ifdef __cplusplus
}
#endif

#endif /* _STRING_UTILS_ */

// END-OF-FILE


// Some plain-C functions for working with strings

#ifndef _STRING_UTILS_
#define _STRING_UTILS_


#ifdef __cplusplus
extern "C" {
#endif


// Includes
#include <stdint.h>


// Some common definitions
#define STRING_UTILS_MAX_DIGITS						5			// Maximum # of digits in the numeric value (uint16_t ~ 0xFFFF ~ 65535)
#define STRING_UTILS_MAX_TEN_POWER_FOR_MAX_DIGITS	10000		// 10 in the power of MAX_DIGITS



uint8_t SU_updateString(char * destinationString, const char * sourceString, uint8_t maxLength);
void SU_appendIndexToString(char * destinationString, const char * sourceString, uint8_t index, uint8_t maxLength);
void SU_getStringFromInt16(char * destinationString, uint16_t value, uint8_t *lsPos);
void SU_getSuffixedStringFromInt16(char * destinationString, uint16_t value, uint8_t length, char suffix);
void SU_getStringFromQFormat(char * destinationString, int16_t value, uint8_t fractionBits, uint8_t maxLength);


#ifdef __cplusplus
}
#endif

#endif /* _STRING_UTILS_ */

// END-OF-FILE
