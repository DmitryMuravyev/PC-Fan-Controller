////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2024 Dmitry Muravyev (youtube.com/@DmitryMuravyev)
// SPDX-License-Identifier: MIT
//
// decompression.h
////////////////////////////////////////////////////////////////////////////////////////


/**
 * @file	decompression.h
 * @brief	Delta-encoded image decompression (used for the startup logo).
 *
 * @details	The class has been modified to decompress single-channel (grayscale)
 * 			data more efficiently and set the pixel color right during decompression.
 * 			ATTENTION! Compared to the full version, the bit length of many variables
 * 			has also been changed to simplify and speed up processing.
 *
 * 			As examples, I have made several modifications to the decompression code
 * 			that differ in capabilities and performance:
 * @verbatim
1) The original class - maximum features:
	- supported image size up to 65535x65535px,
	- up to 32 bits per data channel,
	- up to 64 bits per frame (modification is required for a larger frame width),
	- decompression per 1 request: up to 2^32 frames (pixels), up to 2^32 compressed/decompressed data size,
	- decompression of 1-2-4 bits frames,
	- AVR "far" memory access support*,
	- splitting compressed data into memory chunks (up to 32767 bytes)**.
Source: /Decompression
Example: /AVR/Arduino_boards/Mega2560_ILI9486 (ATMega2560 + ILI9486 480x320 display).

2) High-speed adaptation:
	- focusing on the low-bit-length operations,
	- supported image size is up to 255x255px,
	- up to 8 bits per data channel,
	- up to 16 bits per frame,
	- decompression of 1-2-4 bits frames,
	- AVR "far" memory access support*,
	- splitting compressed data into memory chunks (up to 32767 bytes)**,
	- high performance.
Source: /Decompression/Mods/Fast
Examples:
	/AVR/Arduino_boards/Mega2560_ILI9486_Fast (ATMega2560 + ILI9486 480x320 display).

3) High-speed with pixel support (THIS ONE):
	- focusing on the low-bit-length operations,
	- supported image size is up to 255x255px,
	- up to 8 bits per data channel,
	- up to 16 bits per frame,
	- colorizing pixels during decompression (if used),
	- direct use of the RGB565 pixel buffer (if needed),
	- AVR "far" memory access support*,
	- splitting compressed data into memory chunks (up to 32767 bytes)**,
	- high performance+.
Source: /Decompression/Mods/Fast+pixel_support
Examples:
	/AVR/Arduino_boards/Nano_ST7789 (Arduino Nano + ST7789 240x240 display),
	/ESP32/Wroom_ST7789 (ESP32 DEVKIT board + ST7789 240x240 display),
	/STM32/F070CB_ST7789_DMA (STM32F070CB chip + ST7789 240x240 display),
	/STM32/F407ZGT6_NT35510_FSMC_DMA (mcudev DevEBox board + NT35510 800x480 display).

4) Tiny adaptation:
	- focusing on the 8-bit operations,
	- supported image size is up to 255x255px,
	- up to 8 bits per data channel,
	- up to 8 bits per frame supported,
	- colorizing pixels during decompression (if used),
	- direct use of the RGB565 pixel buffer (if needed),
	- high performance++.
Source: /Decompression/Mods/Tiny+pixel_support
Examples:
	/AVR/Arduino_boards/Nano_ST7789_Tiny (Arduino Nano + ST7789 240x240 display),
	/AVR/ATtiny85 (ATtiny85 chip + SPI output).

* Relevant only for the AVR platform.
** To do this, a pointer to an array of far pointers should be passed
to the decompression procedure instead of a direct pointer to a data array:

static uint_farptr_t dataChunks[n];
decompressNextFrames(dc, dataChunks, decompressedData, framesCount);
   @endverbatim
 *
 * 			The description in the code body will be quite brief, so if you want more detailed information,
 * 			then please refer to the following links, where I talk about how this compression method works,
 * 			about problems that arose during the development and how I solved them:
 *
 * 			Boosty (Russian):
 * 			- Sprint 1 - https://boosty.to/muravyev/posts/9241e5a2-a490-4530-a376-e9cae0f9e9bf?share=post_link
 * 			- Sprint 2 - https://boosty.to/muravyev/posts/dfdd0b1d-f27d-446a-ab38-d55cbc7cd580?share=post_link
 * 			- Sprint 3 - https://boosty.to/muravyev/posts/bc58a9af-f075-4004-a98e-6535edab9925?share=post_link
 *
 * 			Patreon (Eng subs):
 * 			- Sprint 1 - https://www.patreon.com/posts/delta-encoding-1-106850934
 * 			- Sprint 2 - https://www.patreon.com/posts/delta-encoding-2-108056837
 * 			- Sprint 3 - https://www.patreon.com/posts/delta-encoding-3-108236064
 */

