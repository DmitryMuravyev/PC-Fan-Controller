////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// ST7789.cpp
////////////////////////////////////////////////////////////////////////////////////////


// Includes
#include <ST7789.h>
#include <stdlib.h>


// Implementation-level definitions
// Call procedure and check the result
#define CALL_AND_CHECK(f)			{ HAL_StatusTypeDef status = (f); if (status != HAL_OK) return (status); }


constexpr uint8_t ST7789::COLOR_MODE_16bit;
constexpr uint8_t ST7789::RAMCTRL_PARAMETERS[2];
constexpr uint8_t ST7789::ROTATION_PARAMETERS[4][3];


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

// Constructor
#if defined(ST7789_CS_USED)
ST7789::ST7789(SPI_HandleTypeDef * handle, GPIO_TypeDef * theResetPort, uint16_t theResetPin, GPIO_TypeDef * theDCPort, uint16_t theDCPin, GPIO_TypeDef * theCSPort, uint16_t theCSPin) :
	spiHandle(handle), resetPort(theResetPort), resetPin(theResetPin), dcPort(theDCPort), dcPin(theDCPin), csPort(theCSPort), csPin(theCSPin) {
}
#else
ST7789::ST7789(SPI_HandleTypeDef * handle, GPIO_TypeDef * theResetPort, uint16_t theResetPin, GPIO_TypeDef * theDCPort, uint16_t theDCPin) :
	spiHandle(handle), resetPort(theResetPort), resetPin(theResetPin), dcPort(theDCPort), dcPin(theDCPin) {
}
#endif


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

// Select chip
void ST7789::selectDisplay(bool val) {
#if defined(ST7789_CS_USED)
	HAL_GPIO_WritePin(csPort, csPin, (GPIO_PinState)!val);
#endif
}


// Select either data or command mode
 void ST7789::selectDataMode(bool val) {
	HAL_GPIO_WritePin(dcPort, dcPin, (GPIO_PinState)val);
}


 // Transmit data via SPI (blocking or DMA)
 HAL_StatusTypeDef ST7789::writeSPIData(uint8_t * data, uint16_t size, bool useDMA) {
	uint8_t i = 0;
	HAL_StatusTypeDef result;
	if (spiHandle == nullptr) return (HAL_ERROR);

	do {
		result = useDMA ? HAL_SPI_Transmit_DMA(spiHandle, data, size) :
				HAL_SPI_Transmit(spiHandle, data, size, ST7789_SPI_TIMEOUT);
		if (result != HAL_OK) HAL_Delay(ST7789_SPI_ATTEMPTS_DELAY);
	} while ((result != HAL_OK) && (i++ < ST7789_SPI_MAX_ATTEMPTS));

	return (result);
}


// Write big endian word
 HAL_StatusTypeDef ST7789::writeBigEndianWord(uint16_t data) {
	uint8_t bigEndianData[2] = {(uint8_t)(data >> 8), (uint8_t)(data & 0xFF)};
	return (writeSPIData(bigEndianData, sizeof(bigEndianData), false));
}


// Configure target window
 HAL_StatusTypeDef ST7789::configureWindow(uint8_t x, uint8_t y, uint8_t width, uint8_t height) {
	waitDMA();

	uint16_t address;
	// Column Address set
	CALL_AND_CHECK(writeCommand(ST7789_CASET));
	// Write X coordinates
	address = x + xShift;
	CALL_AND_CHECK(writeDataWord(address));
	address += (width -1);
	CALL_AND_CHECK(writeDataWord(address));

	// Row Address set
	CALL_AND_CHECK(writeCommand(ST7789_RASET));
	// Write Y coordinates
	address = y + yShift;
	CALL_AND_CHECK(writeDataWord(address));
	address += (height - 1);
	CALL_AND_CHECK(writeDataWord(address));

	// Write to RAM
	CALL_AND_CHECK(writeCommand(ST7789_RAMWR));
	selectDataMode(true);

	return(HAL_OK);
}


