////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2026 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// usb_reports.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	usb_reports.h
 * @brief	Data sizes of the USB HID reports (shared by the C report descriptor and the C++ code).
 * @note	The sizes depending on FANS_COUNT are calculated here. The correspondence
 * 			to the actual structures is checked by static_assert() in main.cpp.
 */

#ifndef _USB_REPORTS_
#define _USB_REPORTS_

#include "board_config.h"


#define USB_REPORT_ALIGN4(size)					(((size) + 3) & ~3)		///< Size of a structure with aligned(4)

#define USB_REPORT_PACKET_SIZE					64			///< Endpoint packet size (including Report ID)
#define USB_REPORT_HEADER_SIZE					6			///< Packet header (Communication_Packet_Header) size (Report ID + code + request ID)
#define USB_REPORT_MAX_PAYLOAD_SIZE				(USB_REPORT_PACKET_SIZE - USB_REPORT_HEADER_SIZE)	///< Maximum report payload: 58 bytes
#define USB_REPORT_MAX_DATA_SIZE				(USB_REPORT_PACKET_SIZE - 1)						///< Report size without Report ID (host command): 63 bytes

/// REPORT_COUNT value for the specified payload size (the Report ID isn't counted in the descriptor)
#define USB_REPORT_COUNT(payloadSize)			(USB_REPORT_HEADER_SIZE - 1 + (payloadSize))

// Report data sizes
#define USB_REPORT_SYSTEM_STATE_SIZE			32			///< sizeof(Sys_Info)
#define USB_REPORT_SYSTEM_CONFIG_SIZE			16			///< sizeof(System_Config)
#define USB_REPORT_FAN_COUNT_SIZE				1			///< Fan count
#define USB_REPORT_FAN_STATE_SIZE				24			///< sizeof(Fan_Info)
#define USB_REPORT_FAN_CONFIG_SIZE				48			///< sizeof(Fan_Config)
#define USB_REPORT_SUBSTITUTION_MATRIX_SIZE		USB_REPORT_ALIGN4(FAN_SUBSTITUTION_MATRIX_SIZE)		///< sizeof(Fan_Substitution_Matrix)
#define USB_REPORT_SENSOR_COUNT_SIZE			1			///< Sensor count
#define USB_REPORT_SENSOR_STATE_SIZE			8			///< sizeof(TSensor_Info)
#define USB_REPORT_SENSOR_CONFIG_SIZE			USB_REPORT_ALIGN4(25 + 3 * FANS_COUNT)				///< sizeof(TSensor_Config): 25 bytes + 3 arrays of FANS_COUNT
#define USB_REPORT_OPERATION_RESULT_SIZE		1			///< Error code


// Limits
#if (FANS_COUNT > 8)
#error "FANS_COUNT > 8 requires a wider TSensor_Info::overcooledByFanMap"
#endif

#if (USB_REPORT_SUBSTITUTION_MATRIX_SIZE > USB_REPORT_MAX_PAYLOAD_SIZE)
#error "Fan Substitution Matrix doesn't fit into a single USB report"
#endif

#if (USB_REPORT_SENSOR_CONFIG_SIZE > USB_REPORT_MAX_PAYLOAD_SIZE)
#error "Sensor configuration doesn't fit into a single USB report"
#endif

#if (USB_REPORT_FAN_CONFIG_SIZE > USB_REPORT_MAX_PAYLOAD_SIZE) || (USB_REPORT_SYSTEM_STATE_SIZE > USB_REPORT_MAX_PAYLOAD_SIZE)
#error "Report data doesn't fit into a single USB report"
#endif

#if (USB_REPORT_COUNT(USB_REPORT_MAX_PAYLOAD_SIZE) != (USB_REPORT_PACKET_SIZE - 1))
#error "USB_REPORT_COUNT() is inconsistent with the packet size"
#endif


#endif /* _USB_REPORTS_ */

// END-OF-FILE
