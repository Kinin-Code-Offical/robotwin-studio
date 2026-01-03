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

std::uint32_t PhysicsWorld::AddDistanceConstraint(std::uint32_t body_a, std::uint32_t body_b,
                                                  const Vec3 &local_a, const Vec3 &local_b,
                                                  float rest_length, float stiffness, float damping,
                                                  float max_force, bool tension_only) {
  if (bodies_.find(body_a) == bodies_.end() || bodies_.find(body_b) == bodies_.end()) {
    return 0;
  }
  DistanceConstraint constraint{};
  constraint.id = next_constraint_id_++;
  constraint.body_a = body_a;
  constraint.body_b = body_b;
  constraint.local_a = local_a;
  constraint.local_b = local_b;
  constraint.rest_length = rest_length;
  constraint.stiffness = stiffness;
  constraint.damping = damping;
  constraint.max_force = max_force;
  constraint.tension_only = tension_only;
  distance_constraints_.push_back(constraint);
  return constraint.id;
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

  if (body.is_sleeping) {
    if (body.force_accum.LengthSq() > 1e-6f || body.torque_accum.LengthSq() > 1e-6f) {
      body.is_sleeping = false;
      body.sleep_timer = 0.0f;
    } else {
      body.force_accum = {};
      body.torque_accum = {};
      return;
    }
  }

  Vec3 accel = body.force_accum * body.inv_mass;
  Vec3 ang_accel = Hadamard(body.torque_accum, body.inv_inertia);
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

  float lin_thresh = config_.sleep_linear_threshold;
  float ang_thresh = config_.sleep_angular_threshold;
  if (body.velocity.LengthSq() < lin_thresh * lin_thresh &&
      body.angular_velocity.LengthSq() < ang_thresh * ang_thresh) {
    body.sleep_timer += dt;
    if (body.sleep_timer >= config_.sleep_time) {
      body.is_sleeping = true;
      body.velocity = {};
      body.angular_velocity = {};
    }
  } else {
    body.sleep_timer = 0.0f;
    body.is_sleeping = false;
  }

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

  ApplyDistanceConstraints(dt);

  for (auto &kvp : vehicles_) {
    StepVehicle(kvp.second, dt);
  }

  for (auto &kvp : bodies_) {
    Integrate(kvp.second, dt);
  }

  GenerateContacts();
  ResolveContacts(dt);

  for (auto &kvp : bodies_) {
    ApplyGroundContact(kvp.second, dt);
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
  if (body.shape == ShapeType::Sphere) {
    return std::max(body.radius, 0.01f);
  }
  return std::max(body.half_extents.y, 0.01f);
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
    body.velocity.y = -body.velocity.y * std::max(0.0f, body.restitution);
  }

  float horiz_speed = std::sqrt(body.velocity.x * body.velocity.x + body.velocity.z * body.velocity.z);
  if (horiz_speed > 0.0f) {
    float friction = std::max(0.0f, body.friction);
    float static_threshold = config_.static_friction * friction * 0.2f;
    if (horiz_speed < static_threshold) {
      body.velocity.x = 0.0f;
      body.velocity.z = 0.0f;
    } else {
      float damp = std::max(0.0f, 1.0f - config_.dynamic_friction * friction * 4.0f * dt);
      body.velocity.x *= damp;
      body.velocity.z *= damp;
    }
  }

  float spin_damp = std::max(0.0f, 1.0f - config_.dynamic_friction * std::max(0.0f, body.friction) * 2.0f * dt);
  body.angular_velocity = body.angular_velocity * spin_damp;
}

PhysicsWorld::Aabb PhysicsWorld::ComputeAabb(const RigidBody &body) const {
  Vec3 extents{};
  if (body.shape == ShapeType::Sphere) {
    float r = std::max(body.radius, 0.001f);
    extents = {r, r, r};
  } else {
    float xx = body.rotation.x;
    float yy = body.rotation.y;
    float zz = body.rotation.z;
    float ww = body.rotation.w;

    float m00 = 1.0f - 2.0f * (yy * yy + zz * zz);
    float m01 = 2.0f * (xx * yy - zz * ww);
    float m02 = 2.0f * (xx * zz + yy * ww);
    float m10 = 2.0f * (xx * yy + zz * ww);
    float m11 = 1.0f - 2.0f * (xx * xx + zz * zz);
    float m12 = 2.0f * (yy * zz - xx * ww);
    float m20 = 2.0f * (xx * zz - yy * ww);
    float m21 = 2.0f * (yy * zz + xx * ww);
    float m22 = 1.0f - 2.0f * (xx * xx + yy * yy);

    Vec3 half = body.half_extents;
    extents = {
        std::fabs(m00) * half.x + std::fabs(m01) * half.y + std::fabs(m02) * half.z,
        std::fabs(m10) * half.x + std::fabs(m11) * half.y + std::fabs(m12) * half.z,
        std::fabs(m20) * half.x + std::fabs(m21) * half.y + std::fabs(m22) * half.z};
  }

  return {body.position - extents, body.position + extents};
}

