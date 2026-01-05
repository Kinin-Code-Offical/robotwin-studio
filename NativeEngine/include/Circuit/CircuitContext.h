#pragma once

#include "CircuitComponent.h"
#include <map>
#include <memory>
#include <vector>


namespace NativeEngine::Circuit {
struct Node {
  std::uint32_t id;
  double voltage;
  double lastVoltage; // For previous time step (Transient analysis)
  bool isGround;
};

class Context {
public:
  Context();
  ~Context();

  void Reset();

  // Graph Construction
  std::uint32_t CreateNode();
  void AddComponent(std::shared_ptr<Component> component);
  Node *GetNode(std::uint32_t id);

  // Simulation
  void Step(double dt);
  double GetNodeVoltage(std::uint32_t nodeId) const;

  // Solver Configuration
  int m_maxIterations = 50;
  double m_epsilon = 1e-6;

  // Solver Interface
  std::size_t GetNodeCount() const { return m_nodes.size(); }
  const std::vector<std::shared_ptr<Component>> &GetComponents() const {
    return m_components;
  }

  // MNA Matrix helpers (Low Level)
  void AddToMatrix(std::size_t row, std::size_t col, double value);
  void AddToRHS(std::size_t row, double value); // Add to Right Hand Side vector

  // Stamping Helpers (High Level)
  // Add conductance between two nodes
  void StampConductance(std::uint32_t nodeA, std::uint32_t nodeB,
                        double conductance);
  // Add current source flowing FROM -> TO ( Conventional Current )
  void StampCurrent(std::uint32_t nodeFROM, std::uint32_t nodeTO,
                    double current);
  // Look up Matrix Index for a node
  int GetMatrixIndex(std::uint32_t nodeId) const;

  // Helper for Components to get their node voltages during iteration
  double GetVoltageSafe(std::uint32_t nodeId) const;

  double m_timeIsTransient = false;
  double m_dt = 0.0;

  // Matrix storage
  std::vector<double> m_matrix;
  std::vector<double> m_rhs;
  std::vector<double> m_solution;

private:
  void SolveMNA();
  void ResizeMatrix(std::size_t size);

  std::vector<Node> m_nodes;
  std::vector<std::shared_ptr<Component>> m_components;
  std::vector<int> m_nodeToMatrixIndex; // Cached during Solve
  double m_time;
};
} // namespace NativeEngine::Circuit
