#pragma once
#include <unordered_map>
#include <vector>

#include "DeterministicRng.h"
#include "PhysicsConfig.h"
#include "RigidBody.h"

namespace NativeEngine::Physics {

class PhysicsWorld {
 public:
  PhysicsWorld();

  void SetConfig(const PhysicsConfig &config);
  const PhysicsConfig &GetConfig() const { return config_; }

  std::uint32_t AddBody(const RigidBody &body);
  bool GetBody(std::uint32_t id, RigidBody &out) const;
  bool SetBody(std::uint32_t id, const RigidBody &body);
  bool ApplyForce(std::uint32_t id, const Vec3 &force);
  bool ApplyForceAtPoint(std::uint32_t id, const Vec3 &force, const Vec3 &point);
  bool ApplyTorque(std::uint32_t id, const Vec3 &torque);

  void Step(float dt_override);
  std::size_t BodyCount() const { return bodies_.size(); }

  std::uint32_t AddVehicle(std::uint32_t body_id, int wheel_count, const float *wheel_positions,
                           const float *wheel_radius, const float *suspension_rest,
                           const float *suspension_k, const float *suspension_damping,
                           const int *driven_wheels);
  void SetWheelInput(std::uint32_t vehicle_id, int wheel_index, float steer, float drive_torque, float brake_torque);
  void SetVehicleAero(std::uint32_t vehicle_id, float drag_coefficient, float downforce);
  void SetVehicleTireModel(std::uint32_t vehicle_id, float B, float C, float D, float E);

 private:
  void Integrate(RigidBody &body, float dt);
  Vec3 ComputeGravity(float dt);
  float ComputeDt(float dt_override);
  void ApplyAerodynamics(RigidBody &body, float dt);
  void ApplyThermal(RigidBody &body, float dt);
  void ApplyDamage(RigidBody &body, const Vec3 &accel, float dt);
  float ComputeBodyRadius(const RigidBody &body) const;
  void ApplyGroundContact(RigidBody &body, float dt);

  struct WheelInput {
    float steer{0.0f};
    float drive_torque{0.0f};
    float brake_torque{0.0f};
  };

  struct WheelState {
    Vec3 local_pos{};
    float radius{0.03f};
    float rest_length{0.05f};
    float spring_k{1400.0f};
    float damping{120.0f};
    float angular_velocity{0.0f};
    float inertia{0.02f};
    bool driven{false};
  };

  struct VehicleState {
    std::uint32_t id{0};
    std::uint32_t body_id{0};
    float pacejka_B{10.0f};
    float pacejka_C{1.9f};
    float pacejka_D{1.0f};
    float pacejka_E{0.97f};
    float drag_coefficient{0.35f};
    float downforce{0.0f};
    float drivetrain_loss{0.08f};
    std::vector<WheelState> wheels{};
    std::vector<WheelInput> inputs{};
  };

  float Pacejka(float slip, float B, float C, float D, float E) const;
  void StepVehicle(VehicleState &vehicle, float dt);

  PhysicsConfig config_{};
  DeterministicRng rng_{config_.noise_seed};
  std::uint32_t next_id_{1};
  std::unordered_map<std::uint32_t, RigidBody> bodies_;
  std::unordered_map<std::uint32_t, VehicleState> vehicles_;
};

}  // namespace NativeEngine::Physics