bool PhysicsWorld::AabbOverlap(const Aabb &a, const Aabb &b) const {
  return (a.min.x <= b.max.x && a.max.x >= b.min.x) &&
         (a.min.y <= b.max.y && a.max.y >= b.min.y) &&
         (a.min.z <= b.max.z && a.max.z >= b.min.z);
}

bool PhysicsWorld::CollideSphereSphere(const RigidBody &a, const RigidBody &b, Contact &out) const {
  Vec3 delta = b.position - a.position;
  float dist_sq = delta.LengthSq();
  float radius = a.radius + b.radius;
  if (dist_sq >= radius * radius) {
    return false;
  }
  float dist = std::sqrt(std::max(dist_sq, 1e-6f));
  Vec3 normal = dist > 1e-5f ? delta / dist : Vec3{0.0f, 1.0f, 0.0f};
  out.a = a.id;
  out.b = b.id;
  out.normal = normal;
  out.penetration = radius - dist;
  out.point = a.position + normal * (a.radius - 0.5f * out.penetration);
  return true;
}

bool PhysicsWorld::CollideSphereBox(const RigidBody &sphere, const RigidBody &box, Contact &out) const {
  Vec3 half = box.half_extents;
  Vec3 min = box.position - half;
  Vec3 max = box.position + half;
  Vec3 closest = {
      std::min(std::max(sphere.position.x, min.x), max.x),
      std::min(std::max(sphere.position.y, min.y), max.y),
      std::min(std::max(sphere.position.z, min.z), max.z)};

  Vec3 delta = sphere.position - closest;
  float dist_sq = delta.LengthSq();
  float r = sphere.radius;
  if (dist_sq >= r * r) {
    return false;
  }
  float dist = std::sqrt(std::max(dist_sq, 1e-6f));
  Vec3 normal = dist > 1e-5f ? delta / dist : Vec3{0.0f, 1.0f, 0.0f};
  out.a = sphere.id;
  out.b = box.id;
  out.normal = normal;
  out.penetration = r - dist;
  out.point = closest;
  return true;
}

bool PhysicsWorld::CollideBoxBox(const RigidBody &a, const RigidBody &b, Contact &out) const {
  Vec3 aMin = a.position - a.half_extents;
  Vec3 aMax = a.position + a.half_extents;
  Vec3 bMin = b.position - b.half_extents;
  Vec3 bMax = b.position + b.half_extents;

  if (!(aMin.x <= bMax.x && aMax.x >= bMin.x &&
        aMin.y <= bMax.y && aMax.y >= bMin.y &&
        aMin.z <= bMax.z && aMax.z >= bMin.z)) {
    return false;
  }

  float penX = std::min(aMax.x - bMin.x, bMax.x - aMin.x);
  float penY = std::min(aMax.y - bMin.y, bMax.y - aMin.y);
  float penZ = std::min(aMax.z - bMin.z, bMax.z - aMin.z);

  Vec3 normal{1.0f, 0.0f, 0.0f};
  float penetration = penX;
  if (penY < penetration) { penetration = penY; normal = {0.0f, 1.0f, 0.0f}; }
  if (penZ < penetration) { penetration = penZ; normal = {0.0f, 0.0f, 1.0f}; }

  Vec3 centerDelta = b.position - a.position;
  if (Dot(centerDelta, normal) < 0.0f) {
    normal = normal * -1.0f;
  }

  out.a = a.id;
  out.b = b.id;
  out.normal = normal;
  out.penetration = penetration;
  out.point = (a.position + b.position) * 0.5f;
  return true;
}

