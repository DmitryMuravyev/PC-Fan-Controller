////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// uart_logger.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include <uart_logger.h>
#include <string.h>
#include <stdbool.h>
#include "string_utils.h"


// Implementation-level definitions
// Call procedure and check the result
#define CALL_AND_CHECK(f)							{ HAL_StatusTypeDef status = (f); if (status != HAL_OK) return (status); }
// Call procedure and get the worse result
#define CALL_AND_GET_WORSE_RESULT(f, v)		{ HAL_StatusTypeDef firstStatus = v; \
											  v = (f); \
											  if (firstStatus != HAL_OK) v = firstStatus; }


static const char UL_paramDelimiterString[] = ": ";
static const char UL_hexPrefixString[] = "0x";
static const char UL_crlfString[] = "\r\n";
static const char UL_severityStrings[LOGGER_SEVERITIES_COUNT][LOGGER_SEVERITY_STRING_LENGTH + 1] = {
	"    \0\0\0\0\0\0",
	"INFO    > ",
	"WARNING > ",
	"ERROR   > ",
	"ALARM   > "};

// UART Handle structure pointer
UART_HandleTypeDef * UL_Huart = NULL;



// Get hexadecimal digit from 0 - 15 decimal value
char UL_getHex(uint8_t hex) {
	if (hex >= 10) {
		return ('A' + hex - 10);
	} else {
		return ('0' + hex);
	}
}


// Transmit string via UART
static HAL_StatusTypeDef transmitString(const char * string) {
	if (UL_Huart == NULL) return (HAL_ERROR);
	uint32_t tickStart = HAL_GetTick();
	while ((UL_Huart->gState != HAL_UART_STATE_READY) &&
			(HAL_GetTick() - tickStart) < LOGGER_UART_TIMEOUT) {};
	return (HAL_UART_Transmit(UL_Huart, (uint8_t*)string, strlen(string), LOGGER_UART_TIMEOUT));
}


// Transmit a string preceded by a severity string
static HAL_StatusTypeDef transmitSeverityString(uint8_t severity, const char * string, bool crlf) {
	if (UL_Huart == NULL) return (HAL_ERROR);
	if (severity >= LOGGER_SEVERITIES_COUNT) severity = Logger_Severity_None;
	HAL_StatusTypeDef result = transmitString(UL_severityStrings[severity]);
	CALL_AND_GET_WORSE_RESULT(transmitString(string), result);
	if (crlf) CALL_AND_GET_WORSE_RESULT(transmitString(UL_crlfString), result);
	return (result);
}


// Log some message with specified severity
HAL_StatusTypeDef UL_logMessage(uint8_t severity, const char * message) {
	return (transmitSeverityString(severity, message, true));
}


// Log message with specified severity and uint16_t parameter
HAL_StatusTypeDef UL_logMessageWithShortIntParam(uint8_t severity, const char * message, uint16_t param) {
	CALL_AND_CHECK(transmitSeverityString(severity, message, false));
	CALL_AND_CHECK(transmitString(UL_paramDelimiterString));

	char paramString[STRING_UTILS_MAX_DIGITS + 2];	// # of digits + suffix + \0
	SU_getSuffixedStringFromInt16(paramString, param, STRING_UTILS_MAX_DIGITS + 1, 0);

	CALL_AND_CHECK(transmitString(paramString));
	CALL_AND_CHECK(transmitString(UL_crlfString));
	return (HAL_OK);
}


// Log message with uint32 parameter displayed in hexadecimal format
HAL_StatusTypeDef UL_logMessageWithLongHexParam(uint8_t severity, const char * message, uint32_t param) {
	CALL_AND_CHECK(transmitSeverityString(severity, message, false));
	CALL_AND_CHECK(transmitString(UL_paramDelimiterString));

	uint8_t bytes = 1;
	while ((bytes < 4) && ((param >> (bytes << 3)) != 0)) bytes++;

	uint8_t prefixLen = strlen(UL_hexPrefixString);
	char paramString[prefixLen + (bytes * 2) + 1];	// prefix + 2 hex characters per byte + '\0'
	memcpy(paramString, UL_hexPrefixString, prefixLen);

	while (1) {
		bytes--;
		uint8_t currentByte = ((param >> (bytes << 3)) & 0xFF);
		paramString[prefixLen++] = UL_getHex(currentByte / 16);
		paramString[prefixLen++] = UL_getHex(currentByte % 16);
		if (bytes == 0) {
			paramString[prefixLen] = '\0';
			break;
		}
	}

	CALL_AND_CHECK(transmitString(paramString));
	CALL_AND_CHECK(transmitString(UL_crlfString));
	return (HAL_OK);
}


// Log message with the data of some buffer displayed in hexadecimal format
HAL_StatusTypeDef UL_logMessageWithHexArrayParam(uint8_t severity, const char * message, const uint8_t * param, uint8_t paramLength) {
	bool emptyParam = (param == NULL) || (paramLength == 0);
	CALL_AND_CHECK(transmitSeverityString(severity, message, emptyParam));
	if (!emptyParam) {
		CALL_AND_CHECK(transmitString(UL_paramDelimiterString));

		uint8_t prefixLen = strlen(UL_hexPrefixString);
		uint8_t hexLen = prefixLen + 3;			// prefix + 2 hex characters + space or '\0'
		char paramString[paramLength * hexLen];

		// Let's use hexLen var as an offset
		hexLen = 0;
		uint8_t i = 0;
		while (1) {
			memcpy(paramString + hexLen, UL_hexPrefixString, prefixLen);
			hexLen += prefixLen;
			paramString[hexLen++] = UL_getHex(param[i] / 16);
			paramString[hexLen++] = UL_getHex(param[i] % 16);
			if (++i >= paramLength) {
				paramString[hexLen] = '\0';
				break;
			} else {
				paramString[hexLen++] = ' ';
			}
		}

		CALL_AND_CHECK(transmitString(paramString));
		CALL_AND_CHECK(transmitString(UL_crlfString));
	}
	return (HAL_OK);
}


// Log message with Q-format (fixed point) parameter
HAL_StatusTypeDef UL_logMessageWithQFormatParam(uint8_t severity, const char * message, int16_t param, uint8_t fractionBits) {
	CALL_AND_CHECK(transmitSeverityString(severity, message, false));
	CALL_AND_CHECK(transmitString(UL_paramDelimiterString));

	char paramString[STRING_UTILS_MAX_DIGITS + 4];	// Field (# of digits + "." + 2 fractional digits) + '\0'
	SU_getStringFromQFormat(paramString, param, fractionBits, STRING_UTILS_MAX_DIGITS + 3);

	CALL_AND_CHECK(transmitString(paramString));
	CALL_AND_CHECK(transmitString(UL_crlfString));
	return (HAL_OK);
}


// Log message with additional string as a parameter
HAL_StatusTypeDef UL_logMessageWithStringParam(uint8_t severity, const char * message, const char * param) {
	CALL_AND_CHECK(transmitSeverityString(severity, message, false));
	CALL_AND_CHECK(transmitString(UL_paramDelimiterString));
	CALL_AND_CHECK(transmitString(param));
	CALL_AND_CHECK(transmitString(UL_crlfString));
	return (HAL_OK);
}

// END-OF-FILE
