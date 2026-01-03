#pragma once
#include <cstdint>
#include "MathTypes.h"

namespace NativeEngine::Physics {

struct PhysicsConfig {
  float base_dt{0.001f};
  Vec3 gravity{0.0f, -9.80665f, 0.0f};
  float gravity_jitter{0.02f};
  float time_jitter{0.00005f};
  float solver_iterations{12.0f};
  std::uint64_t noise_seed{0xA31F2C9B1E45D7ULL};
  float contact_slop{0.0005f};
  float restitution{0.2f};
  float static_friction{0.8f};
  float dynamic_friction{0.6f};
  float air_density{1.225f};
  Vec3 wind{0.0f, 0.0f, 0.0f};
  float ambient_temp_c{20.0f};
  float rain_intensity{0.0f};
  float thermal_exchange{0.08f};
};

}  // namespace NativeEngine::Physics
