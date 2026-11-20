# 🎛️ Virtual Measurement Instruments Specification

**Objective:** Provide "God Tier" realistic virtual instruments for debugging hardware simulations inside RobotWin Studio.

## 1. Instrument Interaction Model

- **3D Probes:**

  - Instruments are not just UI overlays; they utilize **3D Probes**.
  - **Oscilloscope:** 2 Probes (Signal/Ground) + Hook Clip model.
  - **Multimeter:** Red/Black probes.
  - **Usage:** User drags the probe in 3D scene to a specific Pin or Trace on the PCB module.
  - **Connection:** Logic snaps the probe to the nearest electrical node.

- **Floating UI Panels (The "Bench"):**
  - Each open instrument renders a high-fidelity 2D interactive panel (Canvas).
  - **Minimize/Dock:** Panels can be minimized to the bottom "Toolbar".
  - **Persistence:** Settings (Timebase, Volts/Div) persist while minimized.

---

## 2. Instrument List & Specs

### 🔬 1. Analog Discovery 2 (All-in-One Model)

Simulates the popular USB multi-instrument. Contains:

#### A. Oscilloscope (2-Channel)

- **Bandwidth:** Simulated 30MHz limit (signals faster than this attenuate).
- **Quantization Error:** 14-bit resolution simulation (tiny stair-steps on zoom).
- **Noise Floor:** Adds realistic thermal noise (~2mV) to the reading.
- **Features:** Trigger (Edge/Pulse), Math (A-B), FFT View.

#### B. Logic Analyzer (16-Channel)

- **Thresholds:** Logic levels configurable (1.8V, 3.3V, 5V).
- **Glitch Detection:** Captures spikes narrower than sample rate (simulated probability).

#### C. Spectrum Analyzer

- **Function:** FFT of the current signal. Useful for verifying EMI noise generation properties mentioned in Architecture.

---

### ⚡ 2. Digital Multimeter (DMM)

- **VDC / VAC:** True RMS simulation.
- **Resistance (Ohms):** Injects a small current to measure voltage drop.
- **Continuity:** Beeps if resistance < 50 Ohms.
- **Error Margin:**
  - DC Voltage: $\pm 0.5\% + 2$ digits.
  - Resistance: $\pm 0.8\%$.
  - **Auto-Ranging Delay:** 0.5s flicker when switching ranges.

---

### 🔋 3. Programmable Power Supply

- **Channels:** 2x Variable (0-30V, 0-5A).
- **Modes:** CC (Constant Current), CV (Constant Voltage).
- **Simulation:**
  - **OVP (Over Voltage Protection):** Cuts output if tripped.
  - **Transients:** Simulates voltage overshoot on load release.

---

### 🧲 4. LCR Meter

- **Purpose:** Measure Inductance (L), Capacitance (C), Resistance (R) of simulated components.
- **Frequency:** Selectable test frequency (100Hz, 1kHz, 10kHz).
- **ESR Measurement:** Shows Equivalent Series Resistance (critical for capacitor health simulation).

---

### 🌊 5. Function / Signal Generator

- **Waves:** Sine, Square, Triangle, Noise.
- **Impedance:** Switchable 50 Ohm / High-Z output.
- **Frequency:** 1Hz - 10MHz.
- **Jitter:** Simulation of phase noise at high frequencies.

---

### 🔌 6. Impedance Analyzer

- **Purpose:** Advanced Bode Plot generation.
- **Usage:** Sweeps a frequency range into a circuit (Filter) and plots Gain/Phase.

---

## 3. Implementation Priorities

1.  **Probe System:** The physics of clicking a 3D object and mapping it to a Node ID in the backend.
2.  **Multimeter:** Simplest proof of concept (Value readout).
3.  **Oscilloscope:** Requires a rolling buffer and shader-based graph rendering for performance.