#ifndef _DELTA_DECOMPRESSION_
#define _DELTA_DECOMPRESSION_

// Includes
#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

// Global definitions

// Use only one option that matches your data.
// Using DECOMPRESSION_FIXED_WINDOW_FIRST, you can decompress data
// compressed not only by this method (Fixed + Adaptive), but also
// a data, compressed by Fixed Window only.
// Likewise using DECOMPRESSION_ADAPTIVE_FLOATING_WINDOW_FIRST,
// you can decompress data, compressed both by Adaptive + Fixed
// and only by Adaptive Window methods.
////////////////////////////////////////////////////////////////////////////////////////
#define DECOMPRESSION_FIXED_WINDOW_ONLY						///< Data compressed by the Fixed Window method only
//#define DECOMPRESSION_ADAPTIVE_FLOATING_WINDOW_ONLY
//#define DECOMPRESSION_FIXED_WINDOW_FIRST
//#define DECOMPRESSION_ADAPTIVE_FLOATING_WINDOW_FIRST
////////////////////////////////////////////////////////////////////////////////////////

#define DECOMPRESSION_MAX_NUMBER_OF_CHANNELS    3			///< The maximum number of channels in the data that you will decompress

#define DECOMPRESSION_USE_SQUARES							///< Uncomment if you have data splitted into squares among those you will decompress


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

/**
 * @brief	Parameters of the compressed data (generated together with the data).
 */
typedef struct
{
	uint8_t numberOfChannels;											///< Number of data channels
	uint8_t bitsPerChannels[DECOMPRESSION_MAX_NUMBER_OF_CHANNELS];		///< Bit width of each channel
	uint8_t blockSizeBits;												///< Bit width of the block size field
	uint8_t bitsPerMethodDeclaration[DECOMPRESSION_MAX_NUMBER_OF_CHANNELS];	///< Bit width of the method declaration for each channel
	uint8_t bitsPerFrame;												///< Bit width of the decompressed frame (pixel)
#if defined(DECOMPRESSION_USE_SQUARES)
	bool splitToSquares;												///< The image is split into squares
	uint16_t imageWidth;												///< Image width in pixels
	uint8_t squareSide;													///< Square side in pixels
	uint16_t squaresPerWidth;											///< Number of squares per image width
	uint16_t squaresPerWidthSize;										///< Number of pixels in a row of squares
#endif
} DecompressionContext;


////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////
////////////////////////////////////////////////////////////////////////////////////////

/**
 * @brief	Width of the decompression buffer in pixels.
 * @note	In the case of decompression images divided into squares,
 * 			set this variable to the width of your decompression buffer.
 */
extern uint16_t decompressionBufferWidth;


/**
 * @brief	Reset the decompression state (must be called before decompressing new data).
 */
void resetDecompression(void);

/**
 * @brief	Decompress the next portion of frames (pixels).
 * @note	The decompression state is kept between the calls, so the data
 * 			can be decompressed in several portions.
 * @param	dc					Parameters of the compressed data.
 * @param	compressedData		Compressed data.
 * @param	decompressedData	Output buffer (nullptr - the frameDecompressed() delegate is called for each frame).
 * @param	framesCount			Number of frames to decompress.
 * @param	colorize			Call the colorizePixel() delegate for each frame.
 */
void decompressNextFrames(DecompressionContext * dc, uint8_t * compressedData, uint16_t * decompressedData, uint16_t framesCount, bool colorize);



#ifdef __cplusplus
}
#endif

#endif // end _DELTA_DECOMPRESSION_

// END-OF-FILE
