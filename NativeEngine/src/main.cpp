/*
 * RoboTwin NativeEngine
 * Optimized C++ Core for Circuit Solving and Physics
 */

#include <cmath>
#include <cstdio>
#include <fstream>

static std::ofstream logFile;

void Log(const char *msg) {
  if (!logFile.is_open()) {
    // Simple relative path - might end up in Unity executable dir or Project
    // dir
    logFile.open("logs/native_engine.log", std::ios::out | std::ios::app);
  }
  if (logFile.is_open()) {
    logFile << msg << std::endl;
  }
}

// Macro to export symbols for DLL/SO
#if defined(_MSC_VER) || defined(_WIN32) || defined(__CYGWIN__)
#define EXPORT __declspec(dllexport)
#else
#define EXPORT
#endif

extern "C" {

/*
 * Steps the heavy simulation logic.
 * dt: Delta Time in seconds
 */
EXPORT void StepSimulation(float dt) {
  // Mock heavy computation: Matrix ops, Solver iterations
  static float internal_time = 0.0f;
  internal_time += dt;

  static int frame = 0;
  frame++;
  if (frame % 60 == 0) {
    char buffer[64];
    sprintf(buffer, "Native Tick: %f", internal_time);
    Log(buffer);
  }
  // Check connectivity by printing occasionally? Or mostly just run.
}

EXPORT int GetEngineVersion() {
  return 100; // v1.0.0
}

/*
 * OHM'S LAW SOLVER (Phase 3 Requirement)
 * Calculates Current (I) = V / R
 */
EXPORT float CalculateCurrent(float voltage, float resistance) {
  if (std::abs(resistance) < 1e-6f) {
    return 0.0f; // Short circuit protection / limit
  }
  return voltage / resistance;
}
}
