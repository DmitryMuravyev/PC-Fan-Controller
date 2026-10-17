/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : main.c
  * @brief          : Main program body
  ******************************************************************************
  * @attention
  *
  * Copyright (c) 2022 STMicroelectronics.
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
#include "main.h"
#include "adc.h"
#include "dma.h"
#include "i2c.h"
#include "iwdg.h"
#include "spi.h"
#include "tim.h"
#include "usart.h"
#include "usb_device.h"
#include "gpio.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */

/////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////

// PLEASE NOTE!!! All important and necessary changes
// for USB Custom HID operation are performed in the following files:
//
// USB_DEVICE/Target/usbd_conf.h		(buffer and report descriptor sizes, set in the .ioc)
// USB_DEVICE/App/usbd_custom_hid_if.c	(report descriptor, data reception)
// Drivers/USB/usbd_customhid.h			(64-byte endpoints, moved from Middlewares)
// Drivers/USB/usbd_customhid.c			(Some corrections to the original code.)
// Drivers/USB/usbd_desc.c				(device descriptor and version, moved from USB_DEVICE/App)
//
// After regenerating the code in CubeMX, please rename this file from '.c' to '.cpp'
// and delete the restored copies of the moved files:
// USB_DEVICE/App/usbd_desc.c
// Middlewares/ST/STM32_USB_Device_Library/Class/CustomHID/Inc/usbd_customhid.h
// Middlewares/ST/STM32_USB_Device_Library/Class/CustomHID/Src/usbd_customhid.c
// (on Windows, these steps should be handled automatically
// by the scripts located in the 'Scripts' folder.)

/////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////////////

#include <new>

#include "version.h"
#include "board_config.h"
#include "usb_reports.h"

#include "ST7789.h"
#include "DS1841.h"
#include "DS18B20.h"
#include "MAX31790.h"

#include "logo.h"

#include "vmeter_mcu.h"
#include "fan_MAX31790_DS1841.h"
#include "tsensor_DS1841.h"
#include "tsensor_DS18B20_ml.h"
#include "tsensor_mcu.h"
#include "tsensor_sw.h"

#include "dynamic_list.h"

#include "string_utils.h"
#include "uart_logger.h"

#include "usbd_customhid.h"
#include "usb_device.h"
#include "usbd_custom_hid_if.h"


/* USER CODE END Includes */

/* Private typedef -----------------------------------------------------------*/
/* USER CODE BEGIN PTD */

#define DISPLAY_ENABLED											// It is possible to compile the firmware without display support
#define LOGGING_ENABLED											// ...and without UART logger.


#define SETTINGS_PAGES_COUNT							4		// The number of memory pages allocated for storing settings.
																// One page = 2KB, which is enough to store the configuration
																// of more than 30 sensors. When rewriting, the pages alternate,
																// so the more of them, the larger the Flash resource will be
																// and will allow you to make more changes to the settings.
																// The minimum value is 2 pages.
#define SETTINGS_COMPLETENESS_MARKER					0xAAAAAAAA		// != 0xFF and != 0x00 - All the bits are written, which means
																		// the write operation has been completed successfully.
#define SETTINGS_COMPLETENESS_MARKER_SIZE				sizeof(OK_Marker)		// 4 bytes
#define SETTINGS_PAGE_SIZE_WO_SENSORS					(sizeof(System_Config_Block) + sizeof(Fan_Config_Block) * FANS_COUNT +\
														sizeof(Substitution_Matrix_Block) + SETTINGS_COMPLETENESS_MARKER_SIZE * 2)
#define MAX_SENSORS_COUNT 								((FLASH_PAGE_SIZE - SETTINGS_PAGE_SIZE_WO_SENSORS) / sizeof(Sensor_Config_Block))
																// Sensors configuration Block size = 2048 - 4 - 20 - (6 * 52) - 20 - 4 = 1688
																// Sensors count = 1688 / 48 = 35
																// 8 bytes left (reserved)
#define SETTINGS_PAGE_RESERVED_BYTES					(FLASH_PAGE_SIZE - SETTINGS_PAGE_SIZE_WO_SENSORS - MAX_SENSORS_COUNT * sizeof(Sensor_Config_Block))

#define MIN_FREE_MEMORY									1024	// Value should be based on memory profiling results
#define ALARM_FREE_MEMORY								256		// Value should be based on memory profiling results

#define UPDATE_DELAY									1000	// Update all (fans/sensors/display) once per second
#define BUTTON_DEBOUNCE_DELAY							50		// The button must be pressed for at least 50 milliseconds.
#define BUTTON_LONG_PRESS_DELAY							1000	// And 1 second for a long press.
#define SHORT_DELAY										100		// For all kinds of blinks and stuff
#define ALARM_DELAY										60000	// Buzzer beeps and LED blinking once per minute
#define CONFIG_PAGE_SESSION_TIMEOUT						10000	// The host configuration session is aborted after 10 seconds of inactivity

#define STATUSES_COUNT									16		// The # of System statuses
#define STATUS_NORMAL_INDEX								1		// Index of the Status_Normal entry in the systemStatuses[] table
#define BUZZER_OPTIONS_COUNT							3		// Buzzer Menu options count
#define BUZZER_BEEP_DURATION							250		// One beep = 250ms ON + 250ms OFF = 0.5s
#define DEFAULT_BUZZER_MODE								Buzzer_Noisy_Mode		// By default, buzzer yells at all alarms and warnings.

#define ADC_CHANNELS_COUNT								8		// ADC channels for TSensors, VMeters and Ref
#define POWER_BUS_VOLTAGE_THRESHOLD_SHIFT				1		// If the voltage drops by 2-4-8 (and so on) times, there will be an alarm.

#define USE_MCU_TSENSOR											// Add MCU TSensor when scanning
#define USE_DIGIPOTS_TSENSOR									// Add digiPOTs TSensors when scanning

#define HOST_PING_TIMEOUT								3000	// The host service requests the System State every second. If there are no requests
																// for 3 seconds, the USB data exchange is considered frozen.
#define USB_RECONNECT_DELAY								200		// USB disconnection duration required for the host to detect it
#define USB_BUS_ACTIVITY_CHECK_DELAY					3		// SOF packets are sent every 1 ms, so the frame number must change within this time


#if defined(DISPLAY_ENABLED)
// menu items, font features, and colors used.

#define DISPLAY_MAIN_SECTIONS_COUNT						5
#define DISPLAY_ACTION_SECTIONS_COUNT					4
#define DISPLAY_INFO_SECTIONS_COUNT						(DISPLAY_MAIN_SECTIONS_COUNT + DISPLAY_ACTION_SECTIONS_COUNT)
#define DISPLAY_MENU_SECTIONS_COUNT						5

#define DISPLAY_SENSORS_SECTION							0
#define DISPLAY_FAN_CONTROL_SECTION						1
#define DISPLAY_FAN_RPM_SECTION							2
#define DISPLAY_FAN_POWER_SECTION						3
#define DISPLAY_SYSTEM_INFO_SECTION						4
#define DISPLAY_SENSOR_SCAN_SECTION						5
#define DISPLAY_FAN_SCAN_SECTION						6
#define DISPLAY_HARDWARE_SCAN_SECTION					7
#define DISPLAY_HOST_REQUEST_SECTION					8

#define DISPLAY_MAIN_MENU_SECTION						DISPLAY_INFO_SECTIONS_COUNT

#define DISPLAY_BUZZER_OPTIONS_SECTION					(DISPLAY_MAIN_MENU_SECTION + 1)
#define DISPLAY_SENSOR_SCAN_CONFIRMATION_SECTION		(DISPLAY_MAIN_MENU_SECTION + 2)
#define DISPLAY_FAN_SCAN_CONFIRMATION_SECTION			(DISPLAY_MAIN_MENU_SECTION + 3)
#define DISPLAY_HARDWARE_SCAN_CONFIRMATION_SECTION		(DISPLAY_MAIN_MENU_SECTION + 4)

#define SECTION_SYSTEM_PARAMETERS_COUNT					6
#define SECTION_DISPLAY_DURATION						7
#define SECTION_SELECTED_DISPLAY_DURATION				30

#define DISPLAY_MAIN_MENU_ITEMS_COUNT					6
#define DISPLAY_CONFIRM_MENU_ITEMS_COUNT				2

#define DISPLAY_MAIN_MENU_BUZZER_ITEM					0
#define DISPLAY_MAIN_MENU_ROTATE_ITEM					1
#define DISPLAY_MAIN_MENU_SENSOR_SCAN_ITEM				2
#define DISPLAY_MAIN_MENU_FAN_SCAN_ITEM					3
#define DISPLAY_MAIN_MENU_HARDWARE_SCAN_ITEM			4
#define DISPLAY_MAIN_MENU_EXIT_ITEM						5
#define DISPLAY_CONFIRM_MENU_YES_ITEM					0

#define DISPLAY_SYSTEM_PARAMETERS_HIGH_PWR_ITEM			0
#define DISPLAY_SYSTEM_PARAMETERS_LOW_PWR_ITEM			1
#define DISPLAY_SYSTEM_PARAMETERS_MCU_VDD_ITEM			2
#define DISPLAY_SYSTEM_PARAMETERS_RAM_ITEM				3
#define DISPLAY_SYSTEM_PARAMETERS_POTS_STATUS_ITEM		4
#define DISPLAY_SYSTEM_PARAMETERS_31790_STATUS_ITEM		5

#define DISPLAY_SECTION_HEADER_FONT						font_16x26
#define DISPLAY_SECTION_HEADER_LENGTH					15
#define DISPLAY_SECTION_HEADER_X_POS					0
#define DISPLAY_SECTION_HEADER_Y_POS					0

#define DISPLAY_MENU_HEADER_FONT						font_11x18
#define DISPLAY_MENU_HEADER_LENGTH						20
#define DISPLAY_MENU_HEADER_X_POS						10
#define DISPLAY_MENU_HEADER_Y_POS						5

#define DISPLAY_MENU_ITEM_FONT							font_16x26
#define DISPLAY_MENU_ITEM_LENGTH						14
#define DISPLAY_MENU_ITEM_X_POS							8
#define DISPLAY_MENU_ITEM_Y_POS							30
#define DISPLAY_MENU_ITEM_BLINKS_COUNT					3

#define DISPLAY_PARAMETER_FONT							font_11x18
#define DISPLAY_PARAMETER_LENGTH						8
#define DISPLAY_PARAMETERS_PER_PAGE						6
#define DISPLAY_PARAMETERS_STEP							30
#define DISPLAY_PARAMETER_X_POS							15
#define DISPLAY_PARAMETER_Y_POS							35

#define DISPLAY_VALUE_FONT								font_16x26
#define DISPLAY_VALUE_LENGTH							7
#define DISPLAY_VALUE_Y_POS								30
#define DISPLAY_VALUE_X_POS								118

#define DISPLAY_STATUS_FONT								font_16x26
#define DISPLAY_STATUS_LENGTH							15
#define DISPLAY_STATUS_X_POS							0
#define DISPLAY_STATUS_Y_POS							(ST7789_DISPLAY_HEIGHT - DISPLAY_STATUS_FONT.height)	// 214

#define DISPLAY_FRAME_STROKE							4
#define DISPLAY_FRAME_V_L_X_POS							0
#define DISPLAY_FRAME_V_R_X_POS							(ST7789_DISPLAY_WIDTH - DISPLAY_FRAME_STROKE)			// 236
#define DISPLAY_FRAME_V_Y_POS							(DISPLAY_SECTION_HEADER_FONT.height)					// 26
#define DISPLAY_FRAME_V_HEIGHT							(ST7789_DISPLAY_HEIGHT - DISPLAY_SECTION_HEADER_FONT.height - DISPLAY_STATUS_FONT.height - DISPLAY_FRAME_STROKE)	// 184
#define DISPLAY_FRAME_H_X_POS							0
#define DISPLAY_FRAME_H_Y_POS							(DISPLAY_STATUS_Y_POS - DISPLAY_FRAME_STROKE)			// 210

#define DISPLAY_VERSION_FONT							font_11x18
#define DISPLAY_SHOW_LOGO

#define DISPLAY_FRAMES_PER_LOGO_BUFFER					(LOGO_IMAGE_WIDTH * LOGO_SQUARE_SIDE)
#define DISPLAY_BUFFERS_PER_LOGO						(LOGO_IMAGE_HEIGHT / LOGO_SQUARE_SIDE)

#define COLOR_UNKNOWN									0xC618
#define COLOR_OK										0x06C6
#define COLOR_WARNING									0xFE80
#define COLOR_ALARM										0xF800
#define COLOR_ACTION									0x7D7F
#define COLOR_PROGRESS									0x001F
#define COLOR_BACKGROUND								0x0000
#define COLOR_VERSION									0xFFFF
#define COLOR_SENSORS									0xFFFA
#define COLOR_FANS										0xD7FF
#define COLOR_SYSTEM									0xFE9F
#define COLOR_MENU										0x6FFA

#define DISPLAY_DEFAULT_BRIGHTNESS						128		// Medium brightness

#else

#define STATUS_LENGTH									24

#endif


// A structure for parsing the DMA output of the ADC
typedef struct
{
	uint16_t fan6Voltage;
	uint16_t fan5Voltage;
	uint16_t fan4Voltage;
	uint16_t fan3Voltage;
	uint16_t hPwrVoltage;					// Fan power supply circuit voltage ADC
	uint16_t lPwrVoltage;					// Digital power supply circuit voltage ADC
	uint16_t mcuTemperature;
	uint16_t vRefVoltage;
} ADC_Data;


// System behavior config
typedef struct __attribute__((packed, aligned(4)))
{
	uint32_t buzzerAlarmsMask;				// A list of events that a buzzer should respond to.
	uint8_t displayBrightness;				// The brightness level of the display backlight 0-255 (PWM duty cycle).
	uint8_t displayRotation;				// Display rotation 0-90-180-270.
	uint8_t configFlags;					// Buzzer and other appearance flags
} Behavior_Config;				// 4 + 1 + 1 + 1 + 1 (padding byte) = 8 bytes


// Current status of the controller
typedef struct __attribute__((packed, aligned(4)))
{
	uint32_t status;						// Status bits
	uint32_t configurationChangeFlags;		// HW/Fan/Sensor scanning and Configuration updating flags
	uint32_t freeRAM;						// Available RAM size
	Behavior_Config behaviorConfig;			// Display/Buzzer and system behavior configuration
	uint16_t highLoadPowerBusVoltage;		// Fan power supply circuit voltage
	uint16_t digitalPowerBusVoltage;		// Digital power supply circuit voltage
	uint16_t mcuPowerBusVoltage;			// MCU Vdd voltage
	uint16_t userActionRequest;				// Requested User Action
	uint8_t userActionFlags;				// Flags for correct processing of user actions
	uint8_t scanProgress;					// Scan progress as a percentage
	uint8_t lastHostRequestErrorCode;		// Last USB-communication request result
} Sys_Info;			// 4 + 4 + 4 + 8 + 2 + 2 + 2 + 2 + 1 + 1 + 1 + 1 (padding bytes) = 32 bytes


/////////////////////////////////////////////////////////////////////////////////////////
// System Configuration structures and constants
// Configuration flags
typedef enum
{
	Config_Flag_Empty						= 0b00000000,		// Dummy flag
	Config_Flag_Professional_Mode			= Fan_Config_Flag_Professional_Mode,
																// The Pro mode implies the ability to switch between PWM and DC-DC
																// Control Modes in case of problems with one of them, and also prohibits
																// the fan from leaving the FAILED status if it has got it once.
	Config_Flag_Fan_Model_Detection			= Fan_Config_Flag_Fan_Model_Detection,
																// Try to detect the features of the fan (size, power, class).
	Config_Flag_Buzzer_Loud_Mode			= 0b00000100,		// Buzzer option:
																//	0 = cricket mode,
																//	1 = continuous tone (louder).
	Config_Flag_Lock_Substitution_Matrix	= 0b00001000,		// If we want to use a custom load distribution matrix from failed fans
																// to the remaining working ones, then we need to set this flag.
} Config_Flags;


// Firmware options flags
typedef enum
{
	Firmware_Flag_Empty						= 0b00000000,		// Dummy flag
	Firmware_Flag_Display_Enabled			= 0b00000001,		// Display supported by firmware
	Firmware_Flag_UART_Logger_Enabled		= 0b00000010		// UART-Logger supported by firmware
} Firmware_Flags;





// System Configuration
typedef struct __attribute__((packed, aligned(4)))
{
	uint32_t firmwareVersion;				// Firmware version for compatibility check
	Behavior_Config behaviorConfig;			// Display/Buzzer and system behavior configuration
	uint16_t normalHLPBVoltage;				// High Load Power Bus voltage (fan supply line).
	uint8_t firmwareFlags;					// Firmware hardcoded options (display, logging, etc.)
} System_Config;				// 4 + 8 + 2 + 1 + 1 (padding byte) = 16 bytes


// Configuration completeness Marker
typedef uint32_t __attribute__((aligned(4))) OK_Marker;			// Checking the completeness of saving to ROM.
																// If equal to 0xFFFFFFFF, then the configuration (block)
																// isn't completely saved. Also used as a SN of the config page.
																// 4 bytes
// Fan Substitution Matrix
typedef struct __attribute__((aligned(4)))
{
	uint8_t data[FAN_SUBSTITUTION_MATRIX_SIZE];
} Fan_Substitution_Matrix;


// System Configuration block
typedef struct __attribute__((packed, aligned(4)))
{
	System_Config config;					// System Configuration (aligned(4))
	OK_Marker okMarker;						// System Configuration completeness Marker (aligned(4))
} System_Config_Block;			// 16 + 4 = 20 bytes


// Fan Configuration block
typedef struct __attribute__((packed, aligned(4)))
{
	Fan_Config config;						// Fan configuration
	OK_Marker okMarker;						// Block completeness Marker (aligned(4))
} Fan_Config_Block;				// 48 + 4 = 52 bytes


// Fan Substitution Matrix block
typedef struct __attribute__((packed, aligned(4)))
{
	Fan_Substitution_Matrix matrix;			// Load distribution matrix from failed fans to the remaining ones
	OK_Marker okMarker;						// Matrix completeness Marker (aligned(4))
} Substitution_Matrix_Block;	// 15 + 1 (padding byte) + 4 = 20 bytes


// Sensor Configuration block
typedef struct __attribute__((packed, aligned(4)))
{
	TSensor_Config config;					// Sensor configuration
	OK_Marker __attribute__((packed, aligned(4))) okMarker;	// Block completeness Marker (aligned(4))
} Sensor_Config_Block;			// 44 + 4 = 48 bytes


// The structure of the Flash configuration page (2048 bytes max)
typedef struct __attribute__((packed, aligned(FLASH_PAGE_SIZE)))
{
	OK_Marker configIndex;								// Serial number of the config page (aligned(4))
	System_Config_Block systemConfig;					// System Configuration (aligned(4))
	Fan_Config_Block fanConfigs[FANS_COUNT];			// Fan config array (aligned(4))
	Substitution_Matrix_Block substitutionMatrix;		// Load distribution matrix (aligned(4))
	Sensor_Config_Block sensorConfigs[MAX_SENSORS_COUNT];	// Sensor config array (first element) (aligned(4))

	// Completeness marker indicates the successful saving of the entire configuration.
	OK_Marker okMarker;									// Completeness Marker (aligned(4))

	uint8_t reserved[SETTINGS_PAGE_RESERVED_BYTES];		// Remaining bytes of the Flash page
} Settings_Page;

static_assert(sizeof(Settings_Page) == FLASH_PAGE_SIZE, "Settings_Page must occupy exactly one Flash page");


/////////////////////////////////////////////////////////////////////////////////////////
// Additional status and display structures
#if defined(DISPLAY_ENABLED)

// The structure of predefined statuses (display enabled)
typedef struct
{
	uint32_t status;									// Current system status
	uint16_t frameColor;								// Display window color
	char statusString[DISPLAY_STATUS_LENGTH + 1];		// Displayed status string
	uint8_t beeps;										// The number of buzzer beeps corresponding to the status
} System_Status_Desc;


// The structure of predefined main information sections
typedef struct
{
	uint16_t frameColor;								// Window color
	char header[DISPLAY_SECTION_HEADER_LENGTH + 1];		// Header text
} Display_Main_Section;


// The structure of predefined menu sections
typedef struct
{
	uint16_t frameColor;								// Window color
	char header[DISPLAY_MENU_HEADER_LENGTH + 1];		// Header text
} Display_Menu_Section;

#else

// The structure of predefined statuses (display disabled)
typedef struct
{
	uint32_t status;									// Current system status
	char statusString[STATUS_LENGTH + 1];				// Status string for output to the UART log
	uint8_t beeps;										// The number of buzzer beeps corresponding to the status
} System_Status_Desc;

#endif


// Possible system statuses (there may be several at the same time),
// sorted by level of danger/importance.
typedef enum
{
	Status_Unknown							= 0b00000000000000000000000000000000,	// Dummy status
	Status_Normal							= 0b00000000000000000000000000000001,	// All OK
	Status_Warning_Bad_Sensor				= 0b00000000000000000000000000000100,	// Sensor configuration error
	Status_Warning_Fan_RPM					= 0b00000000000000000000000000010000,	// Low Fan RPM
	Status_Warning_Fan_Power				= 0b00000000000000000000000001000000,	// Low Fan Power
	Status_Warning_Temperature				= 0b00000000000000000000000100000000,	// Attention temperature level
	Status_Alarm_Bad_Sensor					= 0b00000000000000000010000000000000,	// Sensor connection error
	Status_Alarm_Fan_RPM					= 0b00000000000000001000000000000000,	// Critical Fan RPM
	Status_Alarm_Fan_Power					= 0b00000000000000100000000000000000,	// Critical Fan power supply
	Status_Alarm_POTs						= 0b00000000000010000000000000000000,	// POTs communication error
	Status_Alarm_MAX						= 0b00000000001000000000000000000000,	// MAX31790 communication error
	Status_Alarm_Fan_Failed					= 0b00000000100000000000000000000000,	// Failed Fan
	Status_Alarm_Low_RAM					= 0b00000010000000000000000000000000,	// Insufficient free RAM
	Status_Alarm_Power						= 0b00001000000000000000000000000000,	// Problems with the power supply of fans
	Status_Alarm_Temperature				= 0b00100000000000000000000000000000,	// Critical temperature
	Status_Alarm_General_Failure			= 0b10000000000000000000000000000000,	// Fatal system failure (usually due to configuration saving errors)
	Status_Category_Warnings				= 0b00000000000000000000011111111110,	// Warnings mask
	Status_Category_Alarms					= 0b11111111111111111111100000000000,	// Errors mask
	Status_Category_Not_OK					= Status_Category_Warnings | Status_Category_Alarms		// Alarms and Warnings mask
} System_Status;


// Buzzer modes
typedef enum
{
	Buzzer_Off_Mode							= 0x00,									// Silent mode
	Buzzer_Alarms_Only_Mode					= Status_Category_Alarms,				// Alarms only
	Buzzer_Noisy_Mode						= Status_Category_Not_OK				// Alarms and Warnings
} Buzzer_Mode;


// Function button states (quite obvious)
typedef enum
{
	Button_State_Released					= 0x01,
	Button_State_Pressed					= 0x02,
	Button_State_Waiting					= 0x03
} Button_State;


#if defined(DISPLAY_ENABLED)

// Display flags
typedef enum
{
	Display_Flag_Empty						= 0b00000000,		// Dummy value
	Display_Flag_Status_Displayed			= 0b00000001,		// The current system status has been displayed
	Display_Flag_Redraw_Section				= 0b00000010,		// The current section needs to be redrawn
	Display_Flag_Section_Complete			= 0b00000100,		// The section has been completely redrawn
	Display_Flag_Active_Sensors_Available	= 0b00001000,		// There are active sensors and must be displayed
	Display_Flag_Inactive_Sensors_Available	= 0b00010000,		// There are inactive sensors and must be displayed
	Display_Flag_Active_Sensors_Processing	= 0b00100000,		// Active sensors are currently displayed
	Display_Flag_Active_Sensors_Done		= 0b01000000,		// The list of active sensors is displayed
	Display_Flag_Window_Needs_ToBe_Cleared	= 0b10000000		// Need to clear the working part of the current section
} Display_Flags;

#endif


/////////////////////////////////////////////////////////////////////////////////////////
// Communication structures and constants
// Communication protocol packet header
typedef struct __attribute__((packed))
{
	uint8_t reportID;											// USB HID Report ID. Not included in the descriptor size.
	uint8_t code;												// Command code
	uint32_t requestID;											// Serial number of the request
} Communication_Packet_Header;		// 1 + 1 + 4 = 6 bytes


// Communication protocol packet structure
typedef struct __attribute__((packed))
{
	Communication_Packet_Header header;							// Header (see above)
	uint8_t data[64 - sizeof(Communication_Packet_Header)];		// Max Report DATA size: 64 - 1 - 1 - 4 = 58
} Communication_Packet;				// 6 + 58 = 64 bytes


// USB data exchange monitoring states
typedef enum
{
	Host_Link_Idle							= 0,				// The host service hasn't been detected yet (initial state)
	Host_Link_Active						= 1,				// The host service polls the device regularly
	Host_Link_Endpoints_Reset				= 2,				// Recovery stage 1 is done, waiting for the host requests
	Host_Link_Reconnecting					= 3					// Recovery stage 2: the USB is stopped, waiting before restart
} Host_Link_States;


// USB HID Reports
typedef enum
{
	USB_HID_Report_System_State				= 1,				// System State Report
	USB_HID_Report_System_Config			= 2,				// System Configuration Report
	USB_HID_Report_Fan_Count				= 3,				// Fan Count Report
	USB_HID_Report_Fan_State				= 4,				// Fan State Report
	USB_HID_Report_Fan_Config				= 5,				// Fan Configuration Report
	USB_HID_Report_Fan_Substitution_Matrix	= 6,				// Fan Substitution Matrix Report
	USB_HID_Report_Sensor_Count				= 7,				// Sensor Count Report
	USB_HID_Report_Sensor_State				= 8,				// Sensor State Report
	USB_HID_Report_Sensor_Config			= 9,				// Sensor Configuration Report
	USB_HID_Report_Operation_Result			= 16,				// The result of executing a command from the host

	USB_HID_Report_Host_Command				= 128				// A report with a command from host
} USB_HID_Reports;

// The report sizes declared in the HID report descriptor (see usb_reports.h)
// must match the sizes of the transferred structures.
static_assert(sizeof(Communication_Packet) == USBD_CUSTOMHID_OUTREPORT_BUF_SIZE, "Packet size must match the OUT report buffer size");
static_assert(sizeof(Communication_Packet) == USB_REPORT_PACKET_SIZE, "Packet size must match the USB report packet size");
static_assert(sizeof(Communication_Packet_Header) == USB_REPORT_HEADER_SIZE, "Update USB_REPORT_HEADER_SIZE");
static_assert(sizeof(Communication_Packet::data) == USB_REPORT_MAX_PAYLOAD_SIZE, "Update USB_REPORT_MAX_PAYLOAD_SIZE");
static_assert(sizeof(Sys_Info) == USB_REPORT_SYSTEM_STATE_SIZE, "Update USB_REPORT_SYSTEM_STATE_SIZE");
static_assert(sizeof(System_Config) == USB_REPORT_SYSTEM_CONFIG_SIZE, "Update USB_REPORT_SYSTEM_CONFIG_SIZE");
static_assert(sizeof(Fan_Info) == USB_REPORT_FAN_STATE_SIZE, "Update USB_REPORT_FAN_STATE_SIZE");
static_assert(sizeof(Fan_Config) == USB_REPORT_FAN_CONFIG_SIZE, "Update USB_REPORT_FAN_CONFIG_SIZE");
static_assert(sizeof(Fan_Substitution_Matrix) == USB_REPORT_SUBSTITUTION_MATRIX_SIZE, "Update USB_REPORT_SUBSTITUTION_MATRIX_SIZE");
static_assert(sizeof(TSensor_Info) == USB_REPORT_SENSOR_STATE_SIZE, "Update USB_REPORT_SENSOR_STATE_SIZE");
static_assert(sizeof(TSensor_Config) == USB_REPORT_SENSOR_CONFIG_SIZE, "Update USB_REPORT_SENSOR_CONFIG_SIZE");


