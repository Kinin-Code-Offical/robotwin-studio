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

        // In a real engine, this would call into the Spice Solver or Physics World
        // For now, we print to stdout to prove the link (Unity captures stdout in Editor)
        // fprintf(stdout, "[NativeEngine] Stepping Simulation... T=%.4f\n", internal_time);
    }

    EXPORT int GetEngineVersion() {
        return 100; // v1.0.0
    }
}
