#pragma once

#include "CircuitComponent.h"
#include "CircuitContext.h"
#include <algorithm>
#include <cmath>


namespace NativeEngine::Circuit {
class Diode : public Component {
public:
  // Standard Silicon Diode Parameters
  // Is = 1pA, Vt = 25.85mV, N = 1
  double Is = 1e-12;
  double Vt = 0.02585;
  double N = 1.0;

  // Linearization limits to prevent overflow
  double Vmax = 3.0;   // Above this, just linear extension
  double Gmin = 1e-12; // Leakage

  std::uint32_t m_nodeAnode = 0;
  std::uint32_t m_nodeCathode = 0;

  Diode(std::uint32_t id) : Component(id, ComponentType::Diode) {}

  void Connect(std::uint8_t pinIndex, std::uint32_t nodeId) override {
    if (pinIndex == 0)
      m_nodeAnode = nodeId; // Anode
    else if (pinIndex == 1)
      m_nodeCathode = nodeId; // Cathode
  }

  void Stamp(Context &ctx) override {
    // 1. Get current voltages
    double vA = ctx.GetVoltageSafe(m_nodeAnode);
    double vK = ctx.GetVoltageSafe(m_nodeCathode);
    double vD = vA - vK;

    double thermalV = N * Vt;

    double G_eq = 0;
    double I_diode = 0;

    if (vD > Vmax) {
      // Linear extension
      double expMax = std::exp(Vmax / thermalV);
      double I_max = Is * (expMax - 1);
      double G_max = (Is / thermalV) * expMax;

      I_diode = I_max + G_max * (vD - Vmax);
      G_eq = G_max;
    } else if (vD < -5.0) {
      // Reverse bias
      G_eq = Gmin;
      I_diode = -Is;
    } else {
      // Normal
      double expV = std::exp(vD / thermalV);
      I_diode = Is * (expV - 1);
      G_eq = (Is / thermalV) * expV + Gmin;
    }

    // Norton Equivalent:
    // Current I = G_eq * V + I_eq
    // I_eq = I - G_eq * V
    double I_source = I_diode - G_eq * vD;

    // Stamp Resistor G_eq
    ctx.StampConductance(m_nodeAnode, m_nodeCathode, G_eq);

    // Stamp Current Source -I_eq (flowing from A to K??)
    // No, I_source is the offset.
    // KCL at A: Currents leaving = 0
    // I_diode_branch = G_eq(Va - Vk) + I_source
    // So: ... + G_eq(Va-Vk) = -I_source
    // RHS A receives -I_source
    // RHS K receives +I_source

    // StampCurrent(Nodes, val) adds to RHS.
    // StampCurrent(From, TO, I) adds +I to TO, -I to FROM.
    // If we want -I_source at A (FROM), we pass I_source to StampCurrent(A, K).
    // A (FROM) gets -I_source
    // K (TO) gets +I_source

    ctx.StampCurrent(m_nodeAnode, m_nodeCathode, I_source);
  }
};
} // namespace NativeEngine::Circuit
