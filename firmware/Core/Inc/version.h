////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// version.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	version.h
 * @brief	Firmware version and product identification.
 */

#ifndef INC_VERSION_H_
#define INC_VERSION_H_


#define FIRMWARE_MAJOR_VERSION			1		///< Used to determine the compatibility of the saved configuration,
#define FIRMWARE_MINOR_VERSION			3		///< as well as the compatible version of the control software.
												///< The MINOR_VERSION value cannot be greater than 9, otherwise
												///< it will not be interpreted correctly by the USB HID protocol.
#define FIRMWARE_AUTHOR_STRING			"Dmitry Muravyev"			///< Author (also used as the USB manufacturer string)
#define FIRMWARE_PRODUCT_NAME_STRING	"Fan Controller"			///< Product name

#define FIRMWARE_STRINGIFY_(x)			#x		///< Helper macro: convert the argument to a string literal
#define FIRMWARE_STRINGIFY(x)			FIRMWARE_STRINGIFY_(x)		///< Helper macro: expand the argument and convert it to a string literal
#define FIRMWARE_VERSION_STRING			FIRMWARE_STRINGIFY(FIRMWARE_MAJOR_VERSION) "." FIRMWARE_STRINGIFY(FIRMWARE_MINOR_VERSION)	///< Version string ("1.3")


#endif /* INC_VERSION_H_ */

// END-OF-FILE
