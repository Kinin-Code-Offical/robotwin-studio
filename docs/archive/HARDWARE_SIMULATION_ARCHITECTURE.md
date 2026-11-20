# ⚡ Hardware Simulation Engine & "Circuit Studio" Architecture

**Status:** Draft / Proposed  
**Module:** `HardwareEngine`  
**File Extension:** `.rtcomp` (RobotWin Component)

## 1. Core Philosophy: "The Compacted Engineering Replica"

We must bridge the gap between **Atomic Circuit Simulation** (SPICE-level, slow) and **Real-Time Simulation** (Game-level, fast).

### The "Compaction" Algorithm Strategy

Instead of simulating distinct electrons flowing through 100 resistors in a custom shield every frame (which kills performance), we introduce a **Compaction Phase**.

1.  **Design Phase (Circuit Studio):** User places discrete components (Resistors, Capacitors, ICs).
2.  **Analysis Phase (Pre-Calculation):** The engine analyzes the schematic connectivity and component ratings.
    - **Thermal Output:** Sum of $I^2R$ potential based on inputs.
    - **Failure Points:** Identifies discrete components running near tolerance (e.g., a 1/4W resistor receiving 0.2W).
    - **Noise Figure:** Calculates total EMI potential based on track lengths and high-frequency switching components.
3.  **Compaction Phase (Baking):**
    - The complex graph is converted into a **Statistical Black Box (`.rtcomp`)**.
    - **Inputs:** `VCC`, `GND`, `Signal_In`
    - **Outputs:** `Signal_Out`, `Heat_Generation`, `Noise_Emission`
    - **Logic:** `Signal_Out = f(Signal_In) + CalculatedNoise`
    - **Failure Logic:** Instead of tracking component health, we use a probabilistic decay model: `If (Temp > 80C) { FailureProbability += 0.01/sec * ComponentCountFactor }`.

---

## 2. `.rtcomp` File Structure

This is the binary format for "Custom Modules".

```json
{
  "Meta": {
    "Name": "Custom Motor Driver",
    "ComponentCount": 45,
    "PCB_Dimensions": [50, 30] // mm
  },
  "Pins": [
    { "Name": "VCC", "Pos": "Corner_TopLeft" },
    { "Name": "PWM", "Pos": "Corner_BottomLeft" }
  ],
  "Behavior Model": {
    "Type": "LinearAmplifier", // or "DigitalLogic", "Passive"
    "Gain": 12.0,
    "Latency": "2us"
  },
  "Fault Model": {
    "MTBF_Base": 500000, // Mean Time Before Failure (seconds)
    "Thermal_Coefficient": 0.05, // % Efficiency drop per Degree C
    "Explosion_Trigger": "Overvoltage > 18V for 0.5s",
    "WeakPoints": [
      { "Location": [20, 10], "Type": "Capacitor_Blow", "Threshold": "12V" }
    ]
  },
  "Visuals": {
    "AutoLayoutPCB": true,
    "3D_Reference": "Assets/Generated/PCBs/Guid.obj"
  }
}
```

---

## 3. Physical Simulation Layers

The `HardwareEngine` runs in a parallel thread to the standard Physics/Firmware.

### A. Thermal & Environmental Layer

- **Global Ambient Temp:** Affects all `.rtcomp` modules.
- **Heat Dissipation:** Modules generate heat -> heats up PCB -> heats up air -> affects neighbor sensors.
- **EMI Field:** High current modules (Motor Drivers) emit a "Noise Sphere". Sensitive modules (OpAmps) within this sphere get signal degradation.

### B. Power Grid (The "Bus")

- **Droop Simulation:** High current draw from Motors causes `VCC` on the bus to drop (e.g., 5.0V -> 4.2V).
- **Brownout:** Processors check this `VCC`. If it drops below BOD (Brown-out Detention, e.g., 2.7V), they trigger a Reset.

---

## 4. PCB Auto-Generator (Procedural Mesh)

When a `.rtcomp` is generated, we create a 3D visualization automatically.

1.  **Layout Algorithm:** "Cross-Minimization Box Packing".
    - High-heat components moved to edges.
    - Connectors pushed to corners (User Requirement).
2.  **Mesh Generation:**
    - **PCB Base:** Green/Blue FR4 extrusion.
    - **Components:** Simplified bounding boxes or proper 3D models if available.
    - **Traces:** Procedural lines textures on the surface.
3.  **Hilight System:** When a fault occurs (e.g., "Resistor burnt"), the specific area on the texture blackens/chars.

---

## 5. UI Integration (The "Circuit Studio" View)

A dedicated mode separate from the Robot Assembler.

- **Canvas:** 2D Schematic capture.
- **3D Preview:** Live rendering of the PCB being generated.
- **Simulation Bar:** "Run Thermal Analysis", "Calculate MTBF", "Export .rtcomp".