void PhysicsWorld::GenerateContacts() {
  contacts_.clear();
  if (bodies_.size() < 2) return;

  std::vector<std::pair<std::uint32_t, Aabb>> aabbs;
  aabbs.reserve(bodies_.size());
  for (auto &kvp : bodies_) {
    aabbs.emplace_back(kvp.first, ComputeAabb(kvp.second));
  }

  for (std::size_t i = 0; i < aabbs.size(); ++i) {
    for (std::size_t j = i + 1; j < aabbs.size(); ++j) {
      const auto &entryA = aabbs[i];
      const auto &entryB = aabbs[j];
      if (!AabbOverlap(entryA.second, entryB.second)) continue;

      auto itA = bodies_.find(entryA.first);
      auto itB = bodies_.find(entryB.first);
      if (itA == bodies_.end() || itB == bodies_.end()) continue;
      auto &bodyA = itA->second;
      auto &bodyB = itB->second;
      if (bodyA.is_static && bodyB.is_static) continue;

      Contact contact{};
      bool hit = false;
      if (bodyA.shape == ShapeType::Sphere && bodyB.shape == ShapeType::Sphere) {
        hit = CollideSphereSphere(bodyA, bodyB, contact);
      } else if (bodyA.shape == ShapeType::Sphere && bodyB.shape == ShapeType::Box) {
        hit = CollideSphereBox(bodyA, bodyB, contact);
      } else if (bodyA.shape == ShapeType::Box && bodyB.shape == ShapeType::Sphere) {
        hit = CollideSphereBox(bodyB, bodyA, contact);
        if (hit) {
          std::swap(contact.a, contact.b);
          contact.normal = contact.normal * -1.0f;
        }
      } else {
        hit = CollideBoxBox(bodyA, bodyB, contact);
      }

      if (hit) {
        float friction = std::sqrt(std::max(bodyA.friction, 0.0f) * std::max(bodyB.friction, 0.0f));
        float restitution = std::max(bodyA.restitution, bodyB.restitution);
        contact.friction = friction;
        contact.restitution = restitution;
        contacts_.push_back(contact);
      }
    }
  }
}

void PhysicsWorld::ResolveContacts(float dt) {
  (void)dt;
  for (auto &contact : contacts_) {
    ResolveContact(contact, dt);
  }
}

void PhysicsWorld::ApplyDistanceConstraints(float dt) {
  for (auto &constraint : distance_constraints_) {
    auto itA = bodies_.find(constraint.body_a);
    auto itB = bodies_.find(constraint.body_b);
    if (itA == bodies_.end() || itB == bodies_.end()) continue;
    auto &a = itA->second;
    auto &b = itB->second;

    Vec3 anchorA = a.position + Rotate(a.rotation, constraint.local_a);
    Vec3 anchorB = b.position + Rotate(b.rotation, constraint.local_b);
    Vec3 delta = anchorB - anchorA;
    float length = delta.Length();
    if (length <= 1e-5f) continue;
    Vec3 dir = delta / length;
    float stretch = length - constraint.rest_length;
    if (constraint.tension_only && stretch <= 0.0f) continue;

    Vec3 velA = a.velocity + Cross(a.angular_velocity, anchorA - a.position);
    Vec3 velB = b.velocity + Cross(b.angular_velocity, anchorB - b.position);
    float relVel = Dot(velB - velA, dir);

    float forceMag = stretch * constraint.stiffness + relVel * constraint.damping;
    if (constraint.tension_only && forceMag < 0.0f) forceMag = 0.0f;
    forceMag = std::min(std::max(forceMag, -constraint.max_force), constraint.max_force);
    Vec3 force = dir * forceMag;

    a.force_accum += force;
    a.torque_accum += Cross(anchorA - a.position, force);
    b.force_accum -= force;
    b.torque_accum += Cross(anchorB - b.position, force * -1.0f);

    if (a.is_sleeping || b.is_sleeping) {
      a.is_sleeping = false;
      b.is_sleeping = false;
      a.sleep_timer = 0.0f;
      b.sleep_timer = 0.0f;
    }
  }
}

void PhysicsWorld::ResolveContact(Contact &contact, float dt) {
  (void)dt;
  auto itA = bodies_.find(contact.a);
  auto itB = bodies_.find(contact.b);
  if (itA == bodies_.end() || itB == bodies_.end()) return;
  auto &a = itA->second;
  auto &b = itB->second;

  float invMassA = a.inv_mass;
  float invMassB = b.inv_mass;
  if (invMassA + invMassB <= 0.0f) return;

  Vec3 ra = contact.point - a.position;
  Vec3 rb = contact.point - b.position;
  Vec3 velA = a.velocity + Cross(a.angular_velocity, ra);
  Vec3 velB = b.velocity + Cross(b.angular_velocity, rb);
  Vec3 rv = velB - velA;

  float velAlongNormal = Dot(rv, contact.normal);
  if (velAlongNormal > 0.0f) {
    return;
  }

  float j = -(1.0f + contact.restitution) * velAlongNormal;
  j /= (invMassA + invMassB);

  Vec3 impulse = contact.normal * j;
  a.velocity -= impulse * invMassA;
  b.velocity += impulse * invMassB;

  Vec3 angImpulseA = Cross(ra, impulse);
  Vec3 angImpulseB = Cross(rb, impulse);
  a.angular_velocity -= Hadamard(angImpulseA, a.inv_inertia);
  b.angular_velocity += Hadamard(angImpulseB, b.inv_inertia);

  if (j > 0.0f) {
    if (a.is_sleeping) {
      a.is_sleeping = false;
      a.sleep_timer = 0.0f;
    }
    if (b.is_sleeping) {
      b.is_sleeping = false;
      b.sleep_timer = 0.0f;
    }
  }

  Vec3 tangent = rv - contact.normal * velAlongNormal;
  if (tangent.LengthSq() > 1e-6f) {
    tangent = Normalize(tangent);
    float jt = -Dot(rv, tangent);
    jt /= (invMassA + invMassB);
    float maxFriction = j * contact.friction;
    jt = std::max(-maxFriction, std::min(jt, maxFriction));
    Vec3 frictionImpulse = tangent * jt;
    a.velocity -= frictionImpulse * invMassA;
    b.velocity += frictionImpulse * invMassB;
    Vec3 angFrictionA = Cross(ra, frictionImpulse);
    Vec3 angFrictionB = Cross(rb, frictionImpulse);
    a.angular_velocity -= Hadamard(angFrictionA, a.inv_inertia);
    b.angular_velocity += Hadamard(angFrictionB, b.inv_inertia);
  }

  float percent = 0.8f;
  float slop = config_.contact_slop;
  float correction = std::max(contact.penetration - slop, 0.0f) / (invMassA + invMassB) * percent;
  Vec3 correctionVec = contact.normal * correction;
  a.position -= correctionVec * invMassA;
  b.position += correctionVec * invMassB;
}

