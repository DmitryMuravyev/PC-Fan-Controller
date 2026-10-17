////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// board_config.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	board_config.h
 * @brief	Board-level constants shared by the application and the library classes.
 * @note	FANS_COUNT defines the size of the sensor configuration (TSensor_Config),
 * 			which is stored in Flash and exchanged with the host application.
 * 			Changing it makes the saved configurations incompatible.
 */

#ifndef _BOARD_CONFIG_
#define _BOARD_CONFIG_


#define FANS_COUNT						6		///< Number of fan channels
#define DCDC_COUNT						4		///< Number of DC-DC converters (fan channels with voltage control)
#define FIRST_DCDC_FAN_INDEX			3		///< Number (1-based) of the first DC-DC-controlled fan
												///< (fans 1-2 are only PWM-controlled)
#define SENSOR_LINES_COUNT				2		///< Number of OneWire lines
#define UPPER_RESISTOR_VALUE			150		///< 15K
#define LOWER_RESISTOR_VALUE			33		///< 3.3K

#define FAN_SUBSTITUTION_MATRIX_SIZE	(FANS_COUNT * (FANS_COUNT - 1) / 2)		///< Substitution matrix size (1 byte per 2 coefficients)


#endif /* _BOARD_CONFIG_ */

// END-OF-FILE