// Init w/o parameters
 HAL_StatusTypeDef ST7789::init(void) {
	return (initWithParams(ST7789_DEFAULT_ROTATION, ST7789_DEFAULT_ENDIANNESS));
}


// Init with parameters
 HAL_StatusTypeDef ST7789::initWithParams(uint8_t rotation, Endianness endianness) {
	// Checking whether GPIOs are defined
	if ((spiHandle == nullptr) || (resetPort == nullptr) || (dcPort == nullptr)
#if defined(ST7789_CS_USED)
		|| (csPort == nullptr)
#endif
		 ) return (HAL_ERROR);

	// Reset display
	HAL_GPIO_WritePin(resetPort, resetPin, GPIO_PIN_RESET);
	HAL_Delay(25);
	HAL_GPIO_WritePin(resetPort, resetPin, GPIO_PIN_SET);
	HAL_Delay(50);

	begin();

#if defined(ST7789_USE_BUFFERING)
	if (mainPixelBuffer == nullptr) return (HAL_ERROR);
#endif

	// Set color mode to RGB565
	CALL_AND_CHECK(writeCommand(ST7789_COLORMODE));
	CALL_AND_CHECK(writeDataByte(COLOR_MODE_16bit));

	// Set display RAM control to Little Endian mode
	isDisplayLE = endianness;
	if (endianness) {
		CALL_AND_CHECK(writeCommand(ST7789_RAMCTRL));
		CALL_AND_CHECK(writeData((uint8_t *)RAMCTRL_PARAMETERS, sizeof(RAMCTRL_PARAMETERS)));
	}

	// Sleep mode Off
	CALL_AND_CHECK(writeCommand(ST7789_SLPOUT));
	// Set display rotation
	CALL_AND_CHECK(setRotation(rotation));
	// Turn on the color inversion
	CALL_AND_CHECK(writeCommand(ST7789_INVON));
	// Normal Display Mode On
	CALL_AND_CHECK(writeCommand(ST7789_NORON));
	// Clear screen
	CALL_AND_CHECK(fillColor(0));
	waitDMA();
	// Display On
	CALL_AND_CHECK(writeCommand(ST7789_DISPON));

	end();

	return(HAL_OK);
}


// Select the display and allocate buffer
void ST7789::begin() {
#if defined(ST7789_USE_BUFFERING)
	if (mainPixelBuffer == nullptr) {
#if defined (ST7789_USE_DOUBLE_BUFFERING)
		mainPixelBuffer = (uint16_t *)malloc(pixelBufferSize << 2);
		secondPixelBuffer = &mainPixelBuffer[pixelBufferSize];
#else
		mainPixelBuffer = (uint16_t *)malloc(pixelBufferSize << 1);
#endif
	}
#endif
	selectDisplay(true);
}


// Deselect the display and free the buffer
void ST7789::end() {
	waitDMA();
	selectDisplay(false);
#if defined(ST7789_USE_BUFFERING) && defined(ST7789_RELEASE_BUFFER_AT_END)
	if (mainPixelBuffer) {
		free(mainPixelBuffer);
		mainPixelBuffer = nullptr;
#if defined(ST7789_USE_DOUBLE_BUFFERING)
		secondPixelBuffer = nullptr;
		currentBufferIndex = 0;
#endif
	}
#endif
}


// Get current buffer
#if defined(ST7789_USE_BUFFERING)

uint16_t * ST7789::getPixelBuffer() {

#if defined(ST7789_USE_BUFFERING) && defined(ST7789_USE_DOUBLE_BUFFERING)
	if (currentBufferIndex) return (secondPixelBuffer);
#endif

	return (mainPixelBuffer);
}
#endif


