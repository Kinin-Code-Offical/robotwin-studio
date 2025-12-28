#include <algorithm>
#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <fstream>
#include <memory>
#include <string>
#include <vector>

#include "../include/Bridge/UnityInterface.h"
#include "../include/Core/AvrComponent.h"
#include "../include/Core/BasicComponents.h"
#include "../include/Core/BvmFormat.hpp"
#include "../include/Core/CircuitContext.h"
#include "../include/Core/Diode.h"
#include "../include/Core/HexLoader.h"

#include "../include/MCU/ATmega328P_ISA.h"

using namespace NativeEngine::Circuit;

namespace {
std::unique_ptr<Context> g_context = nullptr;
SharedState g_sharedState; // Legacy State

Context &GetContext() {
  if (!g_context) {
    g_context = std::make_unique<Context>();
  }
  return *g_context;
}

AvrComponent *FindAvrComponent(Context &ctx) {
  for (auto &comp : ctx.GetComponents()) {
    if (comp && comp->GetType() == ComponentType::IC_Pin) {
      return static_cast<AvrComponent *>(comp.get());
    }
  }
  return nullptr;
}

std::vector<AvrComponent *> GetAvrComponents(Context &ctx) {
  std::vector<AvrComponent *> out;
  for (auto &comp : ctx.GetComponents()) {
    if (!comp || comp->GetType() != ComponentType::IC_Pin) {
      continue;
    }
    out.push_back(static_cast<AvrComponent *>(comp.get()));
  }
  return out;
}

AvrComponent *FindAvrByIndex(Context &ctx, int index) {
  if (index < 0) {
    return nullptr;
  }
  int current = 0;
  for (auto &comp : ctx.GetComponents()) {
    if (!comp || comp->GetType() != ComponentType::IC_Pin) {
      continue;
    }
    if (current == index) {
      return static_cast<AvrComponent *>(comp.get());
    }
    current++;
  }
  return nullptr;
}

void UpdateSharedState(Context &ctx) {
  for (int i = 0; i < MAX_NODES; ++i) {
    g_sharedState.node_voltages[i] = 0.0f;
  }

  for (int i = 0; i < MAX_NODES; ++i) {
    g_sharedState.node_voltages[i] =
        static_cast<float>(ctx.GetNodeVoltage(static_cast<std::uint32_t>(i)));
  }
}

bool LoadHexIntoAvr(AvrComponent *avr, const char *hexText) {
  if (!hexText) {
    return false;
  }
  if (!avr) {
    return false;
  }
  bool ok =
      NativeEngine::Utils::HexLoader::LoadHexText(avr->m_flash, hexText);
  if (ok) {
    avr->m_cpu.pc = 0;
  }
  return ok;
}

bool LoadBvmIntoAvr(AvrComponent *avr, const std::uint8_t *buffer,
                    std::size_t size) {
  if (!buffer || size == 0) {
    return false;
  }
  bvm::BvmView view{};
  const char *error = nullptr;
  if (!bvm::Open(buffer, size, view, &error)) {
    return false;
  }

  bvm::SectionView text{};
  if (!bvm::FindSection(view, ".text", text)) {
    return false;
  }

  if (!avr) {
    return false;
  }

  if ((text.flags & bvm::SectionTextHex) != 0) {
    std::string hex(reinterpret_cast<const char *>(text.data),
                    static_cast<std::size_t>(text.size));
    return LoadHexIntoAvr(avr, hex.c_str());
  }

  if ((text.flags & bvm::SectionTextRaw) != 0) {
    auto count = std::min<std::size_t>(avr->m_flash.size(), text.size);
    std::memcpy(avr->m_flash.data(), text.data, count);
    avr->m_cpu.pc = 0;
    return true;
  }

  return false;
}
} // namespace

