#pragma once
#include <cstdint>

namespace NativeEngine::Physics {

class DeterministicRng {
 public:
  explicit DeterministicRng(std::uint64_t seed = 0x9E3779B97F4A7C15ULL)
      : state_(seed) {}

  void Seed(std::uint64_t seed) { state_ = seed; }

  std::uint32_t NextU32() {
    // SplitMix64
    std::uint64_t z = (state_ += 0x9E3779B97F4A7C15ULL);
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ULL;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EBULL;
    return static_cast<std::uint32_t>(z ^ (z >> 31));
  }

  float NextFloat01() {
    return (NextU32() & 0x00FFFFFF) / static_cast<float>(0x01000000);
  }

  float NextFloatSigned() { return NextFloat01() * 2.0f - 1.0f; }

 private:
  std::uint64_t state_;
};

}  // namespace NativeEngine::Physics