// Configure screen rotation
HAL_StatusTypeDef ST7789::setRotation(uint8_t rotation) {
	rotation = rotation % 4; // Let's allow cyclic screen rotations
	if (currentRotation != rotation) {
		CALL_AND_CHECK(writeCommand(ST7789_MADCTL));
		CALL_AND_CHECK(writeDataByte(ROTATION_PARAMETERS[rotation][0]));
		xShift = ROTATION_PARAMETERS[rotation][1];
		yShift = ROTATION_PARAMETERS[rotation][2];
		currentRotation = rotation;
	}
	return (HAL_OK);
}


// Set target window
HAL_StatusTypeDef ST7789::setWindow(uint8_t x, uint8_t y, uint8_t width, uint8_t height) {
#if defined(ST7789_USE_BUFFERING) && defined(ST7789_USE_DOUBLE_BUFFERING)
	nextBufferWindow[0] = x;
	nextBufferWindow[1] = y;
	nextBufferWindow[2] = width;
	nextBufferWindow[3] = height;
	windowChanged = true;
#else
	CALL_AND_CHECK(configureWindow(x, y, width, height));
#endif

	return (HAL_OK);
}


// Fill screen with solid color
HAL_StatusTypeDef ST7789::fillColor(uint16_t color) {
	CALL_AND_CHECK(setWindow(0, 0, ST7789_DISPLAY_WIDTH, ST7789_DISPLAY_HEIGHT));
	CALL_AND_CHECK(fillPixels(color, ST7789_DISPLAY_WIDTH * ST7789_DISPLAY_HEIGHT));
	return (HAL_OK);
}


// Fill rectangle with solid color
HAL_StatusTypeDef ST7789::fillRect(uint8_t x, uint8_t y, uint8_t width, uint8_t height, uint16_t color) {
	CALL_AND_CHECK(setWindow(x, y, width, height));
	CALL_AND_CHECK(fillPixels(color, width * height));
	return (HAL_OK);
}


// Draw character at position
HAL_StatusTypeDef ST7789::writeChar(uint8_t x, uint8_t y, char c, FontDef font, uint16_t color, uint16_t bgColor) {
	CALL_AND_CHECK(setWindow(x, y, font.width, font.height));

#if defined(ST7789_USE_BUFFERING)
	uint16_t * currentBuffer = getPixelBuffer();
	uint16_t pixelIndex = 0;
	uint16_t characterCellSize = font.width * font.height;
#endif

	// Replace the characters missing in the font
	if ((c < 32) || (c > 126)) c = '?';

	for (uint8_t row = 0; row < font.height; row++) {
		uint16_t mask = 1 << (font.width - 1);
		uint16_t data = font.data[(c - 32) * font.height + row];
		for (uint8_t col = 0; col < font.width; col++) {
			if (data & mask)

#if defined(ST7789_USE_BUFFERING)
			{
				currentBuffer[pixelIndex] = color;
			} else {
				currentBuffer[pixelIndex] = bgColor;
			}
			pixelIndex++;

#if (ST7789_BUFFER_HEIGHT < 2)
			if (pixelIndex >= pixelBufferSize) {
				CALL_AND_CHECK(writePixels(currentBuffer, pixelIndex, isDisplayLE));
				characterCellSize -= pixelIndex;
				pixelIndex = 0;
			}
#endif

#else
			{
				CALL_AND_CHECK(writePixel(color));
			} else {
				CALL_AND_CHECK(writePixel(bgColor));
			}
#endif

			mask >>= 1;
		}
	}

#if defined(ST7789_USE_BUFFERING)
	CALL_AND_CHECK(writePixels(currentBuffer, characterCellSize, isDisplayLE));
#endif

	return (HAL_OK);
}


// Draw string at position
HAL_StatusTypeDef ST7789::writeString(uint8_t x, uint8_t y, const char *str, FontDef font, uint16_t color, uint16_t bgColor) {
	while (*str) {
		if ((x + font.width) > ST7789_DISPLAY_WIDTH) {
			x = 0;
			y += font.height;
			if ((y + font.height) > ST7789_DISPLAY_HEIGHT) {
				break;
			}

			if (*str == ' ') {
				// skip spaces in the beginning of the new line
				str++;
				continue;
			}
		}
		CALL_AND_CHECK(writeChar(x, y, *str, font, color, bgColor));
		x += font.width;
		str++;
	}

	return (HAL_OK);
}


