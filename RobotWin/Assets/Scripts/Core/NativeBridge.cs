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

        [DllImport(PLUGIN_NAME, EntryPoint = "Physics_SetBody")]
        public static extern int Physics_SetBody(uint id, ref RigidBody body);

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

        [DllImport(PLUGIN_NAME, EntryPoint = "GetPinVoltageForAvr")]
        public static extern float GetPinVoltageForAvr(int avrIndex, int pinIndex);

        [DllImport(PLUGIN_NAME, EntryPoint = "SetAnalogVoltageForAvr")]
        public static extern int SetAnalogVoltageForAvr(int avrIndex, int pinIndex, float voltage);

        // Legacy Wrapper
        public static int GetVersion() => GetEngineVersion();
        public static void StepSimulation(float dt) => Native_Step(dt); // Wrapper for old StepSimulation

        // --- God Tier Helpers ---
        /// <summary>
        /// Gets the digital state (HIGH/LOW) of a pin on the first AVR.
        /// Threshold is 2.5V.
        /// </summary>
        public static int GetPinState(int pin)
        {
            // Assuming AvrIndex 0 for single MCU robot
            float v = GetPinVoltageForAvr(0, pin);
            return v > 2.5f ? 1 : 0;
        }

        public static float GetPinVoltage(int pin)
        {
            return GetPinVoltageForAvr(0, pin);
        }

        public static int GetPinPwm(int pin)
        {
            // Pwm is just voltage averaged over time or instant check?
            // If simulation is stepped fine enough, we catch the high/low.
            // But for 'GetPinPwm', we likely want the Duty Cycle.
            // NativeEngine might not expose Duty Cycle directly yet.
            // Fallback: Return raw voltage scaled to 0-255
            float v = GetPinVoltage(pin);
            return (int)((v / 5.0f) * 255f);
        }

        public static float GetPinServo(int pin)
        {
            // Servo signal is PWM. Angle depends on pulse width.
            // Mock: Voltage -> Angle (0-180)
            float v = GetPinVoltage(pin);
            return (v / 5.0f) * 180f;
        }

        public static void SetAnalogInput(int pin, int value)
        {
            // Map 0-1023 to 0-5V and drive the circuit node connected to this AVR pin with a
            // finite-impedance Norton driver (native-side).
            int clamped = Mathf.Clamp(value, 0, 1023);
            float voltage = (clamped / 1023.0f) * 5.0f;
            SetAnalogVoltageForAvr(0, pin, voltage);
        }

        // Instance Compatibility (Mock Singleton for legacy code)
        public static class Instance
        {
            public static int GetPinState(int pin) => NativeBridge.GetPinState(pin);
            public static float GetPinVoltage(int pin) => NativeBridge.GetPinVoltage(pin);
            public static int GetPinPwm(int pin) => NativeBridge.GetPinPwm(pin);
            public static float GetPinServo(int pin) => NativeBridge.GetPinServo(pin);
            public static void SetAnalogInput(int pin, int value) => NativeBridge.SetAnalogInput(pin, value);
        }

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
