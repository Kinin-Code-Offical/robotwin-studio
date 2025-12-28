#pragma once

#include "../MCU/ATmega328P_ISA.h"
#include "CircuitComponent.h"
#include "CircuitContext.h"
#include <algorithm>
#include <cstring>
#include <vector>

namespace NativeEngine::Circuit {
class AvrComponent : public Component {
public:
  // Pin Mapping (Standard Arduino Uno)
  // 0-7: PORTD (Digital 0-7)
  // 8-13: PORTB (Digital 8-13)
  // 14-19: PORTC (Analog A0-A5)
  static constexpr int PIN_COUNT = 20;

  std::uint32_t m_pinNodes[PIN_COUNT];
  AvrCore m_cpu;
  std::vector<std::uint8_t> m_flash;
  std::vector<std::uint8_t> m_sram;
  std::uint8_t m_io[0x80];
  std::uint8_t m_regs[32];

  // Norton Equivalent Driver Parameters
  double R_out = 20.0; // 20 Ohms output impedance
  double R_in = 1e9;   // 1 GigaOhm input impedance
  double G_out;
  double G_in;

  AvrComponent(std::uint32_t id) : Component(id, ComponentType::IC_Pin) {
    G_out = 1.0 / R_out;
    G_in = 1.0 / R_in;

    // Initialize CPU
    m_flash.resize(32 * 1024, 0); // 32KB
    m_sram.resize(2 * 1024, 0);   // 2KB
    std::memset(m_io, 0, sizeof(m_io));
    std::memset(m_regs, 0, sizeof(m_regs));
    std::fill(m_pinNodes, m_pinNodes + PIN_COUNT, 0); // Disconnected

    AVR_Init(&m_cpu, m_flash.data(), static_cast<uint32_t>(m_flash.size()),
             m_sram.data(), static_cast<uint32_t>(m_sram.size()), m_io,
             sizeof(m_io), m_regs, sizeof(m_regs));
  }

  void Connect(std::uint8_t pinIndex, std::uint32_t nodeId) override {
    if (pinIndex < PIN_COUNT) {
      m_pinNodes[pinIndex] = nodeId;
    }
  }

  void Step(double dt) override {
    // Calculate cycles
    // 16MHz clock
    std::uint64_t cycles = static_cast<std::uint64_t>(dt * 16000000.0);
    if (cycles == 0)
      cycles = 1;

    // We should really interleave simulation and execution for accuracy.
    // But for now, step entire CPU chunk, then Sync?
    // Or Step 1 instruction, Step Circuit?
    // "System Step" is usually small (e.g. 1us = 16 cycles).

    while (cycles > 0) {
      std::uint8_t cost = AVR_ExecuteNext(&m_cpu);
      if (cost == 0)
        cost = 1; // Safety
      if (cycles >= cost)
        cycles -= cost;
      else
        cycles = 0;
    }
  }

  void Stamp(Context &ctx) override {
    // Sync Input Pins (Read Voltage -> Update CPU Register)
    // Sync Output Pins (Read CPU Register -> Stamp Voltage Source)

    // Pin 0-7: PORTD
    SyncPort(ctx, AVR_PORTD, AVR_DDRD, AVR_PIND, 0, 8);

    // Pin 8-13: PORTB
    SyncPort(ctx, AVR_PORTB, AVR_DDRB, AVR_PINB, 8, 6);

    // Pin 14-19: PORTC
    SyncPort(ctx, AVR_PORTC, AVR_DDRC, AVR_PINC, 14, 6);
  }

private:
  void SyncPort(Context &ctx, int portReg, int ddrReg, int pinReg,
                int pinOffset, int count) {
    std::uint8_t portVal = AVR_IoRead(&m_cpu, portReg);
    std::uint8_t ddrVal = AVR_IoRead(&m_cpu, ddrReg);
    std::uint8_t pinVal = 0; // Input read accumulator

    for (int i = 0; i < count; ++i) {
      int pinIndex = pinOffset + i;
      std::uint32_t nodeId = m_pinNodes[pinIndex];
      if (nodeId == 0)
        continue; // Not connected or Ground? Don't short ground.

      bool isOutput = (ddrVal & (1 << i));
      bool isHigh = (portVal & (1 << i));

      if (isOutput) {
        // Output Mode
        // Model as Voltage Source with series resistance (Norton)
        // I = G * V_target + I_eq?
        // Norton: Current Source I in parallel with G
        // V_Target = 5V or 0V.
        // I_Norton = V_Target / R = V_Target * G.
        // Stamp Current Source I_Norton.
        // Stamp Conductance G.

        double targetV = isHigh ? 5.0 : 0.0;
        double In = targetV * G_out;

        // Stamp Resistor (Conductance) to Ground?
        // No, "Driver" is connected to Node.
        // Driver Side 1: Target Voltage (Phantom/Ideal)
        // Driver Side 2: Node
        // Resistor between Phantom and Node.
        // This is complex for MNA without adding a node.

        // Norton Equivalent:
        // Current Source I_sc = V_th / R_th entering the Node.
        // Conductance G_th connected between Node and Ground.

        // Connected to Ground? Yes the MCU ground reference is 0.

        // KCL at Node: Sum(Currents Leaving) = 0
        // Current Leaving through Driver Resistor = (V_node - V_target) * G
        // = V_node * G - V_target * G
        // Equality: ... + V_node*G - V_target*G = 0
        // ... + V_node*G = V_target*G

        // So:
        // Add G to Matrix[Node, Node]
        // Add (V_target * G) to RHS[Node] (Because it's "Current Entering")

        ctx.StampConductance(nodeId, 0, G_out); // Conductance to Ground
        ctx.StampCurrent(0, nodeId, In); // Current Source from Ground to Node
      } else {
        // Input Mode
        // High Impedance to Ground
        ctx.StampConductance(nodeId, 0, G_in);

        // Read Voltage
        double v = ctx.GetVoltageSafe(nodeId);
        if (v > 2.5) // TTL Threshold
        {
          pinVal |= (1 << i);
        }
      }
    }

    // Update PIN register
    AVR_IoWrite(&m_cpu, pinReg, pinVal);
  }
};
} // namespace NativeEngine::Circuit
