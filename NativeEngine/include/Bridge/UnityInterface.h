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

enum
{
    MAX_COMPONENTS = 3,
    MAX_NODES = 4,
    MAX_CURRENTS = 2
};

#pragma pack(push, 4)
typedef struct
{
    volatile uint32_t component_positions[MAX_COMPONENTS][2];
    volatile float node_voltages[MAX_NODES];
    volatile float currents[MAX_CURRENTS];
    volatile uint32_t error_flags;
    volatile uint32_t tick;
} SharedState;
#pragma pack(pop)

UNITY_EXPORT void StepSimulation(float dt);
UNITY_EXPORT int GetEngineVersion(void);
UNITY_EXPORT float CalculateCurrent(float voltage, float resistance);
UNITY_EXPORT const SharedState* GetSharedState(void);
UNITY_EXPORT void SetComponentXY(uint32_t index, uint32_t x, uint32_t y);
UNITY_EXPORT int LoadHexFromText(const char* text);
UNITY_EXPORT int LoadHexFromFile(const char* path);

#ifdef __cplusplus
}
#endif
