#pragma once
#include <algorithm>
#include "MathTypes.h"

namespace NativeEngine::Physics {

enum class ShapeType : std::uint8_t { Sphere = 0, Box = 1 };

struct RigidBody {
  std::uint32_t id{0};
  float mass{1.0f};
  float inv_mass{1.0f};
  Vec3 position{};
  Vec3 velocity{};
  Vec3 force_accum{};
  Quat rotation{};
  Vec3 angular_velocity{};
  Vec3 torque_accum{};
  float linear_damping{0.01f};
  float angular_damping{0.02f};
  float drag_coefficient{0.9f};
  float cross_section_area{0.02f};
  float surface_area{0.2f};
  float temperature_c{20.0f};
  float material_strength{25000.0f};
  float fracture_toughness{0.6f};
  ShapeType shape{ShapeType::Sphere};
  float radius{0.5f};
  Vec3 half_extents{0.5f, 0.5f, 0.5f};
  float friction{0.8f};
  float restitution{0.2f};
  Vec3 inertia{1.0f, 1.0f, 1.0f};
  Vec3 inv_inertia{1.0f, 1.0f, 1.0f};
  float damage{0.0f};
  float sleep_timer{0.0f};
  bool is_sleeping{false};
  bool is_broken{false};
  bool is_static{false};

  void SetMass(float m) {
    mass = m;
    inv_mass = (m <= 0.0f || is_static) ? 0.0f : 1.0f / m;
    UpdateInertia();
  }

  void UpdateInertia() {
    if (is_static || mass <= 0.0f) {
      inertia = {0.0f, 0.0f, 0.0f};
      inv_inertia = {0.0f, 0.0f, 0.0f};
      return;
    }

    if (shape == ShapeType::Sphere) {
      float r = std::max(radius, 0.001f);
      float i = 0.4f * mass * r * r;
      inertia = {i, i, i};
    } else {
      float x = half_extents.x * 2.0f;
      float y = half_extents.y * 2.0f;
      float z = half_extents.z * 2.0f;
      inertia = {
          (1.0f / 12.0f) * mass * (y * y + z * z),
          (1.0f / 12.0f) * mass * (x * x + z * z),
          (1.0f / 12.0f) * mass * (x * x + y * y)};
    }

    inv_inertia = {
        inertia.x <= 0.0f ? 0.0f : 1.0f / inertia.x,
        inertia.y <= 0.0f ? 0.0f : 1.0f / inertia.y,
        inertia.z <= 0.0f ? 0.0f : 1.0f / inertia.z};
  }
};

}  // namespace NativeEngine::Physics
