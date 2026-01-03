#include "../../include/Physics/PhysicsWorld.h"
#include <algorithm>
#include <cmath>

namespace NativeEngine::Physics {

namespace {
constexpr float kPi = 3.14159265358979323846f;
}  // namespace

PhysicsWorld::PhysicsWorld() { rng_.Seed(config_.noise_seed); }

void PhysicsWorld::SetConfig(const PhysicsConfig &config) {
  config_ = config;
  rng_.Seed(config_.noise_seed);
}

std::uint32_t PhysicsWorld::AddBody(const RigidBody &body) {
  RigidBody copy = body;
  if (copy.id == 0) {
    copy.id = next_id_++;
  }
  copy.SetMass(copy.mass);
  bodies_[copy.id] = copy;
  return copy.id;
}

bool PhysicsWorld::GetBody(std::uint32_t id, RigidBody &out) const {
  auto it = bodies_.find(id);
  if (it == bodies_.end()) {
    return false;
  }
  out = it->second;
  return true;
}

bool PhysicsWorld::SetBody(std::uint32_t id, const RigidBody &body) {
  auto it = bodies_.find(id);
  if (it == bodies_.end()) {
    return false;
  }
  RigidBody copy = body;
  copy.id = id;
  copy.SetMass(copy.mass);
  it->second = copy;
  return true;
}

bool PhysicsWorld::ApplyForce(std::uint32_t id, const Vec3 &force) {
  auto it = bodies_.find(id);
  if (it == bodies_.end()) {
    return false;
  }
  it->second.force_accum += force;
  return true;
}

bool PhysicsWorld::ApplyForceAtPoint(std::uint32_t id, const Vec3 &force, const Vec3 &point) {
  auto it = bodies_.find(id);
  if (it == bodies_.end()) {
    return false;
  }
  auto &body = it->second;
  body.force_accum += force;
  Vec3 r = point - body.position;
  body.torque_accum += Cross(r, force);
  return true;
}

bool PhysicsWorld::ApplyTorque(std::uint32_t id, const Vec3 &torque) {
  auto it = bodies_.find(id);
  if (it == bodies_.end()) {
    return false;
  }
  it->second.torque_accum += torque;
  return true;
}

float PhysicsWorld::ComputeDt(float dt_override) {
  float base = (dt_override > 0.0f) ? dt_override : config_.base_dt;
  float jitter = config_.time_jitter * rng_.NextFloatSigned();
  float dt = base + jitter;
  if (dt < 1e-5f) dt = 1e-5f;
  return dt;
}

Vec3 PhysicsWorld::ComputeGravity(float dt) {
  (void)dt;
  float jitter = config_.gravity_jitter * rng_.NextFloatSigned();
  return {config_.gravity.x, config_.gravity.y + jitter, config_.gravity.z};
}

void PhysicsWorld::Integrate(RigidBody &body, float dt) {
  if (body.is_static || body.inv_mass <= 0.0f) {
    body.force_accum = {};
    body.torque_accum = {};
    return;
  }

  Vec3 accel = body.force_accum * body.inv_mass;
  Vec3 ang_accel = body.torque_accum * body.inv_mass;
  ApplyDamage(body, accel, dt);
  ApplyAerodynamics(body, dt);
  ApplyThermal(body, dt);

  if (body.is_broken) {
    body.linear_damping = 0.25f;
    body.angular_damping = 0.3f;
  }
  body.velocity += accel * dt;
  body.angular_velocity += ang_accel * dt;
  body.velocity = body.velocity * (1.0f - body.linear_damping);
  body.position += body.velocity * dt;

  body.angular_velocity = body.angular_velocity * (1.0f - body.angular_damping);
  body.rotation = body.rotation * Quat::FromAxisAngle(body.angular_velocity, dt);

  ApplyGroundContact(body, dt);

  body.force_accum = {};
  body.torque_accum = {};
}

void PhysicsWorld::Step(float dt_override) {
  float dt = ComputeDt(dt_override);
  Vec3 gravity = ComputeGravity(dt);

  for (auto &kvp : bodies_) {
    auto &body = kvp.second;
    if (!body.is_static) {
      body.force_accum += gravity * body.mass;
    }
  }

  for (auto &kvp : vehicles_) {
    StepVehicle(kvp.second, dt);
  }

  for (auto &kvp : bodies_) {
    Integrate(kvp.second, dt);
  }
}

float PhysicsWorld::Pacejka(float slip, float B, float C, float D, float E) const {
  float x = B * slip;
  return D * std::sin(C * std::atan(x - E * (x - std::atan(x))));
}

void PhysicsWorld::StepVehicle(VehicleState &vehicle, float dt) {
  auto body_it = bodies_.find(vehicle.body_id);
  if (body_it == bodies_.end()) {
    return;
  }
  auto &body = body_it->second;
  if (vehicle.wheels.empty()) {
    return;
  }

  Vec3 forward = Rotate(body.rotation, {0.0f, 0.0f, 1.0f});
  Vec3 right = Rotate(body.rotation, {1.0f, 0.0f, 0.0f});
  Vec3 up = Rotate(body.rotation, {0.0f, 1.0f, 0.0f});

  for (std::size_t i = 0; i < vehicle.wheels.size(); ++i) {
    auto &wheel = vehicle.wheels[i];
    WheelInput input = (i < vehicle.inputs.size()) ? vehicle.inputs[i] : WheelInput{};

    Vec3 wheel_world = body.position + Rotate(body.rotation, wheel.local_pos);
    float ground_y = 0.0f;
    float penetration = (wheel.radius + ground_y) - wheel_world.y;
    if (penetration <= 0.0f) {
      wheel.angular_velocity *= 0.99f;
      continue;
    }

    float compression = wheel.rest_length + penetration;
    Vec3 r = wheel_world - body.position;
    Vec3 contact_vel = body.velocity + Cross(body.angular_velocity, r);
    float vel_up = Dot(contact_vel, up);
    float spring_force = compression * wheel.spring_k - vel_up * wheel.damping;
    if (spring_force < 0.0f) spring_force = 0.0f;

    Vec3 wheel_forward = forward;
    Vec3 wheel_right = right;
    if (std::abs(input.steer) > 0.0001f) {
      Quat steer_rot = Quat::FromAxisAngle(up, input.steer);
      wheel_forward = Rotate(steer_rot, forward);
      wheel_right = Rotate(steer_rot, right);
    }

    float v_long = Dot(contact_vel, wheel_forward);
    float v_lat = Dot(contact_vel, wheel_right);
    float denom = std::max(std::abs(v_long), 0.5f);
    float slip_ratio = (wheel.angular_velocity * wheel.radius - v_long) / denom;
    float slip_angle = std::atan2(v_lat, std::abs(v_long) + 0.1f);

    float mu_long = Pacejka(slip_ratio, vehicle.pacejka_B, vehicle.pacejka_C, vehicle.pacejka_D, vehicle.pacejka_E);
    float mu_lat = Pacejka(slip_angle, vehicle.pacejka_B, vehicle.pacejka_C, vehicle.pacejka_D, vehicle.pacejka_E);

    float f_long = mu_long * spring_force;
    float f_lat = mu_lat * spring_force;

    Vec3 tire_force = wheel_forward * f_long - wheel_right * f_lat + up * spring_force;
    body.force_accum += tire_force;

    float drive = input.drive_torque * (wheel.driven ? 1.0f : 0.0f);
    drive *= (1.0f - vehicle.drivetrain_loss);
    float brake = input.brake_torque;
    float rolling = 0.02f * spring_force * wheel.radius;
    float torque = drive - brake - rolling;
    float ang_accel = torque / std::max(wheel.inertia, 0.001f);
    wheel.angular_velocity += ang_accel * dt;
  }

  Vec3 relative_wind = body.velocity - config_.wind;
  float speed = relative_wind.Length();
  if (speed > 0.1f) {
    float drag = 0.5f * config_.air_density * vehicle.drag_coefficient * speed * speed;
    Vec3 drag_force = Normalize(relative_wind) * -drag;
    body.force_accum += drag_force;
  }

  if (vehicle.downforce > 0.0f) {
    body.force_accum += up * (-vehicle.downforce);
  }
}

std::uint32_t PhysicsWorld::AddVehicle(std::uint32_t body_id, int wheel_count, const float *wheel_positions,
                                       const float *wheel_radius, const float *suspension_rest,
                                       const float *suspension_k, const float *suspension_damping,
                                       const int *driven_wheels) {
  if (wheel_count <= 0 || !wheel_positions) {
    return 0;
  }

  VehicleState vehicle{};
  vehicle.id = next_id_++;
  vehicle.body_id = body_id;
  vehicle.wheels.resize(static_cast<std::size_t>(wheel_count));
  vehicle.inputs.resize(static_cast<std::size_t>(wheel_count));

  for (int i = 0; i < wheel_count; ++i) {
    std::size_t base = static_cast<std::size_t>(i) * 3;
    WheelState &wheel = vehicle.wheels[static_cast<std::size_t>(i)];
    wheel.local_pos = {wheel_positions[base], wheel_positions[base + 1], wheel_positions[base + 2]};
    wheel.radius = wheel_radius ? wheel_radius[i] : 0.03f;
    wheel.rest_length = suspension_rest ? suspension_rest[i] : 0.05f;
    wheel.spring_k = suspension_k ? suspension_k[i] : 1400.0f;
    wheel.damping = suspension_damping ? suspension_damping[i] : 120.0f;
    wheel.driven = driven_wheels ? driven_wheels[i] != 0 : false;
  }

  vehicles_[vehicle.id] = vehicle;
  return vehicle.id;
}

void PhysicsWorld::SetWheelInput(std::uint32_t vehicle_id, int wheel_index, float steer, float drive_torque, float brake_torque) {
  auto it = vehicles_.find(vehicle_id);
  if (it == vehicles_.end()) return;
  if (wheel_index < 0 || wheel_index >= static_cast<int>(it->second.inputs.size())) return;
  auto &input = it->second.inputs[static_cast<std::size_t>(wheel_index)];
  input.steer = steer;
  input.drive_torque = drive_torque;
  input.brake_torque = brake_torque;
}

void PhysicsWorld::SetVehicleAero(std::uint32_t vehicle_id, float drag_coefficient, float downforce) {
  auto it = vehicles_.find(vehicle_id);
  if (it == vehicles_.end()) return;
  it->second.drag_coefficient = drag_coefficient;
  it->second.downforce = downforce;
}

void PhysicsWorld::SetVehicleTireModel(std::uint32_t vehicle_id, float B, float C, float D, float E) {
  auto it = vehicles_.find(vehicle_id);
  if (it == vehicles_.end()) return;
  it->second.pacejka_B = B;
  it->second.pacejka_C = C;
  it->second.pacejka_D = D;
  it->second.pacejka_E = E;
}

void PhysicsWorld::ApplyAerodynamics(RigidBody &body, float dt) {
  (void)dt;
  Vec3 relative_wind = body.velocity - config_.wind;
  float speed = relative_wind.Length();
  if (speed < 0.1f) return;
  float drag = 0.5f * config_.air_density * body.drag_coefficient * body.cross_section_area * speed * speed;
  Vec3 drag_force = Normalize(relative_wind) * -drag;
  body.force_accum += drag_force;
}

void PhysicsWorld::ApplyThermal(RigidBody &body, float dt) {
  float ambient = config_.ambient_temp_c;
  float delta = body.temperature_c - ambient;
  float cooling = config_.thermal_exchange * delta;
  float heating = body.velocity.LengthSq() * 0.0015f + body.damage * 0.4f;
  float rain_cool = config_.rain_intensity * 0.6f;
  body.temperature_c += (heating - cooling - rain_cool) * dt;
}

void PhysicsWorld::ApplyDamage(RigidBody &body, const Vec3 &accel, float dt) {
  float stress = accel.Length() * body.mass / std::max(body.surface_area, 0.01f);
  float torsion = body.torque_accum.Length() / std::max(body.surface_area, 0.01f);
  stress += torsion * 0.1f;
  if (stress > body.material_strength) {
    float overload = (stress / body.material_strength) - 1.0f;
    body.damage += overload * body.fracture_toughness * dt;
  }
  if (body.damage > 1.0f) {
    body.is_broken = true;
  }
}

float PhysicsWorld::ComputeBodyRadius(const RigidBody &body) const {
  float area = std::max(body.cross_section_area, 0.0001f);
  return std::sqrt(area / kPi);
}

void PhysicsWorld::ApplyGroundContact(RigidBody &body, float dt) {
  float radius = ComputeBodyRadius(body);
  float target_y = radius;
  float penetration = target_y - body.position.y;
  if (penetration <= config_.contact_slop) {
    return;
  }

  body.position.y += penetration;
  if (body.velocity.y < 0.0f) {
    body.velocity.y = -body.velocity.y * config_.restitution;
  }

  float horiz_speed = std::sqrt(body.velocity.x * body.velocity.x + body.velocity.z * body.velocity.z);
  if (horiz_speed > 0.0f) {
    float static_threshold = config_.static_friction * 0.2f;
    if (horiz_speed < static_threshold) {
      body.velocity.x = 0.0f;
      body.velocity.z = 0.0f;
    } else {
      float damp = std::max(0.0f, 1.0f - config_.dynamic_friction * 4.0f * dt);
      body.velocity.x *= damp;
      body.velocity.z *= damp;
    }
  }

  float spin_damp = std::max(0.0f, 1.0f - config_.dynamic_friction * 2.0f * dt);
  body.angular_velocity = body.angular_velocity * spin_damp;
}

}  // namespace NativeEngine::Physics
