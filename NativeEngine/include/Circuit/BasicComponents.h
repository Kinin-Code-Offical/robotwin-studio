#pragma once

#include "CircuitComponent.h"
#include "CircuitContext.h"

namespace NativeEngine::Circuit {
class Resistor : public Component {
public:
  Resistor(std::uint32_t id, double resistance)
      : Component(id, ComponentType::Resistor), m_resistance(resistance) {
    if (m_resistance < 1e-9)
      m_resistance = 1e-9;
    m_conductance = 1.0 / m_resistance;
  }

  void Connect(std::uint8_t pinIndex, std::uint32_t nodeId) override {
    if (pinIndex == 0)
      m_nodeA = nodeId;
    else if (pinIndex == 1)
      m_nodeB = nodeId;
  }

  void Stamp(Context &ctx) override {
    ctx.StampConductance(m_nodeA, m_nodeB, m_conductance);
  }

  double GetResistance() const { return m_resistance; }

  std::uint32_t m_nodeA = 0;
  std::uint32_t m_nodeB = 0;

private:
  double m_resistance;
  double m_conductance;
};

class VoltageSource : public Component {
public:
  VoltageSource(std::uint32_t id, double voltage)
      : Component(id, ComponentType::VoltageSource), m_voltage(voltage) {}

  void Connect(std::uint8_t pinIndex, std::uint32_t nodeId) override {
    if (pinIndex == 0)
      m_nodePos = nodeId; // +
    else if (pinIndex == 1)
      m_nodeNeg = nodeId; // -
  }

  void Stamp(Context &ctx) override {
    int nPos = ctx.GetMatrixIndex(m_nodePos);
    int nNeg = ctx.GetMatrixIndex(m_nodeNeg);
    int idx = static_cast<int>(m_matrixIndex);

    if (idx == -1)
      return;

    if (nPos != -1) {
      ctx.AddToMatrix(nPos, idx, 1.0);
      ctx.AddToMatrix(idx, nPos, 1.0);
    }
    if (nNeg != -1) {
      ctx.AddToMatrix(nNeg, idx, -1.0);
      ctx.AddToMatrix(idx, nNeg, -1.0);
    }
    ctx.AddToRHS(idx, m_voltage);
  }

  double GetVoltage() const { return m_voltage; }
  void SetVoltage(double v) { m_voltage = v; }

  std::uint32_t m_nodePos = 0;
  std::uint32_t m_nodeNeg = 0;
  std::size_t m_matrixIndex = 0;

private:
  double m_voltage;
};

// Norton-equivalent driver: stamps a conductance to ground and a current source
// such that the node is driven towards a target voltage with finite impedance.
class AnalogDriver : public Component {
public:
  AnalogDriver(std::uint32_t id, double voltage, double resistance_ohms = 1000.0)
      : Component(id, ComponentType::AnalogDriver), m_voltage(voltage) {
    SetResistance(resistance_ohms);
  }

  void Connect(std::uint8_t pinIndex, std::uint32_t nodeId) override {
    if (pinIndex == 0) {
      m_node = nodeId;
    }
  }

  void Stamp(Context &ctx) override {
    if (m_node == 0) {
      return;
    }
    // Conductance to ground.
    ctx.StampConductance(m_node, 0, m_conductance);
    // Current source from ground to node.
    ctx.StampCurrent(0, m_node, m_voltage * m_conductance);
  }

  void SetVoltage(double v) { m_voltage = v; }
  double GetVoltage() const { return m_voltage; }

  void SetResistance(double r) {
    if (r < 1e-6) {
      r = 1e-6;
    }
    m_resistance = r;
    m_conductance = 1.0 / m_resistance;
  }

  std::uint32_t m_node = 0;

private:
  double m_voltage = 0.0;
  double m_resistance = 1000.0;
  double m_conductance = 1.0 / 1000.0;
};
} // namespace NativeEngine::Circuit