// Commands from the host
typedef enum
{
	Command_Get_System_State				= 1,				// Get System State (Report ID 1)
	Command_Get_System_Config				= 2,				// Get System Configuration (Report ID 2)
	Command_Get_Fan_Count					= 3,				// Get Fan Count (Report ID 3)
	Command_Get_Fan_N_State					= 4,				// Get Fan # State (Report ID 4)
	Command_Get_Fan_N_Config				= 5,				// Get Fan # Configuration (Report ID 5)
	Command_Get_Fan_Substitution_Matrix		= 6,				// Get Fan Substitution Matrix (Report ID 6)
	Command_Get_Sensor_Count				= 7,				// Get Sensor Count (Report ID 7)
	Command_Get_Sensor_N_State				= 8,				// Get Sensor # State (Report ID 8)
	Command_Get_Sensor_SN_State				= 9,				// Get Sensor ######## State  (Report ID 8)
	Command_Get_Sensor_N_Config				= 10,				// Get Sensor # Configuration (Report ID 9)
	Command_Get_Sensor_SN_Config			= 11,				// Get Sensor ######## Configuration (Report ID 9)

	Command_Update_Sensor_SN_Temperature	= 16,				// Set Software Sensor ######## Temperature

	Command_Reload_Config					= 20,				// Reload active Configuration
	Command_Set_Behavior_Config				= 21,				// Set some options, display and buzzer settings without saving them to the system configuration

	Command_Set_Fan_N_Control_Value			= 24,				// Set Fan # Control Value
	Command_Set_Fan_N_Control_Basis			= 25,				// Set Fan # Control Basis
	Command_Set_Fan_N_Control_Mode			= 26,				// Set Fan # Control Mode
	Command_Set_Fan_N_Operation_Mode		= 27,				// Set Fan # Operation Mode
	Command_Reset_Fan_N						= 28,				// Reset Fan #
	Command_Reset_Sensor_SN					= 29,				// Reset Sensor ########

	Command_Rescan_Fan_Hardware				= 32,				// Rescan Fan # Hardware (#6 = rescan all hardware)
	Command_Rescan_Fan_Model				= 33,				// Rescan Fan # Model (#6 = rescan all fans)
	Command_Rescan_Sensors					= 34,				// Rescan Sensors

	Command_Add_Sensor						= 40,				// Add Software Sensor (appended to the end of the list)
	Command_Remove_Sensor_SN				= 41,				// Remove ######## Sensor

	Command_Set_System_Config				= 64,				// Set System Configuration (configuration page will be completed)
	Command_Set_Fan_N_Config				= 65,				// Set Fan # Configuration
	Command_Set_Fan_Substitution_Matrix		= 66,				// Set Fan Substitution Matrix
	Command_Set_Sensor_Config				= 67,				// Set Sensor ######## Configuration (SN is contained in the configuration itself)

	Command_Start_Clean_Config_Page			= 96,				// Start New Configuration Page
	Command_Finalize_Config_Page			= 97,				// Write Config OK Marker, 'close' Configuration Page and activate it

	Command_Factory_Reset					= 128,				// Erase all configuration pages and reboot
	Command_Reboot							= 129,				// Reboot MCU
	Command_Prepare_To_Host_Shutdown		= 130				// Prepare controller to Host shutdown (reboot or power off)
} Communication_Commands;


// Communication Protocol Operation Results
typedef enum
{
	Response_OK								= 0,				// Request accepted/executed successfully
	Response_NACK							= 1,				// Command Not ACKnowledged
	Response_Busy							= 2,				// System is busy
	Response_Request_Scheduled				= 3,				// The request will be executed in the main thread (in this case, the main loop).
	Response_Request_Processing				= 4,				// The request has been previously received and is being processed (User Action).
	Response_Request_Done					= 5,				// User Action successfully done
	Response_Request_Error					= 6,				// Error processing User Action
	Response_Request_Unknown_Result			= 7,				// The request was processed, but the result of execution is unknown.

	Response_System_Not_Configured			= 16,				// No System Configuration available

	Response_Fan_Not_Found					= 20,				// There is no fan with this index in the System
	Response_Fan_Not_Configured				= 21,				// No Fan Configuration available

	Response_Sensor_Not_Found				= 30,				// There is no sensor with this SN in the System
	Response_Sensor_Not_Configured			= 31,				// No Sensor Configuration available
	Response_Sensor_Already_Exists_Error	= 32,				// The sensor being added already exists

	Response_Configuration_Update_Error		= 50,				// Failed to update configuration (for future use)
	Response_Flash_Page_Already_Started		= 51,				// The previously started Flash page must be closed before starting a new one
	Response_Flash_No_Page_Started			= 52				// The new configuration page in Flash has not been erased
} Operation_Results;


// Flags of configuration intervention states
typedef enum
{
	CC_Flag_Empty							= 0b00000000000000000000000000000000,	// Dummy value
	CC_Flag_No_Config						= 0b00000000000000000000000000000001,	// No configuration found
	CC_Flag_HW_Failed						= 0b00000000000000000000000000000010,	// Hardware scanning failure
	CC_Flag_Prewarm_Failed					= 0b00000000000000000000000000000100,	// Fan pre-warm failure
	CC_Flag_Fan_Failed						= 0b00000000000000000000000000001000,	// Fan scan failure
	CC_Flag_Sensor_Failed					= 0b00000000000000000000000000010000,	// Sensor scan failure
	CC_Flag_Too_Many_Sensors_Detected		= 0b00000000000000000000000000100000,	// More sensors were detected than could be stored in memory
	CC_Flag_Too_Many_Sensors_Stored			= 0b00000000000000000000000001000000,	// Saved sensors do not fit in RAM
	CC_Flag_Sensor_Not_Found				= 0b00000000000000000000000010000000,	// The target sensor for the operation wasn't found
	CC_Flag_Sensor_Config_Error				= 0b00000000000000000000000100000000,	// Sensor configuration is not applicable
	CC_Flag_Fan_Config_Error				= 0b00000000000000000000001000000000,	// Fan configuration is not applicable
	CC_Flag_System_Config_Error				= 0b00000000000000000000010000000000,	// System configuration is not applicable
	CC_Flag_EEPROM_Failure					= 0b00000000000000000000100000000000,	// Writing to the EEPROM failure
	CC_Flag_All_Fan_Channels_Involved		= 0b00001000000000000000000000000000,	// Scanning all Fan channels
	CC_Flag_Initialization_Complete			= 0b00010000000000000000000000000000,	// Controller initialization completion flag
	CC_Flag_HW_In_Progress					= 0b00100000000000000000000000000000,	// Hardware scanning in progress
	CC_Flag_Fan_In_Progress					= 0b01000000000000000000000000000000,	// Fan scanning in progress
	CC_Flag_Sensor_In_Progress				= 0b10000000000000000000000000000000,	// Sensor scanning in progress
	CC_Flag_Progress_Mask					= 0b11100000000000000000000000000000	// Any scanning in progress mask
} Configuration_Change_Flags;


// User action requests
typedef enum
{
	UA_Request_Empty						= 0,				// Dummy value

	UA_Request_Set_Fan_Control_Value		= 8,				// Set new Control Value for the Fan
	UA_Request_Set_Fan_Control_Basis		= 9,				// Set new Control Basis for the Fan
	UA_Request_Set_Fan_Control_Mode			= 10,				// Set Fan's Control Mode
	UA_Request_Set_Fan_Operation_Mode		= 11,				// Set Fan's Operation Mode
	UA_Request_Reset_Fan					= 12,				// Reset Fan
	UA_Request_Reset_Sensor					= 13,				// Reset Sensor

	UA_Request_Set_Behavior_Config			= 32,				// Set Behavior Config without saving it to the system Configuration

	UA_Request_System_Config_Update			= 64,				// Replace System Configuration with the specified one
	UA_Request_Fan_Config_Update			= 65,				// Replace Fan Configuration with the specified one
	UA_Request_Substitution_Matrix_Update	= 66,				// Replace Fan Substitution Matrix with the specified one
	UA_Request_Sensor_Config_Update			= 67,				// Replace Sensor Configuration with the specified one
	UA_Request_Sensor_Add					= 68,				// Add Software Sensor
	UA_Request_Sensor_Remove				= 69,				// Delete Sensor (except the built-in ones)

	UA_Request_Start_Clean_Config_Page		= 128,				// Start New Configuration Page
	UA_Request_Finalize_Config_Page			= 129,				// Write Config OK Marker, 'close' Configuration Page and activate it
	UA_Request_Config_Reload				= 130,				// Reload Configuration

	UA_Request_Factory_Reset				= 192,				// Erase all configuration pages and reboot
	UA_Request_Reset						= 193,				// Reset the device

	UA_Request_Hardware_Scan				= 256,				// Scan Hardware		- 0b0000000100000000
	UA_Request_Fan_Scan						= 512,				// Scan Fan(s)			- 0b0000001000000000
	UA_Request_Sensor_Scan					= 1024,				// Scan Sensors			- 0b0000010000000000
	UA_Request_Scan_Mask					= 1792,				// Scan request mask	- 0b0000011100000000

	UA_Request_Initiated_By_Menu			= 32768				// The action was initiated through the menu
																// and the controller's function buttons.
																// 						- 0b1000000000000000
} User_Action_Requests;


// User Action flags
typedef enum
{
	UA_Flag_Empty							= 0b00000000,		// Dummy value
	UA_Flag_Last_Config_Request_Done		= 0b00000001,		// Last request successfully done
	UA_Flag_Last_Config_Request_Error		= 0b00000010,		// Last request failed
	UA_Flag_Config_Page_Started				= 0b00000100,		// Clean Configuration Flash page started

	UA_Flag_System_Config_Changed			= 0b00010000,		// System Configuration was changed
	UA_Flag_Fan_Config_Changed				= 0b00100000,		// Fan Configuration was changed
	UA_Flag_Substitution_Matrix_Changed		= 0b01000000,		// Fan Substitution Matrix changed
	UA_Flag_Sensor_Config_Changed			= 0b10000000,		// Sensor Configuration was changed
	UA_Flag_Config_Changes_Mask				= 0b11110000		// Something has changed mask
} User_Action_Flags;


// Accepted scan request
typedef enum
{
	SA_Flag_Empty							= 0b00000000,		// Dummy value
	SA_Flag_Hardware_Scan_Request			= 0b00000001,		// Scan Hardware
	SA_Flag_Fan_Scan_Request				= 0b00000010,		// Scan Fan
	SA_Flag_Sensor_Scan_Request				= 0b00000100		// Scan Sensor
} Scan_Action_Flags;


/* USER CODE END PTD */

/* Private define ------------------------------------------------------------*/
/* USER CODE BEGIN PD */

// Call some EEPROM writing procedure and check the result
#define USE_EEPROM_AND_CHECK(f, o)			{ HAL_StatusTypeDef status = (f); if (status != HAL_OK) { o |= CC_Flag_EEPROM_Failure; return (o); } }
// Call procedure and check the result
#define CALL_AND_CHECK(f)					{ HAL_StatusTypeDef status = (f); if (status != HAL_OK) return (status); }


/* USER CODE END PD */

/* Private macro -------------------------------------------------------------*/
/* USER CODE BEGIN PM */

/* USER CODE END PM */

/* Private variables ---------------------------------------------------------*/

/* USER CODE BEGIN PV */

#if defined(DISPLAY_ENABLED)

// Definition of system statuses (display colors, and status strings
// and the number of beeps) for the display version.
const System_Status_Desc systemStatuses[STATUSES_COUNT] = {
		{Status_Unknown,				COLOR_UNKNOWN,	" UNKNOWN       ", 0},
		{Status_Normal,					COLOR_OK,		" OK            ", 0},
		{Status_Warning_Bad_Sensor,		COLOR_WARNING,	" WARNING SENS  ", 1},
		{Status_Warning_Fan_RPM,		COLOR_WARNING,	" WARNING RPM   ", 1},
		{Status_Warning_Fan_Power,		COLOR_WARNING,	" WARN FAN PWR  ", 1},
		{Status_Warning_Temperature,	COLOR_WARNING,	" WARNING TEMP  ", 1},
		{Status_Alarm_Bad_Sensor,		COLOR_ALARM,	" ALARM SENSOR  ", 2},
		{Status_Alarm_Fan_RPM,			COLOR_ALARM,	" ALARM RPM     ", 2},
		{Status_Alarm_Fan_Power,		COLOR_ALARM,	" ALARM FAN PWR ", 2},
		{Status_Alarm_POTs,				COLOR_ALARM,	" ALARM POT(s)  ", 3},
		{Status_Alarm_MAX,				COLOR_ALARM,	" ALARM 31790   ", 3},
		{Status_Alarm_Fan_Failed,		COLOR_ALARM,	" FAN FAILED    ", 3},
		{Status_Alarm_Low_RAM,			COLOR_ALARM,	" LOW RAM       ", 4},
		{Status_Alarm_Power,			COLOR_ALARM,	" ALARM HI PWR  ", 4},
		{Status_Alarm_Temperature,		COLOR_ALARM,	" ALARM TEMP    ", 5},
		{Status_Alarm_General_Failure,	COLOR_ALARM,	" FATAL FAILURE ", 10}
};


#else

// Definition of system statuses (UART logger strings
// and the number of beeps) for the version w/o display.
const System_Status_Desc systemStatuses[STATUSES_COUNT] = {
		{Status_Unknown,				"UNKNOWN                 ", 0},
		{Status_Normal,					"OK                      ", 0},
		{Status_Warning_Bad_Sensor,		"WARNING SENSOR OPERATION", 1},
		{Status_Warning_Fan_RPM,		"WARNING FAN RPM         ", 1},
		{Status_Warning_Fan_Power,		"WARNING FAN POWER       ", 1},
		{Status_Warning_Temperature,	"WARNING TEMPERATURE     ", 1},
		{Status_Alarm_Bad_Sensor,		"ALARM SENSOR OPERATION  ", 2},
		{Status_Alarm_Fan_RPM,			"ALARM FAN RPM           ", 2},
		{Status_Alarm_Fan_Power,		"ALARM FAN POWER         ", 2},
		{Status_Alarm_POTs,				"ALARM POT(s) OPERATION  ", 3},
		{Status_Alarm_MAX,				"ALARM MAX31790 OPERATION", 3},
		{Status_Alarm_Fan_Failed,		"ALARM FAN FAILED        ", 3},
		{Status_Alarm_Low_RAM,			"ALARM LOW RAM           ", 4},
		{Status_Alarm_Power,			"ALARM FAN PS LOW VOLTAGE", 4},
		{Status_Alarm_Temperature,		"ALARM TEMPERATURE       ", 5},
		{Status_Alarm_General_Failure,	"FATAL SYSTEM FAILURE    ", 10}
};

#endif

#if defined(DISPLAY_ENABLED)

// Parameters of the main information sections
const Display_Main_Section displaySections[DISPLAY_INFO_SECTIONS_COUNT] = {
		// Main Info sections
		{COLOR_SENSORS,		" Sensor   `C   "},
		{COLOR_FANS,		"  Fan   Contrl "},
		{COLOR_FANS,		"  Fan    RPM   "},
		{COLOR_FANS,		"  Fan   Pwr(v) "},
		{COLOR_SYSTEM,		" System        "},
		// Scan sections
		{COLOR_ALARM,		" Scan Sensors  "},
		{COLOR_ALARM,		" Scan Fans     "},
		{COLOR_ALARM,		" Scan Hardware "},
		{COLOR_ACTION,		" Host Request  "}
};


// Parameters of the menu sections
const Display_Menu_Section menuDisplaySections[DISPLAY_MENU_SECTIONS_COUNT] = {
		{COLOR_MENU,	"     Main  Menu     "},
		{COLOR_ACTION,	"   Alarm  Options   "},
		{COLOR_ACTION,	"  Rescan Sensors ?  "},
		{COLOR_ACTION,	"    Rescan Fans ?   "},
		{COLOR_ALARM,	"  Rescan Hardware ? "}
};


// System state parameters
const char systemParameters[SECTION_SYSTEM_PARAMETERS_COUNT][DISPLAY_PARAMETER_LENGTH + 1] = {
		"High Pwr",
		"Low Pwr",
		"MCU Vdd",
		"RAM (KB)",
		"POTs",
		"M31790"
};


// Main menu items
const char mainMenuItems[DISPLAY_MAIN_MENU_ITEMS_COUNT][DISPLAY_MENU_ITEM_LENGTH + 1] = {
		"Buzzer options",
		"Rotate display",
		"Scan sensors  ",
		"Scan fans     ",
		"Scan hardware ",
		"Exit          "
};


// Buzzer menu items
const char muteMenuItems[BUZZER_OPTIONS_COUNT][DISPLAY_MENU_ITEM_LENGTH + 1] = {
		"Buzzer -    ON",
		"Buzzer - ALRMS",
		"Buzzer -   OFF"
};


// Confirm menu items
const char confirmMenuItems[DISPLAY_CONFIRM_MENU_ITEMS_COUNT][DISPLAY_MENU_ITEM_LENGTH + 1] = {
		"YES           ",
		"NO            "
};


// Available buzzer options
const uint32_t buzzerOptions[BUZZER_OPTIONS_COUNT] = {Buzzer_Noisy_Mode, Buzzer_Alarms_Only_Mode, Buzzer_Off_Mode};

// Additional display strings
const char unknownString[DISPLAY_VALUE_LENGTH + 1] = "   ?   ";
const char inactiveString[DISPLAY_VALUE_LENGTH + 1] = "   -   ";
const char okString[DISPLAY_VALUE_LENGTH + 1] =      "  OK!  ";
const char failureString[DISPLAY_VALUE_LENGTH + 1] = "FAILURE";
const char preWarmString[DISPLAY_VALUE_LENGTH + 1] = "priming";
const char warmingString[DISPLAY_VALUE_LENGTH + 1] = "ramping";
const char fanPinsString[DISPLAY_VALUE_LENGTH + 1] = "   Pin ";
const char doneMessage[] = "Done!";
const char errorMessage[] = "ERROR:";
const char noConfigMessage[] = "No config";
const char loadingMessage[] = "Activation...";
const char noSensorMessage[] = "No sensor";
const char busyMessage[] = "Busy";
const char versionMessage[] = FIRMWARE_PRODUCT_NAME_STRING " v" FIRMWARE_VERSION_STRING;

#endif


// Allowed IRQs for OneWire bus counting.
// Interrupts for processing USB HID reports from the host
// will be the only ones active during non-critical phases
// of OneWire communication. During critical moments,
// even those will be disabled.
const IRQn_Type owIrqs[] = { USB_IRQn };

// Default completeness marker;
const OK_Marker completenessMarker = SETTINGS_COMPLETENESS_MARKER;

// Configuration pages
const Settings_Page __attribute__((aligned(FLASH_PAGE_SIZE))) configurationPages[SETTINGS_PAGES_COUNT] = { 0x00 };
static_assert(SETTINGS_PAGES_COUNT >= 2, "At least 2 configuration pages are required");

// Active System Configuration pointer
const Settings_Page * activeConfigurationPage = nullptr;

/*
// A variant of forced filling with constants (0xFF) during compilation.
const std::array<std::array<uint8_t, FLASH_PAGE_SIZE>, 2> __attribute__((aligned(FLASH_PAGE_SIZE))) configurationPages = []() {
    std::array<std::array<uint8_t, FLASH_PAGE_SIZE>, 2> arr;
    for (auto& row : arr) {
      row.fill(0xFF);
    }
    return arr;
}();
*/


// Peripherals
// Display
#if defined(DISPLAY_ENABLED)

ST7789 display(&hspi1, LCD_RESET_GPIO_Port, LCD_RESET_Pin, LCD_DC_GPIO_Port, LCD_DC_Pin);

// Runtime Variables
uint8_t displayLastSection = UINT8_MAX;							// Previous section index
uint16_t displayLastFrameColor = COLOR_UNKNOWN;					// Previous section window color
uint8_t displayFlags = Display_Flag_Redraw_Section;				// Runtime display flags
uint8_t displaySection = 0;										// Current section
uint8_t displaySelectedItemIndex = 0;							// Selected menu item (also used for complex information sections)
uint8_t displayLastSelectedItemIndex = 0;						// Previous selected menu item (--//--)
uint8_t sectionCurrentItemIndex = 0;							// Current top displayed item
uint8_t sectionItemsCount = 0;									// Total items in section
uint8_t sectionDisplayDuration = SECTION_DISPLAY_DURATION;		// Current section display duration
uint32_t displaySectionTimer = 0;								// How long has the current section been displayed

#endif


// Parse ADC input data
union {
	ADC_Data data;
	uint16_t array[ADC_CHANNELS_COUNT];
} adcRaw;

// DigiPOTs driver
DS1841 digiPOTs = DS1841(&hi2c1);


// DS18B20 drivers (for each line)
DS18B20 sensorLines[SENSOR_LINES_COUNT] = {
		DS18B20(T_SENS_1_GPIO_Port, T_SENS_1_Pin, &htim3, owIrqs, 1),
		DS18B20(T_SENS_2_GPIO_Port, T_SENS_2_Pin, &htim3, owIrqs, 1)};

// MAX31790 driver
MAX31790 maxDriver = MAX31790(&hi2c1);

// MCU voltmeter and the ADC reference channel at the same time
VMeter_MCU mcuVMeter = VMeter_MCU(&adcRaw.data.vRefVoltage);

// Voltmeters (internal ADC-based)
VMeter loVMeter = VMeter(&adcRaw.data.lPwrVoltage, &mcuVMeter, UPPER_RESISTOR_VALUE, LOWER_RESISTOR_VALUE);
VMeter hiVMeter = VMeter(&adcRaw.data.hPwrVoltage, &mcuVMeter, UPPER_RESISTOR_VALUE, LOWER_RESISTOR_VALUE);
VMeter dcdcVMeters[DCDC_COUNT] = {
		VMeter(&adcRaw.data.fan3Voltage, &mcuVMeter, UPPER_RESISTOR_VALUE, LOWER_RESISTOR_VALUE),
		VMeter(&adcRaw.data.fan4Voltage, &mcuVMeter, UPPER_RESISTOR_VALUE, LOWER_RESISTOR_VALUE),
		VMeter(&adcRaw.data.fan5Voltage, &mcuVMeter, UPPER_RESISTOR_VALUE, LOWER_RESISTOR_VALUE),
		VMeter(&adcRaw.data.fan6Voltage, &mcuVMeter, UPPER_RESISTOR_VALUE, LOWER_RESISTOR_VALUE)
	};

// Fans
Fan_MAX31790_DS1841 fans[FANS_COUNT] = {
		Fan_MAX31790_DS1841(&maxDriver, 0, nullptr, 0, &hiVMeter, nullptr, 0),
		Fan_MAX31790_DS1841(&maxDriver, 1, nullptr, 0, &hiVMeter, nullptr, 0),
		Fan_MAX31790_DS1841(&maxDriver, 2, &digiPOTs, 0, &dcdcVMeters[0], EN_BUCK_1_GPIO_Port, EN_BUCK_1_Pin),
		Fan_MAX31790_DS1841(&maxDriver, 3, &digiPOTs, 1, &dcdcVMeters[1], EN_BUCK_2_GPIO_Port, EN_BUCK_2_Pin),
		Fan_MAX31790_DS1841(&maxDriver, 4, &digiPOTs, 2, &dcdcVMeters[2], EN_BUCK_3_GPIO_Port, EN_BUCK_3_Pin),
		Fan_MAX31790_DS1841(&maxDriver, 5, &digiPOTs, 3, &dcdcVMeters[3], EN_BUCK_4_GPIO_Port, EN_BUCK_4_Pin)
	};

// Internal TSensors
TSensor_MCU mcuTSensor = TSensor_MCU(&adcRaw.data.mcuTemperature, &mcuVMeter);
TSensor_DS1841 dcdcTSensors[DCDC_COUNT] = {
		TSensor_DS1841(&digiPOTs, 0),
		TSensor_DS1841(&digiPOTs, 1),
		TSensor_DS1841(&digiPOTs, 2),
		TSensor_DS1841(&digiPOTs, 3)
	};


// Runtime Variables

uint8_t txBuffer[64];
volatile bool hostRequestPending = false;
volatile uint8_t hostLinkState = Host_Link_Idle;				// USB data exchange monitoring state (see Host_Link_States)
volatile uint32_t hostLinkTick = 0;								// The last System State request from the host or the recovery stage start
volatile Sys_Info systemState = { 0 };							// Current System state
volatile uint32_t lastAcceptedRequestID = 0;					// The ID of the last request accepted for processing as a User Action.
volatile uint8_t selectedObjectIndex = FANS_COUNT;					// Selected Fan/Sensor for configuration change or scan request
volatile uint8_t buttonPressCount = 0;							// Function button clicks count
volatile uint8_t buttonStatus = Button_State_Released;			// Function button status
volatile uint8_t beepsCount = 0;								// The number of remaining buzzer beeps multiplied by 2
volatile uint32_t buzzerTick = 0;								// Last buzzer beeps timestamp
volatile bool longOpProcessing = false;							// Flag of long operation (initialization, hardware/fan scanning, etc.)
uint32_t lastButtonTick = 0;									// Last button click timestamp
uint32_t lastUpdateTick = 0;									// Last System update timestamp
uint32_t configPageSessionTick = 0;								// The last activity of the host configuration session


// Host request processing lock
PendSvLock pendSvLock;

// A dynamic list of sensors. The objects are placed on the heap,
// so to avoid memory fragmentation, I will recreate the entire list
// every time the contents of the list change.
DynamicList tSensors = DynamicList(pendSvLock);
ListCursor isrSensorSearchContext;

// Communication vars
// The USB handler
extern USBD_HandleTypeDef hUsbDeviceFS;

// A buffer for storing configuration change requests data.
// I have some difficulty determining which variable should be volatile
// and which should be non-volatile. I will assume that large amounts
// of memory accessed through pointers (mostly memcpy() or Flash
// read/write operations) cannot be optimized using a register caching.
// The union will be aligned to the strictest alignment requirement
// among all its members.
union SafeBuffer {
	System_Config systemConfig;
	Behavior_Config behaviorConfig;
	Fan_Config fanConfig;
	Fan_Substitution_Matrix substitutionMatrix;
	TSensor_Config sensorConfig;
    //Sys_Info systemInfo;
    //Fan_Info fanInfo;
    //TSensor_Info sensInfo;
	volatile uint8_t byteParameter;
	volatile uint16_t wordParameter;
	volatile uint32_t dwordParameter;
} safeBuffer;

// The address of a blank (erased) Flash memory page
// for a complete configuration update requests
// (rather than its individual segments).
const Settings_Page * newSettingsPage = nullptr;


// Heap top
extern "C" char* sbrk(int incr);


/* USER CODE END PV */

/* Private function prototypes -----------------------------------------------*/
void SystemClock_Config(void);
/* USER CODE BEGIN PFP */

/* USER CODE END PFP */

/* Private user code ---------------------------------------------------------*/
/* USER CODE BEGIN 0 */


// For future needs
// Add "HAL_RCC_EnableCSS();" to SystemClock_Config()
/*
void HAL_RCC_CSSCallback() {
	if (__HAL_RCC_GET_FLAG(RCC_FLAG_HSERDY) != RESET) {
	}
}
*/


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Memory tools

