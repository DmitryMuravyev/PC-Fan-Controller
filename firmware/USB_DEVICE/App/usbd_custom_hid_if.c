/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : usbd_custom_hid_if.c
  * @version        : v2.0_Cube
  * @brief          : USB Device Custom HID interface file.
  ******************************************************************************
  * @attention
  *
  * Copyright (c) 2025 STMicroelectronics.
  * All rights reserved.
  *
  * This software is licensed under terms that can be found in the LICENSE file
  * in the root directory of this software component.
  * If no LICENSE file comes with this software, it is provided AS-IS.
  *
  ******************************************************************************
  */
/* USER CODE END Header */

/* Includes ------------------------------------------------------------------*/
#include "usbd_custom_hid_if.h"

/* USER CODE BEGIN INCLUDE */

#include "usb_reports.h"


/* USER CODE END INCLUDE */

/* Private typedef -----------------------------------------------------------*/
/* Private define ------------------------------------------------------------*/
/* Private macro -------------------------------------------------------------*/

/* USER CODE BEGIN PV */
/* Private variables ---------------------------------------------------------*/

/* USER CODE END PV */

/** @addtogroup STM32_USB_OTG_DEVICE_LIBRARY
  * @brief Usb device.
  * @{
  */

/** @addtogroup USBD_CUSTOM_HID
  * @{
  */

/** @defgroup USBD_CUSTOM_HID_Private_TypesDefinitions USBD_CUSTOM_HID_Private_TypesDefinitions
  * @brief Private types.
  * @{
  */

/* USER CODE BEGIN PRIVATE_TYPES */

/* USER CODE END PRIVATE_TYPES */

/**
  * @}
  */

/** @defgroup USBD_CUSTOM_HID_Private_Defines USBD_CUSTOM_HID_Private_Defines
  * @brief Private defines.
  * @{
  */

/* USER CODE BEGIN PRIVATE_DEFINES */

/* USER CODE END PRIVATE_DEFINES */

/**
  * @}
  */

/** @defgroup USBD_CUSTOM_HID_Private_Macros USBD_CUSTOM_HID_Private_Macros
  * @brief Private macros.
  * @{
  */

/* USER CODE BEGIN PRIVATE_MACRO */

/* USER CODE END PRIVATE_MACRO */

/**
  * @}
  */

/** @defgroup USBD_CUSTOM_HID_Private_Variables USBD_CUSTOM_HID_Private_Variables
  * @brief Private variables.
  * @{
  */

