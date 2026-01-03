#pragma once
#include "MathTypes.h"

namespace NativeEngine::Physics {

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
  float damage{0.0f};
  bool is_broken{false};
  bool is_static{false};

  void SetMass(float m) {
    mass = m;
    inv_mass = (m <= 0.0f || is_static) ? 0.0f : 1.0f / m;
  }
};

}  // namespace NativeEngine::Physics
