<h1>Fan Controller firmware</h1>

Firmware of the [Fan Controller](/README.md) board, based on the STM32F070CB microcontroller. It is an STM32CubeIDE project written in C++ on top of the STM32 HAL, without an RTOS.

<h1>What it does</h1>

Once a second, the firmware reads all temperature sensors, calculates the required power of every fan, and adjusts the fans smoothly. Each sensor has its own temperature range for each fan, so, for example, the CPU temperature can drive the CPU and case fans, while a drive sensor affects only the fan next to the drives. If a fan fails, its load is redistributed to the remaining fans according to the substitution matrix.

The controller works on its own and keeps its configuration in the microcontroller's Flash memory. The control software (see [software](/software)) is only needed to monitor the controller from the computer, to change the configuration, and to pass the computer's own temperature sensors to the controller.

<h1>Features</h1>

<h2>Fans</h2>

- 6 fan channels: PWM control and speed measurement for all of them (MAX31790), supply voltage control via DC-DC converters with digital potentiometers (DS1841) for 4 of them;
- control modes: PWM, DC-DC (supply voltage) and On/Off; the "Pro" mode allows switching between PWM and DC-DC if one of them fails;
- operation modes: Normal, Manual, Silent and Turbo;
- automatic detection of the channel hardware and the connected fans: 3- or 4-wire connection (using a two-wire configuration is possible, but it must be set up via the control software), PWM and voltage control ranges, minimum speed, spin-up requirements;
- optional detection of the fan model features: size (inertia), power, server/desktop class (experimental);
- smooth speed changes with a configurable rate, spin-up of stopped fans, PID speed control;
- the fans are stopped when all their sensors are below the lower temperature limit (can be disabled per fan);
- speed and supply voltage monitoring with warnings and alarms; failed fans are restarted, and their load is redistributed to the remaining ones;
- if the microcontroller hangs, the watchdog restarts it; in case of a fatal error, all fans are switched to full speed.

<h2>Temperature sensors</h2>

- Up to 35 sensors in total;
- DS18B20 sensors on 2 OneWire buses; each sensor is identified by its serial number, and its settings are bound to it; new sensors are added by a sensor scan (from the menu or the control software);
- sensors built into the DS1841 digiPOTs and the microcontroller;
- software sensors: the temperatures of the computer's CPU, GPU, motherboard, drives etc., transmitted by the host service;
- for each sensor: warning and alarm thresholds, and a lower/upper temperature limit and an impact for each fan, including the fan power to use if the sensor becomes unavailable.

<h2>Interface</h2>

- IPS display 240x240 (ST7789, DMA);
- function button, error indicator, alarm buzzer (loud and cricket modes);
- USB connection (HID device, no drivers required);
- optional diagnostic log over UART.

<h2>Configuration</h2>

- Automatic configuration on the first start;
- the configuration is stored in Flash; 4 pages are written in turn to spread the wear, and an interrupted write never damages the active configuration;
- the complete configuration can be replaced from the host in one session (backup restore).

<h1>How to use it</h1>

<h2>Display and button</h2>

The display shows several information sections in turn, each for 7 seconds: **Sensors**, **Fan control**, **Fan RPM**, **Fan power** and **System info**. During scanning and while the host is changing the configuration, the corresponding progress section is shown.

| Action | Information sections | Menu |
|---|---|---|
| Short press | Next section (it stays on the screen for 30 seconds) | Next menu item |
| Long press (1 s) | Enter the menu | Select the item |

Menu items:
- **Buzzer options:** ON (alarms and warnings), ALRMS (alarms only), OFF;
- **Rotate display:** rotates the image by 90°;
- **Scan sensors**, **Scan fans**, **Scan hardware:** start the corresponding scan (with confirmation);
- **Exit.**

If the controller is busy (for example, while the host is changing the configuration), menu actions are rejected with the "Busy" message.

<h2>Without the display</h2>