/** Usb HID report descriptor. */
__ALIGN_BEGIN static uint8_t CUSTOM_HID_ReportDesc_FS[USBD_CUSTOM_HID_REPORT_DESC_SIZE] __ALIGN_END =
{
  /* USER CODE BEGIN 0 */

//////////////////////////////////////////////////////////////////////
// Custom code block by Dmitry Muravyev (DM)
//////////////////////////////////////////////////////////////////////
//
// COMPLETE REPORT DESCRIPTOR (173 bytes)
//
/*
	0x06, 0x00, 0xFF,		// USAGE_PAGE (Vendor Defined Page 1)
	0x09, 0x01,				// USAGE (Vendor Usage 1)

	0xA1, 0x01,				// COLLECTION (Application)

	// System Status Report
	0x85, 0x01,				// REPORT_ID (1)						System Status Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x15, 0x00, 			// LOGICAL_MINIMUM (0)
	0x26, 0xFF, 0x00,		// LOGICAL_MAXIMUM (255)
	0x75, 0x08,				// REPORT_SIZE (8)
	0x95, USB_REPORT_COUNT(USB_REPORT_SYSTEM_STATE_SIZE),			// REPORT_COUNT		Packet header (5) + sizeof(Sys_Info)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// System Configuration Report
	0x85, 0x02,				// REPORT_ID (2)						System Configuration Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x15, 0x00, 			// LOGICAL_MINIMUM (0)
	0x26, 0xFF, 0x00,		// LOGICAL_MAXIMUM (255)
	0x75, 0x08,				// REPORT_SIZE (8)
	0x95, USB_REPORT_COUNT(USB_REPORT_SYSTEM_CONFIG_SIZE),			// REPORT_COUNT		Packet header (5) + sizeof(System_Config)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Fan Count Report
	0x85, 0x03,				// REPORT_ID (3)						Fan Count Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x15, 0x00, 			// LOGICAL_MINIMUM (0)
	0x26, 0xFF, 0x00,		// LOGICAL_MAXIMUM (255)
	0x75, 0x08,				// REPORT_SIZE (8)
	0x95, USB_REPORT_COUNT(USB_REPORT_FAN_COUNT_SIZE),				// REPORT_COUNT		Packet header (5) + 1 (Fan Count)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Fan # State Report
	0x85, 0x04,				// REPORT_ID (4)						Fan_# State Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x15, 0x00, 			// LOGICAL_MINIMUM (0)
	0x26, 0xFF, 0x00,		// LOGICAL_MAXIMUM (255)
	0x75, 0x08,				// REPORT_SIZE (8)
	0x95, USB_REPORT_COUNT(USB_REPORT_FAN_STATE_SIZE),				// REPORT_COUNT		Packet header (5) + sizeof(Fan_Info)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Fan # Configuration Report
	0x85, 0x05,				// REPORT_ID (5)						Fan # Configuration Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x15, 0x00, 			// LOGICAL_MINIMUM (0)
	0x26, 0xFF, 0x00,		// LOGICAL_MAXIMUM (255)
	0x75, 0x08,				// REPORT_SIZE (8)
	0x95, USB_REPORT_COUNT(USB_REPORT_FAN_CONFIG_SIZE),				// REPORT_COUNT		Packet header (5) + sizeof(Fan_Config)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Fan Substitution Matrix Report
	0x85, 0x06,				// REPORT_ID (6)						Fan Substitution Matrix Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x15, 0x00, 			// LOGICAL_MINIMUM (0)
	0x26, 0xFF, 0x00,		// LOGICAL_MAXIMUM (255)
	0x75, 0x08,				// REPORT_SIZE (8)
	0x95, USB_REPORT_COUNT(USB_REPORT_SUBSTITUTION_MATRIX_SIZE),	// REPORT_COUNT		Packet header (5) + sizeof(Fan_Substitution_Matrix)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Sensor Count Report
	0x85, 0x07,				// REPORT_ID (7)						Sensor Count Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x15, 0x00, 			// LOGICAL_MINIMUM (0)
	0x26, 0xFF, 0x00,		// LOGICAL_MAXIMUM (255)
	0x75, 0x08,				// REPORT_SIZE (8)
	0x95, USB_REPORT_COUNT(USB_REPORT_SENSOR_COUNT_SIZE),			// REPORT_COUNT		Packet header (5) + 1 (Sensor Count)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Sensor State Report
	0x85, 0x08,				// REPORT_ID (8)						Sensor State Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x15, 0x00, 			// LOGICAL_MINIMUM (0)
	0x26, 0xFF, 0x00,		// LOGICAL_MAXIMUM (255)
	0x75, 0x08,				// REPORT_SIZE (8)
	0x95, USB_REPORT_COUNT(USB_REPORT_SENSOR_STATE_SIZE),			// REPORT_COUNT		Packet header (5) + sizeof(TSensor_Info)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Sensor Configuration Report
	0x85, 0x09,				// REPORT_ID (9)						Sensor Configuration Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x15, 0x00, 			// LOGICAL_MINIMUM (0)
	0x26, 0xFF, 0x00,		// LOGICAL_MAXIMUM (255)
	0x75, 0x08,				// REPORT_SIZE (8)
	0x95, USB_REPORT_COUNT(USB_REPORT_SENSOR_CONFIG_SIZE),			// REPORT_COUNT		Packet header (5) + sizeof(TSensor_Config)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// The result of executing a command from the host
	0x85, 0x10,				// REPORT_ID (16)						The result of executing a command from the host
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x15, 0x00, 			// LOGICAL_MINIMUM (0)
	0x26, 0xFF, 0x00,		// LOGICAL_MAXIMUM (255)
	0x75, 0x08,				// REPORT_SIZE (8)
	0x95, USB_REPORT_COUNT(USB_REPORT_OPERATION_RESULT_SIZE),		// REPORT_COUNT		Packet header (5) + error code (1)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Request from the Host
	0x85, 0x80,				// REPORT_ID (128)						Request from the Host
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x15, 0x00, 			// LOGICAL_MINIMUM (0)
	0x26, 0xFF, 0x00,		// LOGICAL_MAXIMUM (255)
	0x75, 0x08,				// REPORT_SIZE (8)
	0x95, USB_REPORT_MAX_DATA_SIZE,									// REPORT_COUNT		Packet size (64) - Report ID (1)
	0x91, 0x02,				// OUTPUT (Data,Var,Abs)
*/
//
// SHORT REPORT DESCRIPTOR (103 bytes)
//

	0x06, 0x00, 0xFF,		// USAGE_PAGE (Vendor Defined Page 1)
	0x09, 0x01,				// USAGE (Vendor Usage 1)

	0xA1, 0x01,				// COLLECTION (Application)

	// System Status Report
	0x85, 0x01,				// REPORT_ID (1)						System Status Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x15, 0x00, 			// LOGICAL_MINIMUM (0)
	0x26, 0xFF, 0x00,		// LOGICAL_MAXIMUM (255)
	0x75, 0x08,				// REPORT_SIZE (8)
	0x95, USB_REPORT_COUNT(USB_REPORT_SYSTEM_STATE_SIZE),			// REPORT_COUNT		Packet header (5) + sizeof(Sys_Info)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// System Configuration Report
	0x85, 0x02,				// REPORT_ID (2)						System Configuration Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x95, USB_REPORT_COUNT(USB_REPORT_SYSTEM_CONFIG_SIZE),			// REPORT_COUNT		Packet header (5) + sizeof(System_Config)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Fan Count Report
	0x85, 0x03,				// REPORT_ID (3)						Fan Count Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x95, USB_REPORT_COUNT(USB_REPORT_FAN_COUNT_SIZE),				// REPORT_COUNT		Packet header (5) + 1 (Fan Count)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Fan # State Report
	0x85, 0x04,				// REPORT_ID (4)						Fan_# State Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x95, USB_REPORT_COUNT(USB_REPORT_FAN_STATE_SIZE),				// REPORT_COUNT		Packet header (5) + sizeof(Fan_Info)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Fan # Configuration Report
	0x85, 0x05,				// REPORT_ID (5)						Fan # Configuration Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x95, USB_REPORT_COUNT(USB_REPORT_FAN_CONFIG_SIZE),				// REPORT_COUNT		Packet header (5) + sizeof(Fan_Config)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Fan Substitution Matrix Report
	0x85, 0x06,				// REPORT_ID (6)						Fan Substitution Matrix Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x95, USB_REPORT_COUNT(USB_REPORT_SUBSTITUTION_MATRIX_SIZE),	// REPORT_COUNT		Packet header (5) + sizeof(Fan_Substitution_Matrix)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Sensor Count Report
	0x85, 0x07,				// REPORT_ID (7)						Sensor Count Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x95, USB_REPORT_COUNT(USB_REPORT_SENSOR_COUNT_SIZE),			// REPORT_COUNT		Packet header (5) + 1 (Sensor Count)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Sensor State Report
	0x85, 0x08,				// REPORT_ID (8)						Sensor State Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x95, USB_REPORT_COUNT(USB_REPORT_SENSOR_STATE_SIZE),			// REPORT_COUNT		Packet header (5) + sizeof(TSensor_Info)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Sensor Configuration Report
	0x85, 0x09,				// REPORT_ID (9)						Sensor Configuration Report
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x95, USB_REPORT_COUNT(USB_REPORT_SENSOR_CONFIG_SIZE),			// REPORT_COUNT		Packet header (5) + sizeof(TSensor_Config)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// The result of executing a command from the host
	0x85, 0x10,				// REPORT_ID (16)						The result of executing a command from the host
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x95, USB_REPORT_COUNT(USB_REPORT_OPERATION_RESULT_SIZE),		// REPORT_COUNT		Packet header (5) + error code (1)
	0x81, 0x02,				// INPUT (Data,Var,Abs)

	// Request from the Host
	0x85, 0x80,				// REPORT_ID (128)						Request from the Host
	0x09, 0x01,				// USAGE (Vendor Usage 1)
	0x95, USB_REPORT_MAX_DATA_SIZE,									// REPORT_COUNT		Packet size (64) - Report ID (1)
	0x91, 0x02,				// OUTPUT (Data,Var,Abs)

//////////////////////////////////////////////////////////////////////
// End custom code block
//////////////////////////////////////////////////////////////////////

  /* USER CODE END 0 */
  0xC0    /*     END_COLLECTION	             */
};

