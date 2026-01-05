using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RobotTwin.Core
{
    /// <summary>
    /// Bridge to the High-Performance C++ NativeEngine.
    /// Handles P/Invoke calls for generic circuit simulation.
    /// </summary>
    public static class NativeBridge
    {
        private const string PLUGIN_NAME = "NativeEngine";

        // --- Generic Circuit API ---

        [DllImport(PLUGIN_NAME, EntryPoint = "Native_CreateContext")]
        public static extern void Native_CreateContext();

        [DllImport(PLUGIN_NAME, EntryPoint = "Native_DestroyContext")]
        public static extern void Native_DestroyContext();

        [DllImport(PLUGIN_NAME, EntryPoint = "Native_AddNode")]
        public static extern int Native_AddNode();

        // params is a reserved keyword, using parameters
        [DllImport(PLUGIN_NAME, EntryPoint = "Native_AddComponent")]
        public static extern int Native_AddComponent(int type, int paramCount, [In] float[] parameters);

        [DllImport(PLUGIN_NAME, EntryPoint = "Native_Connect")]
        public static extern void Native_Connect(int compId, int pinIndex, int nodeId);

        [DllImport(PLUGIN_NAME, EntryPoint = "Native_Step")]
        public static extern void Native_Step(float dt);

        [DllImport(PLUGIN_NAME, EntryPoint = "Native_GetVoltage")]
        public static extern float Native_GetVoltage(int nodeId);

        [DllImport(PLUGIN_NAME, EntryPoint = "LoadHexFromFile")]
        public static extern int LoadHexFromFile(string path);

        // --- Physics Engine API ---

        [StructLayout(LayoutKind.Sequential)]
        public struct PhysicsConfig
        {
            public float base_dt;
            public float gravity_x;
            public float gravity_y;
            public float gravity_z;
            public float gravity_jitter;
            public float time_jitter;
            public float solver_iterations;
            public ulong noise_seed;
            public float contact_slop;
            public float restitution;
            public float static_friction;
            public float dynamic_friction;
            public float air_density;
            public float wind_x;
            public float wind_y;
            public float wind_z;
            public float ambient_temp_c;
            public float rain_intensity;
            public float thermal_exchange;
            public float sleep_linear_threshold;
            public float sleep_angular_threshold;
            public float sleep_time;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RigidBody
        {
            public uint id;
            public float mass;
            public float pos_x;
            public float pos_y;
            public float pos_z;
            public float vel_x;
            public float vel_y;
            public float vel_z;
            public float rot_w;
            public float rot_x;
            public float rot_y;
            public float rot_z;
            public float ang_x;
            public float ang_y;
            public float ang_z;
            public float linear_damping;
            public float angular_damping;
            public float drag_coefficient;
            public float cross_section_area;
            public float surface_area;
            public float temperature_c;
            public float material_strength;
            public float fracture_toughness;
            public int shape_type;
            public float radius;
            public float half_x;
            public float half_y;
            public float half_z;
            public float friction;
            public float restitution;
            public float damage;
            public int is_broken;
            public int is_static;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RaycastHit
        {
            public uint body_id;
            public float hit_x;
            public float hit_y;
            public float hit_z;
            public float normal_x;
            public float normal_y;
            public float normal_z;
            public float distance;
        }

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_CreateWorld")]
        public static extern void Physics_CreateWorld();

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_DestroyWorld")]
        public static extern void Physics_DestroyWorld();

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_SetConfig")]
        public static extern void Physics_SetConfig(ref PhysicsConfig config);

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_AddBody")]
        public static extern uint Physics_AddBody(ref RigidBody body);

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_GetBody")]
        public static extern int Physics_GetBody(uint id, out RigidBody body);

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_Step")]
        public static extern void Physics_Step(float dt);

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_AddVehicle")]
        public static extern uint Physics_AddVehicle(uint body_id, int wheel_count,
            [In] float[] wheel_positions, [In] float[] wheel_radius, [In] float[] suspension_rest,
            [In] float[] suspension_k, [In] float[] suspension_damping, [In] int[] driven_wheels);

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_SetWheelInput")]
        public static extern void Physics_SetWheelInput(uint vehicle_id, int wheel_index, float steer, float drive_torque, float brake_torque);

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_SetVehicleAero")]
        public static extern void Physics_SetVehicleAero(uint vehicle_id, float drag_coefficient, float downforce);

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_SetVehicleTireModel")]
        public static extern void Physics_SetVehicleTireModel(uint vehicle_id, float B, float C, float D, float E);

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_ApplyForce")]
        public static extern int Physics_ApplyForce(uint body_id, float fx, float fy, float fz);

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_ApplyForceAtPoint")]
        public static extern int Physics_ApplyForceAtPoint(uint body_id, float fx, float fy, float fz, float px, float py, float pz);

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_ApplyTorque")]
        public static extern int Physics_ApplyTorque(uint body_id, float tx, float ty, float tz);

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_AddDistanceConstraint")]
        public static extern uint Physics_AddDistanceConstraint(uint body_a, uint body_b,
            float ax, float ay, float az, float bx, float by, float bz,
            float rest_length, float stiffness, float damping, float max_force, int tension_only);

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_Raycast")]
        public static extern int Physics_Raycast(float ox, float oy, float oz,
            float dx, float dy, float dz, float max_distance, out RaycastHit hit);

        // --- Legacy / Helper API ---

        [DllImport(PLUGIN_NAME, EntryPoint = "GetEngineVersion")]
        public static extern int GetEngineVersion();

        // Legacy Wrapper
        public static int GetVersion() => GetEngineVersion();
        public static void StepSimulation(float dt) => Native_Step(dt); // Wrapper for old StepSimulation


        // Enum mirroring Circuit/CircuitComponent.h
        public enum ComponentType
        {
            Resistor = 0,
            VoltageSource = 1,
            Ground = 2,
            Diode = 3,
            LED = 4,
            Switch = 5,
            IC_Pin = 6
        }
    }
}
