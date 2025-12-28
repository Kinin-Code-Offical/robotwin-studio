#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstring>
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

Context &GetContext() {
  if (!g_context) {
    g_context = std::make_unique<Context>();
  }
  return *g_context;
}
} // namespace

extern "C" {
UNITY_EXPORT void Native_CreateContext() {
  g_context = std::make_unique<Context>();
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
}

UNITY_EXPORT float Native_GetVoltage(int nodeId) {
  return static_cast<float>(
      GetContext().GetNodeVoltage(static_cast<std::uint32_t>(nodeId)));
}

UNITY_EXPORT int LoadHexFromFile(const char *path) {
  auto &ctx = GetContext();
  for (auto &c : ctx.GetComponents()) {
    // Just check all components, if it casts to AVR try loading
    if (c->GetType() == ComponentType::IC_Pin) {
      auto avr = std::static_pointer_cast<AvrComponent>(c);
      if (avr) {
        std::ifstream file(path);
        if (!file.is_open())
          return 0;
        std::string content((std::istreambuf_iterator<char>(file)),
                            std::istreambuf_iterator<char>());
        bool ok = NativeEngine::Utils::HexLoader::LoadHexText(avr->m_flash,
                                                              content.c_str());
        // Reset CPU on load?
        avr->m_cpu.pc = 0;
        return ok ? 1 : 0;
      }
    }
  }
  return 0;
}

UNITY_EXPORT int GetEngineVersion() {
  return 320; // 3.2 AVR
}
}
