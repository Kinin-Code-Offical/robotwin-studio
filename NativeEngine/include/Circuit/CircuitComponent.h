#pragma once

#include <cstdint>
#include <string>
#include <vector>


namespace NativeEngine::Circuit {
// Forward declaration
struct Node;
class Context;

enum class ComponentType {
  Resistor,
  VoltageSource,
  Ground,
  Diode,
  LED,
  Switch,
  IC_Pin // Connection point for complex ICs like AVR
};

/// <summary>
/// Abstract base class for all circuit components.
/// Represents a 2-terminal or Multi-terminal device.
/// </summary>
class Component {
public:
  Component(std::uint32_t id, ComponentType type) : m_id(id), m_type(type) {}
  virtual ~Component() = default;

  std::uint32_t GetId() const { return m_id; }
  ComponentType GetType() const { return m_type; }

  // Connect a specific pin of this component to a circuit node
  virtual void Connect(std::uint8_t pinIndex, std::uint32_t nodeId) = 0;

  // Populate the MNA Matrix (Modified Nodal Analysis)
  virtual void Stamp(Context &ctx) = 0;

  // Step simulation time (Optional, for CPUs etc)
  virtual void Step(double dt) {}

protected:
  std::uint32_t m_id;
  ComponentType m_type;
};
} // namespace NativeEngine::Circuit
