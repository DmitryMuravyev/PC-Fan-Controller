@echo off

REM set SCRIPT_DIR=%CD%
REM set SCRIPT_DIR=%0%
REM set SCRIPT_DIR=%~dp0%
REM set SCRIPT_DIR=%~f0%

for %%A in ("%~dp0.") do set PROJECT_DIR=%%~dpA

REM Rename main.c back to main.cpp
set CORE_SRC_PATH=%PROJECT_DIR%Core\Src\

if exist "%CORE_SRC_PATH%main.c" (
	if exist "%CORE_SRC_PATH%main.cpp" (
		echo ERROR: Both main.c and main.cpp exist in Core\Src. Merge them manually.
		exit /b 1
	)
	ren "%CORE_SRC_PATH%main.c" main.cpp
)

REM Delete the restored copies of the files moved to Drivers\USB
set CUSTOM_HID_PATH=%PROJECT_DIR%Middlewares\ST\STM32_USB_Device_Library\Class\CustomHID\
set USBD_DESC_APP_PATH=%PROJECT_DIR%USB_DEVICE\App\

if exist "%CUSTOM_HID_PATH%Inc\usbd_customhid.h" del "%CUSTOM_HID_PATH%Inc\usbd_customhid.h"
if exist "%CUSTOM_HID_PATH%Src\usbd_customhid.c" del "%CUSTOM_HID_PATH%Src\usbd_customhid.c"
if exist "%USBD_DESC_APP_PATH%usbd_desc.c" del "%USBD_DESC_APP_PATH%usbd_desc.c"