extern "C" {
UNITY_EXPORT void Native_CreateContext() {
  g_context = std::make_unique<Context>();
  std::memset(&g_sharedState, 0, sizeof(SharedState));
}

UNITY_EXPORT void Native_DestroyContext() { g_context.reset(); }

UNITY_EXPORT int Native_AddNode() {
  return static_cast<int>(GetContext().CreateNode());
}

UNITY_EXPORT int Native_AddComponent(int type, int paramCount, float *params) {
  auto &ctx = GetContext();
  static std::uint32_t nextId = 1;
  std::uint32_t id = nextId++;

  std::shared_ptr<Component> comp = nullptr;

  ComponentType cType = static_cast<ComponentType>(type);
  if (cType == ComponentType::Resistor) {
    double r = (paramCount >= 1) ? params[0] : 1000.0;
    comp = std::make_shared<Resistor>(id, r);
  } else if (cType == ComponentType::VoltageSource) {
    double v = (paramCount >= 1) ? params[0] : 5.0;
    comp = std::make_shared<VoltageSource>(id, v);
  } else if (cType == ComponentType::Diode) {
    comp = std::make_shared<Diode>(id);
  } else if (cType == ComponentType::IC_Pin) // Using IC_Pin (6) for AVR
  {
    comp = std::make_shared<AvrComponent>(id);
  }

  if (comp) {
    ctx.AddComponent(comp);
    return static_cast<int>(id);
  }
  return -1;
}

UNITY_EXPORT void Native_Connect(int compId, int pinIndex, int nodeId) {
  auto &ctx = GetContext();
  for (auto &c : ctx.GetComponents()) {
    if (static_cast<int>(c->GetId()) == compId) {
      c->Connect(static_cast<std::uint8_t>(pinIndex),
                 static_cast<std::uint32_t>(nodeId));
      break;
    }
  }
}

UNITY_EXPORT void Native_Step(float dt) {
  GetContext().Step(static_cast<double>(dt));
  UpdateSharedState(GetContext());
  g_sharedState.tick++;
}

UNITY_EXPORT float Native_GetVoltage(int nodeId) {
  return static_cast<float>(
      GetContext().GetNodeVoltage(static_cast<std::uint32_t>(nodeId)));
}

UNITY_EXPORT int LoadHexFromFile(const char *path) {
  auto &ctx = GetContext();
  std::ifstream file(path);
  if (!file.is_open()) {
    return 0;
  }
  std::string content((std::istreambuf_iterator<char>(file)),
                      std::istreambuf_iterator<char>());
  return LoadHexIntoAvr(FindAvrByIndex(ctx, 0), content.c_str()) ? 1 : 0;
}

// --- Legacy Exports ---
UNITY_EXPORT int GetEngineVersion() { return 300; }

UNITY_EXPORT const SharedState *GetSharedState() { return &g_sharedState; }

UNITY_EXPORT void SetComponentXY(uint32_t index, uint32_t x, uint32_t y) {
  if (index >= MAX_COMPONENTS) {
    return;
  }
  g_sharedState.component_positions[index][0] = x;
  g_sharedState.component_positions[index][1] = y;
}

UNITY_EXPORT int LoadHexFromText(const char *hexText) {
  return LoadHexIntoAvr(FindAvrByIndex(GetContext(), 0), hexText) ? 1 : 0;
}

UNITY_EXPORT int LoadBvmFromMemory(const uint8_t *buffer, uint32_t size) {
  return LoadBvmIntoAvr(FindAvrByIndex(GetContext(), 0), buffer, size) ? 1 : 0;
}

UNITY_EXPORT int LoadBvmFromFile(const char *path) {
  if (!path) {
    return 0;
  }
  std::ifstream file(path, std::ios::binary);
  if (!file.is_open()) {
    return 0;
  }
  std::vector<std::uint8_t> data(
      (std::istreambuf_iterator<char>(file)),
      std::istreambuf_iterator<char>());
  return LoadBvmIntoAvr(FindAvrByIndex(GetContext(), 0), data.data(),
                        data.size())
             ? 1
             : 0;
}

UNITY_EXPORT int GetAvrCount() { return static_cast<int>(GetAvrComponents(GetContext()).size()); }

UNITY_EXPORT float GetPinVoltageForAvr(int avrIndex, int pinIndex) {
  if (pinIndex < 0 || pinIndex >= AvrComponent::PIN_COUNT) {
    return 0.0f;
  }
  auto &ctx = GetContext();
  auto *avr = FindAvrByIndex(ctx, avrIndex);
  if (!avr) {
    return 0.0f;
  }
  std::uint32_t nodeId = avr->m_pinNodes[pinIndex];
  if (nodeId == 0) {
    return 0.0f;
  }
  return static_cast<float>(ctx.GetNodeVoltage(nodeId));
}

UNITY_EXPORT int LoadHexForAvr(int index, const char *path) {
  auto &ctx = GetContext();
  std::ifstream file(path);
  if (!file.is_open()) {
    return 0;
  }
  std::string content((std::istreambuf_iterator<char>(file)),
                      std::istreambuf_iterator<char>());
  return LoadHexIntoAvr(FindAvrByIndex(ctx, index), content.c_str()) ? 1 : 0;
}

UNITY_EXPORT int LoadHexTextForAvr(int index, const char *hexText) {
  return LoadHexIntoAvr(FindAvrByIndex(GetContext(), index), hexText) ? 1 : 0;
}

UNITY_EXPORT int LoadBvmForAvrMemory(int index, const uint8_t *buffer,
                                     uint32_t size) {
  return LoadBvmIntoAvr(FindAvrByIndex(GetContext(), index), buffer, size) ? 1
                                                                           : 0;
}

UNITY_EXPORT int LoadBvmForAvrFile(int index, const char *path) {
  if (!path) {
    return 0;
  }
  std::ifstream file(path, std::ios::binary);
  if (!file.is_open()) {
    return 0;
  }
  std::vector<std::uint8_t> data(
      (std::istreambuf_iterator<char>(file)),
      std::istreambuf_iterator<char>());
  return LoadBvmIntoAvr(FindAvrByIndex(GetContext(), index), data.data(),
                        data.size())
             ? 1
             : 0;
}
}
