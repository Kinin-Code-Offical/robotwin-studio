#pragma once
#include <cmath>

namespace NativeEngine::Physics {

struct Vec3 {
  float x{0.0f};
  float y{0.0f};
  float z{0.0f};

  Vec3() = default;
  Vec3(float xIn, float yIn, float zIn) : x(xIn), y(yIn), z(zIn) {}

  Vec3 operator+(const Vec3 &rhs) const { return {x + rhs.x, y + rhs.y, z + rhs.z}; }
  Vec3 operator-(const Vec3 &rhs) const { return {x - rhs.x, y - rhs.y, z - rhs.z}; }
  Vec3 operator*(float s) const { return {x * s, y * s, z * s}; }
  Vec3 operator/(float s) const { return {x / s, y / s, z / s}; }

  Vec3 &operator+=(const Vec3 &rhs) {
    x += rhs.x; y += rhs.y; z += rhs.z;
    return *this;
  }

  Vec3 &operator-=(const Vec3 &rhs) {
    x -= rhs.x; y -= rhs.y; z -= rhs.z;
    return *this;
  }

  Vec3 &operator*=(float s) {
    x *= s; y *= s; z *= s;
    return *this;
  }

  float Length() const { return std::sqrt(x * x + y * y + z * z); }
  float LengthSq() const { return x * x + y * y + z * z; }
};

inline float Dot(const Vec3 &a, const Vec3 &b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
inline Vec3 Cross(const Vec3 &a, const Vec3 &b) {
  return {a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x};
}
inline Vec3 Hadamard(const Vec3 &a, const Vec3 &b) { return {a.x * b.x, a.y * b.y, a.z * b.z}; }
inline Vec3 AbsVec(const Vec3 &v) { return {std::fabs(v.x), std::fabs(v.y), std::fabs(v.z)}; }
inline Vec3 Normalize(const Vec3 &v) {
  float len = v.Length();
  if (len <= 1e-6f) return {0.0f, 0.0f, 0.0f};
  return v / len;
}

struct Quat {
  float w{1.0f};
  float x{0.0f};
  float y{0.0f};
  float z{0.0f};

  static Quat Identity() { return {}; }

  static Quat FromAxisAngle(const Vec3 &axis, float radians) {
    Vec3 n = Normalize(axis);
    float half = radians * 0.5f;
    float s = std::sin(half);
    return {std::cos(half), n.x * s, n.y * s, n.z * s};
  }

  Quat operator*(const Quat &rhs) const {
    return {
        w * rhs.w - x * rhs.x - y * rhs.y - z * rhs.z,
        w * rhs.x + x * rhs.w + y * rhs.z - z * rhs.y,
        w * rhs.y - x * rhs.z + y * rhs.w + z * rhs.x,
        w * rhs.z + x * rhs.y - y * rhs.x + z * rhs.w};
  }
};

inline Vec3 Rotate(const Quat &q, const Vec3 &v) {
  Quat vq{0.0f, v.x, v.y, v.z};
  Quat iq{q.w, -q.x, -q.y, -q.z};
  Quat rq = q * vq * iq;
  return {rq.x, rq.y, rq.z};
}

}  // namespace NativeEngine::Physics