/* USER CODE BEGIN PRIVATE_VARIABLES */

/* USER CODE END PRIVATE_VARIABLES */

/**
  * @}
  */

/** @defgroup USBD_CUSTOM_HID_Exported_Variables USBD_CUSTOM_HID_Exported_Variables
  * @brief Public variables.
  * @{
  */
extern USBD_HandleTypeDef hUsbDeviceFS;

/* USER CODE BEGIN EXPORTED_VARIABLES */

/* USER CODE END EXPORTED_VARIABLES */
/**
  * @}
  */

/** @defgroup USBD_CUSTOM_HID_Private_FunctionPrototypes USBD_CUSTOM_HID_Private_FunctionPrototypes
  * @brief Private functions declaration.
  * @{
  */

static int8_t CUSTOM_HID_Init_FS(void);
static int8_t CUSTOM_HID_DeInit_FS(void);
static int8_t CUSTOM_HID_OutEvent_FS(uint8_t event_idx, uint8_t state);

/**
  * @}
  */

USBD_CUSTOM_HID_ItfTypeDef USBD_CustomHID_fops_FS =
{
  CUSTOM_HID_ReportDesc_FS,
  CUSTOM_HID_Init_FS,
  CUSTOM_HID_DeInit_FS,
  CUSTOM_HID_OutEvent_FS
};