The firmware can be built without display support (see [Build options](#build-options)). In this case:
- **short press:** mutes the buzzer for the current warnings and alarms (more serious problems will still be signaled);
- **long press:** rescans everything: hardware, fans and sensors.

<h1>Flashing</h1>

Each firmware release (`firmware-vX.Y`, see *Releases* on the repository page) contains the files `FanController-X.Y.hex` and `FanController-X.Y.bin`. There are two ways to flash them.

<h2>Via USB (built-in DFU bootloader)</h2>

The STM32F070 has a built-in USB DFU bootloader, so a programmer is not required.

1. Install [STM32CubeProgrammer](https://www.st.com/en/development-tools/stm32cubeprog.html) (it also installs the DFU driver on Windows).
2. Start the microcontroller in bootloader mode: close the **BOOT** jumper on the board, then power it up (the USB cable must be connected). The board appears as "STM32 BOOTLOADER" / "DFU in FS Mode".
3. In STM32CubeProgrammer, select **USB**, click **Connect**, open the `.hex` file, click **Download**.
4. Remove the **BOOT** jumper and power-cycle the board.

The same with the command-line tool:

```
STM32_Programmer_CLI -c port=usb1 -w FanController-1.3.hex -v -rst
```

A `.bin` file needs the start address: `-w FanController-1.3.bin 0x08000000`.

Firmware updates from the control software (remotely, via the service) are planned for future versions.

<h2>Via ST-Link (SWD)</h2>

Connect an ST-Link to the SWD pins and flash the `.hex` file with STM32CubeProgrammer (**ST-LINK** interface), or build and run the project directly from STM32CubeIDE (see below).

<h1>Building</h1>

<h2>Requirements</h2>

- [STM32CubeIDE](https://www.st.com/en/development-tools/stm32cubeide.html) (the project was created with STM32CubeMX 6.18.1 and the STM32Cube FW_F0 V1.11.6 package);
- an ST-Link programmer for debugging (optional).

<h2>Opening and building the project</h2>

1. *File* → *Import* → *General* → *Existing Projects into Workspace*, select the `firmware` folder. The project is called "PC Fan Controller". The generated `Drivers` and `Middlewares` folders are included in the repository, so the project builds without regenerating the code.
2. Select the **Release** build configuration and build the project (*Project* → *Build Project*). The results are `Release/FanController.elf`, `FanController.hex` and `FanController.bin`.

The debug configurations (`PC Fan Controller Release.launch`, `PC Fan Controller Live Debug.launch`) use ST-Link with OpenOCD.

<h2>Build options</h2>

In [Core/Src/main.cpp](Core/Src/main.cpp):

```C++
#define DISPLAY_ENABLED			// It is possible to compile the firmware without display support
#define LOGGING_ENABLED			// ...and without UART logger.
```

The UART log (USART1, 115200 baud, 8N1) shows the initialization, scanning results, configuration changes and errors.

The board-level constants are in [Core/Inc/board_config.h](Core/Inc/board_config.h):

```C
#define FANS_COUNT				6		// Number of fan channels
#define DCDC_COUNT				4		// Number of DC-DC converters (fan channels with voltage control)
#define FIRST_DCDC_FAN_INDEX	3		// Number (1-based) of the first DC-DC-controlled fan
#define SENSOR_LINES_COUNT		2		// Number of OneWire lines
```

${\color{red}Attention!}$ `FANS_COUNT` defines the size of the configuration stored in Flash and transmitted to the host. Changing it makes the saved configurations incompatible (and requires the same change in the software). The firmware supports up to 8 fans; the sizes of the USB reports are checked at compile time.

<h2>Regenerating the code in STM32CubeMX</h2>

Several generated files had to be changed, so they were moved out of the folders that STM32CubeMX regenerates:

| File | Changes |
|---|---|
| `USB_DEVICE/Target/usbd_conf.h` | Buffer and report descriptor sizes (set in the `.ioc` file) |
| `USB_DEVICE/App/usbd_custom_hid_if.c` | Report descriptor and data reception (inside the USER CODE sections) |
| `Drivers/USB/usbd_customhid.h` | 64-byte endpoints (moved from `Middlewares`) |
| `Drivers/USB/usbd_customhid.c` | Request checks (moved from `Middlewares`) |
| `Drivers/USB/usbd_desc.c` | Device descriptor and firmware version (moved from `USB_DEVICE/App`) |

Besides that, the main file is `main.cpp` (C++), while STM32CubeMX only updates `main.c`.

On Windows, this is handled automatically by the scripts in the [Scripts](Scripts) folder, which are registered in the `.ioc` file (*Project Manager* → *Code Generator* → *User Actions*):
- `BeforeCodeGeneration.bat` renames `main.cpp` to `main.c`;
- `AfterCodeGeneration.bat` renames it back and deletes the restored copies of the moved files.

On other systems, do the same manually:
1. Before generating: rename `Core/Src/main.cpp` to `main.c`.
2. After generating: rename it back to `main.cpp` and delete these files:
   - `USB_DEVICE/App/usbd_desc.c`
   - `Middlewares/ST/STM32_USB_Device_Library/Class/CustomHID/Inc/usbd_customhid.h`
   - `Middlewares/ST/STM32_USB_Device_Library/Class/CustomHID/Src/usbd_customhid.c`

<h1>USB interface</h1>

The controller is a USB HID device (Custom HID, 64-byte reports), so no drivers are required. The host sends commands in an output report and receives the answer in one of the input reports; the protocol is implemented in the [host service](/software).

The device uses STMicroelectronics' VID `0x0483` and PID `0x5750` (the ones of ST's Custom HID examples), with the manufacturer string "Dmitry Muravyev" and the product string "Fan Controller". The service identifies the controller by these strings. If you make your own device based on this project, please get your own VID/PID pair (for example, from [pid.codes](https://pid.codes) for open-source hardware).

<h1>Project structure</h1>

| Folder | Contents |
|---|---|
| `Core/Src/main.cpp` | Main loop, configuration, menu and display, USB protocol |
| `Core/Inc`, `Core/Src` | Fan classes (`fan_base`, `fan_MAX31790_DS1841`), temperature sensor classes (`tsensor_*`), voltmeters, utilities, UART logger, image decompression |
| `Drivers/DS18B20`, `DS1841`, `MAX31790`, `ST7789`, `OneWire`, `I2C_UTILS`, `Lock` | Drivers of the board's chips and helper classes |
| `Drivers/USB` | Modified USB files (see above) |
| `Drivers/CMSIS`, `Drivers/STM32F0xx_HAL_Driver`, `Middlewares` | STMicroelectronics libraries |
| `Scripts` | Code generation scripts |

The header files are documented in the Doxygen format.

<h1>License</h1>

The firmware source code is licensed under the MIT license, see [LICENSE](/LICENSE). Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