// Fill configured window with pixels
HAL_StatusTypeDef ST7789::fillPixels(uint16_t color, uint16_t count) {
	// Prepare color in the required endianness
	if (!isDisplayLE) color = __builtin_bswap16(color);

#if defined(ST7789_USE_BUFFERING)
	// Prepare buffer
	uint16_t pixelsToFill = (count > pixelBufferSize) ? pixelBufferSize : count;
	uint16_t * currentBuffer = getPixelBuffer();
	for (uint16_t i = 0; i < pixelsToFill; i++) currentBuffer[i] = color;

	while (count) {
		CALL_AND_CHECK(writePixels(currentBuffer, pixelsToFill, isDisplayLE));
		count -= pixelsToFill;
		if (count < pixelsToFill) pixelsToFill = count;
	}

#else
	while (count) {
		CALL_AND_CHECK(writeSPIData((uint8_t *)&color, sizeof(color), false));
		count--;
	}
#endif

	return (HAL_OK);
}


// Write next pixel
HAL_StatusTypeDef ST7789::writePixel(uint16_t color) {
	if (isDisplayLE) color = __builtin_bswap16(color);
	return(writeBigEndianWord(color));
}


// Write next chunk of pixels
HAL_StatusTypeDef ST7789::writePixels(uint16_t * pixels, uint16_t count, uint8_t littleEndian) {

	// Set window for the current buffer
#if defined(ST7789_USE_BUFFERING) && defined(ST7789_USE_DOUBLE_BUFFERING)
	if (windowChanged) {
		CALL_AND_CHECK(configureWindow(nextBufferWindow[0], nextBufferWindow[1], nextBufferWindow[2], nextBufferWindow[3]));
		windowChanged = false;
	} else {
#if defined(ST7789_USE_DMA)
		waitDMA();
#endif
	}
#endif

	// If display is the same endianness as our data
	// then we can use DMA
	if (isDisplayLE == littleEndian) {

#if defined(ST7789_USE_DMA)

#if !(defined(ST7789_USE_BUFFERING) && defined(ST7789_USE_DOUBLE_BUFFERING))
		waitDMA();
#endif
		CALL_AND_CHECK(writeSPIData((uint8_t *)pixels, count << 1, true));

#if defined(ST7789_USE_BUFFERING) && defined(ST7789_USE_DOUBLE_BUFFERING)
		// To be able to output the same buffer multiple times,
		// it is better for us to determine the number of the next buffer
		// depending on the current one instead of just swapping them each time.
		currentBufferIndex = (pixels == mainPixelBuffer);
#endif

#else
		// Or just transmit the data
		CALL_AND_CHECK(writeData((uint8_t *)pixels, count << 1));

#endif

	}
	// Else we have to make sure that the endianness is correct for every pixel
	else
	{
		for(uint16_t i = 0; i < count; i++) CALL_AND_CHECK(writeBigEndianWord(pixels[i]));
	}

	return (HAL_OK);
}


// Write command
HAL_StatusTypeDef ST7789::writeCommand(uint8_t command) {
	selectDataMode(false);
	return(writeSPIData(&command, sizeof(command), false));
}


// Write one byte of data
HAL_StatusTypeDef ST7789::writeDataByte(uint8_t data) {
	selectDataMode(true);
	return(writeSPIData(&data, sizeof(data), false));
}


// Write one word of data with endianness control
// (for pixel use writePixel).
HAL_StatusTypeDef ST7789::writeDataWord(uint16_t data) {
	selectDataMode(true);
	return(writeBigEndianWord(data));
}


// Write a chunk of data
// (for pixels use writePixels).
HAL_StatusTypeDef ST7789::writeData(uint8_t * data, uint16_t size) {
	selectDataMode(true);
	return(writeSPIData(data, size, false));
}


// END-OF-FILE
