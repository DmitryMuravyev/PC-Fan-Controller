////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// ST7789.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	ST7789.h
 * @brief	Very simple "driver" which allows you to output images
 * 			or individual pixels on a display based on the ST7789 controller.
 * 			It is designed only for use with 240x240 pixel displays.
 * 			Some extra modifications required for other displays.
 * 			Also it supports DMA and double buffering.
 *
 * @attention	For the normal operation of DMA and buffering,
 * 				it is necessary that the display byte order corresponds
 * 				to the byte order of the transmitted pixels.
 */

#ifndef _ST7789_
#define _ST7789_


// Includes
#include <stdint.h>
#include <cstddef>
#include <stdbool.h>
#include "stm32f0xx_hal.h"
#include "fonts.h"


// Types

/**
 * @brief	Pixel data byte order.
 */
typedef enum {
	BigEndian							= 0,	///< Big endian (the native ST7789 order)
	LittleEndian						= 1		///< Little endian (the native MCU order)
} Endianness;


// Class-level definitions
// SPI Constants
#define ST7789_SPI_TIMEOUT				100		///< SPI transfer timeout in milliseconds
#define ST7789_SPI_MAX_ATTEMPTS			3		///< Number of additional attempts in case of an SPI error
#define ST7789_SPI_ATTEMPTS_DELAY		10		///< Delay between the attempts in milliseconds

// Display configuration
#define ST7789_DEFAULT_ROTATION			1		///< Default rotation 0-3, 2 - normal top to bottom
#define ST7789_DEFAULT_ENDIANNESS		BigEndian	///< Default pixel data byte order

// At the moment only one screen resolution supported
// (support for other resolutions can be easily added)
#define ST7789_DISPLAY_WIDTH			240		///< Display width in pixels
#define ST7789_DISPLAY_HEIGHT			240		///< Display height in pixels

/// If you plan to use DMA, declare this symbol
#define ST7789_USE_DMA

/// You can shift the responsibility for the buffer to this module.
/// In addition, even without DMA, using a buffer speeds up
/// the painting of rectangular areas in one color.
#define ST7789_USE_BUFFERING

/// Double buffering can be used together with DMA.
/// One of the buffers is transferring to the display,
/// while the second one is rendering. Then they are swapped.
#define ST7789_USE_DOUBLE_BUFFERING

// You can free the buffer during the "end()" call to avoid
// wasting RAM when no display operations are performed.
//#define ST7789_RELEASE_BUFFER_AT_END

/// The buffer size in pixels will be defined as
/// the display width multiplied by this number.
/// Also you can change the buffer size as you want,
/// changing pixelBufferSize variable outside of the begin() / end() calls.
#define ST7789_BUFFER_HEIGHT			2

// Uncomment and configure port/pin if Chip Select pin used
//#define ST7789_CS_USED

// Display commands
#define ST7789_COLORMODE				0x3A	///< Interface Pixel Format
#define ST7789_RAMCTRL					0xB0	///< RAM Control
#define ST7789_MADCTL					0x36	///< Memory Data Access Control
#define ST7789_INVON					0x21	///< Display Inversion On
#define ST7789_SLPOUT					0x11	///< Sleep Out
#define ST7789_NORON					0x13	///< Normal Display Mode On
#define ST7789_DISPON					0x29	///< Display On
#define ST7789_CASET					0x2A	///< Column Address Set
#define ST7789_RASET					0x2B	///< Row Address Set
#define ST7789_RAMWR					0x2C	///< Memory Write

// Command arguments
#define ST7789_MADCTL_MY				0x80	///< Page Address Order: 0 = Top to Bottom, 1 = Bottom to Top.
#define ST7789_MADCTL_MX				0x40	///< Column Address Order: 0 = Left to Right, 1 = Right to Left.
#define ST7789_MADCTL_MV				0x20	///< Page/Column Order: 0 = Normal Mode, 1 = Reverse Mode.
#define ST7789_MADCTL_ML				0x10	///< Line Address Order: 0 = LCD Refresh Top to Bottom, 1 = LCD Refresh Bottom to Top.
#define ST7789_MADCTL_RGB				0x00	///< RGB/BGR Order: 0 = RGB, 1 = BGR.
#define ST7789_MADCTL_MH				0x00	///< Display Data Latch Data Order: 0 = LCD Refresh Left to Right, 1 = LCD Refresh Right to Left.


/**
 * @brief	ST7789 240x240 display connected via SPI (with optional DMA and double buffering).
 * @note	With double buffering, setWindow() only stores the window, which is applied
 * 			by the next writePixels() call (after the previous DMA transfer is completed).
 */
class ST7789 {

private:

protected:

	static constexpr uint8_t COLOR_MODE_16bit = 0x55;							///< RGB565 color mode
	static constexpr uint8_t RAMCTRL_PARAMETERS[2] = { 0x00, 0xE8 };			///< RAM Control parameters (little endian)
	static constexpr uint8_t ROTATION_PARAMETERS[4][3] = { { ST7789_MADCTL_MX | ST7789_MADCTL_MY | ST7789_MADCTL_RGB, 0, 80 },
	                                                        { ST7789_MADCTL_MY | ST7789_MADCTL_MV | ST7789_MADCTL_RGB, 80, 0 },
	                                                        { ST7789_MADCTL_RGB, 0, 0},
	                                                        { ST7789_MADCTL_MX | ST7789_MADCTL_MV | ST7789_MADCTL_RGB, 0, 0 } };
																				///< MADCTL value, X and Y offsets for each rotation

	bool isDisplayLE = ST7789_DEFAULT_ENDIANNESS;	///< The display expects little endian pixel data

	uint8_t xShift = 0;								///< X offset of the visible area in the display RAM
	uint8_t yShift = 0;								///< Y offset of the visible area in the display RAM

	#if defined (ST7789_USE_BUFFERING)
		uint16_t * mainPixelBuffer = nullptr;		///< Pixel buffer (the first one)
		#if defined (ST7789_USE_DOUBLE_BUFFERING)
			uint16_t * secondPixelBuffer = nullptr;	///< The second pixel buffer
			uint8_t currentBufferIndex = 0;			///< Index of the buffer available for rendering
			uint8_t nextBufferWindow[4];			///< Window to be applied with the next buffer (x, y, width, height)
			bool windowChanged = false;				///< The window has to be applied with the next buffer
		#endif
	#endif

	#if defined(ST7789_USE_DMA)
		/// Wait for the DMA transfer to be completed
		inline void waitDMA () { while (spiHandle->hdmatx->State != HAL_DMA_STATE_READY) {}} //{ asm volatile("nop"); }
	#else
		/// Wait for the DMA transfer to be completed (DMA isn't used)
		inline void waitDMA () {}
	#endif

	/**
	 * @brief	Select/deselect the display (Chip Select, if used).
	 * @param	val					true - select.
	 */
	void selectDisplay(bool val);

	/**
	 * @brief	Select the data or command mode (DC pin).
	 * @param	val					true - data, false - command.
	 */
	void selectDataMode(bool val);

	/**
	 * @brief	Transmit data via SPI (blocking or DMA).
	 * @param	data				Data.
	 * @param	size				Number of bytes.
	 * @param	useDMA				Use DMA (the function returns before the transfer is completed).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writeSPIData(uint8_t * data, uint16_t size, bool useDMA);

	/**
	 * @brief	Transmit a 16-bit word in the big endian byte order.
	 * @param	data				Word.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writeBigEndianWord(uint16_t data);

	/**
	 * @brief	Set the target window in the display RAM and start the memory write.
	 * @param	x					X coordinate.
	 * @param	y					Y coordinate.
	 * @param	width				Width.
	 * @param	height				Height.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef configureWindow(uint8_t x, uint8_t y, uint8_t width, uint8_t height);


public:

	SPI_HandleTypeDef * const spiHandle;			///< SPI Handle for the display's instance

	GPIO_TypeDef * const resetPort;					///< Reset GPIO Port
	const uint16_t resetPin;						///< Reset GPIO Pin

	GPIO_TypeDef * const dcPort;					///< Data/Command (DC) GPIO Port
	const uint16_t dcPin;							///< Data/Command (DC) GPIO Pin

	#if defined(ST7789_CS_USED)
		GPIO_TypeDef * const csPort;				///< Chip Select (CS) GPIO Port
		const uint16_t csPin;						///< Chip Select (CS) GPIO Pin
	#endif


	#if defined (ST7789_USE_BUFFERING)
		uint16_t pixelBufferSize = ST7789_DISPLAY_WIDTH * ST7789_BUFFER_HEIGHT;	///< Pixel buffer size in pixels
	#endif

#if defined(ST7789_CS_USED)
	/**
	 * @brief	Constructor.
	 * @param	handle				SPI Handle.
	 * @param	theResetPort		Reset GPIO Port.
	 * @param	theResetPin			Reset GPIO Pin.
	 * @param	theDCPort			Data/Command GPIO Port.
	 * @param	theDCPin			Data/Command GPIO Pin.
	 * @param	theCSPort			Chip Select GPIO Port.
	 * @param	theCSPin			Chip Select GPIO Pin.
	 */
	ST7789(SPI_HandleTypeDef * handle, GPIO_TypeDef * theResetPort, uint16_t theResetPin, GPIO_TypeDef * theDCPort, uint16_t theDCPin, GPIO_TypeDef * theCSPort = nullptr, uint16_t theCSPin = 0);
#else
	/**
	 * @brief	Constructor.
	 * @param	handle				SPI Handle.
	 * @param	theResetPort		Reset GPIO Port.
	 * @param	theResetPin			Reset GPIO Pin.
	 * @param	theDCPort			Data/Command GPIO Port.
	 * @param	theDCPin			Data/Command GPIO Pin.
	 */
	ST7789(SPI_HandleTypeDef * handle, GPIO_TypeDef * theResetPort, uint16_t theResetPin, GPIO_TypeDef * theDCPort, uint16_t theDCPin);
#endif
	virtual ~ST7789() = default;
	ST7789(const ST7789&) = delete;
	ST7789& operator=(const ST7789&) = delete;

