# 8. Instruments

The instruments in RobotWin Studio behave like real ones: the oscilloscope's probe loads the circuit, the multimeter's fuse can blow, the bench supply goes into current limit. Most of them are in the tabs at the bottom of the **Circuit** page.

![Circuit page and oscilloscope](../../images/en/schematic.png)

## Oscilloscope

The **Oscilloscope** tab is a 4-channel digital oscilloscope.

### Attaching a probe

1. Start the simulation (F5).
2. Click the wire you want to measure; the probe goes on the first free channel.
3. For a particular channel, right-click the wire and choose **… → osiloskop CH1** … **CH4**. A probe can be attached before the simulation too; it measures once the simulation starts.

### Settings

| Section | Settings |
| --- | --- |
| Horizontal | Time/div, position, roll mode |
| Channel | Volts/div, position, coupling (DC / AC / GND), probe 1× / 10×, 20 MHz bandwidth limit, invert, current probe |
| Trigger | Mode (**Auto**, **Normal**, **Single**), source (1–4), slope (rising / falling), level, holdoff |
| Maths | **A−B**, **A+B**, **A×B** (a voltage times a current is the power, W), **∫A** (a current's charge, C; a power's energy, J), **dA/dt** (slew rate, V/s); A and B are any two channels. The scale fits the result by itself, and the measurements work on the math channel too (the **mean** of V×I is the average power). |
| Buttons | **RUN / STOP**, **SINGLE**, **AUTOSET**, **REF**, cursors |

With **Probe loading** on, the probe connects to the circuit as 1 MΩ ∥ 110 pF (1×) or 10 MΩ ∥ 12 pF (10×); in high-impedance circuits you see how the measurement disturbs the circuit.

Automatic measurements: maximum, minimum, peak-to-peak, amplitude, mean, RMS, period, frequency, duty cycle, rise and fall time.

**REF (reference):** keeps the screen as a bench scope's REF memory does. The kept screen is drawn under the next runs, faint, in its channels' colours, and it stays when the simulation is stopped and started again: change a resistor and see the new waveform over the old one. When the time/div and the record length are the same, the status line gives each channel's deviation from the reference as an RMS (`REF CH1 Δ120 mV`). Pressing **REF** again drops the reference.

### Serial protocol decoder

The oscilloscope panel's **Decoder** section reads the probed wires as UART, I²C or SPI and draws a decode lane along the bottom of the screen (bytes as boxes, START/STOP and ACK as marks, errors red). **UART**: channel A is the line; **Baud** and **Parity** (none, even, odd); the start bit, the data bits (least significant first), the parity and the stop bit are read at the middle of each bit, a low stop bit is a framing error and a parity that does not hold a parity error. **I²C**: A is SCL, B SDA; START (repeated START too), the 7-bit address and R/W, ACK/NACK after every byte, the data, STOP. **SPI**: A is SCK, B the data (MOSI or MISO), an optional CS (selected while low); **Mode** 0–3 sets the clock's idle level and the edge the data is read on. The logic threshold is half way between the record's lowest and highest level; a slow edge, a glitch or a wrong baud rate shows as it would. The Arduino Uno's `Serial` drives its TX pin (D1) bit by bit: probe D1 and turn on the UART decoder to read what the sketch prints off the line's voltage. Its receiver reads the RX pin (D0) too: wire one board's TX to another's RX and the second receives what the first prints, off the wire (below).

Clicking a byte in the decode strip puts the time cursors at its start and end (its duration in the cursor readout), highlights its wire in the circuit and reads it in the status line with its time ("UART 0x41 · 2.08 ms · 1.04 ms · N5"); the picked byte is outlined in the strip.

### Receiving on a serial line

The Uno's USART receiver follows D0's voltage, with the moments it crossed the threshold between the circuit's steps, and decodes it as the chip does:

