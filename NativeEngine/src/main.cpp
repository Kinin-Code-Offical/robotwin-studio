/*
 * RoboTwin NativeEngine
 * Optimized C++ Core for Circuit Solving and Physics
 */

#include <cmath>
#include <cstdio>

// Macro to export symbols for DLL/SO
#if defined(_MSC_VER)
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
