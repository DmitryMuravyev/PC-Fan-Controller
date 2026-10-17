////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2025 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// uart_logger.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	uart_logger.h
 * @brief	Simple blocking logger: text messages with a severity prefix and an optional parameter via UART.
 * @note	Set UL_Huart before use. Not intended to be called from interrupts.
 */

#ifndef _UART_LOGGER_
#define _UART_LOGGER_


// Includes
#include <stdint.h>
#include "stm32f0xx_hal.h"


#ifdef __cplusplus
extern "C" {
#endif

// Definitions

#define LOGGER_UART_TIMEOUT				10			///< Transmission timeout per string in milliseconds. At 115200 baud,
													///< this should be enough to transmit more than 100 characters.
#define LOGGER_SEVERITIES_COUNT			5			///< The number of different alarm/severity levels. The levels only affect
													///< the first word before the main message begins.
#define LOGGER_SEVERITY_STRING_LENGTH	10			///< Length of the severity prefix string

/**
 * @brief	Severity levels.
 */
typedef enum
{
	Logger_Severity_None				= 0,		///< No prefix (used for continuation lines)
	Logger_Severity_Info				= 1,		///< Information severity level
	Logger_Severity_Warning				= 2,		///< Warning severity level
	Logger_Severity_Error				= 3,		///< Error severity level
	Logger_Severity_Alarm				= 4			///< Alarm severity level
} UART_Logger_Severities;


extern UART_HandleTypeDef * UL_Huart;				///< UART used for logging (nullptr - logging is disabled)


/**
 * @brief	Get the hexadecimal digit for a 0 - 15 value.
 * @param	hex					Value (0 - 15).
 * @retval	Hexadecimal digit ('0' - '9', 'A' - 'F').
 */
char UL_getHex(uint8_t hex);

/**
 * @brief	Log a message with the specified severity.
 * @param	severity			Severity level (see UART_Logger_Severities).
 * @param	message				Message.
 * @retval	HAL status.
 */
HAL_StatusTypeDef UL_logMessage(uint8_t severity, const char * message);

/**
 * @brief	Log a message with an unsigned decimal parameter ("Message: 123").
 * @param	severity			Severity level (see UART_Logger_Severities).
 * @param	message				Message.
 * @param	param				Parameter.
 * @retval	HAL status.
 */
HAL_StatusTypeDef UL_logMessageWithShortIntParam(uint8_t severity, const char * message, uint16_t param);

/**
 * @brief	Log a message with a hexadecimal parameter ("Message: 0x1A2B").
 * @param	severity			Severity level (see UART_Logger_Severities).
 * @param	message				Message.
 * @param	param				Parameter (only significant bytes are printed).
 * @retval	HAL status.
 */
HAL_StatusTypeDef UL_logMessageWithLongHexParam(uint8_t severity, const char * message, uint32_t param);

/**
 * @brief	Log a message with a byte array in hexadecimal format ("Message: 0x01 0x02 ...").
 * @param	severity			Severity level (see UART_Logger_Severities).
 * @param	message				Message.
 * @param	param				Byte array (nullptr - only the message is logged).
 * @param	paramLength			Array length (0 - only the message is logged).
 * @retval	HAL status.
 */
HAL_StatusTypeDef UL_logMessageWithHexArrayParam(uint8_t severity, const char * message, const uint8_t * param, uint8_t paramLength);

/**
 * @brief	Log a message with a Q-format (fixed point) parameter ("Message: 12.34").
 * @param	severity			Severity level (see UART_Logger_Severities).
 * @param	message				Message.
 * @param	param				Q-format value.
 * @param	fractionBits		Number of fraction bits.
 * @retval	HAL status.
 */
HAL_StatusTypeDef UL_logMessageWithQFormatParam(uint8_t severity, const char * message, int16_t param, uint8_t fractionBits);

/**
 * @brief	Log a message with a string parameter ("Message: text").
 * @param	severity			Severity level (see UART_Logger_Severities).
 * @param	message				Message.
 * @param	param				String parameter.
 * @retval	HAL status.
 */
HAL_StatusTypeDef UL_logMessageWithStringParam(uint8_t severity, const char * message, const char * param);


#ifdef __cplusplus
}
#endif

#endif /* _UART_LOGGER_ */

// END-OF-FILE
