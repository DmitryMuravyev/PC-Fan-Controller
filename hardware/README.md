<h1>Fan Controller hardware</h1>

Hardware of the [Fan Controller](/README.md) board: schematic, PCB and Gerber files. The board is designed in [EasyEDA](https://easyeda.com) (Standard Edition); the same project is published on [OSHWLab](https://oshwlab.com/sneer2sneer/pc-fan-controller).

- Board revision: **1.0**
- PCB: 4 layers, 100 × 70 mm

<h1>Files</h1>

| File | Contents |
|---|---|
| `Fan-Controller_Schematic.json` | Schematic, EasyEDA Standard source (sheets "Controller" and "FANs Power Control") |
| `Fan-Controller_PCB.json` | PCB, EasyEDA Standard source |
| `Fan-Controller_Schematic_Main.png`, `Fan-Controller_Schematic_Power.png` | Schematic sheets as images, for viewing without EasyEDA |
| `Gerber_PCB_PC Fan Controller.zip` | Gerber and drill files for PCB manufacturing |

To edit the design, open the `.json` files in EasyEDA Standard Edition (*File* → *Open* → *EasyEDA Source*), or import them into EasyEDA Pro.

<h2>Bill of materials</h2>

A verified bill of materials (BOM) is not published. The component values and footprints are given in the schematic, which is the reference for building the board. Select the specific parts by value, footprint and ratings (voltage, current, tolerance), and check their availability yourself.

The manufacturer and supplier part numbers that remain in the EasyEDA source files are for reference only and have not been checked. If you export a BOM from EasyEDA, compare it with the schematic values before ordering.

<h1>Main components</h1>

| Designator | Part | Function |
|---|---|---|
| U10 | STM32F070CBT6 | Microcontroller |
| U9 | MAX31790 | 6-channel PWM fan controller with speed measurement |
| U5–U8 | ST1S41 | DC-DC step-down converters (supply voltage of fans 3–6) |
| U1–U4 | DS1841 | Digital potentiometers in the feedback of the DC-DC converters (128 steps) |
| U11 | LDL1117S33 | 3.3V regulator |
| U12 | 1.3" IPS display 240x240, ST7789 | Display module |
| D3 | USBLC6-2SC6 | USB ESD protection |
| X1 | 12 MHz ceramic resonator | Microcontroller clock (the firmware expects exactly 12 MHz) |
| X2 | 32.768 kHz crystal | MAX31790 clock, for accurate fan speed measurement |

The links to the components used by the author are on the [OSHWLab page](https://oshwlab.com/sneer2sneer/pc-fan-controller).

<h1>Connectors and jumpers</h1>

| Designator | Type | Purpose |
|---|---|---|
| FAN1, FAN2 | KF2510-4A (4-pin fan) | Fans with PWM control only, powered directly from the fan power rail |
| FAN3–FAN6 | KF2510-4A (4-pin fan) | Fans with PWM and supply voltage control (via the DC-DC converters) |
| SENS1, SENS2 | XH2.54, 3 pins | OneWire buses for DS18B20 sensors (pull-up resistors are on the board) |
| PWR1 | Molex 4-pin (IDE) | +12V and +5V from the PC power supply |
| PWR2 | 5569, 2x2, 4.2 mm | +12V |
| USB | XH2.54, 4 pins | USB connection (e.g. to an internal USB header of the motherboard) |
| PGM | 2.54 mm, 1x5 | SWD for ST-Link |
| UART | 2.54 mm, 1x4 | Diagnostic log (see [firmware/README.md](/firmware/README.md)) |
| BOOT | 2.54 mm, 1x2 | Jumper for the built-in USB DFU bootloader |
| JMP1 | Wire jumper "SINGLE PS" | Joins the fan and the logic power rails (single power supply) |
| FUNC, RESET | Buttons | Function button (menu) and microcontroller reset |
| LED1, LED2 | LEDs | Error and power indicators |

Pinouts (pin 1 → last pin):
- **SENS1, SENS2:** GND, data, 3.3V;
- **PGM:** GND, RESET, SWCLK, SWDIO, 3.3V;
- **UART:** GND, TX, RX, 3.3V (TX and RX are the microcontroller's, so connect TX to the RX of the USB-UART adapter, and vice versa);
- **BOOT:** BOOT0, 3.3V (closing the jumper starts the bootloader, see [Flashing](/firmware/README.md#flashing)).

<h1>Power</h1>

The board has two power rails:
- **fan rail:** +12V from PWR1 or PWR2, through the fuse F1 (8–10A). It powers FAN1 and FAN2 directly and the DC-DC converters of FAN3–FAN6;
- **logic rail:** +5V from PWR1 or from USB (through the diodes D1 and D2), through the fuse F2 (1–2A), to the 3.3V regulator.

Supported power options:

| Option | Connection | JMP1 |
|---|---|---|
| Molex (12V + 5V) | PWR1 | Open |
| Power 12V + USB | PWR2 + USB | Open |
| Molex + Power 12V | PWR1 + PWR2 (the 12V inputs are connected in parallel) | Open |
| Single power supply, 5–18V | PWR1 (+12V pin) or PWR2 | **Closed** |

With a single power supply, the fans get the supply voltage, so it must match the fans.

<h1>Manufacturing and assembly</h1>

1. Order the PCB using the Gerber archive (4 layers, 100 × 70 mm). The Gerber files were generated from the current PCB source on 2026-10-17 (EasyEDA 6.5.60).
2. Assemble the board according to the schematic (see [Bill of materials](#bill-of-materials)). Check in particular:
   - **X1 must be a 12 MHz resonator:** the firmware's clock configuration (12 MHz × 4 = 48 MHz) requires it, and USB doesn't work with another frequency;
   - the 32.768 kHz crystal X2 needs the 10pF load capacitors C43 and C48.
3. Flash the firmware via USB (BOOT jumper) or via SWD (PGM header), see [firmware/README.md](/firmware/README.md#flashing). On the first start, the controller detects its hardware and the connected fans.

<h1>License</h1>

The hardware design files are licensed under the MIT license, see [LICENSE](/LICENSE).

<h1>Links</h1>

Hardware project on OSHWLab - [https://oshwlab.com/sneer2sneer/pc-fan-controller](https://oshwlab.com/sneer2sneer/pc-fan-controller)

Video about the hardware: [https://youtu.be/2WcZQeidf3s](https://youtu.be/2WcZQeidf3s)
