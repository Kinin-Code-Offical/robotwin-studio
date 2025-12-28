#pragma once
#include <stdint.h>

#ifdef _WIN32
#define UNITY_EXPORT __declspec(dllexport)
#else
#define UNITY_EXPORT
#endif

#ifdef __cplusplus
extern "C" {
#endif

// --- Legacy Constants (Deprecating) ---
enum { MAX_COMPONENTS = 3, MAX_NODES = 4, MAX_CURRENTS = 2, MAX_PINS = 20 };

// --- Legacy Structs (Deprecating) ---
#pragma pack(push, 4)
typedef struct {
  volatile uint32_t component_positions[MAX_COMPONENTS][2];
  volatile float node_voltages[MAX_NODES];
  volatile float pin_voltages[MAX_PINS];
  volatile float currents[MAX_CURRENTS];
  volatile uint32_t error_flags;
  volatile uint64_t tick;
} SharedState;
#pragma pack(pop)

// --- Legacy API ---
UNITY_EXPORT int GetEngineVersion(void);
UNITY_EXPORT const SharedState *GetSharedState(void);

// --- New Generic Circuit API ---
UNITY_EXPORT void Native_CreateContext();
UNITY_EXPORT void Native_DestroyContext();
UNITY_EXPORT int Native_AddNode();
UNITY_EXPORT int Native_AddComponent(int type, int paramCount, float *params);
// Component Types: Resistor=0, VoltageSource=1
UNITY_EXPORT void Native_Connect(int compId, int pinIndex, int nodeId);
UNITY_EXPORT void Native_Step(float dt);
UNITY_EXPORT float Native_GetVoltage(int nodeId);
UNITY_EXPORT int LoadHexFromFile(const char *path);

#ifdef __cplusplus
}
#endif
