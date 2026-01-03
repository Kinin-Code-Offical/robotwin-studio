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
  volatile float currents[MAX_CURRENTS];
  volatile uint32_t error_flags;
  volatile uint64_t tick;
} SharedState;
#pragma pack(pop)

// --- Legacy API ---
UNITY_EXPORT int GetEngineVersion(void);
UNITY_EXPORT const SharedState *GetSharedState(void);
UNITY_EXPORT void SetComponentXY(uint32_t index, uint32_t x, uint32_t y);
UNITY_EXPORT int GetAvrCount(void);
UNITY_EXPORT float GetPinVoltageForAvr(int avrIndex, int pinIndex);

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
UNITY_EXPORT int LoadHexFromText(const char *hexText);
UNITY_EXPORT int LoadBvmFromMemory(const uint8_t *buffer, uint32_t size);
UNITY_EXPORT int LoadBvmFromFile(const char *path);
UNITY_EXPORT int LoadHexForAvr(int index, const char *path);
UNITY_EXPORT int LoadHexTextForAvr(int index, const char *hexText);
UNITY_EXPORT int LoadBvmForAvrMemory(int index, const uint8_t *buffer,
                                     uint32_t size);
UNITY_EXPORT int LoadBvmForAvrFile(int index, const char *path);

// --- Physics Engine API (Draft) ---
typedef struct {
  float base_dt;
  float gravity_x;
  float gravity_y;
  float gravity_z;
  float gravity_jitter;
  float time_jitter;
  float solver_iterations;
  uint64_t noise_seed;
  float contact_slop;
  float restitution;
  float static_friction;
  float dynamic_friction;
  float air_density;
  float wind_x;
  float wind_y;
  float wind_z;
  float ambient_temp_c;
  float rain_intensity;
  float thermal_exchange;
} PhysicsConfig_C;

typedef struct {
  uint32_t id;
  float mass;
  float pos_x, pos_y, pos_z;
  float vel_x, vel_y, vel_z;
  float rot_w, rot_x, rot_y, rot_z;
  float ang_x, ang_y, ang_z;
  float linear_damping;
  float angular_damping;
  float drag_coefficient;
  float cross_section_area;
  float surface_area;
  float temperature_c;
  float material_strength;
  float fracture_toughness;
  float damage;
  int is_broken;
  int is_static;
} RigidBody_C;

UNITY_EXPORT void Physics_CreateWorld();
UNITY_EXPORT void Physics_DestroyWorld();
UNITY_EXPORT void Physics_SetConfig(const PhysicsConfig_C *config);
UNITY_EXPORT uint32_t Physics_AddBody(const RigidBody_C *body);
UNITY_EXPORT int Physics_GetBody(uint32_t id, RigidBody_C *out);
UNITY_EXPORT void Physics_Step(float dt);
UNITY_EXPORT uint32_t Physics_AddVehicle(uint32_t body_id, int wheel_count,
                                         const float *wheel_positions,
                                         const float *wheel_radius,
                                         const float *suspension_rest,
                                         const float *suspension_k,
                                         const float *suspension_damping,
                                         const int *driven_wheels);
UNITY_EXPORT void Physics_SetWheelInput(uint32_t vehicle_id, int wheel_index,
                                        float steer, float drive_torque,
                                        float brake_torque);
UNITY_EXPORT void Physics_SetVehicleAero(uint32_t vehicle_id,
                                         float drag_coefficient,
                                         float downforce);
UNITY_EXPORT void Physics_SetVehicleTireModel(uint32_t vehicle_id, float B,
                                              float C, float D, float E);
UNITY_EXPORT int Physics_ApplyForce(uint32_t body_id, float fx, float fy, float fz);
UNITY_EXPORT int Physics_ApplyForceAtPoint(uint32_t body_id, float fx, float fy, float fz,
                                           float px, float py, float pz);
UNITY_EXPORT int Physics_ApplyTorque(uint32_t body_id, float tx, float ty, float tz);

#ifdef __cplusplus
}
#endif
