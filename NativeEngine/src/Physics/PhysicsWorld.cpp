#include "../../include/Physics/PhysicsWorld.h"
#include <algorithm>
#include <cmath>
#include <limits>

namespace NativeEngine::Physics
{

  namespace
  {
    constexpr float kPi = 3.14159265358979323846f;

    std::uint64_t MakeContactKey(std::uint32_t a, std::uint32_t b)
    {
      if (a > b)
        std::swap(a, b);
      return (static_cast<std::uint64_t>(a) << 32) | static_cast<std::uint64_t>(b);
    }

    void ApplyImpulse(RigidBody &body, const Vec3 &impulse, const Vec3 &r)
    {
      body.velocity += impulse * body.inv_mass;
      body.angular_velocity += Hadamard(Cross(r, impulse), body.inv_inertia);
    }
  } // namespace

  PhysicsWorld::PhysicsWorld()
  {
    rng_.Seed(config_.noise_seed);
    ground_planes_.push_back({{0.0f, 1.0f, 0.0f}, 0.0f});
  }

  void PhysicsWorld::SetConfig(const PhysicsConfig &config)
  {
    config_ = config;
    rng_.Seed(config_.noise_seed);
  }

  std::uint32_t PhysicsWorld::AddBody(const RigidBody &body)
  {
    RigidBody copy = body;
    if (copy.id == 0)
    {
      copy.id = next_id_++;
    }
    copy.SetMass(copy.mass);
    bodies_[copy.id] = copy;
    body_cache_dirty_ = true;
    return copy.id;
  }

  bool PhysicsWorld::GetBody(std::uint32_t id, RigidBody &out) const
  {
    auto it = bodies_.find(id);
    if (it == bodies_.end())
    {
      return false;
    }
    out = it->second;
    return true;
  }

  bool PhysicsWorld::SetBody(std::uint32_t id, const RigidBody &body)
  {
    auto it = bodies_.find(id);
    if (it == bodies_.end())
    {
      return false;
    }
    RigidBody copy = body;
    copy.id = id;
    copy.SetMass(copy.mass);
    it->second = copy;
    return true;
  }

  bool PhysicsWorld::ApplyForce(std::uint32_t id, const Vec3 &force)
  {
    auto it = bodies_.find(id);
    if (it == bodies_.end())
    {
      return false;
    }
    it->second.force_accum += force;
    return true;
  }

  bool PhysicsWorld::ApplyForceAtPoint(std::uint32_t id, const Vec3 &force, const Vec3 &point)
  {
    auto it = bodies_.find(id);
    if (it == bodies_.end())
    {
      return false;
    }
    auto &body = it->second;
    body.force_accum += force;
    Vec3 r = point - body.position;
    body.torque_accum += Cross(r, force);
    return true;
  }

  bool PhysicsWorld::ApplyTorque(std::uint32_t id, const Vec3 &torque)
  {
    auto it = bodies_.find(id);
    if (it == bodies_.end())
    {
      return false;
    }
    it->second.torque_accum += torque;
    return true;
  }