	uint8_t currentRotation = UINT8_MAX;			///< Current rotation (invalid value, so the rotation is always set at the initialization)

	/**
	 * @brief	Initialize the display with the default rotation and byte order.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef init();

	/**
	 * @brief	Initialize the display.
	 * @param	rotation			Rotation (0 - 3).
	 * @param	endianness			Pixel data byte order.
	 * @retval	HAL status, HAL_ERROR if the pins aren't defined or the buffer couldn't be allocated.
	 */
	HAL_StatusTypeDef initWithParams(uint8_t rotation, Endianness endianness);

	/**
	 * @brief	Select the display and allocate the buffer (if not allocated yet).
	 */
	void begin();

	/**
	 * @brief	Wait for the DMA transfer, deselect the display and (optionally) free the buffer.
	 */
	void end();

	#if defined (ST7789_USE_BUFFERING)
		/**
		 * @brief	Get the pixel buffer available for rendering.
		 * @retval	Pixel buffer (pixelBufferSize pixels).
		 */
		uint16_t * getPixelBuffer();
	#endif

	/**
	 * @brief	Set the display rotation.
	 * @param	rotation			Rotation (0 - 3, larger values are taken modulo 4).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setRotation(uint8_t rotation);

	/**
	 * @brief	Set the target window for the subsequent pixel output.
	 * @param	x					X coordinate.
	 * @param	y					Y coordinate.
	 * @param	width				Width.
	 * @param	height				Height.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef setWindow(uint8_t x, uint8_t y, uint8_t width, uint8_t height);

	/**
	 * @brief	Fill the entire screen with a solid color.
	 * @param	color				RGB565 color.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef fillColor(uint16_t color);

	/**
	 * @brief	Fill a rectangle with a solid color.
	 * @param	x					X coordinate.
	 * @param	y					Y coordinate.
	 * @param	width				Width.
	 * @param	height				Height.
	 * @param	color				RGB565 color.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef fillRect(uint8_t x, uint8_t y, uint8_t width, uint8_t height, uint16_t color);

	/**
	 * @brief	Draw a character.
	 * @param	x					X coordinate.
	 * @param	y					Y coordinate.
	 * @param	ch					Character (32 - 126, others are drawn as '?').
	 * @param	font				Font.
	 * @param	color				Character color (RGB565).
	 * @param	bgColor				Background color (RGB565).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writeChar(uint8_t x, uint8_t y, char ch, FontDef font, uint16_t color, uint16_t bgColor);

	/**
	 * @brief	Draw a string (wrapping to the next line at the right edge of the screen).
	 * @param	x					X coordinate.
	 * @param	y					Y coordinate.
	 * @param	str					String.
	 * @param	font				Font.
	 * @param	color				Characters color (RGB565).
	 * @param	bgColor				Background color (RGB565).
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writeString(uint8_t x, uint8_t y, const char *str, FontDef font, uint16_t color, uint16_t bgColor);

	/**
	 * @brief	Fill the configured window with a solid color.
	 * @param	color				RGB565 color.
	 * @param	count				Number of pixels.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef fillPixels(uint16_t color, uint16_t count);

	/**
	 * @brief	Write the next pixel to the configured window.
	 * @param	color				RGB565 color.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writePixel(uint16_t color);

	/**
	 * @brief	Write the next chunk of pixels to the configured window.
	 * @note	DMA is used if the byte order of the data corresponds to the display one.
	 * @param	pixels				Pixels (RGB565).
	 * @param	count				Number of pixels.
	 * @param	littleEndian		Byte order of the pixel data.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writePixels(uint16_t * pixels, uint16_t count, uint8_t littleEndian);

	/**
	 * @brief	Write a command byte.
	 * @param	command				Command.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writeCommand(uint8_t command);

	/**
	 * @brief	Write one byte of data.
	 * @param	data				Data byte.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writeDataByte(uint8_t data);

	/**
	 * @brief	Write one word of data in the big endian byte order (for pixels use writePixel()).
	 * @param	data				Data word.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writeDataWord(uint16_t data);

	/**
	 * @brief	Write a chunk of data (for pixels use writePixels()).
	 * @param	data				Data.
	 * @param	size				Number of bytes.
	 * @retval	HAL status.
	 */
	HAL_StatusTypeDef writeData(uint8_t * data, uint16_t size);

};

#endif /* _ST7789_ */

// END-OF-FILE