// Get free SRAM size
uint32_t __attribute__((noinline)) getFreeMemory() {
  uint8_t stackVar;
  return (&stackVar - reinterpret_cast<uint8_t*>(sbrk(0)));
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Dynamic list functions

// Get total initialized sensors count.
// To save memory, I will not add static objects
// of the built-in sensors to the dynamic list.
// But in the next version it will be better
// to do it like that.
uint8_t getSensorCount() {
	uint8_t count = 0;

#if defined(USE_DIGIPOTS_TSENSOR)
	count += DCDC_COUNT;
#endif

#if defined(USE_MCU_TSENSOR)
	count++;
#endif

	count += tSensors.length();
	return (count);
}


// Get sensor at specified index
TSensor_base * getSensorAtIndex(uint8_t index, ListCursor * searchContext = nullptr) {
#if defined(USE_DIGIPOTS_TSENSOR)
	if (index < DCDC_COUNT) {
		return (static_cast<TSensor_base*>(&dcdcTSensors[index]));
	} else {
		index -= DCDC_COUNT;
	}
#endif

#if defined(USE_MCU_TSENSOR)
	if (index == 0) {
		return (static_cast<TSensor_base*>(&mcuTSensor));
	} else {
		index--;
	}
#endif

	if (index < tSensors.length()) {
		return (static_cast<TSensor_base*>(tSensors.getObjectAtIndex(index, searchContext)));
	}

	return (nullptr);
}


// Get a sensor object from a dynamic list by its serial number
/*
// Dynamic list only version
TSensor_base * getSensorBySerialNumber(const uint8_t * serialNumber, uint8_t sensorType, ListCursor * searchContext = nullptr) {
	// Here 'tSensors' object couldn't be NULL
	TSensor_base * currentSensor = static_cast<TSensor_base*>(tSensors.getFirst(searchContext));

	while ((currentSensor != nullptr) &&
		   ((currentSensor->configuration == nullptr) ||
		    (memcmp(currentSensor->configuration->serialNumber, serialNumber, TSENSOR_SN_SIZE) != 0) ||
			((sensorType != TSensor_Type_Unknown) && (currentSensor->sensorType != sensorType)))) {
		currentSensor = static_cast<TSensor_base*>(tSensors.getNext(searchContext));
	}

	return (currentSensor);
}
*/

// Get a sensor object from a dynamic list by its serial number
TSensor_base * getSensorBySerialNumber(const uint8_t * serialNumber, uint8_t sensorType, ListCursor * searchContext = nullptr) {
	for (uint8_t i = 0; i < getSensorCount(); i++) {
		TSensor_base * currentSensor = static_cast<TSensor_base*>(getSensorAtIndex(i, searchContext));
		if ((currentSensor->configuration != nullptr) &&
			(memcmp(currentSensor->configuration->serialNumber, serialNumber, TSENSOR_SN_SIZE) == 0) &&
			((sensorType == TSensor_Type_Unknown) || (currentSensor->sensorType == sensorType))) return (currentSensor);
	}
	return (nullptr);
}


// A callback for freeing up the memory of a dynamic object being deleted
void releaseSensorObject(void * object) {
	if (object != nullptr) {
		TSensor_base * sensor = static_cast<TSensor_base*>(object);
		switch (sensor->sensorType) {
			case TSensor_Type_MCU:
				// Here and below we are absolutely sure about the class
				// of the object, so we can safely use static_cast.
				delete (static_cast<TSensor_MCU*>(object));
				break;

			case TSensor_Type_DS1841:
				delete (static_cast<TSensor_DS1841*>(object));
				break;

			case TSensor_Type_DS18B20:
				delete (static_cast<TSensor_DS18B20_ML*>(object));
				break;

			case TSensor_Type_SW:
				delete (static_cast<TSensor_SW*>(object));
				break;

			default:
				// Alternatively, we can use a default destructor in the base
				// class and override it in the derived classes.
				delete(sensor);
		}
	}
}


// Pass the action requested via menu/button to the main thread.
// The request is rejected (Busy) while the host configuration session
// is started or another request is being processed.
bool requestMenuAction(uint16_t request) {
	bool accepted = false;

	__disable_irq();
	if (((systemState.userActionFlags & UA_Flag_Config_Page_Started) == 0) &&
		(systemState.userActionRequest == UA_Request_Empty)) {
		systemState.userActionRequest = request;
		selectedObjectIndex = FANS_COUNT;
		accepted = true;
	}
	__enable_irq();

	return (accepted);
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Display functions

#if defined(DISPLAY_ENABLED)

// Check display rotation
HAL_StatusTypeDef checkDisplayRotation() {
	if (systemState.behaviorConfig.displayRotation != display.currentRotation) {
		HAL_StatusTypeDef result = display.setRotation(systemState.behaviorConfig.displayRotation);
		if (result != HAL_OK) {
			return (result);
		} else {
			if (displaySection != DISPLAY_HOST_REQUEST_SECTION) {
				displayLastSection = UINT8_MAX;
				displayLastFrameColor = COLOR_UNKNOWN;
				displayFlags &= ~Display_Flag_Status_Displayed;
			}
			display.fillColor(COLOR_BACKGROUND);
		}
	}
	return (HAL_OK);
}


// Get predefined color of the section
uint16_t getDisplaySectionColor(uint8_t section) {
	if (section < DISPLAY_INFO_SECTIONS_COUNT) {
		return (displaySections[section].frameColor);
	} else {
		return (menuDisplaySections[section - DISPLAY_INFO_SECTIONS_COUNT].frameColor);
	}
}


// Getting a darker shade for displaying inactive objects.
uint16_t getDarkerColor(uint16_t color) {
	color ^= 0x52AA; // Reduce color components with magic constant
/*
	// Reduce color to 3/4
	uint8_t red = (color & 0b1111100000000000) >> 12;
	uint8_t green = (color & 0b0000011111100000) >> 6;
	uint8_t blue = (color & 0b0000000000011111) > 1;
	red += red >> 1;
	green += green >> 1;
	blue += blue >> 1;
	color = (red << 11) | (green << 5) | blue;
*/
	return (color);
}


// Get the X coordinate for the string
// to position it in the center of the screen.
uint8_t getXPositionForString(const char * string, uint8_t fontWidth) {
	return ((ST7789_DISPLAY_WIDTH - strlen(string) * fontWidth) / 2);
}


// Clear working area of screen
void clearMainWindow() {
	display.fillRect(DISPLAY_FRAME_STROKE, DISPLAY_FRAME_V_Y_POS, ST7789_DISPLAY_WIDTH - DISPLAY_FRAME_STROKE * 2, DISPLAY_FRAME_V_HEIGHT, COLOR_BACKGROUND);
	displayFlags &= ~Display_Flag_Window_Needs_ToBe_Cleared;
}


// Check and draw section header
void drawHeader(uint8_t section) {
	if (displayLastSection != section) {
		if (section < DISPLAY_INFO_SECTIONS_COUNT) {
			// Info section
			display.writeString(0, 0, displaySections[section].header, DISPLAY_SECTION_HEADER_FONT, COLOR_BACKGROUND, getDisplaySectionColor(section));
		} else {
			// Menu section
			uint16_t color = getDisplaySectionColor(section);
			display.fillRect(0, 0, ST7789_DISPLAY_WIDTH, DISPLAY_SECTION_HEADER_FONT.height, color);
			display.writeString(DISPLAY_MENU_HEADER_X_POS, DISPLAY_MENU_HEADER_Y_POS, menuDisplaySections[section - DISPLAY_INFO_SECTIONS_COUNT].header, DISPLAY_MENU_HEADER_FONT, COLOR_BACKGROUND, color);
		}

		displayFlags &= ~Display_Flag_Section_Complete;
		displayFlags |= Display_Flag_Redraw_Section;
		displayLastSection = section;
	}
}


// Draw section frame
void drawFrame(uint16_t color) {
	if (displayLastFrameColor != color) {
		display.fillRect(DISPLAY_FRAME_V_L_X_POS, DISPLAY_FRAME_V_Y_POS, DISPLAY_FRAME_STROKE, DISPLAY_FRAME_V_HEIGHT, color);		// Draw left vertical line
		display.fillRect(DISPLAY_FRAME_V_R_X_POS, DISPLAY_FRAME_V_Y_POS, DISPLAY_FRAME_STROKE, DISPLAY_FRAME_V_HEIGHT, color);		// Draw right vertical line
		display.fillRect(DISPLAY_FRAME_H_X_POS, DISPLAY_FRAME_H_Y_POS, ST7789_DISPLAY_WIDTH, DISPLAY_FRAME_STROKE, color);			// Draw bottom horizontal line
		displayLastFrameColor = color;
	}
}


// Draw status
void drawStatus(uint8_t statusIndex) {
	display.writeString(0, DISPLAY_STATUS_Y_POS, systemStatuses[statusIndex].statusString, DISPLAY_VALUE_FONT, COLOR_BACKGROUND, systemStatuses[statusIndex].frameColor);
	displayFlags |= Display_Flag_Status_Displayed;
}


// Draw the parameter name aligned to the right
void drawParameterNameAtPosition(uint8_t & position, const char *name, uint16_t color, bool increment = true) {
	// Clear the string
	uint8_t valuePosition = position - (DISPLAY_PARAMETER_Y_POS - DISPLAY_VALUE_Y_POS);
	display.fillRect(DISPLAY_MENU_ITEM_X_POS, valuePosition, DISPLAY_MENU_ITEM_FONT.width * DISPLAY_MENU_ITEM_LENGTH, DISPLAY_MENU_ITEM_FONT.height, COLOR_BACKGROUND);

	// Prepare empty string
	char displayString[DISPLAY_PARAMETER_LENGTH + 2];
	memset(displayString, ' ', 9);

	uint8_t len = 0;
	while ((name[len]) && (len < DISPLAY_PARAMETER_LENGTH)) len++;

	memcpy(displayString + DISPLAY_PARAMETER_LENGTH - len, name, len);
	displayString[DISPLAY_PARAMETER_LENGTH] = ':';
	displayString[DISPLAY_PARAMETER_LENGTH + 1] = 0;

	display.writeString(DISPLAY_PARAMETER_X_POS, position, displayString, DISPLAY_PARAMETER_FONT, color, COLOR_BACKGROUND);
	if (increment) position += DISPLAY_PARAMETERS_STEP;
}


// Draw parameter value
void drawParameterValueAtPosition(uint8_t & position, const char * string, uint16_t color, bool increment = true, bool invert = true) {
	display.writeString(DISPLAY_VALUE_X_POS, position, string, DISPLAY_VALUE_FONT, invert ? COLOR_BACKGROUND : color, invert ? color : COLOR_BACKGROUND);
	if (increment) position += DISPLAY_PARAMETERS_STEP;
}


// Draw scan progress bar
void drawProgressBarAtPosition(uint8_t position, uint8_t progress, uint16_t color) {
	uint8_t valueWidth = DISPLAY_VALUE_LENGTH * DISPLAY_VALUE_FONT.width;
	uint8_t barWidth = (uint8_t)((uint16_t)valueWidth * progress / 100);
	display.fillRect(DISPLAY_VALUE_X_POS, position, barWidth, DISPLAY_VALUE_FONT.height, color);
	display.fillRect(DISPLAY_VALUE_X_POS + barWidth, position, valueWidth - barWidth, DISPLAY_VALUE_FONT.height, COLOR_BACKGROUND);
}


// Draw menu sections items
void drawMenuItemAtPosition(uint8_t & position, uint8_t section, uint8_t itemIndex, bool selected, bool increment = true) {
	// Get current menu items
	const char * menuItem = nullptr;
	switch (section) {
		// Buzzer options menu
		case DISPLAY_BUZZER_OPTIONS_SECTION:
			menuItem = muteMenuItems[itemIndex];
			break;

		// Scan confirmation menus
		case DISPLAY_SENSOR_SCAN_CONFIRMATION_SECTION:
		case DISPLAY_FAN_SCAN_CONFIRMATION_SECTION:
		case DISPLAY_HARDWARE_SCAN_CONFIRMATION_SECTION:
			menuItem = confirmMenuItems[itemIndex];
			break;

		// Main menu
		default:
			if ((section == DISPLAY_MAIN_MENU_SECTION) && (itemIndex == DISPLAY_MAIN_MENU_BUZZER_ITEM)) {
				for (uint8_t i = 0; i < BUZZER_OPTIONS_COUNT; i++) {
					if (systemState.behaviorConfig.buzzerAlarmsMask == buzzerOptions[i]) {
						menuItem = muteMenuItems[i];
						break;
					}
				}
			}

			if (menuItem == nullptr) menuItem = mainMenuItems[itemIndex];
			break;
	}
	uint16_t color = getDisplaySectionColor(section);
	display.writeString(DISPLAY_MENU_ITEM_X_POS, position, menuItem, DISPLAY_MENU_ITEM_FONT, selected ? COLOR_BACKGROUND : color, selected ? color : COLOR_BACKGROUND);
	if (increment) position += DISPLAY_PARAMETERS_STEP;
}


// Blink selected menu item (unfortunately,
// it's a blocking operation, but that's okay for this version).
void blinkMenuItem(uint8_t section, uint8_t itemIndex, uint8_t firstItemIndex) {
	uint8_t state = 0;
	for (uint8_t i = 0; i < DISPLAY_MENU_ITEM_BLINKS_COUNT * 2; i++) {
		uint8_t menuPos = DISPLAY_MENU_ITEM_Y_POS + DISPLAY_PARAMETERS_STEP * (itemIndex - firstItemIndex);
		drawMenuItemAtPosition(menuPos, section, itemIndex, state, false);
		HAL_Delay(SHORT_DELAY);
		state = !state;
	}
}


// Calculate submenu section index from the selected main menu item
uint8_t menuItemToSection(uint8_t menuItem, uint8_t & sectionItemsCount) {
	if (menuItem == DISPLAY_MAIN_MENU_BUZZER_ITEM) {
		sectionItemsCount = BUZZER_OPTIONS_COUNT;
		return (DISPLAY_BUZZER_OPTIONS_SECTION);
	} else if ((menuItem >= DISPLAY_MAIN_MENU_SENSOR_SCAN_ITEM) && (menuItem <= DISPLAY_MAIN_MENU_HARDWARE_SCAN_ITEM)) {
		sectionItemsCount = DISPLAY_CONFIRM_MENU_ITEMS_COUNT;
		return (DISPLAY_SENSOR_SCAN_CONFIRMATION_SECTION + menuItem - DISPLAY_MAIN_MENU_SENSOR_SCAN_ITEM);
	} else {
		sectionItemsCount = 0;
		return (0);
	}
}


// Determine scan request type from the confirmation menu section index
// and pass it to the main thread. Returns false if the controller is busy.
bool setScanRequestFromConfirmationSection(uint8_t confirmationSection) {
	switch (confirmationSection) {
		case DISPLAY_HARDWARE_SCAN_CONFIRMATION_SECTION:
			return (requestMenuAction(UA_Request_Hardware_Scan | UA_Request_Initiated_By_Menu));

		case DISPLAY_FAN_SCAN_CONFIRMATION_SECTION:
			return (requestMenuAction(UA_Request_Fan_Scan | UA_Request_Initiated_By_Menu));

		case DISPLAY_SENSOR_SCAN_CONFIRMATION_SECTION:
			return (requestMenuAction(UA_Request_Sensor_Scan | UA_Request_Initiated_By_Menu));

		default:
			return (true);
	}
}


// Draw information message (with optional error code)
// in the screen center
void displayMessage(const char * message, uint16_t code, char suffix, uint16_t color) {
	if (message != nullptr) {
		clearMainWindow();

		uint8_t xPos;
		uint8_t yPos = (ST7789_DISPLAY_HEIGHT - DISPLAY_SECTION_HEADER_FONT.height) / 2;
		if (code != 0) {
			yPos += DISPLAY_SECTION_HEADER_FONT.height / 2;
			char codeString[DISPLAY_VALUE_LENGTH + 1];
			SU_getSuffixedStringFromInt16(codeString, code, DISPLAY_VALUE_LENGTH, suffix);
			xPos = getXPositionForString(codeString, DISPLAY_SECTION_HEADER_FONT.width);
			display.writeString(xPos, yPos, codeString, DISPLAY_SECTION_HEADER_FONT, color, COLOR_BACKGROUND);
			yPos -= DISPLAY_SECTION_HEADER_FONT.height;
		}

		xPos = getXPositionForString(message, DISPLAY_SECTION_HEADER_FONT.width);
		display.writeString(xPos, yPos, message, DISPLAY_SECTION_HEADER_FONT, color, COLOR_BACKGROUND);

		sectionDisplayDuration = SECTION_DISPLAY_DURATION;
		displayFlags |= Display_Flag_Window_Needs_ToBe_Cleared;
		displaySectionTimer = 0;
	}
}


// Checking for active and inactive sensors in the dynamic list
void analyzeSensorList() {
	uint8_t sensorsCount = getSensorCount();
	uint8_t currentSensorIndex = 0;
	bool activeSensorFound = false;
	bool inactiveSensorFound = false;

	displayFlags &= ~(Display_Flag_Active_Sensors_Available | Display_Flag_Inactive_Sensors_Available);

	while ((currentSensorIndex < sensorsCount) &&
			(!activeSensorFound || !inactiveSensorFound)) {
		TSensor_base * currentSensor = getSensorAtIndex(currentSensorIndex++);

		if ((currentSensor != nullptr) && (currentSensor->configuration != nullptr)) {
			if ((currentSensor->configuration->flags & TSensor_Flag_Visible) != 0) {
				if ((currentSensor->configuration->flags & TSensor_Flag_Active) != 0) {
					activeSensorFound = true;
				} else {
					inactiveSensorFound = true;
				}
			}
		}
	}

	if (activeSensorFound) displayFlags |= Display_Flag_Active_Sensors_Available;
	if (inactiveSensorFound) displayFlags |= Display_Flag_Inactive_Sensors_Available;
}

#endif


// Display a message during configuration loading
// and initialization of Fans/Sensors
void displayLoadingMessage() {
#if defined(DISPLAY_ENABLED)

	bool logoDisplayed = false;

#ifdef DISPLAY_SHOW_LOGO

	if (activeConfigurationPage == nullptr) {
		display.setWindow((ST7789_DISPLAY_WIDTH - LOGO_IMAGE_WIDTH) / 2, (ST7789_DISPLAY_HEIGHT - DISPLAY_PARAMETER_FONT.height - LOGO_IMAGE_HEIGHT) / 2, LOGO_IMAGE_WIDTH, LOGO_IMAGE_HEIGHT);

		// Set the width of our buffer for correct logo decompression
		decompressionBufferWidth = LOGO_IMAGE_WIDTH;
		resetDecompression();
		for (uint8_t i = 0; i < DISPLAY_BUFFERS_PER_LOGO; i++) {

	#if defined (ST7789_USE_BUFFERING)
			uint16_t * currentScreenBuffer = display.getPixelBuffer();
	#else
			uint16_t currentScreenBuffer[DISPLAY_FRAMES_PER_LOGO_BUFFER];
	#endif
			// Decompress one buffer
			decompressNextFrames((DecompressionContext *)&logoDC, (uint8_t *)logoData, currentScreenBuffer, DISPLAY_FRAMES_PER_LOGO_BUFFER, false);
			// And display it
			display.writePixels(currentScreenBuffer, DISPLAY_FRAMES_PER_LOGO_BUFFER, LittleEndian);
		}

		uint8_t xPos = getXPositionForString(versionMessage, DISPLAY_PARAMETER_FONT.width);
		uint8_t yPos = (ST7789_DISPLAY_HEIGHT + LOGO_IMAGE_HEIGHT + DISPLAY_PARAMETER_FONT.height) / 2;
		for (uint8_t i = 0; i < (sizeof(versionMessage) - 1); i++) {
			display.writeChar(xPos, yPos, versionMessage[i], DISPLAY_PARAMETER_FONT, COLOR_VERSION, COLOR_BACKGROUND);
			xPos += DISPLAY_PARAMETER_FONT.width;
			HAL_Delay(SHORT_DELAY);
		}

		logoDisplayed = true;
	}

#endif

	if (!logoDisplayed) {
		displayMessage(loadingMessage, 0, 0, COLOR_PROGRESS);
	}

#endif
}


// Draw scan progress
void updateScanning(uint8_t scanTask, uint8_t percentDone, void * sender, void * attribute) {
	if ((sender == nullptr) || (attribute == nullptr)) return;
	Fan_MAX31790_DS1841 * senderFan = static_cast<Fan_MAX31790_DS1841*>(sender);
	uint8_t fanIndex = senderFan->getMAX31790FanIndex();

	// Calculate the percentage of the entire scanning process progress
	systemState.scanProgress = (percentDone == 0xFF) ? 0xFF :
		((systemState.configurationChangeFlags & CC_Flag_All_Fan_Channels_Involved) != 0) ?
				(uint8_t)(((uint16_t)fanIndex * 100 + percentDone) / FANS_COUNT) : percentDone;

#if defined (DISPLAY_ENABLED)

	Fan_Config * newConfig = static_cast<Fan_Config*>(attribute);
	uint8_t position = (fanIndex % DISPLAY_PARAMETERS_PER_PAGE) * DISPLAY_PARAMETERS_STEP;

	if (percentDone == 0) {
		// Draw Fan channel name
		position += DISPLAY_PARAMETER_Y_POS;
		drawParameterNameAtPosition(position, newConfig->name, COLOR_ALARM, false);

		position -= (DISPLAY_PARAMETER_Y_POS - DISPLAY_VALUE_Y_POS);
		switch (scanTask) {
			// Prewarm in progress
			case Fan_Scan_Task_PreWarm:
				drawParameterValueAtPosition(position, preWarmString, COLOR_WARNING, false, false);
				break;

			// Warming up in progress
			case Fan_Scan_Task_WarmUp:
				drawParameterValueAtPosition(position, warmingString, COLOR_WARNING, false);
				break;

			default:
				break;
		}

	} else {
		position += DISPLAY_VALUE_Y_POS;

		if (percentDone == 0xFF) {
			// Error occurred
			drawParameterValueAtPosition(position, failureString, COLOR_ALARM, false);
		} else {
			if (percentDone < 100) {
				// Draw progress bar
				drawProgressBarAtPosition(position, percentDone, COLOR_PROGRESS);
			} else {
				// Draw the result
				char displayString[DISPLAY_VALUE_LENGTH + 1];
				memset(displayString, ' ', DISPLAY_VALUE_LENGTH);
				displayString[DISPLAY_VALUE_LENGTH] = 0;
				uint16_t color = COLOR_WARNING;

				if (scanTask == Fan_Scan_Task_Hardware) {
					// Available equipment
					uint8_t fanHardware = newConfig->hardware;
					bool pwmHardwareAvailable = ((fanHardware & Fan_Hardware_PWM_Available) != 0) &&
							((fanHardware & Fan_Hardware_RPM_Available) != 0);
					bool dcdcHardwareAvailable = ((fanHardware & Fan_Hardware_DCDC_Available) != 0) &&
							((fanHardware & Fan_Hardware_Voltmeter_Available) != 0);
					bool mosfetHardwareAvailable = ((fanHardware & Fan_Hardware_MOSFET_Available) != 0) &&
							((fanHardware & Fan_Hardware_Voltmeter_Available) != 0);
					if (pwmHardwareAvailable) displayString[1] = '"';
					if (dcdcHardwareAvailable) displayString[3] = '#';
					if (mosfetHardwareAvailable) displayString[5] = '$';

					// There may be other checks on a different set of equipment
					if (senderFan->potDriver == nullptr) {
						if (pwmHardwareAvailable) color = COLOR_OK; else color = COLOR_ALARM;
					} else {
						if (pwmHardwareAvailable && dcdcHardwareAvailable && mosfetHardwareAvailable) color = COLOR_OK; else
							if (fanHardware == 0) color = COLOR_ALARM;
					}

				} else if (scanTask == Fan_Scan_Task_Fan) {
					// Fan model
					uint8_t fanModel = (newConfig->model & Fan_Model_Connectivity_Mask);
					if (fanModel != Fan_Model_Unconnected) {
						char pins;
						if (fanModel & Fan_Model_PWM) pins = '4'; else
							if (fanModel & Fan_Model_Tachometer) pins = '3'; else pins = '2';
						strncpy(displayString, fanPinsString, DISPLAY_VALUE_LENGTH + 1);
						displayString[1] = pins;
						color = COLOR_OK;
					} else {
						strncpy(displayString, unknownString, DISPLAY_VALUE_LENGTH + 1);
					}
				}

				drawParameterValueAtPosition(position, displayString, color, false);
			}
		}
	}
#endif
}


// Get the index of the most dangerous status in the table, the bit of which is currently set.
uint8_t getStatusIndex(uint32_t status) {
	uint8_t statusIndex = STATUSES_COUNT;
	// Search for the most dangerous status
	while (--statusIndex) {
		if ((systemStatuses[statusIndex].status & status) != 0) break;
	}
	return (statusIndex);
}


// Check the system status and, if necessary, issue an alarm.
void checkStatus(uint32_t newStatus) {

#if defined(DISPLAY_ENABLED) || defined(LOGGING_ENABLED)

	uint8_t newStatusIndex = getStatusIndex(newStatus);

#endif

	bool statusChanged = (systemState.status != newStatus);

#if defined(DISPLAY_ENABLED)

	if (statusChanged || ((displayFlags & Display_Flag_Status_Displayed) == 0))
		drawStatus(newStatusIndex);

#endif

	// Activate buzzer
	uint32_t currentTick = HAL_GetTick();
	uint8_t newBuzzerStatusIndex = getStatusIndex(newStatus & systemState.behaviorConfig.buzzerAlarmsMask);
	uint8_t oldBuzzerStatusIndex = getStatusIndex(systemState.status & systemState.behaviorConfig.buzzerAlarmsMask);
	if ((beepsCount == 0) &&
		(((newBuzzerStatusIndex > STATUS_NORMAL_INDEX) && ((currentTick - buzzerTick) >= ALARM_DELAY)) ||
		((newBuzzerStatusIndex > oldBuzzerStatusIndex) && ((currentTick - buzzerTick) >= UPDATE_DELAY)))) {
		beepsCount = systemStatuses[newBuzzerStatusIndex].beeps * 2;
		buzzerTick = 0;
	}

	if (statusChanged) {

// Logging block
#if defined(LOGGING_ENABLED)

		uint8_t severity = Logger_Severity_Info;
		if ((newStatus & Status_Category_Alarms) != 0) {
			severity = Logger_Severity_Alarm;
			HAL_GPIO_WritePin(ERR_LED_GPIO_Port, ERR_LED_Pin, GPIO_PIN_SET);
		} else {
			if ((newStatus & Status_Category_Warnings) != 0) severity = Logger_Severity_Warning;
			HAL_GPIO_WritePin(ERR_LED_GPIO_Port, ERR_LED_Pin, GPIO_PIN_RESET);
		}

		UL_logMessage(severity, "System status changed:");
		uint8_t i = newStatusIndex;
		while (i != 0) {
			uint32_t currentStatus = systemStatuses[i].status;

			/*
			// This is for the case when (if) the OK status
			// can be combined with error statuses.
			// But we won't do that.
			if (((currentStatus & newStatus) != 0) &&
				((currentStatus != StatusNormal) || (newStatus == StatusNormal))) {
				UL_logMessage(Logger_Severity_None, systemStatuses[i].statusString);
			}
			*/

			if ((currentStatus & newStatus) != 0) {
				UL_logMessage(Logger_Severity_None, systemStatuses[i].statusString);
			}
			i--;
		}

// End of logging block
#else
		if ((newStatus & Status_Category_Alarms) != 0) {
			HAL_GPIO_WritePin(ERR_LED_GPIO_Port, ERR_LED_Pin, GPIO_PIN_SET);
		} else {
			HAL_GPIO_WritePin(ERR_LED_GPIO_Port, ERR_LED_Pin, GPIO_PIN_RESET);
		}
#endif

		systemState.status = newStatus;
	}
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
// Working with configuration and Flash memory

// Fill the System Configuration with the user's current settings
void prepareSystemConfig(System_Config * config, bool updateBehavior) {
	config->firmwareVersion = (FIRMWARE_MAJOR_VERSION << 16) | FIRMWARE_MINOR_VERSION;

	if (updateBehavior) {
		config->behaviorConfig.buzzerAlarmsMask = systemState.behaviorConfig.buzzerAlarmsMask;
		config->behaviorConfig.displayBrightness = systemState.behaviorConfig.displayBrightness;
		config->behaviorConfig.displayRotation = systemState.behaviorConfig.displayRotation;
		config->behaviorConfig.configFlags = systemState.behaviorConfig.configFlags;
	}

	config->firmwareFlags = Firmware_Flag_Empty;

#if defined(DISPLAY_ENABLED)
	config->firmwareFlags |= Firmware_Flag_Display_Enabled;
#endif

#if defined(LOGGING_ENABLED)
	config->firmwareFlags |= Firmware_Flag_UART_Logger_Enabled;
#endif
}


// Apply System Behavior Configuration
bool applySystemBehaviorConfig(const Behavior_Config * config) {
	if (config != nullptr) {
		bool buzzerAlarmLevelChanged = systemStatuses[getStatusIndex(systemState.behaviorConfig.buzzerAlarmsMask & systemState.status)].beeps !=
				systemStatuses[getStatusIndex(config->buzzerAlarmsMask & systemState.status)].beeps;
		bool buzzerWaveformChanged = ((systemState.behaviorConfig.configFlags ^ config->configFlags) & Config_Flag_Buzzer_Loud_Mode) != 0;

		systemState.behaviorConfig.buzzerAlarmsMask = config->buzzerAlarmsMask;
		systemState.behaviorConfig.displayBrightness = config->displayBrightness;
		systemState.behaviorConfig.displayRotation = config->displayRotation;
		systemState.behaviorConfig.configFlags = config->configFlags;

		// Stop the current alarm signal if its level or waveform has changed
		if (buzzerAlarmLevelChanged || buzzerWaveformChanged)  {
			beepsCount = 0;
			buzzerTick = 0;
			HAL_GPIO_WritePin(BUZZER_GPIO_Port, BUZZER_Pin, GPIO_PIN_RESET);
			HAL_GPIO_WritePin(ERR_LED_GPIO_Port, ERR_LED_Pin, GPIO_PIN_RESET);
		}
	}

	// Apply new config
	Fan_base::configFlags = (systemState.behaviorConfig.configFlags & Fan_Config_Flag_Mask);

#if defined(DISPLAY_ENABLED)
	TIM1->CCR1 = systemState.behaviorConfig.displayBrightness;
	return (checkDisplayRotation() == HAL_OK);
#else
	return (true);
#endif
}


// Get the saved configuration of the Fan by its name or index
const Fan_Config * getFanConfig(const Settings_Page * page, char * name, uint8_t * searchIndex = nullptr, uint8_t * foundIndex = nullptr) {
	const Fan_Config_Block * currentConfigBlock;
	const Fan_Config * fanConfig = nullptr;

	if (page != nullptr) {
		uint8_t i = (searchIndex != nullptr) ? *searchIndex : 0;
		while (i < FANS_COUNT) {
			currentConfigBlock = &page->fanConfigs[i];
			// Check the completeness of the saved configuration
			if (currentConfigBlock->okMarker != SETTINGS_COMPLETENESS_MARKER) {
				break;
			} else {
				// Is this our guy?
				if (((name != nullptr) && (strcmp(name, currentConfigBlock->config.name) == 0)) ||
					((searchIndex != nullptr) && (i == *searchIndex))) {
					fanConfig = &currentConfigBlock->config;
					break;
				}
			}

			i++;
		}

		// Return the index of the found fan
		if (foundIndex != nullptr) *foundIndex = i;
	}

	return (fanConfig);
}


// Get the saved configuration of the sensor by its name, serial number or index
const TSensor_Config * getSensorConfig(const Settings_Page * page, char * name, uint8_t * serialNumber, uint8_t * searchIndex = nullptr, uint8_t * foundIndex = nullptr) {
	const Sensor_Config_Block * currentConfigBlock;
	const TSensor_Config * sensorConfig = nullptr;

	if (page != nullptr) {
		uint8_t i = (searchIndex != nullptr) ? *searchIndex : 0;
		// While we aren't run out of the page...
		while (i < MAX_SENSORS_COUNT) {
			currentConfigBlock = &page->sensorConfigs[i];
			if (currentConfigBlock->okMarker != SETTINGS_COMPLETENESS_MARKER) {
				break;
			} else {
				// ... and the saved configuration is complete
				if (((name != nullptr) && (strcmp(name, currentConfigBlock->config.name) == 0)) ||
					((serialNumber != nullptr) && (memcmp(serialNumber, currentConfigBlock->config.serialNumber, TSENSOR_SN_SIZE) == 0)) ||
					((searchIndex != nullptr) && (i == *searchIndex))) {
					// Is this our guy?
					sensorConfig = &currentConfigBlock->config;
					break;
				}
			}

			i++;
		}

		// Return the index of the found sensor or the total number of sensors,
		// if that was the purpose of going through the list.
		if (foundIndex != nullptr) *foundIndex = i;
	}

	return (sensorConfig);
}


// Choose a new name for the fan and return the index of the next one
uint8_t getNewFanName(const Settings_Page * page, char * name, const char * baseName, uint8_t startingIndex) {
	do {
		SU_appendIndexToString(name, baseName, startingIndex++, FAN_NAME_SIZE);
		// We will search in the new settings page filled with detected sensors,
		// so we will not check the names of those sensors that were
		// disconnected and will not be detected during re-scanning).
	} while ((getFanConfig(page, name) != nullptr) && (startingIndex != 0));

	return (startingIndex);
}


// Choose a new name for the sensor and return the index of the next one
uint8_t getNewSensorName(const Settings_Page * page, char * name, const char * baseName, uint8_t startingIndex, bool skipFirstUpdate) {
	do {
		if (!skipFirstUpdate) {
			SU_appendIndexToString(name, baseName, startingIndex++, TSENSOR_NAME_SIZE);
			if (startingIndex == 0) break;
		} else {
			skipFirstUpdate = false;
		}
		// We will search in the new settings page filled with detected sensors,
		// so we will not check the names of those sensors that were
		// disconnected and will not be detected during re-scanning).
	} while (getSensorConfig(page, name, nullptr) != nullptr);

	return (startingIndex);
}


// Generate the serial number of the sensor from its base name and
// serial number. This will only be used for embedded sensors.
// Using this SN, we can always understand which sensor it belongs to.
void prepareSensorSerialNumber(uint8_t * serialNumber, const char * baseName, uint8_t index) {
	SU_updateString(reinterpret_cast<char*>(serialNumber), baseName, TSENSOR_SN_SIZE);
	serialNumber[TSENSOR_SN_SIZE - 1] = index;
}


// Recalculate the load distribution matrix (from failed fans
// to the remaining working ones).
// The coefficient can take values from 0 to 15, expressed
// in tens of percent. I.e. 0 = 0%, 1 = 10%, 10 = 100%, 15 = 150%.
// The sum of the Control Values multiplied by these coefficients
// can far exceed 100%.
// Example: if the CPU radiator fan fails (well, let's say) and
// at the same time, according to the temperature sensors, it should
// rotate at half its possible speed, then this load can be distributed
// to the surrounding fans with an increasing coefficient so that
// there is at least some cooling and we do not get overheating.
void recalculateSubstitutionMatrix(Fan_Substitution_Matrix & matrix, const Settings_Page * page, uint8_t fansDiscovered) {
	memset(&matrix, 0, sizeof(Fan_Substitution_Matrix));
	if (fansDiscovered > 1) {	// For single Fan there no sense in substitution
		fansDiscovered--;
		// 10 ~ 100%
		uint8_t loadRedirectionConstant = 10 / fansDiscovered;
		if (((systemState.behaviorConfig.configFlags & Config_Flag_Professional_Mode) != 0) &&
			((loadRedirectionConstant * fansDiscovered) < 10)) loadRedirectionConstant++;

		// So for the home usage we will get:
		// 2 Fans: LRC = 100%	(if one of them fails, the other will take all its load)
		// 3 Fans: LRC = 50% 	(2 * 50% = 100%)
		// 4 Fans: LRC = 30%	(3 * 30% = 90% - almost OK)
		// 5 Fans: LRC = 20%	(4 * 20% = 80% - a little insufficient, but OK)
		// 6 Fans: LRC = 20%	(5 * 20% = 100% - fine!)
		//
		// And in professional mode:
		// 2 Fans: LRC = 100%
		// 3 Fans: LRC = 50% 	(2 * 50% = 100%)
		// 4 Fans: LRC = 40%	(3 * 40% = 120%)
		// 5 Fans: LRC = 30%	(4 * 30% = 120%)
		// 6 Fans: LRC = 20%	(5 * 20% = 100%)

		bool hiBits = true;
		uint8_t fanOffset = 0;

		for (uint8_t i = 0; i < FANS_COUNT; i++) {
			const Fan_Config * sourceFanConfig = getFanConfig(page, nullptr, &i);
			bool noSourceFan = ((sourceFanConfig == nullptr) ||
					(sourceFanConfig->model == Fan_Model_Unconnected) ||
					(sourceFanConfig->controlMode == Fan_Control_Disabled));
			for (uint8_t j = 0; j < FANS_COUNT; j++) {
				if (i != j) {
					const Fan_Config * targetFanConfig = getFanConfig(page, nullptr, &j);
					bool noTargetFan = ((targetFanConfig == nullptr) ||
							(targetFanConfig->model == Fan_Model_Unconnected) ||
							(targetFanConfig->controlMode == Fan_Control_Disabled));
					uint8_t k = (noSourceFan || noTargetFan) ? 0 : loadRedirectionConstant;
					if (hiBits) {
						matrix.data[fanOffset] = (k << 4);
					} else {
						matrix.data[fanOffset] |= k;
						fanOffset++;
					}
					hiBits = !hiBits;
				}
			}
		}
	}
}


// Get the load redistribution coefficient from the
// specified faulty Fan to the specified replacement one.
uint8_t getSubstitutionCoefficient(uint8_t failedFan, uint8_t substitutionFan) {
	if ((activeConfigurationPage != nullptr) && (activeConfigurationPage->substitutionMatrix.okMarker == SETTINGS_COMPLETENESS_MARKER)) {
		if (substitutionFan == failedFan) {
			return (0);
		} else if (substitutionFan > failedFan) {
			substitutionFan--;
		}
		uint8_t offset = failedFan * (FANS_COUNT - 1) + substitutionFan;
		bool hiBits = ((offset & 0b00000001) == 0);
		uint8_t k = activeConfigurationPage->substitutionMatrix.matrix.data[offset >> 1];
		if (hiBits) k >>= 4;
		return (k & 0xF);
	}

	return (0);
}


// Analyze the saved settings pages and select the most complete
// and up-to-date one.
const Settings_Page * flashGetSettingsPage(uint32_t & activePageSerialNumber, const Settings_Page *& alterPage) {
	uint8_t bestPageScore = 0;
	uint8_t foundPageIndex = 0;
	const Settings_Page * bestPage = nullptr;
	bool bestPageCompleted = false;
	uint32_t bestPageSN = 0;

	for (uint8_t i = 0; i < SETTINGS_PAGES_COUNT; i++) {
		const Settings_Page * currentPage = &configurationPages[i];

		// This marker simultaneously indicates that the page is not empty
		// and represents its serial number.
		uint32_t currentConfigSN = currentPage->configIndex;

		if ((currentConfigSN != 0xFFFFFFFF) && (currentConfigSN != 0x00000000) &&
				((currentPage->systemConfig.config.firmwareVersion >> 16) == FIRMWARE_MAJOR_VERSION) &&
				((currentPage->systemConfig.config.firmwareVersion & 0xFFFF) <= FIRMWARE_MINOR_VERSION)) {
			uint8_t pageScore = 0;

			if (currentPage->systemConfig.okMarker == SETTINGS_COMPLETENESS_MARKER) pageScore++;

			uint8_t j = 0;
			while ((j < FANS_COUNT) && (currentPage->fanConfigs[j].okMarker == SETTINGS_COMPLETENESS_MARKER)) {
				pageScore++;
				j++;
			}

			if (currentPage->substitutionMatrix.okMarker == SETTINGS_COMPLETENESS_MARKER) pageScore += FANS_COUNT;

			uint8_t sensorsCount;
			getSensorConfig(currentPage, nullptr, nullptr, nullptr, &sensorsCount);
			pageScore += sensorsCount;

			// And this marker indicates that the page was saved successfully
			bool configCompleted = (currentPage->okMarker == SETTINGS_COMPLETENESS_MARKER);

			// If we have found the first completed configuration...
			if ((configCompleted > bestPageCompleted) ||
				// ...or the configuration is completed and more recent...
				(configCompleted && (currentConfigSN > bestPageSN)) ||
				// ...or the configurations found are incomplete, but one of them is more detailed.
				(!configCompleted && !bestPageCompleted && (pageScore > bestPageScore))) {
				bestPageCompleted = configCompleted;
				bestPageSN = currentConfigSN;
				bestPageScore = pageScore;
				bestPage = currentPage;
				foundPageIndex = i;
			}
		}
	}

	// If a suitable page is found, let's use a pointer to the next page
	// as the one where we can save some new configuration.
	if (bestPage != nullptr) {
		foundPageIndex++;
		if (foundPageIndex >= SETTINGS_PAGES_COUNT) foundPageIndex = 0;
	}

	activePageSerialNumber = bestPageSN;
	alterPage = &configurationPages[foundPageIndex];
	return (bestPage);
}


// Erase configuration page
HAL_StatusTypeDef flashEraseSettingsPage(const Settings_Page * page) {
	FLASH_EraseInitTypeDef eraseInitStruct;
	eraseInitStruct.TypeErase = FLASH_TYPEERASE_PAGES;
	eraseInitStruct.PageAddress = reinterpret_cast<uint32_t>(page);// - FLASH_BASE;
	eraseInitStruct.NbPages = 1;

	__disable_irq();
	HAL_StatusTypeDef result = HAL_FLASH_Unlock();

	uint32_t errorAddress;
	if (result == HAL_OK) result = HAL_FLASHEx_Erase(&eraseInitStruct, &errorAddress);

	HAL_FLASH_Lock();
	__enable_irq();

    return (result);
}


// Write the next block of data to the clean configuration page
HAL_StatusTypeDef flashWriteBlock(const uint32_t * source, uint32_t destinationAddress, uint16_t size) {
	// We will write in 4-byte words (rounding the size up)
	uint16_t wordsCount = (size + sizeof(uint32_t) - 1) / sizeof(uint32_t);
	uint16_t currentWord = 0;

	__disable_irq();
	HAL_StatusTypeDef result = HAL_FLASH_Unlock();

	while ((result == HAL_OK) && (currentWord < wordsCount)) {
		result = HAL_FLASH_Program(FLASH_TYPEPROGRAM_WORD, destinationAddress, source[currentWord]);
		destinationAddress += sizeof(uint32_t);
		currentWord++;
	}

	HAL_FLASH_Lock();
	__enable_irq();
	return (result);
}


// Start clean configuration page
HAL_StatusTypeDef flashStartSettingsPage(const Settings_Page * page, uint32_t serialNumber) {
	CALL_AND_CHECK(flashEraseSettingsPage(page));
	return (flashWriteBlock(reinterpret_cast<const uint32_t*>(&serialNumber), reinterpret_cast<const uint32_t>(&page->configIndex), SETTINGS_COMPLETENESS_MARKER_SIZE));
}


// Save System Settings data structure to the config page
HAL_StatusTypeDef flashWriteSystemConfigToPage(const Settings_Page * page, System_Config * config) {
	if (((config->firmwareVersion >> 16) == FIRMWARE_MAJOR_VERSION) &&
		((config->firmwareVersion & 0xFFFF) <= FIRMWARE_MINOR_VERSION)) {
		// "Update" firmware version to the current one
		config->firmwareVersion = (FIRMWARE_MAJOR_VERSION << 16) | FIRMWARE_MINOR_VERSION;
		CALL_AND_CHECK(flashWriteBlock(reinterpret_cast<const uint32_t*>(config), reinterpret_cast<const uint32_t>(&page->systemConfig.config), sizeof(System_Config)));
		return (flashWriteBlock(reinterpret_cast<const uint32_t*>(&completenessMarker), reinterpret_cast<const uint32_t>(&page->systemConfig.okMarker), SETTINGS_COMPLETENESS_MARKER_SIZE));
	}
	// Unsupported firmware version
	return (HAL_ERROR);
}


// Save Fan Configuration data structure to the config page
HAL_StatusTypeDef flashWriteFanConfigToPage(const Settings_Page * page, const Fan_Config * config, uint8_t fanIndex) {
	CALL_AND_CHECK(flashWriteBlock(reinterpret_cast<const uint32_t*>(config), reinterpret_cast<const uint32_t>(&page->fanConfigs[fanIndex].config), sizeof(Fan_Config)));
	return (flashWriteBlock(reinterpret_cast<const uint32_t*>(&completenessMarker), reinterpret_cast<const uint32_t>(&page->fanConfigs[fanIndex].okMarker), SETTINGS_COMPLETENESS_MARKER_SIZE));
}


// Save Fan Substitution Matrix data structure to the config page
HAL_StatusTypeDef flashWriteSubstitutionMatrixToPage(const Settings_Page * page, Fan_Substitution_Matrix * matrix) {
	CALL_AND_CHECK(flashWriteBlock(reinterpret_cast<const uint32_t*>(matrix), reinterpret_cast<const uint32_t>(&page->substitutionMatrix.matrix), sizeof(Fan_Substitution_Matrix)));
	return (flashWriteBlock(reinterpret_cast<const uint32_t*>(&completenessMarker), reinterpret_cast<const uint32_t>(&page->substitutionMatrix.okMarker), SETTINGS_COMPLETENESS_MARKER_SIZE));
}


// Save Sensor Configuration data structure to the config page
HAL_StatusTypeDef flashWriteTSensorConfigToPage(const Settings_Page * page, const TSensor_Config * config, uint8_t sensorIndex) {
	CALL_AND_CHECK(flashWriteBlock(reinterpret_cast<const uint32_t*>(config), reinterpret_cast<const uint32_t>(&page->sensorConfigs[sensorIndex].config), sizeof(TSensor_Config)));
	return (flashWriteBlock(reinterpret_cast<const uint32_t*>(&completenessMarker), reinterpret_cast<const uint32_t>(&page->sensorConfigs[sensorIndex].okMarker), SETTINGS_COMPLETENESS_MARKER_SIZE));
}


// Write the completeness marker to the end of page
HAL_StatusTypeDef flashFinalizeSettingsPage(const Settings_Page * page) {
	return (flashWriteBlock(reinterpret_cast<const uint32_t*>(&completenessMarker), reinterpret_cast<const uint32_t>(&page->okMarker), SETTINGS_COMPLETENESS_MARKER_SIZE));
}


// Load configuration structures from the specified page
// into Fan and Sensor objects, and apply the overall System Configuration.
// If the changes have occurred only for one Fan or Sensor,
// then we can apply the settings only to it, and for the rest,
// simply update the pointers to the current configuration.
uint32_t loadSettingsPage(const Settings_Page * page, bool applySystemConfig, bool applyFanSettings, bool reloadSensors, uint8_t fanIndex, uint8_t * reloadSensorSN, bool showLoadingMessage) {
	uint32_t resultFlags = CC_Flag_Empty;

	if (page == nullptr) {
		return (resultFlags);
	} else if (page->okMarker == 0xFFFFFFFF) {
		return (CC_Flag_No_Config);
	}

// Logging block
#if defined(LOGGING_ENABLED)
	UL_logMessageWithLongHexParam(Logger_Severity_Info, "Loading configuration", page->configIndex);
#endif
// End of logging block

	// Apply System Configuration, if necessary.
	if (applySystemConfig && (page->systemConfig.okMarker == SETTINGS_COMPLETENESS_MARKER)) {
		if (!applySystemBehaviorConfig(&page->systemConfig.config.behaviorConfig)) {
			resultFlags |= CC_Flag_System_Config_Error;
		}
	}

	// It may take some time to load the configuration and
	// initialize the fans/sensors, so this is a good moment
	// to display a message or logo.
	if (showLoadingMessage) displayLoadingMessage();


	// Apply new config for fans
	uint8_t searchIndex = 0;
	const Fan_Config * currentFanConfig;
	while ((currentFanConfig = getFanConfig(page, nullptr, &searchIndex, nullptr)) != nullptr) {
		// Apply the settings either to all fans, or to the specified one,
		// if it is set by the incoming parameters.
		if (applyFanSettings && ((fanIndex >= FANS_COUNT) || (fanIndex == searchIndex))) {

// Logging block
#if defined(LOGGING_ENABLED)
			UL_logMessageWithShortIntParam(Logger_Severity_Info, "Load Fan", searchIndex);
#endif
// End of logging block
			if (fans[searchIndex].applyConfig(const_cast<Fan_Config*>(currentFanConfig), true) != HAL_OK)
				resultFlags |= CC_Flag_Fan_Config_Error;
		} else {
			fans[searchIndex].configuration = const_cast<Fan_Config*>(currentFanConfig);
		}

		searchIndex++;
	}

	// Clear the dynamic list of sensors by deleting all objects
	if (reloadSensors) {
		while (tSensors.length() > 0) tSensors.deleteAtIndex(0, &releaseSensorObject);

		// Built-in sensors are static objects, so we only detach them from the previous
		// configuration page. They will be attached again if the new page contains their configurations.
		mcuTSensor.configuration = nullptr;
		for (uint8_t i = 0; i < DCDC_COUNT; i++) {
			dcdcTSensors[i].configuration = nullptr;
		}
	}

	// Apply new config for sensors
	searchIndex = 0;
	const TSensor_Config * currentSensorConfig;
	while ((currentSensorConfig = getSensorConfig(page, nullptr, nullptr, &searchIndex, nullptr)) != nullptr) {

		// Obtain sensor object
		TSensor_base * sensorToLoad = nullptr;
		switch (currentSensorConfig->type) {
			case TSensor_Type_MCU:
				sensorToLoad = &mcuTSensor;
				break;

			case TSensor_Type_DS1841: {
				uint8_t potIndex = currentSensorConfig->serialNumber[TSENSOR_SN_SIZE - 1];
				if (potIndex < DCDC_COUNT) sensorToLoad = &dcdcTSensors[potIndex];
				break;
			}

			case TSensor_Type_DS18B20:
			case TSensor_Type_SW:
				if (reloadSensors) {
					uint32_t availableRAM = getFreeMemory();
					if (currentSensorConfig->type == TSensor_Type_DS18B20) {
						// Check available RAM...
						if (availableRAM >= (MIN_FREE_MEMORY + sizeof(TSensor_DS18B20_ML))) {
							// Create new DS18B20 object
							sensorToLoad = new (std::nothrow) TSensor_DS18B20_ML(sensorLines, SENSOR_LINES_COUNT);
						}
					} else {
						// Check available RAM...
						if (availableRAM >= (MIN_FREE_MEMORY + sizeof(TSensor_SW))) {
							// ...and create new Software Sensor object
							sensorToLoad = new (std::nothrow) TSensor_SW();
						}
					}

					if ((sensorToLoad != nullptr) && !tSensors.addObject(sensorToLoad)) {
						releaseSensorObject(sensorToLoad);
						sensorToLoad = nullptr;
					}

				} else {
					// Or get the sensor object from the dynamic list
					sensorToLoad = getSensorBySerialNumber(currentSensorConfig->serialNumber, currentSensorConfig->type);
				}
				break;

			default:
				break;
		}

		if (sensorToLoad != nullptr) {
			// Check if we need to re-apply configuration...
			if (reloadSensors ||
				((sensorToLoad->configuration != nullptr) && (reloadSensorSN != nullptr) &&
				(memcmp(sensorToLoad->configuration->serialNumber, reloadSensorSN, TSENSOR_SN_SIZE) == 0))) {

// Logging block
#if defined(LOGGING_ENABLED)
				UL_logMessageWithHexArrayParam(Logger_Severity_Info, "Load Sensor", currentSensorConfig->serialNumber, TSENSOR_SN_SIZE);
#endif
// End of logging block

				if (sensorToLoad->applyConfig(const_cast<TSensor_Config*>(currentSensorConfig)) != HAL_OK)
					resultFlags |= CC_Flag_Sensor_Config_Error;
			} else {
				// ... or just to replace the config page pointer.
				sensorToLoad->configuration = const_cast<TSensor_Config*>(currentSensorConfig);
			}
		} else {
			if (reloadSensors) {
				// Out of memory error
				resultFlags |= CC_Flag_Too_Many_Sensors_Stored;

// Logging block
#if defined(LOGGING_ENABLED)
				UL_logMessage(Logger_Severity_Alarm, "Out of RAM");
#endif
// End of logging block

			} else {
				// Sensor not found error (wrong SN)
				resultFlags |= CC_Flag_Sensor_Not_Found;

// Logging block
#if defined(LOGGING_ENABLED)
				UL_logMessage(Logger_Severity_Alarm, "Sensor not found");
#endif
// End of logging block

			}
		}

		searchIndex++;
	}

	// Switch to the new configuration
	activeConfigurationPage = page;

	return (resultFlags);
}


// Re-save the configuration with the optional use of an external configuration
// for the System or for any of the Fans or Sensors, with the ability to re-scan
// the Fan hardware/capabilities (for all or only for the specified Fan)
// and to re-scan Sensors. If there are no changes in the configuration of the
// System/Fans/Sensors, they are copied from the active configuration page.
uint32_t updateSettingsPage(const Settings_Page * activePage, const Settings_Page * alterPage, uint32_t activePageSN,
							   System_Config * replacementSettings, Fan_Config * replacementFanConfig, Fan_Substitution_Matrix * replacementSubstitutionMatrix, TSensor_Config * replacementSensorConfig,
							   bool updateSystemBehavior, bool rescanHardware, bool rescanFans, bool rescanSensors, bool addSensor,
							   uint8_t fanIndex, uint8_t * deleteSensorSN, UpdateScanningDelegate progressDelegate) {

	uint32_t resultFlags = CC_Flag_Empty;
	uint8_t searchIndex = 0;
	uint8_t fansDiscovered = 0;
	bool lockSubstitutionMatrix = (systemState.behaviorConfig.configFlags & Config_Flag_Lock_Substitution_Matrix) != 0;

	// Let's start recording in flash (erase the page)
	USE_EEPROM_AND_CHECK(flashStartSettingsPage(alterPage, ++activePageSN), resultFlags);

// Logging block
#if defined(LOGGING_ENABLED)
	UL_logMessage(Logger_Severity_Info, "Flash STARTED");
#endif
// End of logging block


	// To save RAM let's limit the lifetime
	// of large stack variables to blocks of code.
	//////////////////////////////////////////////
	// System configuration block
	{
		// Simply copy the specified (or the default one) configuration.
		System_Config newSystemConfig;
		if (replacementSettings != nullptr) {
			newSystemConfig = *replacementSettings;
			lockSubstitutionMatrix = (newSystemConfig.behaviorConfig.configFlags & Config_Flag_Lock_Substitution_Matrix) != 0;
		} else {
			if ((activePage != nullptr) && (activePage->systemConfig.okMarker == SETTINGS_COMPLETENESS_MARKER)) {
				newSystemConfig = activePage->systemConfig.config;
			} else {
				// There's no any configuration yet...
				memset(&newSystemConfig, 0, sizeof(System_Config));
				updateSystemBehavior = true;	// ...so save current user settings.
				rescanHardware = true;			// ...and update normalHLPBVoltage.
			}
		}

		// Override the hardware properties and also
		// save current behavior in case it was not received
		// from the host.
		prepareSystemConfig(&newSystemConfig, updateSystemBehavior);

		// Update the reference value of the Fan power supply circuit voltage.
		if (rescanHardware) {
			newSystemConfig.normalHLPBVoltage = hiVMeter.getVoltage();

// Logging block
#if defined(LOGGING_ENABLED)
			// Fan PS bus voltage
			UL_logMessageWithQFormatParam(Logger_Severity_None, "Fan PS bus voltage", newSystemConfig.normalHLPBVoltage, ADC_CHANNEL_FRACTION_BITS);
#endif
// End of logging block

		} else if (activePage != nullptr) {
			// Or override it with the previous one
			newSystemConfig.normalHLPBVoltage = activePage->systemConfig.config.normalHLPBVoltage;
		}

		// Write System Configuration block
		USE_EEPROM_AND_CHECK(flashWriteSystemConfigToPage(alterPage, &newSystemConfig), resultFlags);
	}

// Logging block
#if defined(LOGGING_ENABLED)
	UL_logMessage(Logger_Severity_Info, "System Configuration DONE");
#endif
// End of logging block


	//////////////////////////////////////////////
	// Fans configuration block
	{
		uint32_t warmUpDuration[FANS_COUNT];
		memset(warmUpDuration, 0, sizeof(warmUpDuration));

		// If we need to re-scan the fans, we can try to start warming them up in advance.
		if (rescanFans && !rescanHardware && (fanIndex >= FANS_COUNT)) {
			for (uint8_t i = 0; i < FANS_COUNT; i++) {
				// We can try to start it for pre-warming only when
				// some HW configuration has already been applied to the fan.
				Fan_Config * currentFanConfig = fans[i].configuration;

				if (currentFanConfig != nullptr) {

// Logging block
#if defined(LOGGING_ENABLED)
					UL_logMessageWithShortIntParam(Logger_Severity_Info, "Start warming fan", i);
#endif
// End of logging block

					if (fans[i].prewarmFan(currentFanConfig, progressDelegate) == HAL_OK) {
						warmUpDuration[i] = HAL_GetTick();
					} else {
						resultFlags |= CC_Flag_Prewarm_Failed;
					}
				}
			}
		}

		//
		for (uint8_t i = 0; i < FANS_COUNT; i++) {
			Fan_Config newFanConfig;
			const Fan_Config * savedFanConfig = getFanConfig(activePage, nullptr, &i);

			// Get the actual fan config...
			if ((fanIndex == i) && (replacementFanConfig != nullptr)) {
				newFanConfig = *replacementFanConfig;
			} else if (savedFanConfig != nullptr) {
				newFanConfig = *savedFanConfig;
			} else {
				// .. or prepare the new one.
				Fan_MAX31790_DS1841::prepareConfig(&newFanConfig, nullptr);
				// Recreate Fan's name
				getNewFanName(alterPage, newFanConfig.name, Fan_base::baseName(), i + 1);
				//currentFanConfig.configOK = SETTINGS_COMPLETENESS_MARKER;
			}

			// Scanning is performed either for all fans, or for a specific one.
			if ((fanIndex >= FANS_COUNT) || (fanIndex == i)) {

				// Re-scanning the hardware capabilities of the board
				if (rescanHardware) {

// Logging block
#if defined(LOGGING_ENABLED)
					UL_logMessageWithShortIntParam(Logger_Severity_Info, "Hardware scan started for Fan", i);
#endif
// End of logging block

					if (fans[i].scanHardware(&newFanConfig, progressDelegate) != HAL_OK) {
						// Hardware scan error
						resultFlags |= CC_Flag_HW_Failed;
						if (progressDelegate != nullptr) progressDelegate(Fan_Scan_Task_Hardware, 0xFF, &fans[i], &newFanConfig);

// Logging block
#if defined(LOGGING_ENABLED)
						UL_logMessageWithShortIntParam(Logger_Severity_Error, "Hardware scan failed for Fan", i);
#endif
// End of logging block

					} else {

// Logging block
#if defined(LOGGING_ENABLED)
						// Fan #
						UL_logMessageWithShortIntParam(Logger_Severity_Info, "Hardware scan results for Fan", i);
						// Hardware capabilities
						UL_logMessageWithLongHexParam(Logger_Severity_None, "Hardware capabilities", newFanConfig.hardware);
						// Min voltage
						UL_logMessageWithQFormatParam(Logger_Severity_None, "Min Voltage", newFanConfig.minVoltage, ADC_CHANNEL_FRACTION_BITS);
						// Min voltage Control Value
						UL_logMessageWithShortIntParam(Logger_Severity_None, "MinV Control", newFanConfig.minVoltageControl);
						// Max voltage
						UL_logMessageWithQFormatParam(Logger_Severity_None, "Max Voltage", newFanConfig.maxVoltage, ADC_CHANNEL_FRACTION_BITS);
						// Max voltage Control Value
						UL_logMessageWithShortIntParam(Logger_Severity_None, "MaxV Control", newFanConfig.maxVoltageControl);
#endif
// End of logging block

					}
				}

				// Re-scanning the connected fan capabilities
				if (rescanFans) {
					if (warmUpDuration[i] != 0) {
						// warmUpDuration[i] = HAL_GetTick() - warmUpDuration[i];
						// I wonder if the compiler converts this line to the
						// following by default? And which option is more effective?
						warmUpDuration[i] = ~warmUpDuration[i];
						warmUpDuration[i] += HAL_GetTick();
					}

// Logging block
#if defined(LOGGING_ENABLED)
					UL_logMessageWithShortIntParam(Logger_Severity_Info, "Model scan started for Fan", i);
#endif
// End of logging block

					if (fans[i].scanFan(&newFanConfig, progressDelegate, warmUpDuration[i]) != HAL_OK) {
						// Fan scan error
						resultFlags |= CC_Flag_Fan_Failed;
						if (progressDelegate != nullptr) progressDelegate(Fan_Scan_Task_Fan, 0xFF, &fans[i], &newFanConfig);

// Logging block
#if defined(LOGGING_ENABLED)
						UL_logMessageWithShortIntParam(Logger_Severity_Error, "Model scan failed for Fan", i);
#endif
// End of logging block

					} else {

// Logging block
#if defined(LOGGING_ENABLED)
						// Fan #
						UL_logMessageWithShortIntParam(Logger_Severity_Info, "Model scan results for Fan", i);
						// Fan capabilities
						UL_logMessageWithLongHexParam(Logger_Severity_None, "Fan capabilities", newFanConfig.model);
						// Top PPM
						UL_logMessageWithShortIntParam(Logger_Severity_None, "Max PPM (~RPM * 2)", newFanConfig.maxPPM);
						// PWM parameters block
						UL_logMessage(Logger_Severity_None, "--- PWM Parameters ---");
						// PWM Spin-up option
						UL_logMessageWithShortIntParam(Logger_Severity_None, "Spin-up option", (newFanConfig.options >> Fan_Options_Spin_PWM_Bit_Shift) & Fan_Options_Spin_Mask);
						// Min Control Value
						UL_logMessageWithShortIntParam(Logger_Severity_None, "Min Control", newFanConfig.minPWMControl);
						// Penultimate PPM
						UL_logMessageWithShortIntParam(Logger_Severity_None, "Penultimate PPM", newFanConfig.pwmPenultimatePPM);
						// Min PPM
						UL_logMessageWithShortIntParam(Logger_Severity_None, "Min PPM (~RPM * 2)", newFanConfig.pwmControlledMinPPM);
						// DC-DC parameters block
						UL_logMessage(Logger_Severity_None, "--- DC-DC Parameters ---");
						// DC-DC Spin-up option
						UL_logMessageWithShortIntParam(Logger_Severity_None, "Spin-up option", (newFanConfig.options >> Fan_Options_Spin_PWR_Bit_Shift) & Fan_Options_Spin_Mask);
						// Min Control Value
						UL_logMessageWithShortIntParam(Logger_Severity_None, "Min Control", newFanConfig.minPWRControl);
						// Min PPM
						UL_logMessageWithShortIntParam(Logger_Severity_None, "Min PPM (~RPM * 2)", newFanConfig.pwrControlledMinPPM);
						// Model features block
						if ((Fan_base::configFlags & Fan_Config_Flag_Fan_Model_Detection) != 0) {
							UL_logMessage(Logger_Severity_None, "--- Model Parameters ---");
							const char unknownString[] = "UNKNOWN";
							// Model size
							UL_logMessageWithStringParam(Logger_Severity_None, "Size",
									((newFanConfig.model & Fan_Model_Size_Detected) == 0) ? unknownString :
									((newFanConfig.model & Fan_Model_Size_Large) != 0) ? "LARGE" : "SMALL");
							// Model power
							UL_logMessageWithStringParam(Logger_Severity_None, "Power",
									((newFanConfig.model & Fan_Model_Power_Detected) == 0) ? unknownString :
									((newFanConfig.model & Fan_Model_Power_High) != 0) ? "HIGH" : "LOW");
							// Model class
							UL_logMessageWithStringParam(Logger_Severity_None, "Class",
									((newFanConfig.model & Fan_Model_Class_Detected) == 0) ? unknownString :
									((newFanConfig.model & Fan_Model_Class_Server) != 0) ? "SERVER" : "HOME");
						}
#endif
// End of logging block

					}
				}

				if (rescanHardware || rescanFans) {
					// Choose the best Control Mode according to the new configuration.
					fans[i].chooseBestControlModeForConfig(&newFanConfig);

					// If several Fans are being scanned and this is not the last of them,
					// then let's turn on the warm-up mode at 50% power again so that
					// we do not overheat the entire system while scanning other Fans.
					// We can easily ignore the result of this operation.
					if ((fanIndex >= FANS_COUNT) && (i != (FANS_COUNT - 1))) fans[i].leaveAt50Percent(&newFanConfig);
				}
			}

			// Write to Flash
			USE_EEPROM_AND_CHECK(flashWriteFanConfigToPage(alterPage, &newFanConfig, i), resultFlags);

			// Count discovered Fans
			if (newFanConfig.model != Fan_Model_Unconnected) fansDiscovered++;
		}
	}

// Logging block
#if defined(LOGGING_ENABLED)
	UL_logMessage(Logger_Severity_Info, "Fans Configuration DONE");
#endif
// End of logging block


	//////////////////////////////////////////////
	// Fan Substitution Matrix block
	{
		Fan_Substitution_Matrix newMatrix;

		if (replacementSubstitutionMatrix != nullptr) {
			newMatrix = *replacementSubstitutionMatrix;
		} else {
			if ((!lockSubstitutionMatrix && rescanFans) ||
					(activePage == nullptr) || (activePage->substitutionMatrix.okMarker != SETTINGS_COMPLETENESS_MARKER)) {
				// Recalculate Substitution Matrix
				recalculateSubstitutionMatrix(newMatrix, alterPage, fansDiscovered);
			} else {
				memcpy(&newMatrix, &activePage->substitutionMatrix.matrix, sizeof(Fan_Substitution_Matrix));
			}
		}

		// Write to Flash
		USE_EEPROM_AND_CHECK(flashWriteSubstitutionMatrixToPage(alterPage, &newMatrix), resultFlags);

// Logging block
#if defined(LOGGING_ENABLED)
		UL_logMessageWithHexArrayParam(Logger_Severity_Info, "Substitution Matrix DONE", newMatrix.data, FAN_SUBSTITUTION_MATRIX_SIZE);
#endif
// End of logging block
	}


	//////////////////////////////////////////////
	// Sensors configuration block
	// (here we do not need an additional block,
	// since all large variables are located inside
	// the 'if (rescanSensors)' block).
	//
	// Process the list of sensors
	const TSensor_Config * savedSensorConfig;
	uint8_t newSensorIndex = 0;
	bool noRoomForSensor = false;

	if (rescanSensors) {
		// We will use the search context located in the Stack
		// so that we can freely use the Heap for our needs.
		// We'll also use this structure to temporarily store
		// the serial numbers of the controller's Sensors.
		OneWire_Search_Context searchContext;
		TSensor_Config newSensorConfig;

// Logging block
#if defined(LOGGING_ENABLED)
		UL_logMessage(Logger_Severity_Info, "Sensor scan started");
#endif
// End of logging block

#if defined(USE_DIGIPOTS_TSENSOR)

		// DS1841 internal sensors
		for (uint8_t i = 0; i < DCDC_COUNT; i++) {
			prepareSensorSerialNumber(searchContext.tempROM, TSensor_DS1841::baseName(), i);
			savedSensorConfig = getSensorConfig(activePage, nullptr, searchContext.tempROM);

			if (savedSensorConfig != nullptr) {
				// Use saved config if we have one
				newSensorConfig = *savedSensorConfig;
			} else {
				TSensor_DS1841::prepareConfig(&newSensorConfig, nullptr, searchContext.tempROM);
				// Recreate and check Sensor's name
				getNewSensorName(alterPage, newSensorConfig.name, TSensor_DS1841::baseName(), i + FIRST_DCDC_FAN_INDEX, false);
				//currentSensorConfig.configOK = SETTINGS_COMPLETENESS_MARKER;
			}

// Logging block
#if defined(LOGGING_ENABLED)
			UL_logMessageWithHexArrayParam(Logger_Severity_Info, "DigiPOT Sensor found", newSensorConfig.serialNumber, TSENSOR_SN_SIZE);
#endif
// End of logging block

			// Write config to Flash
			if ((noRoomForSensor = (newSensorIndex >= MAX_SENSORS_COUNT))) {
				goto SensorsDone;
			} else {
				USE_EEPROM_AND_CHECK(flashWriteTSensorConfigToPage(alterPage, &newSensorConfig, newSensorIndex++), resultFlags);
			}
		}

#endif

#if defined(USE_MCU_TSENSOR)

		// MCU internal sensor
		prepareSensorSerialNumber(searchContext.tempROM, TSensor_MCU::baseName(), 0);
		savedSensorConfig = getSensorConfig(activePage, nullptr, searchContext.tempROM);

		if (savedSensorConfig != nullptr) {
			// Use saved config if we have one
			newSensorConfig = *savedSensorConfig;
		} else {
			// We don't need to change the standard name of this sensor
			TSensor_MCU::prepareConfig(&newSensorConfig, nullptr, searchContext.tempROM);
			// Check the name
			getNewSensorName(alterPage, newSensorConfig.name, TSensor_MCU::baseName(), 1, true);
			//currentSensorConfig.configOK = SETTINGS_COMPLETENESS_MARKER;
		}

// Logging block
#if defined(LOGGING_ENABLED)
		UL_logMessageWithHexArrayParam(Logger_Severity_Info, "MCU Sensor found", newSensorConfig.serialNumber, TSENSOR_SN_SIZE);
#endif
// End of logging block

		// Write config to Flash
		if ((noRoomForSensor = (newSensorIndex >= MAX_SENSORS_COUNT))) {
			goto SensorsDone;
		} else {
			USE_EEPROM_AND_CHECK(flashWriteTSensorConfigToPage(alterPage, &newSensorConfig, newSensorIndex++), resultFlags);
		}

#endif

		// Scan OneWire lines for the DS18B20 sensors
		for (uint8_t i = 0; (i < SENSOR_LINES_COUNT) && ((resultFlags & CC_Flag_Too_Many_Sensors_Detected) == 0); i++) {
			uint8_t index = 1;
			DS18B20 * currentLine = &sensorLines[i];
			currentLine->restartSearch(searchContext);

			while(currentLine->findNext(searchContext, false) == HAL_OK) {
				savedSensorConfig = getSensorConfig(activePage, nullptr, searchContext.tempROM);

				if (savedSensorConfig != nullptr) {
					// Use saved config if we have one
					newSensorConfig = *savedSensorConfig;
				} else {
					TSensor_DS18B20_ML::prepareConfig(&newSensorConfig, nullptr, searchContext.tempROM);
					// Recreate and check Sensor's name
					index = getNewSensorName(alterPage, newSensorConfig.name, TSensor_DS18B20_ML::baseName(), index, false);
					//currentSensorConfig.configOK = SETTINGS_COMPLETENESS_MARKER;
				}

// Logging block
#if defined(LOGGING_ENABLED)
				UL_logMessageWithHexArrayParam(Logger_Severity_Info, "DS18B20 Sensor found", newSensorConfig.serialNumber, TSENSOR_SN_SIZE);
#endif
// End of logging block

				// Write config to Flash
				if ((noRoomForSensor = (newSensorIndex >= MAX_SENSORS_COUNT))) {
					goto SensorsDone;
				} else {
					USE_EEPROM_AND_CHECK(flashWriteTSensorConfigToPage(alterPage, &newSensorConfig, newSensorIndex++), resultFlags);
				}

			}

			if (searchContext.crcErrorFlag) resultFlags |= CC_Flag_Sensor_Failed;
		}
	}

	// Here we must either completely copy the previously saved sensors,
	// or re-save only the software ones.
	while ((savedSensorConfig = getSensorConfig(activePage, nullptr, nullptr, &searchIndex, nullptr)) != nullptr) {
		searchIndex++;	// Let's increment it right away so we don't have to think about it later.
		if (!rescanSensors || TSensor_base::isSoftwareSensor(savedSensorConfig)) {
			if ((replacementSensorConfig != nullptr) && !addSensor &&
				(memcmp(replacementSensorConfig->serialNumber, savedSensorConfig->serialNumber, TSENSOR_SN_SIZE) == 0)) {
				savedSensorConfig = replacementSensorConfig;
			}

			if ((deleteSensorSN != nullptr) && (memcmp(deleteSensorSN, savedSensorConfig->serialNumber, TSENSOR_SN_SIZE) == 0)) {

// Logging block
#if defined(LOGGING_ENABLED)
				UL_logMessageWithHexArrayParam(Logger_Severity_Info, "Delete Sensor", deleteSensorSN, TSENSOR_SN_SIZE);
#endif
// End of logging block

				continue;
			}

			// Write config to Flash
			if ((noRoomForSensor = (newSensorIndex >= MAX_SENSORS_COUNT))) {
				goto SensorsDone;
			} else {
				USE_EEPROM_AND_CHECK(flashWriteTSensorConfigToPage(alterPage, savedSensorConfig, newSensorIndex++), resultFlags);
			}
		}
	}

	if (addSensor && (replacementSensorConfig != nullptr)) {

// Logging block
#if defined(LOGGING_ENABLED)
		UL_logMessageWithHexArrayParam(Logger_Severity_Info, "Add Sensor", replacementSensorConfig->serialNumber, TSENSOR_SN_SIZE);
#endif
// End of logging block

		// Write config to Flash
		if ((noRoomForSensor = (newSensorIndex >= MAX_SENSORS_COUNT))) {
			goto SensorsDone;
		} else {
			USE_EEPROM_AND_CHECK(flashWriteTSensorConfigToPage(alterPage, replacementSensorConfig, newSensorIndex /* ++ */), resultFlags);
		}
	}


SensorsDone:
	// Check if we've run out of available Flash
	if (noRoomForSensor) {

// Logging block
#if defined(LOGGING_ENABLED)
		UL_logMessage(Logger_Severity_Error, "Out of Flash (too many sensors)");
#endif
// End of logging block

		resultFlags |= CC_Flag_Too_Many_Sensors_Detected;
	}

// Logging block
#if defined(LOGGING_ENABLED)
	UL_logMessage(Logger_Severity_Info, "Sensors Configuration DONE");
#endif
// End of logging block


	// Now we have to finish flash page
	USE_EEPROM_AND_CHECK(flashFinalizeSettingsPage(alterPage), resultFlags);

// Logging block
#if defined(LOGGING_ENABLED)
	UL_logMessage(Logger_Severity_Info, "Flash FINALIZED");
#endif
// End of logging block


	return (resultFlags);
}


// A single entry point for reconfiguration operations.
// Allows both to make changes and load (activate) them.
// Operands:
//
// replacementSettings 			- System Configuration structure, to replace the current one.
// replacementFanConfig 		- The same for the Fan configuration (see fanIndex parameter).
// replacementSensorConfig		- Sensor configuration to replace an existing one or add a new sensor (addSensor).
// updateSystemBehavior			- Update System Behavior to the current one (screen rotation, brightness, buzzer option, etc.)
// rescanHardware				- Start hardware scanning for all Fan channels or for the specified one (fanIndex).
// rescanFans					- Same as rescanHardware, but for the Fan model scanning.
// rescanSensors				- Rescan DS18B20 sensors and save the software ones.
// addSensor					- Add new software sensor with configuration? specified by replacementSensorConfig.
// fanIndex						- Rescan/Update specified Fan or all Fans if fanIndex = FANS_COUNT
// sensorToDelete				- Delete Sensor with specified serial number.
// showDoneMessage				- Show Done message if all OK.
// showErrorMessage				- Show Error message if any.
//
// If no parameters are set (NULL/false), the most complete of the saved configurations will be loaded.
bool updateConfig(System_Config * replacementSettings, Fan_Config * replacementFanConfig, Fan_Substitution_Matrix * replacementSubstitutionMatrix, TSensor_Config * replacementSensorConfig,
				bool updateSystemBehavior, bool rescanHardware, bool rescanFans, bool rescanSensors, bool addSensor,
				uint8_t fanIndex, uint8_t * sensorToDelete, bool showDoneMessage, bool showErrorMessage) {

	// There may be many lengthy operations ahead, so we will reset the watchdog
	// from within the interrupt to prevent it from triggering.
	// For now, this is a compromise solution, given that
	// the configuration is performed quite infrequently.
	longOpProcessing = true;

	uint32_t activePageSN;
	const Settings_Page * alterPage;
	const Settings_Page * activePage = flashGetSettingsPage(activePageSN, alterPage);
	bool noSavedConfig = (activePage == nullptr);
	systemState.configurationChangeFlags = CC_Flag_Empty;

	if (noSavedConfig) {
		if (!rescanHardware) {
			systemState.configurationChangeFlags |= CC_Flag_No_Config;

			if (showErrorMessage) {

#if defined(DISPLAY_ENABLED)
				displayMessage(noConfigMessage, 0, 0, COLOR_ALARM);
#endif

// Logging block
#if defined(LOGGING_ENABLED)
				UL_logMessage(Logger_Severity_Warning, "No active configuration found");
#endif
// End of logging block

			}

			// Notify the host that all configuration parts have changed
			systemState.userActionFlags |= (UA_Flag_System_Config_Changed |
											UA_Flag_Substitution_Matrix_Changed |
											UA_Flag_Fan_Config_Changed |
											UA_Flag_Sensor_Config_Changed);

			systemState.configurationChangeFlags |= CC_Flag_Initialization_Complete;

			longOpProcessing = false;
			return (false);

		} else {
			alterPage = reinterpret_cast<const Settings_Page*>(configurationPages);
			updateSystemBehavior = true;
		}
	}

	uint32_t resultFlags;
	// Write new page only if something changes
	bool configShouldBeChanged = (updateSystemBehavior || rescanHardware || rescanFans || rescanSensors ||
			(replacementSettings != nullptr) || (replacementFanConfig != nullptr) || (replacementSubstitutionMatrix != nullptr) ||
			(replacementSensorConfig != nullptr) || (sensorToDelete != nullptr));

	if (configShouldBeChanged) {
		// Setting the required flags
		uint32_t progressFlags = CC_Flag_Empty;

		if (rescanHardware) progressFlags |= CC_Flag_HW_In_Progress;
		if (rescanFans) progressFlags |= CC_Flag_Fan_In_Progress;
		if (rescanSensors) progressFlags |= CC_Flag_Sensor_In_Progress;

		systemState.configurationChangeFlags |= progressFlags;
		if ((rescanHardware || rescanFans) && (fanIndex >= FANS_COUNT)) systemState.configurationChangeFlags |= CC_Flag_All_Fan_Channels_Involved;

		// Perform update
		resultFlags = updateSettingsPage(activePage, alterPage, activePageSN,
				replacementSettings, replacementFanConfig, replacementSubstitutionMatrix, replacementSensorConfig,
				updateSystemBehavior, rescanHardware, rescanFans, rescanSensors, addSensor,
				fanIndex, sensorToDelete, &updateScanning);

		// Update result flags
		systemState.configurationChangeFlags &= ~(progressFlags | CC_Flag_All_Fan_Channels_Involved);
		systemState.configurationChangeFlags |= resultFlags;

		if ((resultFlags & CC_Flag_EEPROM_Failure) != 0) {

// Logging block
#if defined(LOGGING_ENABLED)
			UL_logMessage(Logger_Severity_Error, "FATAL FLASH ERROR!");
#endif
// End of logging block

			Error_Handler();
		}

		activePage = alterPage;
	}

	// Determine which settings to re-apply, and for which to simply update the pointer.
	bool applySystemConfig = updateSystemBehavior || !configShouldBeChanged || (replacementSettings != nullptr);
	bool applyFanSettings = rescanHardware || rescanFans || !configShouldBeChanged || (replacementFanConfig != nullptr);
	rescanSensors = rescanSensors || !configShouldBeChanged || addSensor || (sensorToDelete != nullptr);
	uint8_t * reloadSensorSN = (replacementSensorConfig == nullptr) ? nullptr : replacementSensorConfig->serialNumber;

	// If something has changed, then set the appropriate flags.
	if (applySystemConfig) systemState.userActionFlags |= UA_Flag_System_Config_Changed;
	if (noSavedConfig || applyFanSettings) systemState.userActionFlags |= UA_Flag_Fan_Config_Changed;
	if (noSavedConfig || !configShouldBeChanged || (replacementSubstitutionMatrix != nullptr) ||
			(rescanFans && ((systemState.behaviorConfig.configFlags & Config_Flag_Lock_Substitution_Matrix) == 0))) systemState.userActionFlags |= UA_Flag_Substitution_Matrix_Changed;
	if (noSavedConfig || rescanSensors || (reloadSensorSN != nullptr)) systemState.userActionFlags |= UA_Flag_Sensor_Config_Changed;

	// Load updated configuration
	resultFlags = loadSettingsPage(activePage, applySystemConfig, applyFanSettings, rescanSensors, fanIndex, reloadSensorSN, !configShouldBeChanged);

	// Update result flags
	systemState.configurationChangeFlags |= resultFlags;

#if defined (DISPLAY_ENABLED)
	// Check for active and inactive sensors
	if (rescanSensors || (reloadSensorSN != nullptr)) analyzeSensorList();
#endif

	// Perform logging and messaging
	if (systemState.configurationChangeFlags != CC_Flag_Empty) {
#if defined (DISPLAY_ENABLED)
		if (showErrorMessage) displayMessage(errorMessage, (uint16_t)(systemState.configurationChangeFlags & 0xFFFF), 0, COLOR_ALARM);
#endif

// Logging block
#if defined(LOGGING_ENABLED)
		UL_logMessageWithLongHexParam(Logger_Severity_Error, "Code", systemState.configurationChangeFlags);
#endif
// End of logging block

	} else {
#if defined (DISPLAY_ENABLED)
		if (showDoneMessage) displayMessage(doneMessage, 0, 0, COLOR_OK);
#endif

// Logging block
#if defined(LOGGING_ENABLED)
		UL_logMessageWithStringParam(Logger_Severity_Info, "Device Configuration", configShouldBeChanged ? "updated" : "loaded");
#endif
// End of logging block
	}

	systemState.configurationChangeFlags |= CC_Flag_Initialization_Complete;
	longOpProcessing = false;
	return (true);
}


// Process User Action requests
void processUserRequest() {
// Logging block
#if defined(LOGGING_ENABLED)
	const char actionString[] = "Action requested";
	const char * actionName = nullptr;
#endif
// End of logging block

	// Prepare host's requests processing
	uint8_t scanAction = SA_Flag_Empty;
	if (systemState.userActionRequest != 0) {
		uint16_t actionRequest = systemState.userActionRequest & ~UA_Request_Initiated_By_Menu;

#if defined(DISPLAY_ENABLED)
		// Configuration change request
		if ((actionRequest >= UA_Request_System_Config_Update) &&
			(actionRequest <= UA_Request_Factory_Reset)) {
			displaySection = DISPLAY_HOST_REQUEST_SECTION;
		} else
#endif
		// Scan request
		if ((actionRequest & UA_Request_Scan_Mask) != 0) {
			if ((systemState.userActionRequest & UA_Request_Hardware_Scan) != 0) {
				// Hardware scan request
				scanAction = SA_Flag_Hardware_Scan_Request;

#if defined(DISPLAY_ENABLED)
				displaySection = DISPLAY_HARDWARE_SCAN_SECTION;
#endif
			} else if ((actionRequest & UA_Request_Fan_Scan) != 0) {
				// Fan scan request
				scanAction = SA_Flag_Fan_Scan_Request;

#if defined(DISPLAY_ENABLED)
				displaySection = DISPLAY_FAN_SCAN_SECTION;
#endif
			} else if ((actionRequest & UA_Request_Sensor_Scan) != 0) {
				// Sensor scan request
				scanAction = SA_Flag_Sensor_Scan_Request;

#if defined(DISPLAY_ENABLED)
				displaySection = DISPLAY_SENSOR_SCAN_SECTION;
#endif
			}
		}

#if defined(DISPLAY_ENABLED)
		// Update display environment if the current section was changed
		if (displaySection != displayLastSection) {
			displaySectionTimer = 0;
			sectionDisplayDuration = SECTION_DISPLAY_DURATION;
			sectionCurrentItemIndex = displaySelectedItemIndex = displayLastSelectedItemIndex = 0;
			displayFlags |= Display_Flag_Window_Needs_ToBe_Cleared;
		}
#endif
	}


	bool result = false;
	bool initiatedByMenu = ((systemState.userActionRequest & UA_Request_Initiated_By_Menu) != 0);
	bool resetRequested = false;
	systemState.userActionRequest &= ~UA_Request_Initiated_By_Menu;

	// Hardware / Fans / Sensors scan
	if ((systemState.userActionRequest & UA_Request_Scan_Mask) != 0) {

// Logging block
#if defined(LOGGING_ENABLED)
		actionName = "SCAN";
		UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block

		bool scanHardware = (scanAction == SA_Flag_Hardware_Scan_Request);
		bool scanFan = (scanAction == SA_Flag_Fan_Scan_Request);
		bool scanSensor = (scanAction == SA_Flag_Sensor_Scan_Request);

		result = updateConfig(nullptr,						// replacementSettings
							nullptr,						// replacementFanConfig
							nullptr,						// replacementSubstitutionMatrix
							nullptr,						// replacementSensorConfig
							initiatedByMenu,				// updateSystemBehavior
							scanHardware,					// rescanHardware
							scanFan,						// rescanFans
							scanSensor,						// rescanSensors
							false,							// addSensor
							selectedObjectIndex,			// fanIndex
							nullptr,						// sensorToDelete
							scanSensor,						// showDoneMessage
							true);							// showErrorMessage

		if (scanSensor || scanFan || scanHardware) {
			__disable_irq();
			if (scanSensor) systemState.userActionRequest &= ~UA_Request_Sensor_Scan;
			if (scanFan) systemState.userActionRequest &= ~UA_Request_Fan_Scan;
			if (scanHardware) systemState.userActionRequest &= ~UA_Request_Hardware_Scan;
			__enable_irq();
		}

	} else {
		uint16_t processedRequest = systemState.userActionRequest;

		switch (processedRequest) {

			// Set new Fan Control Value
			case UA_Request_Set_Fan_Control_Value:
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "FAN CONTROL VALUE";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block
				result = (fans[selectedObjectIndex].setControl(safeBuffer.wordParameter) == HAL_OK);
				break;


			// Set Fan Control Basis (power)
			case UA_Request_Set_Fan_Control_Basis:
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "FAN CONTROL BASIS";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block
				result = (fans[selectedObjectIndex].setControlBasis(safeBuffer.wordParameter) == HAL_OK);
				break;


			// Change Fan Control Mode (Disabled/ON-OFF/DC-DC/PWM)
			case UA_Request_Set_Fan_Control_Mode:
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "FAN CONTROL MODE";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block
				result = (fans[selectedObjectIndex].setControlMode(safeBuffer.byteParameter) == HAL_OK);
				break;


			// Change Fan Operation Mode (Normal/Manual/Silent/Turbo/Stopped)
			case UA_Request_Set_Fan_Operation_Mode:
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "FAN OPERATION MODE";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block
				result = (fans[selectedObjectIndex].setOperationMode(safeBuffer.byteParameter) == HAL_OK);
				break;


			// Reset Fan state
			case UA_Request_Reset_Fan:
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "RESET FAN";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block
				fans[selectedObjectIndex].resetState();
				result = true;
				break;


			// Reset Sensor state
			case UA_Request_Reset_Sensor: {
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "RESET SENSOR";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block
				TSensor_base * sensor = getSensorBySerialNumber(safeBuffer.sensorConfig.serialNumber, TSensor_Type_Unknown);
				if (sensor != nullptr) {
					sensor->resetState();
					result = true;
				}
				break;
			}


			// Update System Configuration
			case UA_Request_System_Config_Update:
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "SYS CONFIG UPDATE";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block

				// If a clean configuration Flash page has been started,
				// we will write new config directly into it.
				// Alternatively, we will create a configuration update request
				// for the main thread with the replacement of only the System Settings.
				if (((systemState.userActionFlags & UA_Flag_Config_Page_Started) != 0) && (newSettingsPage != nullptr)) {
					result = (flashWriteSystemConfigToPage(newSettingsPage, &safeBuffer.systemConfig) == HAL_OK);
					if (result) {
						systemState.configurationChangeFlags &= ~CC_Flag_EEPROM_Failure;
					} else {
						systemState.configurationChangeFlags |= CC_Flag_EEPROM_Failure;
					}
				} else {
					result = updateConfig(&safeBuffer.systemConfig,		// replacementSettings
										nullptr,						// replacementFanConfig
										nullptr,						// replacementSubstitutionMatrix
										nullptr,						// replacementSensorConfig
										false,							// updateSystemBehavior
										false,							// rescanHardware
										false,							// rescanFans
										false,							// rescanSensors
										false,							// addSensor
										FANS_COUNT,						// fanIndex
										nullptr,						// sensorToDelete
										true,							// showDoneMessage
										true);							// showErrorMessage
				}
				break;


			// Update Fan Configuration
			// Similar to the previous request
			case UA_Request_Fan_Config_Update:
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "FAN CONFIG UPDATE";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block
				if (((systemState.userActionFlags & UA_Flag_Config_Page_Started) != 0) && (newSettingsPage != nullptr)) {
					result = (flashWriteFanConfigToPage(newSettingsPage, &safeBuffer.fanConfig, selectedObjectIndex) == HAL_OK);
					if (result) {
						systemState.configurationChangeFlags &= ~CC_Flag_EEPROM_Failure;
					} else {
						systemState.configurationChangeFlags |= CC_Flag_EEPROM_Failure;
					}
				} else {
					result = updateConfig(nullptr,						// replacementSettings
										&safeBuffer.fanConfig,			// replacementFanConfig
										nullptr,						// replacementSubstitutionMatrix
										nullptr,						// replacementSensorConfig
										false,							// updateSystemBehavior
										false,							// rescanHardware
										false,							// rescanFans
										false,							// rescanSensors
										false,							// addSensor
										selectedObjectIndex,			// fanIndex
										nullptr,						// sensorToDelete
										true,							// showDoneMessage
										true);							// showErrorMessage
				}
				break;


			// Update Fan Substitution Matrix
			// Similar to the previous request
			case UA_Request_Substitution_Matrix_Update:
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "SUBSTITUTION MATRIX UPDATE";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block
				if (((systemState.userActionFlags & UA_Flag_Config_Page_Started) != 0) && (newSettingsPage != nullptr)) {
					result = (flashWriteSubstitutionMatrixToPage(newSettingsPage, &safeBuffer.substitutionMatrix) == HAL_OK);
					if (result) {
						systemState.configurationChangeFlags &= ~CC_Flag_EEPROM_Failure;
					} else {
						systemState.configurationChangeFlags |= CC_Flag_EEPROM_Failure;
					}
				} else {
					result = updateConfig(nullptr,						// replacementSettings
										nullptr,						// replacementFanConfig
										&safeBuffer.substitutionMatrix,	// replacementSubstitutionMatrix
										nullptr,						// replacementSensorConfig
										false,							// updateSystemBehavior
										false,							// rescanHardware
										false,							// rescanFans
										false,							// rescanSensors
										false,							// addSensor
										selectedObjectIndex,			// fanIndex
										nullptr,						// sensorToDelete
										true,							// showDoneMessage
										true);							// showErrorMessage
				}
				break;


			// Update Sensor Configuration
			// Similar to the previous request
			case UA_Request_Sensor_Config_Update:
			case UA_Request_Sensor_Add:
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = (processedRequest == UA_Request_Sensor_Add) ? "ADD SENSOR" : "SENSOR CONFIG UPDATE";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block
				if (((systemState.userActionFlags & UA_Flag_Config_Page_Started) != 0) && (newSettingsPage != nullptr)) {
					// Get and check the index of new sensor config
					uint8_t objectIndex;
					if (getSensorConfig(newSettingsPage, nullptr, safeBuffer.sensorConfig.serialNumber, nullptr, &objectIndex) == nullptr) {
						if (objectIndex < MAX_SENSORS_COUNT) {
							result = (flashWriteTSensorConfigToPage(newSettingsPage, &safeBuffer.sensorConfig, objectIndex) == HAL_OK);
							if (result) {
								systemState.configurationChangeFlags &= ~CC_Flag_EEPROM_Failure;
							} else {
								systemState.configurationChangeFlags |= CC_Flag_EEPROM_Failure;
							}
						} else {
							systemState.configurationChangeFlags |= CC_Flag_Too_Many_Sensors_Detected;
						}
						systemState.configurationChangeFlags &= ~CC_Flag_Sensor_Config_Error;
					} else {
						systemState.configurationChangeFlags |= CC_Flag_Sensor_Config_Error;
					}
				} else {
					result = updateConfig(nullptr,						// replacementSettings
										nullptr,						// replacementFanConfig
										nullptr,						// replacementSubstitutionMatrix
										&safeBuffer.sensorConfig,		// replacementSensorConfig
										false,							// updateSystemBehavior
										false,							// rescanHardware
										false,							// rescanFans
										false,							// rescanSensors
										(processedRequest == UA_Request_Sensor_Add),	// addSensor
										FANS_COUNT,						// fanIndex
										nullptr,						// sensorToDelete
										true,							// showDoneMessage
										true);							// showErrorMessage
				}
				break;


			// Delete Sensor
			case UA_Request_Sensor_Remove:
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "DELETE SENSOR";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block
				result = updateConfig(nullptr,						// replacementSettings
									nullptr,						// replacementFanConfig
									nullptr,						// replacementSubstitutionMatrix
									nullptr,						// replacementSensorConfig
									false,							// updateSystemBehavior
									false,							// rescanHardware
									false,							// rescanFans
									false,							// rescanSensors
									false,							// addSensor
									FANS_COUNT,						// fanIndex
									safeBuffer.sensorConfig.serialNumber,	// sensorToDelete
									true,							// showDoneMessage
									true);							// showErrorMessage
				break;


			// Start New Configuration Page
			case UA_Request_Start_Clean_Config_Page: {
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "START CONFIG PAGE";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block

				// The possibility of a complete one-time configuration update
				// using commands from the host. After all the new configuration is written,
				// we need to finalize the page and then the new configuration page
				// is completed and loaded (activated) as the most recent one.
				// Get the next page and its SN
				uint32_t activePageSN;
				const Settings_Page * nextPage;
				flashGetSettingsPage(activePageSN, nextPage);

				// Never erase the page currently in use. This may happen if several pages
				// have been finalized without reloading the configuration. In this case,
				// the following page is used, even if it contains the most recent
				// (but not yet activated) configuration, since it will be replaced anyway.
				if (nextPage == activeConfigurationPage) {
					nextPage = &configurationPages[(nextPage - configurationPages + 1) % SETTINGS_PAGES_COUNT];
				}

				// Start recording in flash (erase the page)...
				result = (flashStartSettingsPage(nextPage, activePageSN + 1) == HAL_OK);

				if (result) {
					newSettingsPage = nextPage;
					systemState.userActionFlags |= UA_Flag_Config_Page_Started;
					systemState.configurationChangeFlags &= ~CC_Flag_EEPROM_Failure;
				} else {
					systemState.configurationChangeFlags |= CC_Flag_EEPROM_Failure;
				}
			}
			break;


			// Write Config OK Marker, 'close' Configuration Page and activate it
			case UA_Request_Finalize_Config_Page:
// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "FINALIZE CONFIG PAGE";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block

				result = (flashFinalizeSettingsPage(newSettingsPage) == HAL_OK);

				// The session is over in any case
				systemState.userActionFlags &= ~UA_Flag_Config_Page_Started;
				newSettingsPage = nullptr;

				if (result) {
					// The finalized page is now the most recent completed one,
					// so it will be selected and loaded as the active configuration.
					result = updateConfig(nullptr,						// replacementSettings
										nullptr,						// replacementFanConfig
										nullptr,						// replacementSubstitutionMatrix
										nullptr,						// replacementSensorConfig
										false,							// updateSystemBehavior
										false,							// rescanHardware
										false,							// rescanFans
										false,							// rescanSensors
										false,							// addSensor
										FANS_COUNT,						// fanIndex
										nullptr,						// sensorToDelete
										true,							// showDoneMessage
										true);							// showErrorMessage
				} else {
					systemState.configurationChangeFlags |= CC_Flag_EEPROM_Failure;
				}
				break;


			// Reload Configuration
			case UA_Request_Config_Reload:

// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "RELOAD CONFIG";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block
				result = updateConfig(nullptr,						// replacementSettings
									nullptr,						// replacementFanConfig
									nullptr,						// replacementSubstitutionMatrix
									nullptr,						// replacementSensorConfig
									false,							// updateSystemBehavior
									false,							// rescanHardware
									false,							// rescanFans
									false,							// rescanSensors
									false,							// addSensor
									FANS_COUNT,						// fanIndex
									nullptr,						// sensorToDelete
									true,							// showDoneMessage
									true);							// showErrorMessage
				break;


			// Erase all configuration pages and reboot
			case UA_Request_Factory_Reset:

// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "FACTORY RESET";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif

				result = true;
				for (uint8_t i = 0; i < SETTINGS_PAGES_COUNT; i++) {
					// Save the results
					if (flashEraseSettingsPage(&configurationPages[i]) != HAL_OK) result = false;
				}
				resetRequested = true;

				break;


			// Reset the device
			case UA_Request_Reset:

// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "RESET";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
				result = true;
				resetRequested = true;

				break;


			// Set a reduced configuration of Display and Buzzer settings
			case UA_Request_Set_Behavior_Config: {

// Logging block
#if defined(LOGGING_ENABLED)
				actionName = "CONFIG OPTIONS UPDATE";
				UL_logMessageWithStringParam(Logger_Severity_Info, actionString, actionName);
#endif
// End of logging block

#if defined(DISPLAY_ENABLED)
				bool displayRotationChanged = safeBuffer.behaviorConfig.displayRotation != systemState.behaviorConfig.displayRotation;
#endif

				result = applySystemBehaviorConfig(&safeBuffer.behaviorConfig);
#if defined(DISPLAY_ENABLED)
				if (displayRotationChanged) {
					displayFlags |= Display_Flag_Window_Needs_ToBe_Cleared;
					displayFlags &= ~(Display_Flag_Active_Sensors_Processing | Display_Flag_Active_Sensors_Done);
					displaySection = 0;
					displaySectionTimer = 0;
					sectionCurrentItemIndex = displaySelectedItemIndex = displayLastSelectedItemIndex = 0;
				}
#endif
			}
			break;


			default:
				// Ignore any wrong requests
				break;
		}

		// Clear the request only if it has not been replaced
		// by a new one (Reboot/Factory Reset) during processing
		__disable_irq();
		if (systemState.userActionRequest == processedRequest) systemState.userActionRequest = UA_Request_Empty;
		__enable_irq();
	}

	// Set the appropriate request result flags
	if (!result) {
		systemState.userActionFlags |= UA_Flag_Last_Config_Request_Error;

// Logging block
#if defined(LOGGING_ENABLED)
		UL_logMessageWithStringParam(Logger_Severity_Error, "Action failed", (actionName == nullptr) ? "?" : actionName);
#endif
// End of logging block

	} else {
		systemState.userActionFlags |= UA_Flag_Last_Config_Request_Done;

// Logging block
#if defined(LOGGING_ENABLED)
		UL_logMessageWithStringParam(Logger_Severity_Info, "Action done", actionName);
#endif
// End of logging block
	}

	// Any processed request extends the host configuration session
	if ((systemState.userActionFlags & UA_Flag_Config_Page_Started) != 0) configPageSessionTick = HAL_GetTick();

	if (resetRequested) {
		HAL_NVIC_SystemReset();
	}

	buttonPressCount = 0;

#if defined(DISPLAY_ENABLED)
	lastUpdateTick = HAL_GetTick();
#endif
}


// Abort the host configuration session if the host has stopped communicating.
// The partially written page remains incomplete and will be ignored.
void checkConfigSessionTimeout() {
	if ((systemState.userActionFlags & UA_Flag_Config_Page_Started) == 0) return;
	if ((HAL_GetTick() - configPageSessionTick) < CONFIG_PAGE_SESSION_TIMEOUT) return;

#if defined(LOGGING_ENABLED)
	bool sessionAborted = false;
#endif

	__disable_irq();
	if (systemState.userActionRequest == UA_Request_Empty) {
		systemState.userActionFlags &= ~UA_Flag_Config_Page_Started;
		newSettingsPage = nullptr;

#if defined(LOGGING_ENABLED)
		sessionAborted = true;
#endif
	}
	__enable_irq();

// Logging block
#if defined(LOGGING_ENABLED)
	if (sessionAborted) UL_logMessage(Logger_Severity_Warning, "Configuration session aborted");
#endif
// End of logging block
}


#if defined(LOGGING_ENABLED)
// Log the state of the USB stack and peripheral (USB freeze diagnostics).
// Device state: 1 = DEFAULT, 2 = ADDRESSED, 3 = CONFIGURED, 4 = SUSPENDED.
// HID state: 0 = IDLE, 1 = BUSY (the IN report hasn't been collected by the host).
// EP1R: STAT_TX = bits 5:4, STAT_RX = bits 13:12 (0 = DISABLED, 1 = STALL, 2 = NAK, 3 = VALID),
// DTOG_TX = bit 6, DTOG_RX = bit 14.
// CNTR: FSUSP = bit 3, LPMODE = bit 2 (the peripheral is in the suspend mode).
void logUSBState(const char * message) {
	USBD_CUSTOM_HID_HandleTypeDef * hhid = static_cast<USBD_CUSTOM_HID_HandleTypeDef *>(hUsbDeviceFS.pClassData);

	UL_logMessage(Logger_Severity_Warning, message);
	UL_logMessageWithLongHexParam(Logger_Severity_None, "Device state (old, current)", (hUsbDeviceFS.dev_old_state << 8) | hUsbDeviceFS.dev_state);
	UL_logMessageWithLongHexParam(Logger_Severity_None, "HID state", (hhid != nullptr) ? hhid->state : 0xFF);
	UL_logMessageWithLongHexParam(Logger_Severity_None, "EP1R", USB->EP1R);
	UL_logMessageWithLongHexParam(Logger_Severity_None, "CNTR", USB->CNTR);
	UL_logMessageWithLongHexParam(Logger_Severity_None, "FNR", USB->FNR);
}
#endif


// Check if the host sends SOF packets (the bus isn't suspended).
// SOF packets are sent every 1 ms, and each one increments the frame number.
bool isUSBBusActive() {
	uint16_t frameNumber = USB->FNR & USB_FNR_FN;
	uint32_t startTick = HAL_GetTick();
	while ((HAL_GetTick() - startTick) < USB_BUS_ACTIVITY_CHECK_DELAY) {}

	return ((USB->FNR & USB_FNR_FN) != frameNumber);
}


// Recovery stage 1: re-arm the Custom HID endpoints without the USB re-enumeration.
// The data toggle of both endpoints is reset to DATA0. If it doesn't match the host's one,
// the first packet in each direction is lost (the host repeats the request),
// and then the exchange continues normally.
void resetUSBEndpoints(bool busActive) {
	HAL_NVIC_DisableIRQ(USB_IRQn);

	USBD_HandleTypeDef * pdev = &hUsbDeviceFS;

	// The bus is active, but the device is still suspended (the resume event has been missed)
	if (busActive && (pdev->dev_state == USBD_STATE_SUSPENDED)) {
		USB->CNTR &= (uint16_t)~USB_CNTR_LPMODE;
		USB->CNTR &= (uint16_t)~USB_CNTR_FSUSP;
		pdev->dev_state = pdev->dev_old_state;
	}

	USBD_CUSTOM_HID_HandleTypeDef * hhid = static_cast<USBD_CUSTOM_HID_HandleTypeDef *>(pdev->pClassData);
	if ((pdev->dev_state == USBD_STATE_CONFIGURED) && (hhid != nullptr)) {
		// Reopening the endpoint resets its data toggle and status
		// (IN = NAK without pending data, OUT = ready to receive).
		USBD_LL_CloseEP(pdev, CUSTOM_HID_EPIN_ADDR);
		USBD_LL_OpenEP(pdev, CUSTOM_HID_EPIN_ADDR, USBD_EP_TYPE_INTR, CUSTOM_HID_EPIN_SIZE);
		USBD_LL_CloseEP(pdev, CUSTOM_HID_EPOUT_ADDR);
		USBD_LL_OpenEP(pdev, CUSTOM_HID_EPOUT_ADDR, USBD_EP_TYPE_INTR, CUSTOM_HID_EPOUT_SIZE);

		hhid->state = CUSTOM_HID_IDLE;
		USBD_LL_PrepareReceive(pdev, CUSTOM_HID_EPOUT_ADDR, hhid->Report_buf, USBD_CUSTOMHID_OUTREPORT_BUF_SIZE);
	}

	HAL_NVIC_EnableIRQ(USB_IRQn);
}


// Monitor the USB data exchange with the host service and try to restore it if it freezes.
// Stage 1 re-arms the HID endpoints (invisible to the host). If it doesn't help,
// stage 2 re-initializes the USB (the host detects a disconnection). After that,
// the monitoring returns to the initial state until the next request from the host.
void checkHostConnection() {
	if (hostLinkState == Host_Link_Idle) return;

	// Read the timestamp before the current tick: the PendSV handler can update it
	// at any moment, and a timestamp newer than the current tick would give a huge elapsed time.
	uint32_t linkTick = hostLinkTick;
	uint32_t elapsed = HAL_GetTick() - linkTick;

	// Recovery stage 2 (completion): the device has been disconnected
	// long enough for the host to detect it, so restart the USB.
	if (hostLinkState == Host_Link_Reconnecting) {
		if (elapsed < USB_RECONNECT_DELAY) return;

		MX_USB_DEVICE_Init();
		hostLinkState = Host_Link_Idle;

// Logging block
#if defined(LOGGING_ENABLED)
		UL_logMessage(Logger_Severity_Info, "USB restarted");
#endif
// End of logging block

		return;
	}

	if (elapsed < HOST_PING_TIMEOUT) return;

	// The request has been received, but hasn't been processed yet
	// (main() holds the lock), so the exchange itself is working.
	if (hostRequestPending) return;

	// The host has suspended the bus (sleep mode, the cable is unplugged, etc.),
	// so the silence is legal. Restart the timeout after the resume.
	bool busActive = isUSBBusActive();
	if (!busActive && (hUsbDeviceFS.dev_state == USBD_STATE_SUSPENDED)) {
		hostLinkTick = HAL_GetTick();
		return;
	}

	uint8_t stage = hostLinkState;

	pendSvLock.lock();

	// A request could arrive while we were checking the bus. Under the lock,
	// the PendSV handler can't change the state and the timestamp, so check them again.
	bool freezeConfirmed = (hostLinkState == stage) && ((HAL_GetTick() - hostLinkTick) >= HOST_PING_TIMEOUT);

	if (freezeConfirmed) {
		if (stage == Host_Link_Active) {
			resetUSBEndpoints(busActive);
			hostLinkState = Host_Link_Endpoints_Reset;
		} else {
			USBD_DeInit(&hUsbDeviceFS);			// Removes the D+ pull-up, so the host detects the disconnection
			hostLinkState = Host_Link_Reconnecting;
		}
		hostLinkTick = HAL_GetTick();
	}

	pendSvLock.unlock();

// Logging block
#if defined(LOGGING_ENABLED)
	if (freezeConfirmed) {
		logUSBState((stage == Host_Link_Active) ? "USB exchange freeze, endpoints reset" : "USB exchange freeze, USB stopped");
	}
#endif
// End of logging block
}


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

/* USER CODE END 0 */

/**
  * @brief  The application entry point.
  * @retval int
  */
int main(void)
{

  /* USER CODE BEGIN 1 */

  /* USER CODE END 1 */

  /* MCU Configuration--------------------------------------------------------*/

  /* Reset of all peripherals, Initializes the Flash interface and the Systick. */
  HAL_Init();

  /* USER CODE BEGIN Init */

  /* USER CODE END Init */

  /* Configure the system clock */
  SystemClock_Config();

  /* USER CODE BEGIN SysInit */

  /* USER CODE END SysInit */

  /* Initialize all configured peripherals */
  MX_GPIO_Init();
  MX_DMA_Init();
  MX_ADC_Init();
  MX_TIM1_Init();
  MX_I2C1_Init();
  MX_USART1_UART_Init();
  MX_SPI1_Init();
  MX_TIM3_Init();
  MX_TIM15_Init();
  MX_USB_DEVICE_Init();
  MX_IWDG_Init();
  /* USER CODE BEGIN 2 */

  // Init timers
  // Display brightness PWM, 1002.67 times per second (48 000 000 / 187 / 256 = 1002.67)
  HAL_TIM_PWM_Start(&htim1, TIM_CHANNEL_1);

  // OneWire us timer, 1 000 000 times per second
  HAL_TIM_Base_Start(&htim3);

  // Init ADC
  HAL_ADCEx_Calibration_Start(&hadc);
  // Delay???
  HAL_ADC_Start_DMA(&hadc, (uint32_t *)&adcRaw, ADC_CHANNELS_COUNT);

  // ADC timer, 100 times per second
  //HAL_NVIC_EnableIRQ(TIM15_IRQn);
  //__HAL_TIM_ENABLE_IT(&htim15, TIM_IT_UPDATE);
  HAL_TIM_Base_Start_IT(&htim15);

  // Init logger
#if defined(LOGGING_ENABLED)
  UL_Huart = &huart1;
  UL_logMessage(Logger_Severity_Info, "MCU Initialized");
  UL_logMessage(Logger_Severity_Info, "Firmware version");
  UL_logMessageWithShortIntParam(Logger_Severity_None, "Major", FIRMWARE_MAJOR_VERSION);
  UL_logMessageWithShortIntParam(Logger_Severity_None, "Minor", FIRMWARE_MINOR_VERSION);
#endif


  // Init Fan channels
  Fan_MAX31790_DS1841::initMAX31790(&maxDriver);
  for (uint8_t i = 0; i < FANS_COUNT; i++) {
	  fans[i].initChannel();
  }

  // Seed the pseudo-random number generator with MCU temperature
  srand(adcRaw.data.mcuTemperature);

  // Reset OneWire lines and start the initial
  // temperature conversion on all OneWire lines at once.
  for (uint8_t i = 0; i < SENSOR_LINES_COUNT; i++) {
	  sensorLines[i].startConversionAll();
  }


#if defined(DISPLAY_ENABLED)

  // Init LCD
  TIM1->CCR1 = systemState.behaviorConfig.displayBrightness = DISPLAY_DEFAULT_BRIGHTNESS;
  systemState.behaviorConfig.displayRotation = ST7789_DEFAULT_ROTATION;
  display.initWithParams(ST7789_DEFAULT_ROTATION, LittleEndian); // Rotate 90 counterclockwise, set display Little Endian data format
  display.begin();
  display.fillColor(COLOR_BACKGROUND);

#endif

  systemState.behaviorConfig.buzzerAlarmsMask = DEFAULT_BUZZER_MODE;
  systemState.behaviorConfig.configFlags = Config_Flag_Empty;

  // Logging block
  #if defined(LOGGING_ENABLED)
    UL_logMessage(Logger_Severity_Info, "Hardware Initialized");
  #endif
  // End of logging block


  // Load configuration or scan hardware and create one
  // To save RAM let's limit the lifetime
  // of stack variables to this block only.
  //////////////////////////////////////////////
  {
#if defined(DISPLAY_ENABLED)
	  uint32_t loadingStartTick = HAL_GetTick();
#endif

	  if (!updateConfig(nullptr, nullptr, nullptr, nullptr, false, false, false, false, false, FANS_COUNT, nullptr, false, true)) {

#if defined(DISPLAY_ENABLED)

		  systemState.userActionRequest = UA_Request_Hardware_Scan;

#else

		  systemState.userActionRequest = UA_Request_Scan_Mask;

#endif

	  	  selectedObjectIndex = FANS_COUNT;
  	  }

#if defined(DISPLAY_ENABLED)

	  while ((HAL_GetTick() - loadingStartTick) < UPDATE_DELAY) {}
	  display.fillColor(COLOR_BACKGROUND);

#else

	  // Reset ADCs and wait a bit for more accurate voltage/temperature measurement
	  HAL_Delay(ADC_Channel::samplingTimeInterval);
	  mcuVMeter.reset();
	  mcuTSensor.reset();
	  loVMeter.reset();
	  hiVMeter.reset();
	  for (uint8_t i = 0; i < DCDC_COUNT; i++) {
		  dcdcVMeters[i].reset();
	  }
	  HAL_Delay(mcuVMeter.getTimeConstant());

#endif

  }

////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

  /* USER CODE END 2 */

  /* Infinite loop */
  /* USER CODE BEGIN WHILE */
  while (1)
  {
	  // Reset Watchdog counter
	  HAL_IWDG_Refresh(&hiwdg);

#if defined(DISPLAY_ENABLED)

		// Start display operations
		uint8_t param_pos = DISPLAY_PARAMETER_Y_POS;
		uint8_t valuePos = DISPLAY_VALUE_Y_POS;
		bool goToNextPage = false;

#endif

		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////
		// MENU PROCESSING
		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////

		// Long press detected
		if ((buttonStatus == Button_State_Pressed) && ((HAL_GetTick() - lastButtonTick) >= BUTTON_LONG_PRESS_DELAY)) {

			// Additional check of button state (the release edge was missed)
			if (HAL_GPIO_ReadPin(FUNC_GPIO_Port, FUNC_Pin) == GPIO_PIN_SET) {
				// Restore EXTI mode to GPIO_MODE_IT_FALLING
				EXTI->RTSR &= ~FUNC_Pin;
				EXTI->FTSR |= FUNC_Pin;
				buttonStatus = Button_State_Released;
				continue;
			}

			buttonStatus = Button_State_Waiting;

#if defined(DISPLAY_ENABLED)

			// Enter menu
			if (displaySection < DISPLAY_MAIN_MENU_SECTION) {
				displaySection = DISPLAY_MAIN_MENU_SECTION;
				sectionCurrentItemIndex = displaySelectedItemIndex = displayLastSelectedItemIndex = 0;
				sectionItemsCount = DISPLAY_MAIN_MENU_ITEMS_COUNT;
				sectionDisplayDuration = SECTION_SELECTED_DISPLAY_DURATION;
			} else {
				// One of menu items was selected by user
				blinkMenuItem(displaySection, displaySelectedItemIndex, sectionCurrentItemIndex);
				bool returnToMainMenu = false;

				switch (displaySection) {

					case DISPLAY_MAIN_MENU_SECTION:

						switch (displaySelectedItemIndex) {

							case DISPLAY_MAIN_MENU_ROTATE_ITEM:
								// Change display orientation
								// If something goes wrong, the user can repeat
								// the operation. So let's ignore the result.
								systemState.behaviorConfig.displayRotation++;
								systemState.behaviorConfig.displayRotation %= 4;
								checkDisplayRotation();
								break;

							case DISPLAY_MAIN_MENU_EXIT_ITEM:
								// Exit from menu to info sections
								goToNextPage = true;
								break;

							case DISPLAY_MAIN_MENU_BUZZER_ITEM:
							case DISPLAY_MAIN_MENU_SENSOR_SCAN_ITEM:
							case DISPLAY_MAIN_MENU_FAN_SCAN_ITEM:
							case DISPLAY_MAIN_MENU_HARDWARE_SCAN_ITEM:
							default:

								// Enter buzzer options or confirmation submenu
								displaySection = menuItemToSection(displaySelectedItemIndex, sectionItemsCount);
								sectionCurrentItemIndex = displaySelectedItemIndex = displayLastSelectedItemIndex = 0;
								break;
						}

						break;

					case DISPLAY_BUZZER_OPTIONS_SECTION:

						systemState.behaviorConfig.buzzerAlarmsMask = buzzerOptions[displaySelectedItemIndex];
						returnToMainMenu = true;

					break;

					case DISPLAY_SENSOR_SCAN_CONFIRMATION_SECTION:
					case DISPLAY_FAN_SCAN_CONFIRMATION_SECTION:
					case DISPLAY_HARDWARE_SCAN_CONFIRMATION_SECTION:

						// Confirmation menu options
						if (displaySelectedItemIndex == DISPLAY_CONFIRM_MENU_YES_ITEM) {
							if (!setScanRequestFromConfirmationSection(displaySection)) {
								// The controller is busy (host configuration session or another request).
								// It's a blocking operation, just like blinking the menu item.
								displayMessage(busyMessage, 0, 0, COLOR_WARNING);
								HAL_Delay(UPDATE_DELAY);
								returnToMainMenu = true;
							}
						} else {
							returnToMainMenu = true;
						}

					break;

				}

				if (returnToMainMenu) {
					displaySection = DISPLAY_MAIN_MENU_SECTION;
					sectionCurrentItemIndex = displaySelectedItemIndex = displayLastSelectedItemIndex = 0;
					sectionItemsCount = DISPLAY_MAIN_MENU_ITEMS_COUNT;
				}

			}

			displaySectionTimer = 0;


#else
			// Rescan all: Hardware, Fans and Sensors!
			// The press is ignored if the controller is busy.
			requestMenuAction(UA_Request_Scan_Mask | UA_Request_Initiated_By_Menu);

#endif

		} else if (buttonPressCount > 0) {

#if defined(DISPLAY_ENABLED)

			// Next menu item or next section/subsection
			if (displaySection >= DISPLAY_MAIN_MENU_SECTION) {
				displaySelectedItemIndex++;
				if (displaySelectedItemIndex >= sectionItemsCount) displaySelectedItemIndex = 0;

				if (displaySelectedItemIndex >= (sectionCurrentItemIndex + DISPLAY_PARAMETERS_PER_PAGE)) {
					displayLastSelectedItemIndex = sectionCurrentItemIndex = displaySelectedItemIndex;
				} else if (displaySelectedItemIndex < sectionCurrentItemIndex) {
					displayLastSelectedItemIndex = sectionCurrentItemIndex = 0;
				}

				displayFlags |= Display_Flag_Redraw_Section;
				displaySectionTimer = 0;

			} else {
				goToNextPage = true;
				sectionDisplayDuration = SECTION_SELECTED_DISPLAY_DURATION;
			}

#else
			// Mute the buzzer for the current statuses
			// (more dangerous statuses will still be signaled)
			uint32_t buzzerAlarmsMask = systemState.behaviorConfig.buzzerAlarmsMask;
			while ((buzzerAlarmsMask & systemState.status) != 0) buzzerAlarmsMask <<= 1;
			systemState.behaviorConfig.buzzerAlarmsMask = buzzerAlarmsMask;

#endif

			buttonPressCount = 0;

		} else {
			// Waiting for the time of the next update
			uint32_t currentTick = HAL_GetTick();
			uint32_t ticksSinceLastUpdate = currentTick - lastUpdateTick;

			// Check the host configuration session, USB session
			// and process the user's request for an action, if any.
			checkConfigSessionTimeout();
			checkHostConnection();
			if (systemState.userActionRequest != 0) {
				processUserRequest();
			}

			// If it is not yet time to update the interface - continue runloop.
			if (ticksSinceLastUpdate < UPDATE_DELAY) continue;
			lastUpdateTick = currentTick;

#if defined(DISPLAY_ENABLED)

			displaySectionTimer += ticksSinceLastUpdate;
			// Check section display timeout
			if (displaySectionTimer >= (sectionDisplayDuration * 1000)) {
				goToNextPage = true;
				sectionDisplayDuration = SECTION_DISPLAY_DURATION;
			}

#endif

		}


#if defined(DISPLAY_ENABLED)

		// Switch to the next section/subsection
		if (goToNextPage) {
			displaySectionTimer = 0;

			if ((displayFlags & Display_Flag_Section_Complete) == 0) {
				// Section incomplete, so display next portion of its parameters
				if ((sectionCurrentItemIndex + DISPLAY_PARAMETERS_PER_PAGE) < sectionItemsCount) {
					sectionCurrentItemIndex += DISPLAY_PARAMETERS_PER_PAGE;
				}
				displayFlags |= Display_Flag_Redraw_Section;
			} else {
				// Save configuration if it was changed (not during the host configuration session,
				// the changes will remain active and will be saved at the next exit from the menu)
				if ((displaySection == DISPLAY_MAIN_MENU_SECTION) &&
					((systemState.userActionFlags & UA_Flag_Config_Page_Started) == 0) &&
					((activeConfigurationPage == nullptr) ||
					(systemState.behaviorConfig.buzzerAlarmsMask != activeConfigurationPage->systemConfig.config.behaviorConfig.buzzerAlarmsMask) ||
					(systemState.behaviorConfig.displayRotation != activeConfigurationPage->systemConfig.config.behaviorConfig.displayRotation))) {
					updateConfig(nullptr, nullptr, nullptr, nullptr, true, false, false, false, false, FANS_COUNT, nullptr, false, true);
				}

				// Clear the working area of the display if we have exited the menu
				if (displaySection >= DISPLAY_MAIN_SECTIONS_COUNT) displayFlags |= Display_Flag_Window_Needs_ToBe_Cleared;

				// Next section
				displaySection++;
				if (displaySection >= DISPLAY_MAIN_SECTIONS_COUNT) {
					displaySection = 0;
					displayFlags &= ~(Display_Flag_Active_Sensors_Processing | Display_Flag_Active_Sensors_Done);
				}
				sectionCurrentItemIndex = displaySelectedItemIndex = displayLastSelectedItemIndex = 0;

			}
		}

#endif

		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////
		// FANS, SENSORS AND STATUS PROCESSING
		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////

		// Calculate metrics and status
		uint32_t newSystemStatus = Status_Unknown;

		//
		systemState.freeRAM = getFreeMemory();
		if (systemState.freeRAM < ALARM_FREE_MEMORY) newSystemStatus |= Status_Alarm_Low_RAM;

		// Power section
		// Fan power supply circuit voltage
		systemState.highLoadPowerBusVoltage = hiVMeter.getVoltage();
		if (activeConfigurationPage != nullptr) {
			uint16_t normalHLPBVoltage = activeConfigurationPage->systemConfig.config.normalHLPBVoltage;
			if ((normalHLPBVoltage != 0) && (systemState.highLoadPowerBusVoltage <
				(normalHLPBVoltage >> POWER_BUS_VOLTAGE_THRESHOLD_SHIFT))) newSystemStatus |= Status_Alarm_Power;
		}

		// Digital power supply circuit voltage
		systemState.digitalPowerBusVoltage = loVMeter.getVoltage();
		// To make the value more accurate, we need to take into account
		// the voltage drop across the diode: 6 / 16 = 0.375.
		if (systemState.digitalPowerBusVoltage < (systemState.highLoadPowerBusVoltage >> POWER_BUS_VOLTAGE_THRESHOLD_SHIFT)) systemState.digitalPowerBusVoltage += 6;

		// MCU Vdd voltage
		systemState.mcuPowerBusVoltage = mcuVMeter.getVoltage();


		// Sensors block
		for (uint8_t i = 0; i < getSensorCount(); i++) {
			TSensor_base * currentSensor = getSensorAtIndex(i);
			if ((currentSensor != nullptr) && currentSensor->pollAndCheck() &&
				(currentSensor->configuration != nullptr) &&
				((currentSensor->configuration->flags & TSensor_Flag_Active) != 0)) {
				uint8_t currentSensorStatus = currentSensor->state.status;
				if ((currentSensorStatus & TSensor_Status_Configured) == 0) newSystemStatus |= Status_Warning_Bad_Sensor;
				if (currentSensorStatus & TSensor_Status_Warning) newSystemStatus |= Status_Warning_Temperature;
				if (currentSensorStatus & TSensor_Status_Error) newSystemStatus |= Status_Alarm_Bad_Sensor;
				if (currentSensorStatus & TSensor_Status_Alarm) newSystemStatus |= Status_Alarm_Temperature;
			}
		}

		// Start the next temperature conversion on all OneWire lines at once.
		// The results will be read by the sensors during the next update cycle
		// (the update period is longer than the maximum conversion time).
		for (uint8_t i = 0; i < SENSOR_LINES_COUNT; i++) {
			sensorLines[i].startConversionAll();
		}


		// Calculations block (Fans load and statuses)
		// To save RAM let's limit the lifetime
		// of large stack variables to this block only.
		//////////////////////////////////////////////
		{
			uint16_t controlValues[FANS_COUNT];
			uint16_t failureSupplements[FANS_COUNT];
			memset(controlValues, 0, sizeof(controlValues));
			memset(failureSupplements, 0, sizeof(failureSupplements));
			//
			// ATTENTION!!!
			// Replace fanActivityMap var with uint16_t or uint32_t
			// in case if the controller supports more than 8 fans!
			uint8_t fanActivityMap = 0;
			//
			//
			for (uint8_t i = 0; i < FANS_COUNT; i++) {
				Fan_MAX31790_DS1841 * currentFan = &fans[i];
				currentFan->doBasicOperations();
				uint32_t currentFanStatus = currentFan->state.status;

				// If the Fan is not disabled and not in manual Operation Mode
				if (currentFan->state.controlMode != Fan_Control_Disabled) {
					if (currentFan->state.operationMode == Fan_Operation_Normal) {
						fanActivityMap |= (1 << i);
						bool overcool = true;
						// Iterate through all Sensors and calculate Fan Control Value
						for (uint8_t j = 0; j < getSensorCount(); j++) {
							TSensor_base * currentSensor = getSensorAtIndex(j);
							if ((currentSensor != nullptr) &&
								(currentSensor->configuration != nullptr) &&
								((currentSensor->configuration->flags & TSensor_Flag_Active) != 0)) {

								bool currentSensorOvercooled;
								uint16_t currentSensorControl = currentSensor->getControlForFan(i, &currentSensorOvercooled);
								if (controlValues[i] < currentSensorControl) controlValues[i] = currentSensorControl;
								overcool &= currentSensorOvercooled;
							}
						}

						// If the sensors affecting this fan are overcooled, so we can halt it.
						currentFan->setHalted(overcool);
					} else {
						// In other Operation Modes, the sensors do not affect the fan
						currentFan->setHalted(false);
					}

					// In this version, we cannot get a MOSFET error,
					// because it is controlled by the GPIO pins of the MCU.
					//if (currentFanStatus & Fan_Status_MOSFET_HW_Error) newSystemStatus |= StatusAlarmFanPower;
					if (currentFanStatus & Fan_Status_DCDC_HW_Error) newSystemStatus |= Status_Alarm_POTs;
					if (currentFanStatus & (Fan_Status_PWM_HW_Error | Fan_Status_TACH_HW_Error)) newSystemStatus |= Status_Alarm_MAX;
					if (currentFanStatus & Fan_Status_PWR_Warning) newSystemStatus |= Status_Warning_Fan_Power;
					if (currentFanStatus & Fan_Status_PWR_Alarm) newSystemStatus |= Status_Alarm_Fan_Power;
					if (currentFanStatus & Fan_Status_PPM_Warning) newSystemStatus |= Status_Warning_Fan_RPM;
					if (currentFanStatus & Fan_Status_PPM_Alarm) newSystemStatus |= Status_Alarm_Fan_RPM;
					if (currentFanStatus & Fan_Status_Failed) {
						newSystemStatus |= Status_Alarm_Fan_Failed;
						// Distribute the failed Fan's load across other fans
						for (uint8_t j = 0; j < FANS_COUNT; j++) {
							if (i != j) {
								uint8_t k = getSubstitutionCoefficient(i, j);
								uint32_t supplement = (uint32_t)failureSupplements[j] + (uint32_t)controlValues[i] * k / 10;
								if (supplement > FAN_CONTROL_TOP) supplement = FAN_CONTROL_TOP;
								failureSupplements[j] = (uint16_t)supplement;
							}
						}
					}
				}
			}

			// Set calculated Control and Supplement Values
			for (uint8_t i = 0; i < FANS_COUNT; i++) {
				if ((fanActivityMap & (1 << i)) != 0) {
					fans[i].setControl(controlValues[i], failureSupplements[i]);
				}
			}
		}

		// Check if all OK
		if ((newSystemStatus & Status_Category_Not_OK) == 0) newSystemStatus = Status_Normal;

		// Check if we need update status and issue an alarm
		checkStatus(newSystemStatus);


		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////
		// DRAWING
		/////////////////////////////////////////////////////////////////////////////////////////
		/////////////////////////////////////////////////////////////////////////////////////////

#if defined(DISPLAY_ENABLED)

		// Update display
		drawHeader(displaySection);
		drawFrame(getDisplaySectionColor(displaySection));
		if (((displayFlags & Display_Flag_Redraw_Section) != 0) &&
			((displayFlags & Display_Flag_Window_Needs_ToBe_Cleared) != 0)) clearMainWindow();

		// Draw current state
		char displayString[DISPLAY_VALUE_LENGTH + 1];
		uint8_t displayedItemsCount = 0;

		switch (displaySection) {

			case DISPLAY_SENSORS_SECTION:
			{
				sectionItemsCount = getSensorCount();

				if (displayFlags & Display_Flag_Redraw_Section) {
					// Decide whether we're drawing active sensors,
					// inactive sensors, or there're no sensors at all.
					if (((displayFlags & Display_Flag_Active_Sensors_Available) != 0) &&
						((displayFlags & Display_Flag_Active_Sensors_Done) == 0)) {
						displayFlags |= Display_Flag_Active_Sensors_Processing;
					} else if ((displayFlags & Display_Flag_Inactive_Sensors_Available) != 0) {
						displayFlags &= ~Display_Flag_Active_Sensors_Processing;
					} else {
						displayMessage(noSensorMessage, 0, 0, COLOR_ALARM);
						displayedItemsCount = DISPLAY_PARAMETERS_PER_PAGE;
						displayFlags |= Display_Flag_Section_Complete;
						break;
					}

					// Get first element from the previous drawing step (or 0).
					displaySelectedItemIndex = displayLastSelectedItemIndex;
				}

				uint8_t i = displaySelectedItemIndex;		// First element
				displayLastSelectedItemIndex = 0;
				TSensor_base * currentSensor = nullptr;
				bool activeSensorsSubsection = (displayFlags & Display_Flag_Active_Sensors_Processing) != 0;

				while (i < sectionItemsCount) {
					currentSensor = getSensorAtIndex(i);

					// Suitable Sensor filter (initialized, visible and active or inactive)
					if ((currentSensor != nullptr) &&
						(currentSensor->configuration != nullptr) &&
						((currentSensor->configuration->flags & TSensor_Flag_Visible) != 0) &&
						(((currentSensor->configuration->flags & TSensor_Flag_Active) != 0) == activeSensorsSubsection)) {

						// If we can draw one more Sensor...
						if (displayedItemsCount < DISPLAY_PARAMETERS_PER_PAGE) {

							if ((displayFlags & Display_Flag_Redraw_Section) != 0) {
								drawParameterNameAtPosition(param_pos, currentSensor->configuration->name, getDisplaySectionColor(displaySection));
							}

							uint8_t currentSensorStatus = currentSensor->state.status;
							if (currentSensorStatus & TSensor_Status_Valid) {
								SU_getStringFromQFormat(displayString, currentSensor->state.temperature, TSENSOR_FRACTION_BITS, DISPLAY_VALUE_LENGTH);
							} else {
								strncpy(displayString, unknownString, DISPLAY_VALUE_LENGTH + 1);
							}
							uint16_t valueBgColor = getDisplaySectionColor(displaySection);
							if (currentSensorStatus & (TSensor_Status_Alarm | TSensor_Status_Error)) valueBgColor = COLOR_ALARM; else
								if ((currentSensorStatus & TSensor_Status_Warning) || !(currentSensorStatus & TSensor_Status_Configured)) valueBgColor = COLOR_WARNING;

							if ((currentSensor->configuration->flags & TSensor_Flag_Active) == 0) valueBgColor = getDarkerColor(valueBgColor);

							drawParameterValueAtPosition(valuePos, displayString, valueBgColor);
							displayedItemsCount++;

						} else {
							// ...or save it for the next time if not.
							displayLastSelectedItemIndex = i;
							break;
						}
					}

					i++;
				}

				// If there're no more suitable Sensors to display,
				// then display the inactive ones or mark the section as completed.
				if (displayLastSelectedItemIndex == 0) {
					if (activeSensorsSubsection && ((displayFlags & Display_Flag_Inactive_Sensors_Available) != 0)) {
						displayFlags |= Display_Flag_Active_Sensors_Done;
					} else {
						displayFlags |= Display_Flag_Section_Complete;
					}
					break;
				}
			}
			break;

			case DISPLAY_FAN_CONTROL_SECTION:
			case DISPLAY_FAN_RPM_SECTION:
			case DISPLAY_FAN_POWER_SECTION:
			{
				sectionItemsCount = FANS_COUNT;

				// Here we simply display the parameters corresponding to the section.
				// At the same time, the Fan names are not redrawn when moving from one
				// fan section to another, because they are the same
				// (of course, if they all fit on the screen).
				uint8_t i = sectionCurrentItemIndex;
				while (1) {
					if (i >= sectionItemsCount) {
						displayFlags |= Display_Flag_Section_Complete;
						break;
					}

					if (displayedItemsCount >= DISPLAY_PARAMETERS_PER_PAGE) break;

					if (((displayFlags & Display_Flag_Redraw_Section) != 0) &&
						((sectionItemsCount > DISPLAY_PARAMETERS_PER_PAGE) || (displaySection == DISPLAY_FAN_CONTROL_SECTION))) {	// Update only at the first entrance to the fan sections
						const char * name = (fans[i].configuration == nullptr) ? unknownString : fans[i].configuration->name;
						drawParameterNameAtPosition(param_pos, name, getDisplaySectionColor(displaySection));
					}

					if (displaySection == DISPLAY_FAN_CONTROL_SECTION) {
						if ((fans[i].state.status & Fan_Status_Running) != 0) {
							SU_getSuffixedStringFromInt16(displayString, ((uint32_t)fans[i].state.scaledControl * 101) >> FAN_CONTROL_BIT_WIDTH, DISPLAY_VALUE_LENGTH, '%');
							char controlChar = ' ';
							uint8_t controlMode = fans[i].state.controlMode;
							if (controlMode == Fan_Control_PWM) controlChar = '"'; else
								if (controlMode == Fan_Control_DCDC) controlChar = '#'; else
									if (controlMode == Fan_Control_MOSFET) controlChar = '$';
							displayString[0] = controlChar;
						} else {
							strncpy(displayString, inactiveString, DISPLAY_VALUE_LENGTH + 1);
						}

					} else if (displaySection == DISPLAY_FAN_RPM_SECTION) {

						SU_getSuffixedStringFromInt16(displayString, fans[i].getRPM(), DISPLAY_VALUE_LENGTH, 0);

					} else if (displaySection == DISPLAY_FAN_POWER_SECTION) {

						SU_getStringFromQFormat(displayString, fans[i].state.currentVoltage, ADC_CHANNEL_FRACTION_BITS, DISPLAY_VALUE_LENGTH);

					}

					// Choose the background color
					uint16_t valueBgColor = getDisplaySectionColor(displaySection);
					if ((fans[i].state.status & Fan_Status_Alarms_Mask) != 0) valueBgColor = COLOR_ALARM; else
						if ((fans[i].state.status & Fan_Status_Warnings_Mask) != 0) valueBgColor = COLOR_WARNING;

					if ((fans[i].configuration == nullptr) || (fans[i].state.controlMode == Fan_Control_Disabled)) valueBgColor = getDarkerColor(valueBgColor);

					drawParameterValueAtPosition(valuePos, displayString, valueBgColor);

					displayedItemsCount++;
					i++;
				}
			}
			break;


			case DISPLAY_SYSTEM_INFO_SECTION:
			{
				sectionItemsCount = SECTION_SYSTEM_PARAMETERS_COUNT;

				// A very simple section - here we display the system metrics.
				uint8_t i = sectionCurrentItemIndex;
				while (1) {
					if (i >= sectionItemsCount) {
						displayFlags |= Display_Flag_Section_Complete;
						break;
					}

					if (displayedItemsCount >= DISPLAY_PARAMETERS_PER_PAGE) break;

					if ((displayFlags & Display_Flag_Redraw_Section) != 0) {
						drawParameterNameAtPosition(param_pos, systemParameters[i], getDisplaySectionColor(displaySection));
					}

					uint16_t valueBgColor = getDisplaySectionColor(displaySection);
					bool hwAlarm;

					switch (i) {

						case DISPLAY_SYSTEM_PARAMETERS_HIGH_PWR_ITEM:
							// High power voltage
							SU_getStringFromQFormat(displayString, systemState.highLoadPowerBusVoltage, ADC_CHANNEL_FRACTION_BITS, DISPLAY_VALUE_LENGTH);
							drawParameterValueAtPosition(valuePos, displayString, (systemState.status & Status_Alarm_Power) ? COLOR_ALARM : valueBgColor);
							break;

						case DISPLAY_SYSTEM_PARAMETERS_LOW_PWR_ITEM:
							// Low power voltage
							SU_getStringFromQFormat(displayString, systemState.digitalPowerBusVoltage, ADC_CHANNEL_FRACTION_BITS, DISPLAY_VALUE_LENGTH);
							drawParameterValueAtPosition(valuePos, displayString, valueBgColor);
							break;

						case DISPLAY_SYSTEM_PARAMETERS_MCU_VDD_ITEM:
							// MCU power voltage
							SU_getStringFromQFormat(displayString, systemState.mcuPowerBusVoltage, ADC_CHANNEL_FRACTION_BITS, DISPLAY_VALUE_LENGTH);
							drawParameterValueAtPosition(valuePos, displayString, valueBgColor);
							break;

						case DISPLAY_SYSTEM_PARAMETERS_RAM_ITEM:
							// Free RAM
							// Convert it to kilobytes for Q-format: freeRAM(KB) = freeRAM / 1024 * 16
							hwAlarm = (systemState.status & Status_Alarm_Low_RAM) != 0;
							SU_getStringFromQFormat(displayString, (uint16_t)(systemState.freeRAM >> (10 - 4)), 4, DISPLAY_VALUE_LENGTH);
							drawParameterValueAtPosition(valuePos, displayString, hwAlarm ? COLOR_WARNING : valueBgColor);
							break;

						case DISPLAY_SYSTEM_PARAMETERS_POTS_STATUS_ITEM:
							// DS1841 POTs state
							hwAlarm = (systemState.status & Status_Alarm_POTs) != 0;
							drawParameterValueAtPosition(valuePos, hwAlarm ? failureString : okString, hwAlarm ? COLOR_ALARM : valueBgColor);
							break;

						case DISPLAY_SYSTEM_PARAMETERS_31790_STATUS_ITEM:
							// MAX31790 state
							hwAlarm = (systemState.status & Status_Alarm_MAX) != 0;
							drawParameterValueAtPosition(valuePos, hwAlarm ? failureString : okString, hwAlarm ? COLOR_ALARM : valueBgColor);
							break;

						default:
							break;
					}

					displayedItemsCount++;
					i++;
				}
			}
			break;


			// Draw menu sections or update selected menu item
			case DISPLAY_MAIN_MENU_SECTION:
			case DISPLAY_BUZZER_OPTIONS_SECTION:
			case DISPLAY_SENSOR_SCAN_CONFIRMATION_SECTION:
			case DISPLAY_FAN_SCAN_CONFIRMATION_SECTION:
			case DISPLAY_HARDWARE_SCAN_CONFIRMATION_SECTION:

				if ((displayFlags & Display_Flag_Redraw_Section) != 0) {

					// Draw all items
					uint8_t i = sectionCurrentItemIndex;
					while (1) {
						if (i >= sectionItemsCount) {
							displayFlags |= Display_Flag_Section_Complete;
							break;
						}

						if (displayedItemsCount >= DISPLAY_PARAMETERS_PER_PAGE) break;

						drawMenuItemAtPosition(valuePos, displaySection, i, (i == displaySelectedItemIndex));
						displayedItemsCount++;
						i++;
					}

				} else {
					// Redraw the selected item if it has changed.
					if (displaySelectedItemIndex != displayLastSelectedItemIndex) {
						// Redraw the previous selected menu item as not selected
						if ((displayLastSelectedItemIndex >= sectionCurrentItemIndex) &&
							(displayLastSelectedItemIndex < (sectionCurrentItemIndex + DISPLAY_PARAMETERS_PER_PAGE))) {
							valuePos = DISPLAY_VALUE_Y_POS + DISPLAY_PARAMETERS_STEP * (displayLastSelectedItemIndex - sectionCurrentItemIndex);
							drawMenuItemAtPosition(valuePos, displaySection, displayLastSelectedItemIndex, false);
						}

						// Draw a new selected menu item
						if (displaySelectedItemIndex != (displayLastSelectedItemIndex + 1)) {
							valuePos = DISPLAY_VALUE_Y_POS + DISPLAY_PARAMETERS_STEP * (displaySelectedItemIndex - sectionCurrentItemIndex);
						}
						drawMenuItemAtPosition(valuePos, displaySection, displaySelectedItemIndex, true);
						displayLastSelectedItemIndex = displaySelectedItemIndex;
					}

					displayedItemsCount = DISPLAY_PARAMETERS_PER_PAGE;
				}
				break;

			case DISPLAY_HOST_REQUEST_SECTION:
				// A configuration change request has been received from the host
				if ((displayFlags & Display_Flag_Redraw_Section) != 0) {
					clearMainWindow();
				}
				// No "break" here

			case DISPLAY_SENSOR_SCAN_SECTION:
			case DISPLAY_FAN_SCAN_SECTION:
			case DISPLAY_HARDWARE_SCAN_SECTION:
				// We will do scan/config operations in the next block of code
				sectionItemsCount = 0;
				displayedItemsCount = DISPLAY_PARAMETERS_PER_PAGE;
				displayFlags |= Display_Flag_Section_Complete;
				break;

			default:
				break;
  	  	}


		// Clearing the image from the data left over
		// from the previous section (when they are changed).
		if ((displayFlags & Display_Flag_Redraw_Section) != 0) {
			for(; displayedItemsCount < DISPLAY_PARAMETERS_PER_PAGE; displayedItemsCount++) {
				if (displaySection < DISPLAY_MAIN_MENU_SECTION) {
					display.fillRect(DISPLAY_PARAMETER_X_POS, param_pos, DISPLAY_PARAMETER_FONT.width * (DISPLAY_PARAMETER_LENGTH + 1), DISPLAY_PARAMETER_FONT.height, COLOR_BACKGROUND);
					display.fillRect(DISPLAY_VALUE_X_POS, valuePos, DISPLAY_VALUE_FONT.width * DISPLAY_VALUE_LENGTH, DISPLAY_VALUE_FONT.height, COLOR_BACKGROUND);
				} else {
					display.fillRect(DISPLAY_MENU_ITEM_X_POS, valuePos, DISPLAY_MENU_ITEM_FONT.width * DISPLAY_MENU_ITEM_LENGTH, DISPLAY_MENU_ITEM_FONT.height, COLOR_BACKGROUND);
				}
				param_pos += DISPLAY_PARAMETERS_STEP;
				valuePos += DISPLAY_PARAMETERS_STEP;
			}
		}

		displayFlags &= ~Display_Flag_Redraw_Section;

#endif

    /* USER CODE END WHILE */

    /* USER CODE BEGIN 3 */
	}
  /* USER CODE END 3 */
}

/**
  * @brief System Clock Configuration
  * @retval None
  */
void SystemClock_Config(void)
{
  RCC_OscInitTypeDef RCC_OscInitStruct = {0};
  RCC_ClkInitTypeDef RCC_ClkInitStruct = {0};
  RCC_PeriphCLKInitTypeDef PeriphClkInit = {0};

  /** Initializes the RCC Oscillators according to the specified parameters
  * in the RCC_OscInitTypeDef structure.
  */
  RCC_OscInitStruct.OscillatorType = RCC_OSCILLATORTYPE_HSI|RCC_OSCILLATORTYPE_HSI14
                              |RCC_OSCILLATORTYPE_LSI|RCC_OSCILLATORTYPE_HSE;
  RCC_OscInitStruct.HSEState = RCC_HSE_ON;
  RCC_OscInitStruct.HSIState = RCC_HSI_ON;
  RCC_OscInitStruct.HSI14State = RCC_HSI14_ON;
  RCC_OscInitStruct.HSICalibrationValue = RCC_HSICALIBRATION_DEFAULT;
  RCC_OscInitStruct.HSI14CalibrationValue = 16;
  RCC_OscInitStruct.LSIState = RCC_LSI_ON;
  RCC_OscInitStruct.PLL.PLLState = RCC_PLL_ON;
  RCC_OscInitStruct.PLL.PLLSource = RCC_PLLSOURCE_HSE;
  RCC_OscInitStruct.PLL.PLLMUL = RCC_PLL_MUL4;
  RCC_OscInitStruct.PLL.PREDIV = RCC_PREDIV_DIV1;
  if (HAL_RCC_OscConfig(&RCC_OscInitStruct) != HAL_OK)
  {
    Error_Handler();
  }

  /** Initializes the CPU, AHB and APB buses clocks
  */
  RCC_ClkInitStruct.ClockType = RCC_CLOCKTYPE_HCLK|RCC_CLOCKTYPE_SYSCLK
                              |RCC_CLOCKTYPE_PCLK1;
  RCC_ClkInitStruct.SYSCLKSource = RCC_SYSCLKSOURCE_PLLCLK;
  RCC_ClkInitStruct.AHBCLKDivider = RCC_SYSCLK_DIV1;
  RCC_ClkInitStruct.APB1CLKDivider = RCC_HCLK_DIV1;

  if (HAL_RCC_ClockConfig(&RCC_ClkInitStruct, FLASH_LATENCY_1) != HAL_OK)
  {
    Error_Handler();
  }
  PeriphClkInit.PeriphClockSelection = RCC_PERIPHCLK_USB|RCC_PERIPHCLK_USART1
                              |RCC_PERIPHCLK_I2C1;
  PeriphClkInit.Usart1ClockSelection = RCC_USART1CLKSOURCE_PCLK1;
  PeriphClkInit.I2c1ClockSelection = RCC_I2C1CLKSOURCE_HSI;
  PeriphClkInit.UsbClockSelection = RCC_USBCLKSOURCE_PLL;

  if (HAL_RCCEx_PeriphCLKConfig(&PeriphClkInit) != HAL_OK)
  {
    Error_Handler();
  }
}

/* USER CODE BEGIN 4 */

// Update TSensors and VMeters according to the new ADC values.
// And here we will also call the procedure for
// smoothly changing the control signal for the fans.
void HAL_ADC_ConvCpltCallback(ADC_HandleTypeDef* hadc) {
	mcuTSensor.update();
	mcuVMeter.update();
	loVMeter.update();
	hiVMeter.update();
	for (uint8_t i = 0; i < DCDC_COUNT; i++) {
		dcdcVMeters[i].update();
	}

	for (uint8_t i = 0; i < FANS_COUNT; i++) {
		fans[i].doIncrements();
	}
}


// Function button clicks handling
void HAL_GPIO_EXTI_Callback(uint16_t GPIO_Pin) {
	if (GPIO_Pin == FUNC_Pin) {
		// Changing the detection mode between the rising and falling edges and analyze the results.

		if (buttonStatus == Button_State_Released) {
			// Change EXTI mode to GPIO_MODE_IT_RISING
			EXTI->RTSR |= FUNC_Pin;
			EXTI->FTSR &= ~FUNC_Pin;

			lastButtonTick = HAL_GetTick();
			//if (!buttonPressCount) firstButtinTick = lastButtonTick;		// Part of double-click detection
			buttonStatus = Button_State_Pressed;
		} else {
			// Change EXTI mode to GPIO_MODE_IT_FALLING
			EXTI->RTSR &= ~FUNC_Pin;
			EXTI->FTSR |= FUNC_Pin;

			uint32_t pressDuration = HAL_GetTick() - lastButtonTick;
			if ((pressDuration > BUTTON_DEBOUNCE_DELAY) && (pressDuration < BUTTON_LONG_PRESS_DELAY)) buttonPressCount++;
			buttonStatus = Button_State_Released;
		}
	}
}


// Buzzer and LED alarm signals processing
// (actually, this is the same timer as for ADC/DMA
// and the interrupt occurs 100 times per second).
void HAL_TIM_PeriodElapsedCallback(TIM_HandleTypeDef *htim) {
	if (htim->Instance == TIM15) {

		// If we haven't given out all the beeps/blinks yet...
		if (beepsCount != 0) {
			uint32_t currentTick = HAL_GetTick();
			bool changeState = ((buzzerTick == 0) ||
					((currentTick - buzzerTick) > BUZZER_BEEP_DURATION));
			bool loudMode = (activeConfigurationPage == nullptr) ? false :
					((systemState.behaviorConfig.configFlags & Config_Flag_Buzzer_Loud_Mode) != 0);
			if (changeState) beepsCount--;
			bool beep = (beepsCount % 2) != 0;

			// Changing the GPIO outputs state (taking into account
			// the possibility of a trigger for the buzzer in cricket mode).
			if (changeState) {
				if (buzzerTick == 0) {
					buzzerTick = currentTick;
				} else {
					buzzerTick += BUZZER_BEEP_DURATION;
				}

				if (beepsCount == 0) {
					HAL_GPIO_WritePin(ERR_LED_GPIO_Port, ERR_LED_Pin, ((systemState.status & Status_Category_Alarms) != 0) ? GPIO_PIN_SET : GPIO_PIN_RESET);
					HAL_GPIO_WritePin(BUZZER_GPIO_Port, BUZZER_Pin, GPIO_PIN_RESET);
				} else {
					if (beep) {
						HAL_GPIO_WritePin(BUZZER_GPIO_Port, BUZZER_Pin, GPIO_PIN_SET);
					} else {
						HAL_GPIO_WritePin(BUZZER_GPIO_Port, BUZZER_Pin, GPIO_PIN_RESET);
					}
					HAL_GPIO_TogglePin(ERR_LED_GPIO_Port, ERR_LED_Pin);
				}

			} else if (!loudMode && beep) {
				// The Loud mode is a constant tone of 1 kilohertz.
				// Cricket mode - the tone is interrupted
				// at a frequency of 100 times per second.
				HAL_GPIO_TogglePin(BUZZER_GPIO_Port, BUZZER_Pin);
			}
		}

		// If we are in the process of a long operation
		// (initialization, scanning hardware/fans, working with Flash), then
		// then reset Watchdog counter.
		if (longOpProcessing) HAL_IWDG_Refresh(&hiwdg);
    }
}


// Processing requests via USB (Custom HID) from the host
void USB_HID_DataReceived(uint8_t reportID, uint8_t * buffer) {
	if (!hostRequestPending) {
		memcpy(txBuffer, buffer, 64);
		hostRequestPending = true;
        SCB->ICSR = SCB_ICSR_PENDSVSET_Msk;  // Trigger PendSV
	}
}



void USB_HID_ProcessRequest(void)
{
    if (!hostRequestPending) return;

    if (!pendSvLock.tryEnterFromHandler()) {
        return;   // main() holds the lock - unlock() will re-trigger us
    }

	// We can use the same buffer for both incoming and outgoing packets.
	// It's even convenient and also saves RAM.
	Communication_Packet * inPacket = reinterpret_cast<Communication_Packet *>(txBuffer);
	Communication_Packet * outPacket = inPacket;

	// We are only interested in reports with commands from the host
	// (in this version we will not use GET REPORT).
	if (inPacket->header.reportID == USB_HID_Report_Host_Command) {

		uint8_t objectIndex = inPacket->data[0];
		uint8_t len = 0;
		uint8_t errorCode = Response_OK;
		TSensor_base * sensor = nullptr;

		do {
			// Check whether this request has been received previously
			// and whether it has already been accepted for execution.
			bool actionRequested = (systemState.userActionRequest != 0);

			if (lastAcceptedRequestID == inPacket->header.requestID) {
				if (actionRequested) {
					errorCode = Response_Request_Processing;
				} else {
					if ((systemState.userActionFlags & UA_Flag_Last_Config_Request_Done) != 0)
						errorCode = Response_Request_Done;
					else if ((systemState.userActionFlags & UA_Flag_Last_Config_Request_Error) != 0)
						errorCode = Response_Request_Error;
					else
						errorCode = Response_Request_Unknown_Result;
				}
				break;
			}

			// While the user request is being processed (for example,
			// during a configuration change), we can only return
			// several statuses.
			bool initializationInProgress = (systemState.configurationChangeFlags & CC_Flag_Initialization_Complete) == 0;
			bool scanInProgress = (systemState.configurationChangeFlags & CC_Flag_Progress_Mask) != 0;
			bool configPageStartedUA = (systemState.userActionFlags & UA_Flag_Config_Page_Started) != 0;
			bool readCommand = (inPacket->header.code >= Command_Get_System_State) &&
								(inPacket->header.code <= Command_Get_Sensor_SN_Config);
			bool configurationCommand = ((inPacket->header.code >= Command_Set_System_Config) &&
										(inPacket->header.code <= Command_Set_Sensor_Config)) ||
										(inPacket->header.code == Command_Start_Clean_Config_Page) ||
										(inPacket->header.code == Command_Finalize_Config_Page);
			bool temperatureUpdateCommand = (inPacket->header.code == Command_Update_Sensor_SN_Temperature);
			bool wildCardCommand = (inPacket->header.code == Command_Get_System_State) ||
									(inPacket->header.code == Command_Get_Fan_Count) ||
									(inPacket->header.code == Command_Get_Fan_N_State) ||
									(inPacket->header.code == Command_Factory_Reset) ||
									(inPacket->header.code == Command_Reboot);

			// During the host configuration session, only read requests,
			// software sensor updates and configuration commands are allowed.
			if ((initializationInProgress ||
					scanInProgress ||
					(actionRequested && !temperatureUpdateCommand) ||
					(configPageStartedUA && !readCommand && !temperatureUpdateCommand && !configurationCommand)) && !wildCardCommand) {
				errorCode = Response_Busy;
				break;
			}

			// Reset the results of the previous request if a new one has arrived
			// For the case of configuration updates, we execute them in the
			// main thread (User Action) only if a new clean flash page
			// is not created to write the full configuration.
			if (((inPacket->header.code >= Command_Set_Fan_N_Control_Value) &&
				(inPacket->header.code <= Command_Remove_Sensor_SN)) ||
				(((systemState.userActionFlags & UA_Flag_Config_Page_Started) == 0) &&
				(inPacket->header.code >= Command_Set_System_Config) && (inPacket->header.code <= Command_Set_Sensor_Config)) ||
				(inPacket->header.code == Command_Set_Behavior_Config) ||
				(inPacket->header.code == Command_Finalize_Config_Page) ||
				(inPacket->header.code == Command_Factory_Reset) ||
				(inPacket->header.code == Command_Reboot)) {

				systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
				systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
				errorCode = Response_Request_Scheduled;
				lastAcceptedRequestID = inPacket->header.requestID;
			}

			// Check if the requested Sensor is available
			switch (inPacket->header.code) {

				// Accessing Sensors by their index
				case Command_Get_Sensor_N_State:
				case Command_Get_Sensor_N_Config:

					sensor = getSensorAtIndex(objectIndex, &isrSensorSearchContext);
					if (sensor == nullptr) errorCode = Response_Sensor_Not_Found;
					break;

				// Accessing Sensors by their serial numbers
				case Command_Get_Sensor_SN_State:
				case Command_Get_Sensor_SN_Config:
				case Command_Update_Sensor_SN_Temperature:
				case Command_Reset_Sensor_SN:
				case Command_Remove_Sensor_SN:

					sensor = getSensorBySerialNumber(inPacket->data, TSensor_Type_Unknown, &isrSensorSearchContext);
					if (sensor == nullptr) errorCode = Response_Sensor_Not_Found;
					break;
			}

			// Parse commands
			switch (inPacket->header.code) {

				/////////////////////////////////////////////////////////////////////////////////////////
				// SIMPLE REQUESTS
				/////////////////////////////////////////////////////////////////////////////////////////

				// Get System state
				case Command_Get_System_State:
					outPacket->header.reportID = USB_HID_Report_System_State;
					len = sizeof(Sys_Info);
					memcpy(outPacket->data, const_cast<const Sys_Info*>(&systemState), len);
					break;

				// Get System Configuration
				case Command_Get_System_Config:
					if (activeConfigurationPage != nullptr) {
						outPacket->header.reportID = USB_HID_Report_System_Config;
						len = sizeof(System_Config);
						memcpy(outPacket->data, &activeConfigurationPage->systemConfig.config, len);
					} else {
						errorCode = Response_System_Not_Configured;
					}
					break;

				// Get Fans count
				case Command_Get_Fan_Count:
					outPacket->header.reportID = USB_HID_Report_Fan_Count;
					outPacket->data[0] = FANS_COUNT;
					len = 1;
					break;

				// Get Fan state
				case Command_Get_Fan_N_State:
					if (objectIndex < FANS_COUNT) {
						outPacket->header.reportID = USB_HID_Report_Fan_State;
						len = sizeof(Fan_Info);
						memcpy(outPacket->data, &fans[objectIndex].state, len);
					} else {
						errorCode = Response_Fan_Not_Found;
					}
					break;

				// Get Fan config
				case Command_Get_Fan_N_Config:
					if (objectIndex < FANS_COUNT) {
						if (fans[objectIndex].configuration != nullptr) {
							outPacket->header.reportID = USB_HID_Report_Fan_Config;
							len = sizeof(Fan_Config);
							memcpy(outPacket->data, fans[objectIndex].configuration, len);
						} else {
							errorCode = Response_Fan_Not_Configured;
						}
					} else {
						errorCode = Response_Fan_Not_Found;
					}
					break;

				// Get Fan Substitution Matrix
				case Command_Get_Fan_Substitution_Matrix:
					if (activeConfigurationPage != nullptr) {
						outPacket->header.reportID = USB_HID_Report_Fan_Substitution_Matrix;
						len = sizeof(Fan_Substitution_Matrix);
						memcpy(outPacket->data, &activeConfigurationPage->substitutionMatrix.matrix, len);
					} else {
						errorCode = Response_System_Not_Configured;
					}
					break;

				// Get Sensors count
				case Command_Get_Sensor_Count:
					outPacket->header.reportID = USB_HID_Report_Sensor_Count;
					outPacket->data[0] = getSensorCount();
					len = 1;
					break;

				// Get Sensor state
				case Command_Get_Sensor_N_State:
				case Command_Get_Sensor_SN_State:
					if (sensor != nullptr) {
						outPacket->header.reportID = USB_HID_Report_Sensor_State;
						len = sizeof(TSensor_Info);
						memcpy(outPacket->data, &sensor->state, len);
					}/* else {
						// We have already set the error code earlier.
						errorCode = Response_Sensor_Not_Found;
					}*/
					break;

				// Get Sensor config
				case Command_Get_Sensor_N_Config:
				case Command_Get_Sensor_SN_Config:
					if (sensor != nullptr) {
						if (sensor->configuration != nullptr) {
							outPacket->header.reportID = USB_HID_Report_Sensor_Config;
							len = sizeof(TSensor_Config);
							memcpy(outPacket->data, sensor->configuration, len);
						} else {
							errorCode = Response_Sensor_Not_Configured;
						}
					}/* else {
						// We have already set the error code earlier.
						errorCode = Response_Sensor_Not_Found;
					}*/
					break;

				// Update software Sensor temperature
				case Command_Update_Sensor_SN_Temperature:
					if (sensor != nullptr) {
						if (sensor->sensorType == TSensor_Type_SW) {
							uint16_t value = inPacket->data[TSENSOR_SN_SIZE] | (inPacket->data[TSENSOR_SN_SIZE + 1] << 8);
							static_cast<TSensor_SW*>(sensor)->updateTemperature(value);
						} else {
							// Only software sensors can be updated by the host
							errorCode = Response_NACK;
						}
					}/* else {
						// We have already set the error code earlier.
						errorCode = Response_Sensor_Not_Found;
					}*/
					break;


				/////////////////////////////////////////////////////////////////////////////////////////
				// MAIN THREAD REQUESTS
				/////////////////////////////////////////////////////////////////////////////////////////

				// Reload active Configuration
				case Command_Reload_Config:
					/*
					// We've done it earlier
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
					*/
					systemState.userActionRequest = UA_Request_Config_Reload;
					break;

				// Set Behavior Config (display/buzzer options)
				case Command_Set_Behavior_Config:
					/*
					// We've done it earlier
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
					*/
					memcpy(&safeBuffer.behaviorConfig, inPacket->data, sizeof(Behavior_Config));
					systemState.userActionRequest = UA_Request_Set_Behavior_Config;
					break;


				// Set Fan Control Value
				case Command_Set_Fan_N_Control_Value:
					if (objectIndex < FANS_COUNT) {
						selectedObjectIndex = objectIndex;
						safeBuffer.wordParameter = inPacket->data[1] | (inPacket->data[2] << 8);
						/*
						// We've done it earlier
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
						*/
						systemState.userActionRequest = UA_Request_Set_Fan_Control_Value;

					} else {
						errorCode = Response_Fan_Not_Found;
					}
					break;

				// Set Fan Control Basis (power)
				case Command_Set_Fan_N_Control_Basis:
					if (objectIndex < FANS_COUNT) {
						selectedObjectIndex = objectIndex;
						safeBuffer.wordParameter = inPacket->data[1] | (inPacket->data[2] << 8);
						/*
						// We've done it earlier
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
						*/
						systemState.userActionRequest = UA_Request_Set_Fan_Control_Basis;

					} else {
						errorCode = Response_Fan_Not_Found;
					}
					break;

				// Set Fan Control Mode
				case Command_Set_Fan_N_Control_Mode:
					if (objectIndex < FANS_COUNT) {
						selectedObjectIndex = objectIndex;
						safeBuffer.byteParameter = inPacket->data[1];
						/*
						// We've done it earlier
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
						*/
						systemState.userActionRequest = UA_Request_Set_Fan_Control_Mode;

					} else {
						errorCode = Response_Fan_Not_Found;
					}
					break;

				// Set Fan Operation Mode
				case Command_Set_Fan_N_Operation_Mode:
					if (objectIndex < FANS_COUNT) {
						selectedObjectIndex = objectIndex;
						safeBuffer.byteParameter = inPacket->data[1];
						/*
						// We've done it earlier
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
						*/
						systemState.userActionRequest = UA_Request_Set_Fan_Operation_Mode;

					} else {
						errorCode = Response_Fan_Not_Found;
					}
					break;

				// Reset Fan
				case Command_Reset_Fan_N:
					if (objectIndex < FANS_COUNT) {
						selectedObjectIndex = objectIndex;
						/*
						// We've done it earlier
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
						*/
						systemState.userActionRequest = UA_Request_Reset_Fan;

					} else {
						errorCode = Response_Fan_Not_Found;
					}
					break;

				// Reset Sensor
				case Command_Reset_Sensor_SN:
					if (sensor != nullptr) {
						// Let's save the request data into a special buffer,
						// in case some other request from the host arrives
						// before it will be executed.
						memcpy(safeBuffer.sensorConfig.serialNumber, sensor->configuration->serialNumber, TSENSOR_SN_SIZE);
						/*
						// We've done it earlier
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
						*/
						systemState.userActionRequest = UA_Request_Reset_Sensor;

					}/* else {
						// We have already set the error code earlier.
						errorCode = Response_Sensor_Not_Found;
					}*/
					break;


				/////////////////////////////////////////////////////////////////////////////////////////
				// SCAN REQUESTS
				/////////////////////////////////////////////////////////////////////////////////////////

				// Scan hardware
				case Command_Rescan_Fan_Hardware:
					selectedObjectIndex = objectIndex;
					if (selectedObjectIndex > FANS_COUNT) selectedObjectIndex = FANS_COUNT;
					/*
					// We've done it earlier
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
					*/
					systemState.userActionRequest = UA_Request_Hardware_Scan;
					break;

				// Scan Fan
				case Command_Rescan_Fan_Model:
					selectedObjectIndex = objectIndex;
					if (selectedObjectIndex > FANS_COUNT) selectedObjectIndex = FANS_COUNT;
					/*
					// We've done it earlier
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
					*/
					systemState.userActionRequest = UA_Request_Fan_Scan;
					break;

				// Scan sensors
				case Command_Rescan_Sensors:
					/*
					// We've done it earlier
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
					*/
					systemState.userActionRequest = UA_Request_Sensor_Scan;
					break;

				/////////////////////////////////////////////////////////////////////////////////////////
				// CONFIGURATION REQUESTS
				/////////////////////////////////////////////////////////////////////////////////////////

				// Add software sensor
				case Command_Add_Sensor:
					// Save the request data into a special buffer until it will be executed
					memcpy(&safeBuffer.sensorConfig, inPacket->data, sizeof(TSensor_Config));
					//
					sensor = getSensorBySerialNumber(safeBuffer.sensorConfig.serialNumber, TSensor_Type_Unknown, &isrSensorSearchContext);
					//
					// If this makes any sense, then another option is to search
					// for the sensor not in the dynamic list of objects (RAM/HEAP),
					// but in the active configuration page in Flash:
					//
					// getSensorConfig(activeConfigurationPage, nullptr, safeBuffer.sensorConfig.serialNumber, nullptr, nullptr)
					//
					if (sensor != nullptr) {
						errorCode = Response_Sensor_Already_Exists_Error;
					} else {
						/*
						// We've done it earlier
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
						*/
						systemState.userActionRequest = UA_Request_Sensor_Add;
					}
					break;

				// Delete Sensor
				case Command_Remove_Sensor_SN:
					if (sensor != nullptr) {
						if (sensor->sensorType >= TSensor_Type_DS18B20) {
							// Save the request data into a special buffer until it will be executed
							memcpy(safeBuffer.sensorConfig.serialNumber, sensor->configuration->serialNumber, TSENSOR_SN_SIZE);
							systemState.userActionRequest = UA_Request_Sensor_Remove;
						} else {
							// Built-in sensors cannot be removed
							errorCode = Response_NACK;
						}
					}/* else {
						// We have already set the error code earlier.
						errorCode = Response_Sensor_Not_Found;
					}*/
					break;

				// Save new System Configuration
				// We'll do all the work in the main thread so just request the action.
				case Command_Set_System_Config:
					// Save the request data into a special buffer until it will be executed
					memcpy(&safeBuffer.systemConfig, inPacket->data, sizeof(System_Config));
					/*
					// We've done it earlier
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
					*/
					systemState.userActionRequest = UA_Request_System_Config_Update;
					break;

				// Save new Fan configuration.
				// Similar to the previous command, just request the action.
				case Command_Set_Fan_N_Config:
					if (objectIndex < FANS_COUNT) {
						// Save the request data into a special buffer until it will be executed
						memcpy(&safeBuffer.fanConfig, &inPacket->data[1], sizeof(Fan_Config));
						/*
						// We've done it earlier
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
						*/
						selectedObjectIndex = objectIndex;
						systemState.userActionRequest = UA_Request_Fan_Config_Update;
					} else {
						errorCode = Response_Fan_Not_Found;
					}
					break;

				// Save Fan Substitution matrix
				// --//--
				case Command_Set_Fan_Substitution_Matrix:
					// Save the request data into a special buffer until it will be executed
					memcpy(&safeBuffer.substitutionMatrix, inPacket->data, sizeof(Fan_Substitution_Matrix));
					/*
					// We've done it earlier
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
					*/
					systemState.userActionRequest = UA_Request_Substitution_Matrix_Update;
					break;

				// Save new Sensor configuration.
				// --//--
				case Command_Set_Sensor_Config:
					// Save the request data into a special buffer until it will be executed
					memcpy(&safeBuffer.sensorConfig, inPacket->data, sizeof(TSensor_Config));

					// Check if the sensor already saved in
					if (((systemState.userActionFlags & UA_Flag_Config_Page_Started) != 0) && (newSettingsPage != nullptr)) {
						if (getSensorConfig(newSettingsPage, nullptr, safeBuffer.sensorConfig.serialNumber, nullptr, &objectIndex) != nullptr) {
							errorCode = Response_Sensor_Already_Exists_Error;
							break;
						}
					}
					/*
					// We've done it earlier
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
					*/
					selectedObjectIndex = objectIndex;
					systemState.userActionRequest = UA_Request_Sensor_Config_Update;
					break;

				// Start new clean configuration page
				case Command_Start_Clean_Config_Page:
					if (((systemState.userActionFlags & UA_Flag_Config_Page_Started) != 0) ||
						(newSettingsPage != nullptr)) {
						errorCode = Response_Flash_Page_Already_Started;
					} else {
						/*
						// We've done it earlier
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
						*/
						systemState.userActionRequest = UA_Request_Start_Clean_Config_Page;
					}
					break;

				// Finalize Configuration page
				case Command_Finalize_Config_Page:
					if (((systemState.userActionFlags & UA_Flag_Config_Page_Started) != 0) &&
						(newSettingsPage != nullptr)) {
						/*
						// We've done it earlier
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
						systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
						*/
						systemState.userActionRequest = UA_Request_Finalize_Config_Page;
					} else {
						errorCode = Response_Flash_No_Page_Started;
					}
					break;

				// Reset configuration to the factory defaults
				case Command_Factory_Reset:
					/*
					// We've done it earlier
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
					*/
					systemState.userActionRequest = UA_Request_Factory_Reset;
					break;

				// Device reset
				case Command_Reboot:
					/*
					// We've done it earlier
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Done;
					systemState.userActionFlags &= ~UA_Flag_Last_Config_Request_Error;
					*/
					systemState.userActionRequest = UA_Request_Reset;
					break;

				// Prepare controller to Host shutdown (reboot or power off)
				case Command_Prepare_To_Host_Shutdown:
					// The host's silence is expected now, so stop monitoring the exchange
					hostLinkState = Host_Link_Idle;
					// For the moment we only need to prepare the sensors for host shutdown
					for (uint8_t i = 0; i < getSensorCount(); i++) {
						TSensor_base * currentSensor = static_cast<TSensor_base*>(getSensorAtIndex(i, &isrSensorSearchContext));
						currentSensor->prepareToHostShutdown();
					}
					break;


				default:
					errorCode = Response_NACK;
					break;
			}
		} while (0);	// F#%$ing goto replacement

		// Send response
		// Since we are reusing the buffer, the next actions can already be considered completed:
		//outPacket->header.code = inPacket->header.code;
		//outPacket->header.requestID = inPacket->header.requestID;
		if (len != 0) {
			len += sizeof(Communication_Packet_Header);
			uint8_t result = USBD_CUSTOM_HID_SendReport(&hUsbDeviceFS, reinterpret_cast<uint8_t *>(outPacket), len);

			if (result == USBD_OK) {
				// Reset the flags for configuration changes after executing
				// the appropriate requests from the host. Let's assume that
				// the host now has the necessary updates.
				switch (outPacket->header.code) {

					// The regular System State request is used as the "keep-alive" signal
					// of the host service (see checkHostConnection())
					case Command_Get_System_State:
						if (hostLinkState != Host_Link_Reconnecting) {
							hostLinkState = Host_Link_Active;
							hostLinkTick = HAL_GetTick();
						}
						break;

					case Command_Get_System_Config:
						systemState.userActionFlags &= ~UA_Flag_System_Config_Changed;
						break;

					case Command_Get_Fan_N_Config:
						systemState.userActionFlags &= ~UA_Flag_Fan_Config_Changed;
						break;

					case Command_Get_Fan_Substitution_Matrix:
						systemState.userActionFlags &= ~UA_Flag_Substitution_Matrix_Changed;
						break;

					case Command_Get_Sensor_N_Config:
					case Command_Get_Sensor_SN_Config:
						systemState.userActionFlags &= ~UA_Flag_Sensor_Config_Changed;
						break;
				}
			}
		} else {
			len = sizeof(Communication_Packet_Header) + 1;
			outPacket->header.reportID = USB_HID_Report_Operation_Result;
			outPacket->data[0] = errorCode;
			// Here we can ignore the result. If necessary,
			// the host can request the system status and get
			// the result of the previous operation from it.
			USBD_CUSTOM_HID_SendReport(&hUsbDeviceFS, reinterpret_cast<uint8_t *>(outPacket), len);
		}

		systemState.lastHostRequestErrorCode = errorCode;
	}

	hostRequestPending = false;
}


/* USER CODE END 4 */

/**
  * @brief  This function is executed in case of error occurrence.
  * @retval None
  */
void Error_Handler(void)
{
  /* USER CODE BEGIN Error_Handler_Debug */
  /* User can add his own implementation to report the HAL error return state */

  __disable_irq();

  // Turn on all fans at 100% regardless of configuration.
  // This is done first, since the display output may stall
  // while the interrupts are disabled.
  for (uint8_t i = 0; i < FANS_COUNT; i++) {
	  fans[i].kernelPanic();
  }

  // Show General Error status
  uint32_t newStatus = (systemState.status | Status_Alarm_General_Failure);
  checkStatus(newStatus);

  for (int i = 0; i < 20; i++) {
      HAL_GPIO_TogglePin(ERR_LED_GPIO_Port, ERR_LED_Pin);
      for (volatile int d = 0; d < 500000; d++);   // crude blocking delay
  }

  NVIC_SystemReset();

  /* USER CODE END Error_Handler_Debug */
}
#ifdef USE_FULL_ASSERT
/**
  * @brief  Reports the name of the source file and the source line number
  *         where the assert_param error has occurred.
  * @param  file: pointer to the source file name
  * @param  line: assert_param error line source number
  * @retval None
  */
void assert_failed(uint8_t *file, uint32_t line)
{
  /* USER CODE BEGIN 6 */
  /* User can add his own implementation to report the file name and line number,
     ex: printf("Wrong parameters value: file %s on line %d\r\n", file, line) */
  /* USER CODE END 6 */
}
#endif /* USE_FULL_ASSERT */