/** @defgroup USBD_CUSTOM_HID_Private_Functions USBD_CUSTOM_HID_Private_Functions
  * @brief Private functions.
  * @{
  */

/* Private functions ---------------------------------------------------------*/

/**
  * @brief  Initializes the CUSTOM HID media low layer
  * @retval USBD_OK if all operations are OK else USBD_FAIL
  */
static int8_t CUSTOM_HID_Init_FS(void)
{
  /* USER CODE BEGIN 4 */
  return (USBD_OK);
  /* USER CODE END 4 */
}

/**
  * @brief  DeInitializes the CUSTOM HID media low layer
  * @retval USBD_OK if all operations are OK else USBD_FAIL
  */
static int8_t CUSTOM_HID_DeInit_FS(void)
{
  /* USER CODE BEGIN 5 */
  return (USBD_OK);
  /* USER CODE END 5 */
}

/**
  * @brief  Manage the CUSTOM HID class events
  * @param  event_idx: Event index
  * @param  state: Event state
  * @retval USBD_OK if all operations are OK else USBD_FAIL
  */
static int8_t CUSTOM_HID_OutEvent_FS(uint8_t event_idx, uint8_t state)
{
  /* USER CODE BEGIN 6 */

//////////////////////////////////////////////////////////////////////
// Custom code block by Dmitry Muravyev (DM)
//////////////////////////////////////////////////////////////////////

	USBD_CUSTOM_HID_HandleTypeDef *hhid = (USBD_CUSTOM_HID_HandleTypeDef*)hUsbDeviceFS.pClassData;
	USB_HID_DataReceived(event_idx, hhid->Report_buf);

//////////////////////////////////////////////////////////////////////
// End custom code block
//////////////////////////////////////////////////////////////////////

  return (USBD_OK);
  /* USER CODE END 6 */
}

/* USER CODE BEGIN 7 */
/**
  * @brief  Send the report to the Host
  * @param  report: The report to be sent
  * @param  len: The report length
  * @retval USBD_OK if all operations are OK else USBD_FAIL
  */
/*
static int8_t USBD_CUSTOM_HID_SendReport_FS(uint8_t *report, uint16_t len)
{
  return USBD_CUSTOM_HID_SendReport(&hUsbDeviceFS, report, len);
}
*/
/* USER CODE END 7 */

/* USER CODE BEGIN PRIVATE_FUNCTIONS_IMPLEMENTATION */

//////////////////////////////////////////////////////////////////////
// Custom code block by Dmitry Muravyev (DM)
//////////////////////////////////////////////////////////////////////

__weak void USB_HID_DataReceived(uint8_t reportID, uint8_t * buffer)
{
  /* Prevent unused argument(s) compilation warning */
  UNUSED(reportID);
  UNUSED(buffer);

  /* NOTE : This function should not be modified, when the callback is needed,
            the USB_HID_DataReceived could be implemented in the user file
   */
}

//////////////////////////////////////////////////////////////////////
// End custom code block
//////////////////////////////////////////////////////////////////////

/* USER CODE END PRIVATE_FUNCTIONS_IMPLEMENTATION */
/**
  * @}
  */

/**
  * @}
  */

/**
  * @}
  */

