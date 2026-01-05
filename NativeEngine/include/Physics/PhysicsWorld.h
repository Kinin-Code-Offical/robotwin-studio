#pragma once
#include <unordered_map>
#include <vector>

#include "DeterministicRng.h"
#include "PhysicsConfig.h"
#include "RigidBody.h"

namespace NativeEngine::Physics {

struct Plane {
  Vec3 normal{};
  float distance{0.0f};
};

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
  std::uint32_t AddDistanceConstraint(std::uint32_t body_a, std::uint32_t body_b,
                                      const Vec3 &local_a, const Vec3 &local_b,
                                      float rest_length, float stiffness, float damping,
                                      float max_force, bool tension_only);

  void Step(float dt_override);
  std::size_t BodyCount() const { return bodies_.size(); }

  std::uint32_t AddVehicle(std::uint32_t body_id, int wheel_count, const float *wheel_positions,
                           const float *wheel_radius, const float *suspension_rest,
                           const float *suspension_k, const float *suspension_damping,
                           const int *driven_wheels);
  void SetWheelInput(std::uint32_t vehicle_id, int wheel_index, float steer, float drive_torque, float brake_torque);
  void SetVehicleAero(std::uint32_t vehicle_id, float drag_coefficient, float downforce);
  void SetVehicleTireModel(std::uint32_t vehicle_id, float B, float C, float D, float E);
  void ClearGroundPlanes();
  void AddGroundPlane(const Vec3 &normal, float distance);

  struct RaycastHit {
    std::uint32_t body_id{0};
    Vec3 point{};
    Vec3 normal{};
    float distance{0.0f};
  };

  bool Raycast(const Vec3 &origin, const Vec3 &direction, float max_distance, RaycastHit &out) const;

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
  void GenerateContacts();
  void ResolveContacts(float dt);
  void ApplyDistanceConstraints(float dt);

  struct Aabb {
    Vec3 min;
    Vec3 max;
  };

  struct Contact {
    std::uint32_t a{0};
    std::uint32_t b{0};
    std::uint64_t key{0};
    Vec3 normal{};
    Vec3 point{};
    float penetration{0.0f};
    float restitution{0.2f};
    float friction{0.8f};
    float cached_normal_impulse{0.0f};
    float cached_tangent_impulse{0.0f};
    float normal_impulse_accum{0.0f};
    float tangent_impulse_accum{0.0f};
  };

  struct DistanceConstraint {
    std::uint32_t id{0};
    std::uint32_t body_a{0};
    std::uint32_t body_b{0};
    Vec3 local_a{};
    Vec3 local_b{};
    float rest_length{1.0f};
    float stiffness{5000.0f};
    float damping{120.0f};
    float max_force{20000.0f};
    bool tension_only{false};
  };

  struct CachedContact {
    Vec3 normal{};
    float normal_impulse{0.0f};
    float tangent_impulse{0.0f};
  };

  struct CachedAabb {
    Aabb aabb{};
    Vec3 position{};
    Quat rotation{};
    Vec3 half_extents{};
    float radius{0.0f};
    ShapeType shape{ShapeType::Sphere};
    bool valid{false};
  };

  void RebuildBodyCache();
  Aabb GetCachedAabb(const RigidBody &body);
  Aabb ComputeAabb(const RigidBody &body) const;
  bool AabbOverlap(const Aabb &a, const Aabb &b) const;
  bool CollideSphereSphere(const RigidBody &a, const RigidBody &b, Contact &out) const;
  bool CollideSphereBox(const RigidBody &sphere, const RigidBody &box, Contact &out) const;
  bool CollideBoxBox(const RigidBody &a, const RigidBody &b, Contact &out) const;
  void ResolveContact(Contact &contact, float dt);
  bool RaycastSphere(const Vec3 &origin, const Vec3 &dir, float max_distance,
                     const RigidBody &body, RaycastHit &out) const;
  bool RaycastBox(const Vec3 &origin, const Vec3 &dir, float max_distance,
                  const RigidBody &body, RaycastHit &out) const;
  float ProjectBoxRadius(const RigidBody &body, const Vec3 &axis) const;

  PhysicsConfig config_{};
  DeterministicRng rng_{config_.noise_seed};
  std::uint32_t next_id_{1};
  std::uint32_t next_constraint_id_{1};
  std::unordered_map<std::uint32_t, RigidBody> bodies_;
  std::vector<RigidBody*> body_cache_;
  std::size_t body_cache_size_{0};
  bool body_cache_dirty_{true};
  std::unordered_map<std::uint32_t, VehicleState> vehicles_;
  std::vector<Contact> contacts_;
  std::unordered_map<std::uint64_t, CachedContact> contact_cache_;
  std::unordered_map<std::uint64_t, CachedContact> contact_cache_scratch_;
  std::unordered_map<std::uint32_t, CachedAabb> aabb_cache_;
  std::vector<DistanceConstraint> distance_constraints_;
  std::vector<Plane> ground_planes_;
};

}  // namespace NativeEngine::Physics
