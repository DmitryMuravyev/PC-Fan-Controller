/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : main.h
  * @brief          : Header for main.c file.
  *                   This file contains the common defines of the application.
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

/* Define to prevent recursive inclusion -------------------------------------*/
#ifndef __MAIN_H
#define __MAIN_H

#ifdef __cplusplus
extern "C" {
#endif

/* Includes ------------------------------------------------------------------*/
#include "stm32f0xx_hal.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */

#include <stdlib.h>
#include <string.h>
#include <stdint.h>
#include <stdbool.h>

/* USER CODE END Includes */

/* Exported types ------------------------------------------------------------*/
/* USER CODE BEGIN ET */

/* USER CODE END ET */

/* Exported constants --------------------------------------------------------*/
/* USER CODE BEGIN EC */

/* USER CODE END EC */

/* Exported macro ------------------------------------------------------------*/
/* USER CODE BEGIN EM */

/* USER CODE END EM */

/* Exported functions prototypes ---------------------------------------------*/
void Error_Handler(void);

/* USER CODE BEGIN EFP */

/**
 * @brief	Configure the system clock (also used after the USB resume, see usbd_conf.c).
 */
void SystemClock_Config(void);

/**
 * @brief	Process the pending host request (called from PendSV_Handler).
 * @note	The weak implementation (see stm32f0xx_it.c) does nothing.
 * 			The request is copied by USB_HID_DataReceived(), and the response
 * 			is sent from the same buffer. If main() holds the PendSV lock,
 * 			the processing is postponed until the lock is released.
 */
void USB_HID_ProcessRequest(void);

/* USER CODE END EFP */

/* Private defines -----------------------------------------------------------*/
#define FUNC_Pin GPIO_PIN_13
#define FUNC_GPIO_Port GPIOC
#define FUNC_EXTI_IRQn EXTI4_15_IRQn
#define LCD_RESET_Pin GPIO_PIN_4
#define LCD_RESET_GPIO_Port GPIOA
#define LCD_DC_Pin GPIO_PIN_2
#define LCD_DC_GPIO_Port GPIOB
#define EN_BUCK_4_Pin GPIO_PIN_12
#define EN_BUCK_4_GPIO_Port GPIOB
#define EN_BUCK_3_Pin GPIO_PIN_13
#define EN_BUCK_3_GPIO_Port GPIOB
#define EN_BUCK_2_Pin GPIO_PIN_14
#define EN_BUCK_2_GPIO_Port GPIOB
#define EN_BUCK_1_Pin GPIO_PIN_15
#define EN_BUCK_1_GPIO_Port GPIOB
#define BUZZER_Pin GPIO_PIN_15
#define BUZZER_GPIO_Port GPIOA
#define ERR_LED_Pin GPIO_PIN_3
#define ERR_LED_GPIO_Port GPIOB
#define FULL_SPEED_Pin GPIO_PIN_4
#define FULL_SPEED_GPIO_Port GPIOB
#define FAN_FAIL_Pin GPIO_PIN_5
#define FAN_FAIL_GPIO_Port GPIOB
#define T_SENS_2_Pin GPIO_PIN_8
#define T_SENS_2_GPIO_Port GPIOB
#define T_SENS_1_Pin GPIO_PIN_9
#define T_SENS_1_GPIO_Port GPIOB

/* USER CODE BEGIN Private defines */


/* USER CODE END Private defines */

#ifdef __cplusplus
}
#endif

#endif /* __MAIN_H */
