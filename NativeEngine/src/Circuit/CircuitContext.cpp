#include "../../include/Circuit/CircuitContext.h"
#include "../../include/Circuit/BasicComponents.h"
#include <algorithm>
#include <cmath>
#include <iostream>

namespace NativeEngine::Circuit {
Context::Context() : m_time(0.0) {
  m_nodes.push_back({0, 0.0, 0.0, true}); // Ground
}

Context::~Context() {}

void Context::Reset() {
  m_nodes.clear();
  m_components.clear();
  m_nodes.push_back({0, 0.0, 0.0, true});
  m_time = 0.0;
}

std::uint32_t Context::CreateNode() {
  std::uint32_t id = static_cast<std::uint32_t>(m_nodes.size());
  m_nodes.push_back({id, 0.0, 0.0, false});
  return id;
}

void Context::AddComponent(std::shared_ptr<Component> component) {
  m_components.push_back(component);
}

Node *Context::GetNode(std::uint32_t id) {
  if (id < m_nodes.size())
    return &m_nodes[id];
  return nullptr;
}

double Context::GetNodeVoltage(std::uint32_t nodeId) const {
  if (nodeId < m_nodes.size())
    return m_nodes[nodeId].voltage;
  return 0.0;
}

double Context::GetVoltageSafe(std::uint32_t nodeId) const {
  if (nodeId < m_nodes.size())
    return m_nodes[nodeId].voltage;
  return 0.0;
}

void Context::ResizeMatrix(std::size_t size) {
  m_matrix.assign(size * size, 0.0);
  m_rhs.assign(size, 0.0);
  m_solution.assign(size, 0.0);
}

void Context::AddToMatrix(std::size_t row, std::size_t col, double value) {
  std::size_t size = m_rhs.size();
  if (row < size && col < size) {
    m_matrix[row * size + col] += value;
  }
}

void Context::AddToRHS(std::size_t row, double value) {
  if (row < m_rhs.size()) {
    m_rhs[row] += value;
  }
}

int Context::GetMatrixIndex(std::uint32_t nodeId) const {
  if (nodeId == 0)
    return -1; // Ground
  if (nodeId < m_nodeToMatrixIndex.size())
    return m_nodeToMatrixIndex[nodeId];
  return -1;
}

void Context::StampConductance(std::uint32_t nodeA, std::uint32_t nodeB,
                               double g) {
  int i = GetMatrixIndex(nodeA);
  int j = GetMatrixIndex(nodeB);

  if (i != -1)
    AddToMatrix(i, i, g);
  if (j != -1)
    AddToMatrix(j, j, g);
  if (i != -1 && j != -1) {
    AddToMatrix(i, j, -g);
    AddToMatrix(j, i, -g);
  }
}

void Context::StampCurrent(std::uint32_t nodeFROM, std::uint32_t nodeTO,
                           double current) {
  int i = GetMatrixIndex(nodeTO);
  int j = GetMatrixIndex(nodeFROM);

  if (i != -1)
    AddToRHS(i, current);
  if (j != -1)
    AddToRHS(j, -current);
}

void SolveLinearSystem(std::vector<double> &A, std::vector<double> &b,
                       std::vector<double> &x, std::size_t n) {
  for (std::size_t k = 0; k < n; k++) {
    std::size_t maxRow = k;
    double maxVal = std::abs(A[k * n + k]);
    for (std::size_t i = k + 1; i < n; i++) {
      if (std::abs(A[i * n + k]) > maxVal) {
        maxVal = std::abs(A[i * n + k]);
        maxRow = i;
      }
    }

    if (k != maxRow) {
      for (std::size_t j = k; j < n; j++)
        std::swap(A[k * n + j], A[maxRow * n + j]);
      std::swap(b[k], b[maxRow]);
    }

    if (std::abs(A[k * n + k]) < 1e-12)
      continue;

    for (std::size_t i = k + 1; i < n; i++) {
      double factor = A[i * n + k] / A[k * n + k];
      for (std::size_t j = k; j < n; j++) {
        A[i * n + j] -= factor * A[k * n + j];
      }
      b[i] -= factor * b[k];
    }
  }

  for (int i = (int)n - 1; i >= 0; i--) {
    double sum = 0.0;
    for (std::size_t j = i + 1; j < n; j++) {
      sum += A[i * n + j] * x[j];
    }
    if (std::abs(A[i * n + i]) > 1e-12)
      x[i] = (b[i] - sum) / A[i * n + i];
    else
      x[i] = 0.0;
  }
}

void Context::Step(double dt) {
  m_dt = dt;
  m_timeIsTransient = true;

  for (auto &node : m_nodes) {
    node.lastVoltage = node.voltage;
  }

  for (int iter = 0; iter < m_maxIterations; ++iter) {
    SolveMNA();
  }

  // Step Components (e.g. CPU)
  for (auto &comp : m_components) {
    comp->Step(dt);
  }

  m_time += dt;
}

void Context::SolveMNA() {
  m_nodeToMatrixIndex.assign(m_nodes.size(), -1);
  std::size_t matrixSize = 0;

  for (std::size_t i = 1; i < m_nodes.size(); ++i) {
    m_nodeToMatrixIndex[i] = static_cast<int>(matrixSize++);
  }

  for (auto &comp : m_components) {
    if (comp->GetType() == ComponentType::VoltageSource) {
      auto vs = std::static_pointer_cast<VoltageSource>(comp);
      vs->m_matrixIndex = matrixSize++;
    }
  }

  if (matrixSize == 0)
    return;

  ResizeMatrix(matrixSize);

  for (auto &comp : m_components) {
    comp->Stamp(*this);
  }

  SolveLinearSystem(m_matrix, m_rhs, m_solution, matrixSize);

  for (std::size_t i = 1; i < m_nodes.size(); ++i) {
    int idx = m_nodeToMatrixIndex[i];
    if (idx != -1) {
      m_nodes[i].voltage = m_solution[idx];
    }
  }
}
} // namespace NativeEngine::Circuit