  std::uint32_t PhysicsWorld::AddDistanceConstraint(std::uint32_t body_a, std::uint32_t body_b,
                                                    const Vec3 &local_a, const Vec3 &local_b,
                                                    float rest_length, float stiffness, float damping,
                                                    float max_force, bool tension_only)
  {
    if (bodies_.find(body_a) == bodies_.end() || bodies_.find(body_b) == bodies_.end())
    {
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

  float PhysicsWorld::ComputeDt(float dt_override)
  {
    float base = (dt_override > 0.0f) ? dt_override : config_.base_dt;
    float jitter = config_.time_jitter * rng_.NextFloatSigned();
    float dt = base + jitter;
    if (dt < 1e-5f)
      dt = 1e-5f;
    return dt;
  }

  Vec3 PhysicsWorld::ComputeGravity(float dt)
  {
    (void)dt;
    float jitter = config_.gravity_jitter * rng_.NextFloatSigned();
    return {config_.gravity.x, config_.gravity.y + jitter, config_.gravity.z};
  }

  void PhysicsWorld::Integrate(RigidBody &body, float dt)
  {
    if (body.is_static || body.inv_mass <= 0.0f)
    {
      body.force_accum = {};
      body.torque_accum = {};
      return;
    }

    if (body.is_sleeping)
    {
      if (body.force_accum.LengthSq() > 1e-6f || body.torque_accum.LengthSq() > 1e-6f)
      {
        body.is_sleeping = false;
        body.sleep_timer = 0.0f;
      }
      else
      {
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

    if (body.is_broken)
    {
      body.linear_damping = 0.25f;
      body.angular_damping = 0.3f;
    }
    body.velocity += accel * dt;
    body.angular_velocity += ang_accel * dt;
    body.velocity = body.velocity * (1.0f - body.linear_damping * dt);
    body.position += body.velocity * dt;

    body.angular_velocity = body.angular_velocity * (1.0f - body.angular_damping * dt);
    body.rotation = Normalize(body.rotation * Quat::FromAxisAngle(body.angular_velocity, dt));

    float lin_thresh = config_.sleep_linear_threshold;
    float ang_thresh = config_.sleep_angular_threshold;
    if (body.velocity.LengthSq() < lin_thresh * lin_thresh &&
        body.angular_velocity.LengthSq() < ang_thresh * ang_thresh)
    {
      body.sleep_timer += dt;
      if (body.sleep_timer >= config_.sleep_time)
      {
        body.is_sleeping = true;
        body.velocity = {};
        body.angular_velocity = {};
      }
    }
    else
    {
      body.sleep_timer = 0.0f;
      body.is_sleeping = false;
    }

    body.force_accum = {};
    body.torque_accum = {};
  }

  void PhysicsWorld::Step(float dt_override)
  {
    float dt = ComputeDt(dt_override);
    RebuildBodyCache();
    float maxStep = 0.0f;
    for (auto *body : body_cache_)
    {
      if (!body)
        continue;
      if (body->is_static)
        continue;
      float speed = body->velocity.Length();
      float radius = ComputeBodyRadius(*body);
      if (radius <= 0.0f)
        radius = 0.05f;
      float step = speed * dt / std::max(radius * 0.5f, 0.01f);
      if (step > maxStep)
        maxStep = step;
    }

    int substeps = 1;
    if (maxStep > 1.0f)
    {
      substeps = static_cast<int>(std::ceil(maxStep));
      if (substeps > 8)
        substeps = 8;
    }
    float subDt = dt / static_cast<float>(substeps);

    for (int step = 0; step < substeps; ++step)
    {
      Vec3 gravity = ComputeGravity(subDt);

      for (auto *body : body_cache_)
      {
        if (!body)
          continue;
        if (!body->is_static)
        {
          body->force_accum += gravity * body->mass;
        }
      }

      ApplyDistanceConstraints(subDt);

      for (auto &kvp : vehicles_)
      {
        StepVehicle(kvp.second, subDt);
      }

      for (auto *body : body_cache_)
      {
        if (!body)
          continue;
        Integrate(*body, subDt);
      }

      GenerateContacts();
      ResolveContacts(subDt);

      for (auto *body : body_cache_)
      {
        if (!body)
          continue;
        ApplyGroundContact(*body, subDt);
      }
    }
  }

  float PhysicsWorld::Pacejka(float slip, float B, float C, float D, float E) const
  {
    float x = B * slip;
    return D * std::sin(C * std::atan(x - E * (x - std::atan(x))));
  }

  void PhysicsWorld::StepVehicle(VehicleState &vehicle, float dt)
  {
    auto body_it = bodies_.find(vehicle.body_id);
    if (body_it == bodies_.end())
    {
      return;
    }
    auto &body = body_it->second;
    if (vehicle.wheels.empty())
    {
      return;
    }

    Vec3 forward = Rotate(body.rotation, {0.0f, 0.0f, 1.0f});
    Vec3 right = Rotate(body.rotation, {1.0f, 0.0f, 0.0f});
    Vec3 up = Rotate(body.rotation, {0.0f, 1.0f, 0.0f});

    for (std::size_t i = 0; i < vehicle.wheels.size(); ++i)
    {
      auto &wheel = vehicle.wheels[i];
      WheelInput input = (i < vehicle.inputs.size()) ? vehicle.inputs[i] : WheelInput{};

      Vec3 wheel_world = body.position + Rotate(body.rotation, wheel.local_pos);
      float ground_y = 0.0f;
      float penetration = (wheel.radius + ground_y) - wheel_world.y;
      if (penetration <= 0.0f)
      {
        wheel.angular_velocity *= 0.99f;
        continue;
      }

      float compression = wheel.rest_length + penetration;
      Vec3 r = wheel_world - body.position;
      Vec3 contact_vel = body.velocity + Cross(body.angular_velocity, r);
      float vel_up = Dot(contact_vel, up);
      float spring_force = compression * wheel.spring_k - vel_up * wheel.damping;
      if (spring_force < 0.0f)
        spring_force = 0.0f;

      Vec3 wheel_forward = forward;
      Vec3 wheel_right = right;
      if (std::abs(input.steer) > 0.0001f)
      {
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
    if (speed > 0.1f)
    {
      float drag = 0.5f * config_.air_density * vehicle.drag_coefficient * speed * speed;
      Vec3 drag_force = Normalize(relative_wind) * -drag;
      body.force_accum += drag_force;
    }

    if (vehicle.downforce > 0.0f)
    {
      body.force_accum += up * (-vehicle.downforce);
    }
  }

  std::uint32_t PhysicsWorld::AddVehicle(std::uint32_t body_id, int wheel_count, const float *wheel_positions,
                                         const float *wheel_radius, const float *suspension_rest,
                                         const float *suspension_k, const float *suspension_damping,
                                         const int *driven_wheels)
  {
    if (wheel_count <= 0 || !wheel_positions)
    {
      return 0;
    }

    VehicleState vehicle{};
    vehicle.id = next_id_++;
    vehicle.body_id = body_id;
    vehicle.wheels.resize(static_cast<std::size_t>(wheel_count));
    vehicle.inputs.resize(static_cast<std::size_t>(wheel_count));

    for (int i = 0; i < wheel_count; ++i)
    {
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

  void PhysicsWorld::SetWheelInput(std::uint32_t vehicle_id, int wheel_index, float steer, float drive_torque, float brake_torque)
  {
    auto it = vehicles_.find(vehicle_id);
    if (it == vehicles_.end())
      return;
    if (wheel_index < 0 || wheel_index >= static_cast<int>(it->second.inputs.size()))
      return;
    auto &input = it->second.inputs[static_cast<std::size_t>(wheel_index)];
    input.steer = steer;
    input.drive_torque = drive_torque;
    input.brake_torque = brake_torque;
  }

  void PhysicsWorld::SetVehicleAero(std::uint32_t vehicle_id, float drag_coefficient, float downforce)
  {
    auto it = vehicles_.find(vehicle_id);
    if (it == vehicles_.end())
      return;
    it->second.drag_coefficient = drag_coefficient;
    it->second.downforce = downforce;
  }

  void PhysicsWorld::SetVehicleTireModel(std::uint32_t vehicle_id, float B, float C, float D, float E)
  {
    auto it = vehicles_.find(vehicle_id);
    if (it == vehicles_.end())
      return;
    it->second.pacejka_B = B;
    it->second.pacejka_C = C;
    it->second.pacejka_D = D;
    it->second.pacejka_E = E;
  }

  void PhysicsWorld::ClearGroundPlanes()
  {
    ground_planes_.clear();
  }

  void PhysicsWorld::AddGroundPlane(const Vec3 &normal, float distance)
  {
    Vec3 n = Normalize(normal);
    if (n.LengthSq() <= 1e-6f)
    {
      return;
    }
    ground_planes_.push_back({n, distance});
  }

  void PhysicsWorld::ApplyAerodynamics(RigidBody &body, float dt)
  {
    (void)dt;
    Vec3 relative_wind = body.velocity - config_.wind;
    float speed = relative_wind.Length();
    if (speed < 0.1f)
      return;
    float drag = 0.5f * config_.air_density * body.drag_coefficient * body.cross_section_area * speed * speed;
    Vec3 drag_force = Normalize(relative_wind) * -drag;
    body.force_accum += drag_force;
  }

  void PhysicsWorld::ApplyThermal(RigidBody &body, float dt)
  {
    float ambient = config_.ambient_temp_c;
    float delta = body.temperature_c - ambient;
    float cooling = config_.thermal_exchange * delta;
    float heating = body.velocity.LengthSq() * 0.0015f + body.damage * 0.4f;
    float rain_cool = config_.rain_intensity * 0.6f;
    body.temperature_c += (heating - cooling - rain_cool) * dt;
  }

  void PhysicsWorld::ApplyDamage(RigidBody &body, const Vec3 &accel, float dt)
  {
    float stress = accel.Length() * body.mass / std::max(body.surface_area, 0.01f);
    float torsion = body.torque_accum.Length() / std::max(body.surface_area, 0.01f);
    stress += torsion * 0.1f;
    if (stress > body.material_strength)
    {
      float overload = (stress / body.material_strength) - 1.0f;
      body.damage += overload * body.fracture_toughness * dt;
    }
    if (body.damage > 1.0f)
    {
      body.is_broken = true;
    }
  }

  float PhysicsWorld::ComputeBodyRadius(const RigidBody &body) const
  {
    if (body.shape == ShapeType::Sphere)
    {
      return std::max(body.radius, 0.01f);
    }
    return std::max(body.half_extents.y, 0.01f);
  }

  void PhysicsWorld::RebuildBodyCache()
  {
    if (!body_cache_dirty_ && body_cache_size_ == bodies_.size())
    {
      return;
    }
    body_cache_.clear();
    body_cache_.reserve(bodies_.size());
    for (auto &kvp : bodies_)
    {
      body_cache_.push_back(&kvp.second);
    }
    body_cache_size_ = bodies_.size();
    body_cache_dirty_ = false;
  }

  PhysicsWorld::Aabb PhysicsWorld::GetCachedAabb(const RigidBody &body)
  {
    auto same_vec = [](const Vec3 &a, const Vec3 &b)
    {
      return a.x == b.x && a.y == b.y && a.z == b.z;
    };
    auto same_quat = [](const Quat &a, const Quat &b)
    {
      return a.w == b.w && a.x == b.x && a.y == b.y && a.z == b.z;
    };

    bool eligible = body.is_static || body.is_sleeping;
    auto &cache = aabb_cache_[body.id];
    if (eligible && cache.valid &&
        cache.shape == body.shape &&
        cache.radius == body.radius &&
        same_vec(cache.half_extents, body.half_extents) &&
        same_vec(cache.position, body.position) &&
        same_quat(cache.rotation, body.rotation))
    {
      return cache.aabb;
    }

    cache.aabb = ComputeAabb(body);
    cache.position = body.position;
    cache.rotation = body.rotation;
    cache.half_extents = body.half_extents;
    cache.radius = body.radius;
    cache.shape = body.shape;
    cache.valid = true;
    return cache.aabb;
  }

  void PhysicsWorld::ApplyGroundContact(RigidBody &body, float dt)
  {
    if (ground_planes_.empty())
    {
      return;
    }

    float radius = ComputeBodyRadius(body);
    for (const auto &plane : ground_planes_)
    {
      float distance = Dot(plane.normal, body.position) - plane.distance;
      float projected = radius;
      if (body.shape == ShapeType::Box)
      {
        projected = ProjectBoxRadius(body, plane.normal);
      }
      float penetration = projected - distance;
      if (penetration <= config_.contact_slop)
      {
        continue;
      }

      body.position += plane.normal * penetration;
      float velAlong = Dot(body.velocity, plane.normal);
      if (velAlong < 0.0f)
      {
        body.velocity -= plane.normal * (1.0f + std::max(0.0f, body.restitution)) * velAlong;
      }

      Vec3 lateral = body.velocity - plane.normal * Dot(body.velocity, plane.normal);
      float horiz_speed = lateral.Length();
      if (horiz_speed > 0.0f)
      {
        float friction = std::max(0.0f, body.friction);
        float static_threshold = config_.static_friction * friction * 0.2f;
        if (horiz_speed < static_threshold)
        {
          body.velocity -= lateral;
        }
        else
        {
          float friction_accel = config_.dynamic_friction * friction * 9.81f;
          float decel = friction_accel * dt;
          if (decel > horiz_speed)
            decel = horiz_speed;
          body.velocity -= lateral * (decel / horiz_speed);
        }
      }

      float spin_damp = std::max(0.0f, 1.0f - config_.dynamic_friction * std::max(0.0f, body.friction) * 2.0f * dt);
      body.angular_velocity = body.angular_velocity * spin_damp;
    }
  }

  PhysicsWorld::Aabb PhysicsWorld::ComputeAabb(const RigidBody &body) const
  {
    Vec3 extents{};
    if (body.shape == ShapeType::Sphere)
    {
      float r = std::max(body.radius, 0.001f);
      extents = {r, r, r};
    }
    else
    {
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

  float PhysicsWorld::ProjectBoxRadius(const RigidBody &body, const Vec3 &axis) const
  {
    Vec3 half = body.half_extents;
    float xx = body.rotation.x;
    float yy = body.rotation.y;
    float zz = body.rotation.z;
    float ww = body.rotation.w;

    Vec3 axisX{
        1.0f - 2.0f * (yy * yy + zz * zz),
        2.0f * (xx * yy + zz * ww),
        2.0f * (xx * zz - yy * ww)};
    Vec3 axisY{
        2.0f * (xx * yy - zz * ww),
        1.0f - 2.0f * (xx * xx + zz * zz),
        2.0f * (yy * zz + xx * ww)};
    Vec3 axisZ{
        2.0f * (xx * zz + yy * ww),
        2.0f * (yy * zz - xx * ww),
        1.0f - 2.0f * (xx * xx + yy * yy)};

    return std::fabs(Dot(axis, axisX)) * half.x +
           std::fabs(Dot(axis, axisY)) * half.y +
           std::fabs(Dot(axis, axisZ)) * half.z;
  }

  bool PhysicsWorld::AabbOverlap(const Aabb &a, const Aabb &b) const
  {
    return (a.min.x <= b.max.x && a.max.x >= b.min.x) &&
           (a.min.y <= b.max.y && a.max.y >= b.min.y) &&
           (a.min.z <= b.max.z && a.max.z >= b.min.z);
  }

  bool PhysicsWorld::CollideSphereSphere(const RigidBody &a, const RigidBody &b, Contact &out) const
  {
    Vec3 delta = b.position - a.position;
    float dist_sq = delta.LengthSq();
    float radius = a.radius + b.radius;
    if (dist_sq >= radius * radius)
    {
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

  bool PhysicsWorld::CollideSphereBox(const RigidBody &sphere, const RigidBody &box, Contact &out) const
  {
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
    if (dist_sq >= r * r)
    {
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

  bool PhysicsWorld::CollideBoxBox(const RigidBody &a, const RigidBody &b, Contact &out) const
  {
    const float kEpsilon = 1e-5f;
    Vec3 aHalf = a.half_extents;
    Vec3 bHalf = b.half_extents;

    float ax = a.rotation.x, ay = a.rotation.y, az = a.rotation.z, aw = a.rotation.w;
    float bx = b.rotation.x, by = b.rotation.y, bz = b.rotation.z, bw = b.rotation.w;

    Vec3 A0{
        1.0f - 2.0f * (ay * ay + az * az),
        2.0f * (ax * ay + az * aw),
        2.0f * (ax * az - ay * aw)};
    Vec3 A1{
        2.0f * (ax * ay - az * aw),
        1.0f - 2.0f * (ax * ax + az * az),
        2.0f * (ay * az + ax * aw)};
    Vec3 A2{
        2.0f * (ax * az + ay * aw),
        2.0f * (ay * az - ax * aw),
        1.0f - 2.0f * (ax * ax + ay * ay)};

    Vec3 B0{
        1.0f - 2.0f * (by * by + bz * bz),
        2.0f * (bx * by + bz * bw),
        2.0f * (bx * bz - by * bw)};
    Vec3 B1{
        2.0f * (bx * by - bz * bw),
        1.0f - 2.0f * (bx * bx + bz * bz),
        2.0f * (by * bz + bx * bw)};
    Vec3 B2{
        2.0f * (bx * bz + by * bw),
        2.0f * (by * bz - bx * bw),
        1.0f - 2.0f * (bx * bx + by * by)};

    float R[3][3] = {
        {Dot(A0, B0), Dot(A0, B1), Dot(A0, B2)},
        {Dot(A1, B0), Dot(A1, B1), Dot(A1, B2)},
        {Dot(A2, B0), Dot(A2, B1), Dot(A2, B2)}};

    float AbsR[3][3];
    for (int i = 0; i < 3; ++i)
    {
      for (int j = 0; j < 3; ++j)
      {
        AbsR[i][j] = std::fabs(R[i][j]) + kEpsilon;
      }
    }

    Vec3 t = b.position - a.position;
    Vec3 tA{Dot(t, A0), Dot(t, A1), Dot(t, A2)};

    float minPen = std::numeric_limits<float>::max();
    Vec3 bestNormal{1.0f, 0.0f, 0.0f};

    auto update_axis = [&](const Vec3 &axis, float dist, float ra, float rb) -> bool
    {
      float pen = ra + rb - std::fabs(dist);
      if (pen < 0.0f)
        return false;
      if (pen < minPen)
      {
        minPen = pen;
        bestNormal = (dist < 0.0f) ? axis * -1.0f : axis;
      }
      return true;
    };

    if (!update_axis(A0, tA.x, aHalf.x, bHalf.x * AbsR[0][0] + bHalf.y * AbsR[0][1] + bHalf.z * AbsR[0][2]))
      return false;
    if (!update_axis(A1, tA.y, aHalf.y, bHalf.x * AbsR[1][0] + bHalf.y * AbsR[1][1] + bHalf.z * AbsR[1][2]))
      return false;
    if (!update_axis(A2, tA.z, aHalf.z, bHalf.x * AbsR[2][0] + bHalf.y * AbsR[2][1] + bHalf.z * AbsR[2][2]))
      return false;

    float tB0 = tA.x * R[0][0] + tA.y * R[1][0] + tA.z * R[2][0];
    float tB1 = tA.x * R[0][1] + tA.y * R[1][1] + tA.z * R[2][1];
    float tB2 = tA.x * R[0][2] + tA.y * R[1][2] + tA.z * R[2][2];

    if (!update_axis(B0, tB0,
                     aHalf.x * AbsR[0][0] + aHalf.y * AbsR[1][0] + aHalf.z * AbsR[2][0],
                     bHalf.x))
      return false;
    if (!update_axis(B1, tB1,
                     aHalf.x * AbsR[0][1] + aHalf.y * AbsR[1][1] + aHalf.z * AbsR[2][1],
                     bHalf.y))
      return false;
    if (!update_axis(B2, tB2,
                     aHalf.x * AbsR[0][2] + aHalf.y * AbsR[1][2] + aHalf.z * AbsR[2][2],
                     bHalf.z))
      return false;

    Vec3 axesA[3] = {A0, A1, A2};
    Vec3 axesB[3] = {B0, B1, B2};
    float tAvals[3] = {tA.x, tA.y, tA.z};
    float aHalfVals[3] = {aHalf.x, aHalf.y, aHalf.z};
    float bHalfVals[3] = {bHalf.x, bHalf.y, bHalf.z};

    for (int i = 0; i < 3; ++i)
    {
      for (int j = 0; j < 3; ++j)
      {
        Vec3 axis = Cross(axesA[i], axesB[j]);
        if (axis.LengthSq() <= 1e-6f)
          continue;

        float ra = aHalfVals[(i + 1) % 3] * AbsR[(i + 2) % 3][j] +
                   aHalfVals[(i + 2) % 3] * AbsR[(i + 1) % 3][j];
        float rb = bHalfVals[(j + 1) % 3] * AbsR[i][(j + 2) % 3] +
                   bHalfVals[(j + 2) % 3] * AbsR[i][(j + 1) % 3];
        float dist = std::fabs(tAvals[(i + 2) % 3] * R[(i + 1) % 3][j] -
                               tAvals[(i + 1) % 3] * R[(i + 2) % 3][j]);
        if (dist > ra + rb)
          return false;
        float pen = ra + rb - dist;
        if (pen < minPen)
        {
          minPen = pen;
          axis = Normalize(axis);
          float sign = (Dot(axis, t) < 0.0f) ? -1.0f : 1.0f;
          bestNormal = axis * sign;
        }
      }
    }

    out.a = a.id;
    out.b = b.id;
    out.normal = bestNormal;
    out.penetration = minPen;
    out.point = (a.position + b.position) * 0.5f;
    return true;
  }

  void PhysicsWorld::GenerateContacts()
  {
    contacts_.clear();
    if (body_cache_.size() < 2)
      return;

    struct BroadphaseEntry
    {
      const RigidBody *body;
      Aabb aabb;
    };

    std::vector<BroadphaseEntry> entries;
    entries.reserve(body_cache_.size());
    for (auto *body : body_cache_)
    {
      if (!body)
        continue;
      entries.push_back({body, GetCachedAabb(*body)});
    }

    float minX = std::numeric_limits<float>::max();
    float minY = std::numeric_limits<float>::max();
    float minZ = std::numeric_limits<float>::max();
    float maxX = std::numeric_limits<float>::lowest();
    float maxY = std::numeric_limits<float>::lowest();
    float maxZ = std::numeric_limits<float>::lowest();
    for (const auto &entry : entries)
    {
      minX = std::min(minX, entry.aabb.min.x);
      minY = std::min(minY, entry.aabb.min.y);
      minZ = std::min(minZ, entry.aabb.min.z);
      maxX = std::max(maxX, entry.aabb.max.x);
      maxY = std::max(maxY, entry.aabb.max.y);
      maxZ = std::max(maxZ, entry.aabb.max.z);
    }
    float rangeX = maxX - minX;
    float rangeY = maxY - minY;
    float rangeZ = maxZ - minZ;
    int axis = 0;
    if (rangeY > rangeX && rangeY >= rangeZ)
    {
      axis = 1;
    }
    else if (rangeZ > rangeX && rangeZ >= rangeY)
    {
      axis = 2;
    }

    auto axis_min = [axis](const Aabb &aabb)
    {
      return axis == 0 ? aabb.min.x : (axis == 1 ? aabb.min.y : aabb.min.z);
    };
    auto axis_max = [axis](const Aabb &aabb)
    {
      return axis == 0 ? aabb.max.x : (axis == 1 ? aabb.max.y : aabb.max.z);
    };

    std::sort(entries.begin(), entries.end(), [axis_min](const BroadphaseEntry &a, const BroadphaseEntry &b)
              { return axis_min(a.aabb) < axis_min(b.aabb); });

    contacts_.reserve(std::min<std::size_t>(entries.size() * 4u, 1024u));

    for (std::size_t i = 0; i < entries.size(); ++i)
    {
      const auto &entryA = entries[i];
      const auto *bodyA = entryA.body;
      if (!bodyA)
        continue;
      for (std::size_t j = i + 1; j < entries.size(); ++j)
      {
        const auto &entryB = entries[j];
        if (axis_min(entryB.aabb) > axis_max(entryA.aabb))
          break;
        if (!AabbOverlap(entryA.aabb, entryB.aabb))
          continue;

        const auto *bodyB = entryB.body;
        if (!bodyB)
          continue;
        if (bodyA->is_static && bodyB->is_static)
          continue;
        if (bodyA->is_sleeping && bodyB->is_sleeping)
          continue;

        Contact contact{};
        contact.key = MakeContactKey(bodyA->id, bodyB->id);
        bool hit = false;
        if (bodyA->shape == ShapeType::Sphere && bodyB->shape == ShapeType::Sphere)
        {
          hit = CollideSphereSphere(*bodyA, *bodyB, contact);
        }
        else if (bodyA->shape == ShapeType::Sphere && bodyB->shape == ShapeType::Box)
        {
          hit = CollideSphereBox(*bodyA, *bodyB, contact);
        }
        else if (bodyA->shape == ShapeType::Box && bodyB->shape == ShapeType::Sphere)
        {
          hit = CollideSphereBox(*bodyB, *bodyA, contact);
          if (hit)
          {
            std::swap(contact.a, contact.b);
            contact.normal = contact.normal * -1.0f;
          }
        }
        else
        {
          hit = CollideBoxBox(*bodyA, *bodyB, contact);
        }

        if (hit)
        {
          float friction = std::sqrt(std::max(bodyA->friction, 0.0f) * std::max(bodyB->friction, 0.0f));
          float restitution = std::max(bodyA->restitution, bodyB->restitution);
          contact.friction = friction;
          contact.restitution = restitution;

          Vec3 ra = contact.point - bodyA->position;
          Vec3 rb = contact.point - bodyB->position;
          Vec3 va = bodyA->velocity + Cross(bodyA->angular_velocity, ra);
          Vec3 vb = bodyB->velocity + Cross(bodyB->angular_velocity, rb);
          Vec3 rv = vb - va;
          float vn = Dot(rv, contact.normal);
          if (vn < -0.1f)
          {
            contact.desired_velocity = -restitution * vn;
          }

          auto cached = contact_cache_.find(contact.key);
          if (cached != contact_cache_.end())
          {
            float alignment = Dot(cached->second.normal, contact.normal);
            if (alignment > 0.7f)
            {
              contact.cached_normal_impulse = cached->second.normal_impulse;
              contact.cached_tangent_impulse = cached->second.tangent_impulse;
            }
          }
          contacts_.push_back(contact);
        }
      }
    }
  }

  void PhysicsWorld::ResolveContacts(float dt)
  {
    if (contacts_.empty())
      return;
    int iterations = static_cast<int>(std::max(1.0f, config_.solver_iterations));
    float cacheDecay = 1.0f - 0.02f * static_cast<float>(iterations);
    cacheDecay = std::min(0.85f, std::max(0.65f, cacheDecay));

    for (auto &contact : contacts_)
    {
      auto itA = bodies_.find(contact.a);
      auto itB = bodies_.find(contact.b);
      if (itA == bodies_.end() || itB == bodies_.end())
        continue;
      auto &a = itA->second;
      auto &b = itB->second;
      Vec3 ra = contact.point - a.position;
      Vec3 rb = contact.point - b.position;

      // Precompute effective mass (denominator for impulse)
      // J = -(1+e)v_rel / (1/Ma + 1/Mb + (Ia^-1(ra x n) x ra).n + ...)
      // Note: Using Hadamard for inertia is an approximation (assumes diagonal inertia in world space)
      Vec3 rnA = Cross(ra, contact.normal);
      Vec3 rnB = Cross(rb, contact.normal);
      Vec3 iA = Hadamard(rnA, a.inv_inertia);
      Vec3 iB = Hadamard(rnB, b.inv_inertia);
      float angA = Dot(Cross(iA, ra), contact.normal);
      float angB = Dot(Cross(iB, rb), contact.normal);
      float denom = a.inv_mass + b.inv_mass + angA + angB;
      contact.effective_mass = (denom > 1e-6f) ? 1.0f / denom : 0.0f;

      Vec3 warmImpulse = contact.normal * contact.cached_normal_impulse;
      Vec3 tangent{};
      if (contact.cached_tangent_impulse != 0.0f)
      {
        Vec3 rv = (b.velocity + Cross(b.angular_velocity, rb)) -
                  (a.velocity + Cross(a.angular_velocity, ra));
        Vec3 tangentCandidate = rv - contact.normal * Dot(rv, contact.normal);
        if (tangentCandidate.LengthSq() > 1e-6f)
        {
          tangent = Normalize(tangentCandidate);
        }
      }
      warmImpulse += tangent * contact.cached_tangent_impulse;
      if (warmImpulse.LengthSq() > 0.0f)
      {
        ApplyImpulse(a, warmImpulse * -1.0f, ra);
        ApplyImpulse(b, warmImpulse, rb);
      }
      contact.normal_impulse_accum = contact.cached_normal_impulse;
      contact.tangent_impulse_accum = contact.cached_tangent_impulse;
    }

    for (int i = 0; i < iterations; ++i)
    {
      for (auto &contact : contacts_)
      {
        ResolveContact(contact, dt);
      }
    }

    contact_cache_scratch_.clear();
    contact_cache_scratch_.reserve(contacts_.size());
    for (const auto &contact : contacts_)
    {
      CachedContact entry{};
      entry.normal = contact.normal;
      entry.normal_impulse = contact.normal_impulse_accum * cacheDecay;
      entry.tangent_impulse = contact.tangent_impulse_accum * cacheDecay;
      contact_cache_scratch_[contact.key] = entry;
    }
    contact_cache_.swap(contact_cache_scratch_);
  }

  void PhysicsWorld::ApplyDistanceConstraints(float dt)
  {
    for (auto &constraint : distance_constraints_)
    {
      auto itA = bodies_.find(constraint.body_a);
      auto itB = bodies_.find(constraint.body_b);
      if (itA == bodies_.end() || itB == bodies_.end())
        continue;
      auto &a = itA->second;
      auto &b = itB->second;

      Vec3 anchorA = a.position + Rotate(a.rotation, constraint.local_a);
      Vec3 anchorB = b.position + Rotate(b.rotation, constraint.local_b);
      Vec3 delta = anchorB - anchorA;
      float length = delta.Length();
      if (length <= 1e-5f)
        continue;
      Vec3 dir = delta / length;
      float stretch = length - constraint.rest_length;
      if (constraint.tension_only && stretch <= 0.0f)
        continue;

      Vec3 velA = a.velocity + Cross(a.angular_velocity, anchorA - a.position);
      Vec3 velB = b.velocity + Cross(b.angular_velocity, anchorB - b.position);
      float relVel = Dot(velB - velA, dir);

      float forceMag = stretch * constraint.stiffness + relVel * constraint.damping;
      if (constraint.tension_only && forceMag < 0.0f)
        forceMag = 0.0f;
      forceMag = std::min(std::max(forceMag, -constraint.max_force), constraint.max_force);
      Vec3 force = dir * forceMag;

      a.force_accum += force;
      a.torque_accum += Cross(anchorA - a.position, force);
      b.force_accum -= force;
      b.torque_accum += Cross(anchorB - b.position, force * -1.0f);

      if (a.is_sleeping || b.is_sleeping)
      {
        a.is_sleeping = false;
        b.is_sleeping = false;
        a.sleep_timer = 0.0f;
        b.sleep_timer = 0.0f;
      }
    }
  }

  void PhysicsWorld::ResolveContact(Contact &contact, float dt)
  {
    auto itA = bodies_.find(contact.a);
    auto itB = bodies_.find(contact.b);
    if (itA == bodies_.end() || itB == bodies_.end())
      return;
    auto &a = itA->second;
    auto &b = itB->second;

    float invMassA = a.inv_mass;
    float invMassB = b.inv_mass;
    if (invMassA + invMassB <= 0.0f)
      return;

    Vec3 ra = contact.point - a.position;
    Vec3 rb = contact.point - b.position;
    Vec3 velA = a.velocity + Cross(a.angular_velocity, ra);
    Vec3 velB = b.velocity + Cross(b.angular_velocity, rb);
    Vec3 rv = velB - velA;

    float velAlongNormal = Dot(rv, contact.normal);
    float j = (contact.desired_velocity - velAlongNormal);
    // Use precomputed effective mass (includes angular inertia)
    j *= contact.effective_mass;
    float newImpulse = contact.normal_impulse_accum + j;
    if (newImpulse < 0.0f)
    {
      j = -contact.normal_impulse_accum;
      contact.normal_impulse_accum = 0.0f;
    }
    else
    {
      contact.normal_impulse_accum = newImpulse;
    }

    Vec3 impulse = contact.normal * j;
    ApplyImpulse(a, impulse * -1.0f, ra);
    ApplyImpulse(b, impulse, rb);

    if (j > 0.0f)
    {
      if (a.is_sleeping)
      {
        a.is_sleeping = false;
        a.sleep_timer = 0.0f;
      }
      if (b.is_sleeping)
      {
        b.is_sleeping = false;
        b.sleep_timer = 0.0f;
      }
    }

    Vec3 tangent = rv - contact.normal * velAlongNormal;
    if (tangent.LengthSq() > 1e-6f)
    {
      tangent = Normalize(tangent);
      float jt = -Dot(rv, tangent);
      jt /= (invMassA + invMassB);
      float maxFriction = contact.normal_impulse_accum * contact.friction;
      float newTangent = contact.tangent_impulse_accum + jt;
      if (newTangent > maxFriction)
        newTangent = maxFriction;
      if (newTangent < -maxFriction)
        newTangent = -maxFriction;
      jt = newTangent - contact.tangent_impulse_accum;
      contact.tangent_impulse_accum = newTangent;
      Vec3 frictionImpulse = tangent * jt;
      ApplyImpulse(a, frictionImpulse * -1.0f, ra);
      ApplyImpulse(b, frictionImpulse, rb);
    }

    float percent = 0.6f;
    float slop = config_.contact_slop;
    float correction = std::max(contact.penetration - slop, 0.0f) / (invMassA + invMassB) * percent;
    Vec3 correctionVec = contact.normal * correction;
    a.position -= correctionVec * invMassA;
    b.position += correctionVec * invMassB;
  }

  bool PhysicsWorld::Raycast(const Vec3 &origin, const Vec3 &direction, float max_distance, RaycastHit &out) const
  {
    Vec3 dir = Normalize(direction);
    if (dir.LengthSq() <= 0.0f)
      return false;
    bool hit_any = false;
    float closest = max_distance;

    for (const auto &kvp : bodies_)
    {
      const auto &body = kvp.second;
      RaycastHit hit{};
      bool hit_body = false;
      if (body.shape == ShapeType::Sphere)
      {
        hit_body = RaycastSphere(origin, dir, closest, body, hit);
      }
      else
      {
        hit_body = RaycastBox(origin, dir, closest, body, hit);
      }
      if (hit_body && hit.distance < closest)
      {
        closest = hit.distance;
        out = hit;
        hit_any = true;
      }
    }

    return hit_any;
  }

  bool PhysicsWorld::RaycastSphere(const Vec3 &origin, const Vec3 &dir, float max_distance,
                                   const RigidBody &body, RaycastHit &out) const
  {
    Vec3 m = origin - body.position;
    float b = Dot(m, dir);
    float c = Dot(m, m) - body.radius * body.radius;
    if (c > 0.0f && b > 0.0f)
      return false;
    float discr = b * b - c;
    if (discr < 0.0f)
      return false;
    float t = -b - std::sqrt(discr);
    if (t < 0.0f)
      t = 0.0f;
    if (t > max_distance)
      return false;

    Vec3 point = origin + dir * t;
    Vec3 normal = Normalize(point - body.position);
    out.body_id = body.id;
    out.point = point;
    out.normal = normal;
    out.distance = t;
    return true;
  }

  bool PhysicsWorld::RaycastBox(const Vec3 &origin, const Vec3 &dir, float max_distance,
                                const RigidBody &body, RaycastHit &out) const
  {
    Vec3 min = body.position - body.half_extents;
    Vec3 max = body.position + body.half_extents;

    float tmin = 0.0f;
    float tmax = max_distance;

    auto check_axis = [&](float start, float direction, float minv, float maxv) -> bool
    {
      if (std::fabs(direction) < 1e-6f)
      {
        return start >= minv && start <= maxv;
      }
      float ood = 1.0f / direction;
      float t1 = (minv - start) * ood;
      float t2 = (maxv - start) * ood;
      if (t1 > t2)
        std::swap(t1, t2);
      if (t1 > tmin)
        tmin = t1;
      if (t2 < tmax)
        tmax = t2;
      return tmin <= tmax;
    };

    if (!check_axis(origin.x, dir.x, min.x, max.x))
      return false;
    if (!check_axis(origin.y, dir.y, min.y, max.y))
      return false;
    if (!check_axis(origin.z, dir.z, min.z, max.z))
      return false;

    float t = tmin >= 0.0f ? tmin : tmax;
    if (t < 0.0f || t > max_distance)
      return false;

    Vec3 point = origin + dir * t;
    Vec3 center = body.position;
    Vec3 local = point - center;
    Vec3 absLocal = AbsVec(local);
    Vec3 normal{};
    float dx = std::fabs(absLocal.x - body.half_extents.x);
    float dy = std::fabs(absLocal.y - body.half_extents.y);
    float dz = std::fabs(absLocal.z - body.half_extents.z);

    if (dx <= dy && dx <= dz)
      normal = {local.x > 0.0f ? 1.0f : -1.0f, 0.0f, 0.0f};
    else if (dy <= dz)
      normal = {0.0f, local.y > 0.0f ? 1.0f : -1.0f, 0.0f};
    else
      normal = {0.0f, 0.0f, local.z > 0.0f ? 1.0f : -1.0f};

    out.body_id = body.id;
    out.point = point;
    out.normal = normal;
    out.distance = t;
    return true;
  }

} // namespace NativeEngine::Physics