bool PhysicsWorld::Raycast(const Vec3 &origin, const Vec3 &direction, float max_distance, RaycastHit &out) const {
  Vec3 dir = Normalize(direction);
  if (dir.LengthSq() <= 0.0f) return false;
  bool hit_any = false;
  float closest = max_distance;

  for (const auto &kvp : bodies_) {
    const auto &body = kvp.second;
    RaycastHit hit{};
    bool hit_body = false;
    if (body.shape == ShapeType::Sphere) {
      hit_body = RaycastSphere(origin, dir, closest, body, hit);
    } else {
      hit_body = RaycastBox(origin, dir, closest, body, hit);
    }
    if (hit_body && hit.distance < closest) {
      closest = hit.distance;
      out = hit;
      hit_any = true;
    }
  }

  return hit_any;
}

bool PhysicsWorld::RaycastSphere(const Vec3 &origin, const Vec3 &dir, float max_distance,
                                 const RigidBody &body, RaycastHit &out) const {
  Vec3 m = origin - body.position;
  float b = Dot(m, dir);
  float c = Dot(m, m) - body.radius * body.radius;
  if (c > 0.0f && b > 0.0f) return false;
  float discr = b * b - c;
  if (discr < 0.0f) return false;
  float t = -b - std::sqrt(discr);
  if (t < 0.0f) t = 0.0f;
  if (t > max_distance) return false;

  Vec3 point = origin + dir * t;
  Vec3 normal = Normalize(point - body.position);
  out.body_id = body.id;
  out.point = point;
  out.normal = normal;
  out.distance = t;
  return true;
}

bool PhysicsWorld::RaycastBox(const Vec3 &origin, const Vec3 &dir, float max_distance,
                              const RigidBody &body, RaycastHit &out) const {
  Vec3 min = body.position - body.half_extents;
  Vec3 max = body.position + body.half_extents;

  float tmin = 0.0f;
  float tmax = max_distance;

  auto check_axis = [&](float start, float direction, float minv, float maxv) -> bool {
    if (std::fabs(direction) < 1e-6f) {
      return start >= minv && start <= maxv;
    }
    float ood = 1.0f / direction;
    float t1 = (minv - start) * ood;
    float t2 = (maxv - start) * ood;
    if (t1 > t2) std::swap(t1, t2);
    if (t1 > tmin) tmin = t1;
    if (t2 < tmax) tmax = t2;
    return tmin <= tmax;
  };

  if (!check_axis(origin.x, dir.x, min.x, max.x)) return false;
  if (!check_axis(origin.y, dir.y, min.y, max.y)) return false;
  if (!check_axis(origin.z, dir.z, min.z, max.z)) return false;

  float t = tmin >= 0.0f ? tmin : tmax;
  if (t < 0.0f || t > max_distance) return false;

  Vec3 point = origin + dir * t;
  Vec3 center = body.position;
  Vec3 local = point - center;
  Vec3 absLocal = AbsVec(local);
  Vec3 normal{};
  float dx = std::fabs(absLocal.x - body.half_extents.x);
  float dy = std::fabs(absLocal.y - body.half_extents.y);
  float dz = std::fabs(absLocal.z - body.half_extents.z);

  if (dx <= dy && dx <= dz) normal = {local.x > 0.0f ? 1.0f : -1.0f, 0.0f, 0.0f};
  else if (dy <= dz) normal = {0.0f, local.y > 0.0f ? 1.0f : -1.0f, 0.0f};
  else normal = {0.0f, 0.0f, local.z > 0.0f ? 1.0f : -1.0f};

  out.body_id = body.id;
  out.point = point;
  out.normal = normal;
  out.distance = t;
  return true;
}

}  // namespace NativeEngine::Physics