- a falling edge is a start bit; it is accepted if the line is still low at its middle, otherwise it is taken for a glitch and dropped;
- each bit is the majority of three samples around its middle (1/8 bit apart with `U2X`, 1/16 without): a short glitch that hits one sample is rejected;
- the character size (5–8 bits), the parity (none, even, odd) and the number of stop bits come from `UCSR0B`/`UCSR0C`; a setting such as `Serial.begin(9600, SERIAL_8E1)` holds for sending and receiving alike;
- a low stop bit sets **FE0** (frame error), a parity that does not hold **UPE0**;
- a received character goes into the two-character buffer, a third waits in the shift register; if a fourth's start bit comes before the program reads, the waiting one is lost and the next character read carries **DOR0** (overrun).

Text typed into the serial monitor enters the same buffer one frame time apart and is never lost.

### Line fault injection

The two parts under **Measurement & Source › Fault injection** are for trying what the firmware does with a faulty line:

| Part | Wiring | Fault type |
| --- | --- | --- |
| **Line Fault Injector** | HAT to the line to fault, GND (and for high faults VCC) to the supply | `glitch-low` / `glitch-high`: short interference pulses at random moments (pulling the line to GND or to VCC); `stuck-low` / `stuck-high`: the line stuck at that level from a moment on |
| **Loose Contact** | carries the wire from A to B | `intermittent`: short breaks at random moments (a loose connector, a cracked joint); `open`: fully open from a moment on |

Settings: average rate (the pulses come at random, this often on average), pulse or break length, start, duration (0 = for good) and seed (the same seed gives the same faults every run). The injector's short is 1 Ω.

The fault enters the circuit physically, and the circuit decides what comes of it:

- at 9600 baud, 2 µs pulses are voted out by the receiver and the message arrives whole;
- 60 µs pulses flip bits, and on an idle line they make up characters (`FF`, `FB`);
- a short pulse just before a start bit can start the frame half a bit early: the real chip does the same;
- a short fighting a driver heats the pin: it burns when an overload lasts longer than its I²t allows (a few milliseconds of dead short), not from microsecond pulses.

## Multimeter

The **Multimeter** tab is a 6000-count, true-RMS, autoranging handheld multimeter.

1. Choose **Probe multimeter (red / black probes)** in the **Instrument** list.
2. Right-click a wire and choose **Multimeter red probe → …**, and on another wire **Multimeter black probe (COM) → …**.
3. Pick the function with the rotary switch: DC V, AC V, DC A, AC A, Ω, continuity (beeps below 30 Ω), diode, capacitance, frequency + duty cycle.

Things to watch (as on a real multimeter):

- To measure current the multimeter goes **in series** with the circuit. The mA input has a 440 mA fuse, the A input an 11 A fuse; too much current blows the fuse.
- The voltage input loads the circuit with 10 MΩ.
- Trying to measure resistance in a live circuit gives a warning.

You can also put a multimeter into the circuit as a part: **Digital Multimeter (6000 counts)** or **Multimeter (current, mA input)**. Then pick that part in the **Instrument** list.

## Bench power supply

The library has **Lab Power Supply 0-30 V 3 A** and **Lab Power Supply 5 V / 100 mA** (bench supplies).

- **CV / CC:** gives the set voltage; at the current limit it switches to constant current (CC). A short circuit does not burn your circuit: the supply goes to CC.
- **OVP:** over-voltage protection; when it trips it switches the output off.
- It has an output switch; the supply cannot sink current (it only sources).

Using the **5 V / 100 mA** supply when first trying a new circuit stops mistakes from burning parts.

## Adjustable AC/DC power supply

**Adjustable AC/DC Supply 0-30 V DC / 0-24 V AC, 3 A** gives one of three outputs, picked with **Output type** in the inspector:

| Output | What it gives |
| --- | --- |
| **DC** | A set voltage (CV) with a current limit (CC), like a bench supply |
| **AC** | A sine: **RMS voltage**, **frequency** and **phase** are set (peak = √2 × RMS; 17 V peak for 12 V~) |
| **DC + AC** | An AC ripple on top of the DC voltage, like a rectified supply or a noisy rail |

- On AC the current limit holds in both directions: the supply sources and sinks current and holds the peak current at the limit.
- **OVP** looks at the voltage's absolute value.
- The symbol shows the setting, e.g. `12V~` or `12+1~`. The circuit's voltage readouts print the RMS value of AC nets with `V~`; the multimeter's AC voltage range measures true RMS too. The simulation report gives the true RMS voltage and current over whole cycles.

