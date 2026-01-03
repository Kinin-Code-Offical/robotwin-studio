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
#include "../include/Physics/PhysicsWorld.h"

#include "../include/MCU/ATmega328P_ISA.h"

using namespace NativeEngine::Circuit;

namespace {
std::unique_ptr<Context> g_context = nullptr;
SharedState g_sharedState; // Legacy State
std::unique_ptr<NativeEngine::Physics::PhysicsWorld> g_physics = nullptr;

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

UNITY_EXPORT void Physics_CreateWorld() {
  g_physics = std::make_unique<NativeEngine::Physics::PhysicsWorld>();
}

UNITY_EXPORT void Physics_DestroyWorld() { g_physics.reset(); }

UNITY_EXPORT void Physics_SetConfig(const PhysicsConfig_C *config) {
  if (!g_physics || !config) {
    return;
  }
  NativeEngine::Physics::PhysicsConfig cfg{};
  cfg.base_dt = config->base_dt;
  cfg.gravity = {config->gravity_x, config->gravity_y, config->gravity_z};
  cfg.gravity_jitter = config->gravity_jitter;
  cfg.time_jitter = config->time_jitter;
  cfg.solver_iterations = config->solver_iterations;
  cfg.noise_seed = config->noise_seed;
  cfg.contact_slop = config->contact_slop;
  cfg.restitution = config->restitution;
  cfg.static_friction = config->static_friction;
  cfg.dynamic_friction = config->dynamic_friction;
  cfg.air_density = config->air_density;
  cfg.wind = {config->wind_x, config->wind_y, config->wind_z};
  cfg.ambient_temp_c = config->ambient_temp_c;
  cfg.rain_intensity = config->rain_intensity;
  cfg.thermal_exchange = config->thermal_exchange;
  cfg.sleep_linear_threshold = config->sleep_linear_threshold;
  cfg.sleep_angular_threshold = config->sleep_angular_threshold;
  cfg.sleep_time = config->sleep_time;
  g_physics->SetConfig(cfg);
}

UNITY_EXPORT uint32_t Physics_AddBody(const RigidBody_C *body) {
  if (!g_physics || !body) {
    return 0;
  }
  NativeEngine::Physics::RigidBody rb{};
  rb.id = body->id;
  rb.mass = body->mass;
  rb.position = {body->pos_x, body->pos_y, body->pos_z};
  rb.velocity = {body->vel_x, body->vel_y, body->vel_z};
  rb.rotation = {body->rot_w, body->rot_x, body->rot_y, body->rot_z};
  rb.angular_velocity = {body->ang_x, body->ang_y, body->ang_z};
  rb.linear_damping = body->linear_damping;
  rb.angular_damping = body->angular_damping;
  rb.drag_coefficient = body->drag_coefficient;
  rb.cross_section_area = body->cross_section_area;
  rb.surface_area = body->surface_area;
  rb.temperature_c = body->temperature_c;
  rb.material_strength = body->material_strength;
  rb.fracture_toughness = body->fracture_toughness;
  rb.shape = static_cast<NativeEngine::Physics::ShapeType>(body->shape_type);
  rb.radius = body->radius;
  rb.half_extents = {body->half_x, body->half_y, body->half_z};
  rb.friction = body->friction;
  rb.restitution = body->restitution;
  rb.damage = body->damage;
  rb.is_broken = body->is_broken != 0;
  rb.is_static = body->is_static != 0;
  rb.SetMass(rb.mass);
  return g_physics->AddBody(rb);
}

UNITY_EXPORT int Physics_GetBody(uint32_t id, RigidBody_C *out) {
  if (!g_physics || !out) {
    return 0;
  }
  NativeEngine::Physics::RigidBody rb{};
  if (!g_physics->GetBody(id, rb)) {
    return 0;
  }
  out->id = rb.id;
  out->mass = rb.mass;
  out->pos_x = rb.position.x;
  out->pos_y = rb.position.y;
  out->pos_z = rb.position.z;
  out->vel_x = rb.velocity.x;
  out->vel_y = rb.velocity.y;
  out->vel_z = rb.velocity.z;
  out->rot_w = rb.rotation.w;
  out->rot_x = rb.rotation.x;
  out->rot_y = rb.rotation.y;
  out->rot_z = rb.rotation.z;
  out->ang_x = rb.angular_velocity.x;
  out->ang_y = rb.angular_velocity.y;
  out->ang_z = rb.angular_velocity.z;
  out->linear_damping = rb.linear_damping;
  out->angular_damping = rb.angular_damping;
  out->drag_coefficient = rb.drag_coefficient;
  out->cross_section_area = rb.cross_section_area;
  out->surface_area = rb.surface_area;
  out->temperature_c = rb.temperature_c;
  out->material_strength = rb.material_strength;
  out->fracture_toughness = rb.fracture_toughness;
  out->shape_type = static_cast<int>(rb.shape);
  out->radius = rb.radius;
  out->half_x = rb.half_extents.x;
  out->half_y = rb.half_extents.y;
  out->half_z = rb.half_extents.z;
  out->friction = rb.friction;
  out->restitution = rb.restitution;
  out->damage = rb.damage;
  out->is_broken = rb.is_broken ? 1 : 0;
  out->is_static = rb.is_static ? 1 : 0;
  return 1;
}

UNITY_EXPORT void Physics_Step(float dt) {
  if (!g_physics) {
    return;
  }
  g_physics->Step(dt);
}

UNITY_EXPORT uint32_t Physics_AddVehicle(uint32_t body_id, int wheel_count,
                                         const float *wheel_positions,
                                         const float *wheel_radius,
                                         const float *suspension_rest,
                                         const float *suspension_k,
                                         const float *suspension_damping,
                                         const int *driven_wheels) {
  if (!g_physics) {
    return 0;
  }
  return g_physics->AddVehicle(body_id, wheel_count, wheel_positions,
                               wheel_radius, suspension_rest, suspension_k,
                               suspension_damping, driven_wheels);
}

UNITY_EXPORT void Physics_SetWheelInput(uint32_t vehicle_id, int wheel_index,
                                        float steer, float drive_torque,
                                        float brake_torque) {
  if (!g_physics) {
    return;
  }
  g_physics->SetWheelInput(vehicle_id, wheel_index, steer, drive_torque,
                           brake_torque);
}

UNITY_EXPORT void Physics_SetVehicleAero(uint32_t vehicle_id,
                                         float drag_coefficient,
                                         float downforce) {
  if (!g_physics) {
    return;
  }
  g_physics->SetVehicleAero(vehicle_id, drag_coefficient, downforce);
}

UNITY_EXPORT void Physics_SetVehicleTireModel(uint32_t vehicle_id, float B,
                                              float C, float D, float E) {
  if (!g_physics) {
    return;
  }
  g_physics->SetVehicleTireModel(vehicle_id, B, C, D, E);
}

UNITY_EXPORT int Physics_ApplyForce(uint32_t body_id, float fx, float fy, float fz) {
  if (!g_physics) {
    return 0;
  }
  return g_physics->ApplyForce(body_id, {fx, fy, fz}) ? 1 : 0;
}

UNITY_EXPORT int Physics_ApplyForceAtPoint(uint32_t body_id, float fx, float fy, float fz,
                                           float px, float py, float pz) {
  if (!g_physics) {
    return 0;
  }
  return g_physics->ApplyForceAtPoint(body_id, {fx, fy, fz}, {px, py, pz}) ? 1 : 0;
}

UNITY_EXPORT int Physics_ApplyTorque(uint32_t body_id, float tx, float ty, float tz) {
  if (!g_physics) {
    return 0;
  }
  return g_physics->ApplyTorque(body_id, {tx, ty, tz}) ? 1 : 0;
}

UNITY_EXPORT uint32_t Physics_AddDistanceConstraint(uint32_t body_a, uint32_t body_b,
                                                    float ax, float ay, float az,
                                                    float bx, float by, float bz,
                                                    float rest_length, float stiffness,
                                                    float damping, float max_force,
                                                    int tension_only) {
  if (!g_physics) {
    return 0;
  }
  return g_physics->AddDistanceConstraint(
      body_a, body_b,
      {ax, ay, az},
      {bx, by, bz},
      rest_length, stiffness, damping, max_force,
      tension_only != 0);
}

UNITY_EXPORT int Physics_Raycast(float ox, float oy, float oz,
                                 float dx, float dy, float dz,
                                 float max_distance, RaycastHit_C *out_hit) {
  if (!g_physics || !out_hit) {
    return 0;
  }
  NativeEngine::Physics::PhysicsWorld::RaycastHit hit{};
  if (!g_physics->Raycast({ox, oy, oz}, {dx, dy, dz}, max_distance, hit)) {
    return 0;
  }
  out_hit->body_id = hit.body_id;
  out_hit->hit_x = hit.point.x;
  out_hit->hit_y = hit.point.y;
  out_hit->hit_z = hit.point.z;
  out_hit->normal_x = hit.normal.x;
  out_hit->normal_y = hit.normal.y;
  out_hit->normal_z = hit.normal.z;
  out_hit->distance = hit.distance;
  return 1;
}

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
