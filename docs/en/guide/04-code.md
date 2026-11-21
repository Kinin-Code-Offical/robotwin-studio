# 4. Code

The **Kod** (code) page (Ctrl+3) is where you write, compile, load and run the firmware of the programmable boards in the circuit (Arduino Uno, Arduino Nano, ESP32) on emulated boards. Each board's code is stored inside the project.

## Before you start

- The circuit needs a programmable board. Without one the page says **Programlanabilir kart yok** (no programmable board); **Arduino Uno ekle** (add an Arduino Uno) adds one.
- Compiling Arduino code needs `arduino-cli`:

  ```text
  winget install ArduinoSA.CLI
  arduino-cli core install arduino:avr
  ```

- Compiling ESP32 code needs the Visual Studio C++ tools (MSVC).

Details: [1. Getting started](01-getting-started.md#to-compile-your-own-code-optional).

## Step by step: your first code

1. Pick the board to program from the **Kart** (board) list.
2. Pick a template from the **Örnek** (example) list, e.g. **Blink (D13 LED)**.
3. Press **Derle** (compile) or **F7**. The build output appears in the **Derleyici** (compiler) tab below. Errors are explained in Turkish and the line is marked.
4. Press **Yükle ve çalıştır** (load and run) or **F5**. The code is compiled, loaded into the emulated board and the simulation starts.
5. See the `Serial.print` output in the **Seri monitör** (serial monitor) tab. Type in the box below and press **Gönder** (send) to send data to the board.

The other toolbar buttons stop the simulation and reset the board.

## Arduino templates

| Template | What it does |
| --- | --- |
| Blink (D13 LED) | Blinks the LED on D13 once a second |
| PWM ile parlaklık (D9) (PWM brightness) | Slowly raises and lowers the power of the LED or motor on D9 |
| Buton ile LED (LED with a button) | The LED on D13 lights while the button on D2 is pressed |
| Analog okuma + seri (analog read + serial) | Reads the voltage on A0 and prints it to the serial monitor |
| HC-SR04 mesafe (distance) | Prints the distance in cm from an ultrasonic sensor |
| Engelden kaçan robot (obstacle avoider) | Avoids obstacles with an HC-SR04 and two motors |
| Çizgi izleyen (2 sensör) (line follower, 2 sensors) | Follows a line with two TCRT5000 |
| I²C tarayıcı (I²C scanner) | Finds the addresses of the devices on the I²C bus |
| MPU-6050 ivme (acceleration) | Reads the accelerometer and gyroscope values |

New code for ESP32 boards starts with an ESP32 Blink that blinks the LED on GPIO2.

## How does the Arduino work?

Your code is compiled with the real `avr-gcc` (through `arduino-cli`). The resulting HEX file runs step by step with the circuit on a cycle-accurate **ATmega328P emulator**. Supported:

- The whole instruction set and interrupts
- Digital pins, external interrupts and pin-change interrupts
- Timer0/1/2 and PWM (`analogWrite`)
- Serial port (USART), ADC (`analogRead` reads the real voltage in the circuit), EEPROM, watchdog
- I²C (TWI); the SSD1306 OLED, PCF8574 + HD44780 LCD and MPU-6050 modules really answer

> Tip: if you have a `.hex` file compiled in the Arduino IDE, load it directly with **HEX yükle…** (load HEX) in the **Kart pinleri** (board pins) tab on the **Devre** (circuit) page.

## How does the ESP32 work?

ESP32 code (a sketch) is compiled natively on your PC with MSVC and runs in step with the circuit. The first build takes a while because the ESP32 hardware layer is built too; later builds come quickly from the cache.

Supported:

- `pinMode`, `digitalRead/Write`, `attachInterrupt`
- `analogRead` (with attenuation and resolution settings), `analogWrite`, `ledc*` PWM, `dacWrite`, `touchRead`, `tone`, `pulseIn`
- Hardware timers, `millis`, `micros`, delays
- FreeRTOS tasks, queues, semaphores
- `Serial`, `Wire` (I²C), `EEPROM`, `Servo` / `ESP32Servo`
- `WiFi` (a virtual network inside the world; no Internet), BLE (basic features), **Bluepad32** (PS5, PS4, Xbox controllers)

`SPI` is only an empty stub; SPI devices do not work.

## Code of boards in the world

To write the code of a board you put in a world, select the board in the world and press **Kartın kodunu yaz** (write the board's code) in the inspector. The board opens in its own tab on the Code page. Details: [7. World](07-world.md).

---

[← Previous: 3. PCB](03-pcb.md) · [Contents](README.md) · [Next: 5. Model →](05-model.md)