Presets: **AC Source 12 V RMS 50 Hz** (a transformer's output; bridge rectifier and capacitor experiments), **AC Source 24 V RMS 60 Hz** (industrial control voltage), **Mains 230 V RMS 50 Hz (isolated, 10 A)** (325 V peak) and **DC 12 V + 1 V RMS ripple 100 Hz** (regulator and filter experiments).

## Tolerances

Real parts are not at their labelled values. The **Simulation: part tolerance** option in the right panel:

| Mode | What happens |
| --- | --- |
| **Nominal (ideal values)** | Every part exactly at its label value |
| **Realistic (fixed seed)** | Each part takes a fixed real value within its tolerance (resistor ±5 %, ceramic ±10 %, electrolytic −20/+80 %, hFE, LED Vf…). The same **Seed** always gives the same board; **New** draws another board. |
| **Monte Carlo (N runs)** | Use the **Monte Carlo** tab below to see the spread |

## Monte Carlo

The **Monte Carlo** tab runs the circuit N times, drawing the tolerances anew each time, and measures a net you choose.

1. Choose the net and the measure (final value, mean, maximum, minimum, peak-to-peak, RMS).
2. Press **Run** (**Cancel** cancels).
3. The result appears as a histogram with the smallest and largest values, the runs they came from and the spread.

Board firmware does not run during Monte Carlo; the analog circuit is examined.

## Manufacturers' SPICE models

With a diode, Schottky, zener, NPN/PNP transistor or MOSFET selected, **Paste a SPICE model…** in the inspector takes the manufacturer's `.model` card (as a datasheet or the vendor's library gives it; `+` continuation lines, `*` and `;` comments, SPICE suffixes: f p n u m k MEG G, mil). The card's numbers become the part's values (one undoable change): a diode's IS, N, RS, BV, IBV, EG, CJO; a transistor's IS, BF, BR, VAF, CJE, CJC; a MOSFET's VTO. Parameters our model does not use (TT, XTI …) are listed in the status bar. Validation: with the 1N4148 card a diode drops at 9 mA what Shockley's law says.

## Frequency response (AC analysis)

The **Frequency response** tab computes the circuit's small-signal response (as SPICE's .AC): the circuit is compiled afresh, its operating point solved, and with the chosen **input source** driven by a 1 V small signal (the other sources at rest) the voltage of the **output net**, against the **reference**, is found as a complex number at every frequency. Capacitors, inductors and motor windings come in with their real impedances; transistors, diodes and op-amps with their slopes at the operating point (small-signal models). The result is a Bode plot: gain (dB) and phase (°) on a logarithmic frequency axis, with the gain at the lowest frequency, the −3 dB corner and the peak gain written beside it. The running simulation is not touched.

Validation (`AcAnalysisTests`): a 1 kΩ / 1 µF low-pass is −3.01 dB and −45° at 159.15 Hz and −20 dB a decade above; a series RLC at resonance (1591.5 Hz) passes 0 dB at 0°; a common-emitter stage's gain is within 2 % of what its operating point's gm gives.

## Fault finding

The **Fault finding** tab finds design mistakes in your circuit.

- **Analyse**: scans the circuit by rules: supply short, LED without a resistor, reversed LED, reversed electrolytic capacitor, floating inputs, missing ground, driving a motor straight from a pin, missing flyback diode, regulator problems…
- **Live in simulation**: watches during simulation how close parts get to their limits, whether the battery is overloaded, whether the supply enters CC mode, and the multimeter fuse.
- **Why?**: explains why a part burned.

## Measuring in the world

On the **World** page:

- The **Cables and measurements** tab below shows each cable's current, power and load percentage.
- With the cable tool, the live voltage of the pin under the mouse is shown.
- The selected object's inspector has a **Live measurement** section.

## Recording

**File → Save simulation recording (.rwsim)…** stores the measurement channels, events and serial output with the project. Opening the recording brings the waveforms back on the oscilloscope and the events in the log.

---

[← Previous: 7. World](07-world.md) · [Contents](README.md) · [Next: 9. Keyboard shortcuts →](09-shortcuts.md)
