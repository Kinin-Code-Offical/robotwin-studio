using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

// =====================================================================================
//  RobotWin Studio SDK — public API (RobotTwin.CoreSim.Sdk)
//
//  Everything a plugin, a C# script or a test needs to write its own physics:
//    IWorldBehavior / WorldBehavior   code that runs every fixed physics step (1 ms)
//    IForceField / ForceField         a field acting on every (or selected) bodies
//    IWindSource                      adds air velocity (acts through drag and propellers)
//    IPartProvider                    custom circuit parts built from AnalogDevice subclasses
//    SensorDevice                     a ready analog output for custom sensors
//  The interfaces below are the stable surface; anything else in CoreSim may change.
// =====================================================================================

namespace RobotTwin.CoreSim.Sdk
{
    /// <summary>The SDK version plugins are compiled against (manifest "sdkVersion").</summary>
    public static class SdkInfo
    {
        public const int ApiVersion = 1;
        public const string Version = "1.0";
    }

    /// <summary>
    /// User code that runs inside the fixed physics step. Called on the simulation thread, in a
    /// fixed order (attachment order), so results are reproducible. Exceptions are caught per
    /// call, reported in the event log and the behaviour is switched off after repeated errors.
    /// </summary>
    public interface IWorldBehavior
    {
        /// <summary>Once, after the world is built and before the first step.</summary>
        void OnStart(IBehaviorContext ctx);

        /// <summary>Every physics step, before the solver: apply forces/torques here (dt = step, usually 1 ms).</summary>
        void OnStep(IBehaviorContext ctx, double dt);

        /// <summary>When the simulation stops.</summary>
        void OnStop(IBehaviorContext ctx);
    }

    /// <summary>Convenience base class: override what you need; public properties marked [Parameter] are filled from the UI.</summary>
    public abstract class WorldBehavior : IWorldBehavior
    {
        public virtual void OnStart(IBehaviorContext ctx) { }

        public abstract void OnStep(IBehaviorContext ctx, double dt);

        /// <summary>After the solver step (read results, update sensors). Optional.</summary>
        public virtual void OnAfterStep(IBehaviorContext ctx, double dt) { }

        public virtual void OnStop(IBehaviorContext ctx) { }
    }

    /// <summary>Optional: read the solved state after each physics step (sensors, logging).</summary>
    public interface IAfterStep
    {
        void OnAfterStep(IBehaviorContext ctx, double dt);
    }

    /// <summary>Shown in the plugin list and the "add behaviour" menu.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class BehaviorAttribute : Attribute
    {
        public BehaviorAttribute(string name) => Name = name;

        public string Name { get; }
        public string Description { get; set; } = string.Empty;

        /// <summary>"object" (attach to one object; ctx.Self is set) or "world" (one instance for the world).</summary>
        public string AttachTo { get; set; } = "object";
    }

    /// <summary>Marks a public settable property (double, int, bool, string, Vec3 as "x,y,z") as a parameter editable in the UI.</summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ParameterAttribute : Attribute
    {
        public ParameterAttribute(string label = "") => Label = label;

        public string Label { get; }
        public string Unit { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double Min { get; set; } = double.NegativeInfinity;
        public double Max { get; set; } = double.PositiveInfinity;
    }

    /// <summary>What a behaviour or field sees of the running world.</summary>
    public interface IBehaviorContext
    {
        /// <summary>Simulated time (s) at the start of this step.</summary>
        double Time { get; }

        /// <summary>Fixed physics step (s).</summary>
        double Dt { get; }

        long StepIndex { get; }

        /// <summary>The object this behaviour is attached to (null for world behaviours).</summary>
        ISimBody? Self { get; }

        IReadOnlyList<ISimBody> Bodies { get; }

        /// <summary>A body by object id ("O3") or display name ("Kol 2"); a moving port as "O3:horn".</summary>
        ISimBody? Body(string idOrName);

        IReadOnlyList<ISimJoint> Joints { get; }

        ISimJoint? Joint(string id);

        IAirState Air { get; }

        Vec3 Gravity { get; }

        ICircuitAccess Circuit { get; }

        /// <summary>Values set in the UI for this attachment (also bound to [Parameter] properties).</summary>
        IReadOnlyDictionary<string, string> Parameters { get; }

        double Param(string name, double fallback);

        /// <summary>Random stream seeded from the world seed and this attachment (reproducible).</summary>
        SdkRandom Random { get; }

        /// <summary>Publishes a named reading (shown in the inspector, readable by other behaviours, drives "SDK sensor" parts).</summary>
        void Publish(string name, double value);

        /// <summary>Reads a published value (NaN when nothing published it).</summary>
        double Read(string name);

        /// <summary>Casts a ray through the world (bodies, floor). Returns false when nothing is hit within maxDistance.</summary>
        bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, out RayHit hit, ISimBody? ignore = null);

        void Log(string message);

        void Warn(string message);
    }

    public struct RayHit
    {
        public double Distance;
        public Vec3 Point;
        public Vec3 Normal;
        public ISimBody? Body;
    }

    /// <summary>A rigid body of the world. Forces you apply act for the current step only.</summary>
    public interface ISimBody
    {
        /// <summary>World object id ("O3"), or "O3:horn" for a moving port, or "obstacle".</summary>
        string Id { get; }

        string Name { get; }

        /// <summary>The world object this body belongs to (null for obstacles).</summary>
        WorldObject? Object { get; }

        bool IsStatic { get; }

        /// <summary>
        /// [rigidbody] Unity's isKinematic: moved only by <see cref="SetVelocity"/> (never by forces, contacts or joints) and
        /// pushing what it meets. A part fixed in the world cannot be made kinematic.
        /// </summary>
        bool IsKinematic { get; set; }

        double Mass { get; }

        /// <summary>Principal moments of inertia (kg·m²).</summary>
        Vec3 Inertia { get; }

        Vec3 Position { get; }

        Quat Orientation { get; }

        Vec3 Velocity { get; }

        Vec3 AngularVelocity { get; }

        /// <summary>Displaced volume (m³) of the collision shape (0 when unknown).</summary>
        double Volume { get; }

        string Material { get; }

        /// <summary>Drag coefficient × area along the body's x, y, z axes (m²). Set it to change the drag law's input.</summary>
        Vec3 DragArea { get; set; }

        Vec3 LocalToWorld(Vec3 local);

        Vec3 WorldToLocal(Vec3 world);

        /// <summary>Velocity of a world point fixed to the body.</summary>
        Vec3 PointVelocity(Vec3 worldPoint);

        /// <summary>Force through the centre of mass (N, world frame).</summary>
        void ApplyForce(Vec3 force);

        /// <summary>Force at a world point (also produces a torque about the centre of mass).</summary>
        void ApplyForceAtPoint(Vec3 force, Vec3 worldPoint);

        /// <summary>Force in the body frame at a body-frame point.</summary>
        void ApplyLocalForce(Vec3 localForce, Vec3 localPoint);

        /// <summary>Torque (N·m, world frame).</summary>
        void ApplyTorque(Vec3 torque);

        /// <summary>Sum of SDK forces applied to this body in the last step (N), for display.</summary>
        Vec3 LastSdkForce { get; }

        /// <summary>Moves the body (and zeroes nothing else). Use sparingly: teleporting through contacts gives large impulses.</summary>
        void Teleport(Vec3 position, Quat orientation);

        void SetVelocity(Vec3 linear, Vec3 angular);
    }

    /// <summary>A joint of the world (user joints, servo horns and motor shafts).</summary>
    public interface ISimJoint
    {
        string Id { get; }

        JointKind Kind { get; }

        /// <summary>Hinge angle (rad) or slider position (m).</summary>
        double Angle { get; }

        double Speed { get; }

        bool Broken { get; }

        /// <summary>Joint axis in world coordinates.</summary>
        Vec3 Axis { get; }

        /// <summary>Anchor point in world coordinates.</summary>
        Vec3 Anchor { get; }

        /// <summary>Torque (hinge, N·m) or force (slider, N) added by user code this step.</summary>
        double MotorTorque { get; set; }

        JointDrive Drive { get; set; }

        double TargetAngle { get; set; }

        double TargetSpeed { get; set; }

        double Stiffness { get; set; }

        double Damping { get; set; }

        double MaxTorque { get; set; }

        Vec3 ReactionForce { get; }

        Vec3 ReactionTorque { get; }

        ISimBody? Parent { get; }

        ISimBody Child { get; }
    }

    /// <summary>The air at the moment of the call.</summary>
    public interface IAirState
    {
        /// <summary>Air density (kg/m³).</summary>
        double Density { get; }

        double TemperatureC { get; }

        /// <summary>Static pressure at the floor (Pa).</summary>
        double Pressure { get; }

        /// <summary>Wind velocity at a world point (m/s), including fans, vortices and wind expressions.</summary>
        Vec3 WindAt(Vec3 point);
    }

    /// <summary>Read-only view of the circuit, plus direct access to device models for custom sensors.</summary>
    public interface ICircuitAccess
    {
        /// <summary>False when the world has no circuit (physics-only run, tests).</summary>
        bool Available { get; }

        /// <summary>Voltage (V) of an object's pin: a component's pin ("SIG", "V+") or "RefDes.Pin" on a board ("J1.2").</summary>
        double Voltage(string objectIdOrName, string pin);

        /// <summary>Simulator signal by key (device temperatures "R1:T", currents …); NaN when unknown.</summary>
        double Signal(string key);

        /// <summary>Device models of an object's part (servo, motor, sensor …) — set their properties to feed sensors.</summary>
        IReadOnlyList<AnalogDevice> Devices(string objectIdOrName, string? innerRefDes = null);
    }

    /// <summary>
    /// A field that acts on bodies. Called once per body per step, before the solver, for every
    /// body it <see cref="Affects"/>. Return the force (and optional torque) in the world frame.
    /// </summary>
    public interface IForceField
    {
        string Name { get; }

        bool Affects(ISimBody body);

        /// <summary>Force on a body (N, world, through the centre of mass) and torque (N·m).</summary>
        (Vec3 Force, Vec3 Torque) Evaluate(ISimBody body, IBehaviorContext ctx);
    }

    /// <summary>Convenience base for plugin fields (all movable bodies, no torque by default).</summary>
    public abstract class ForceField : IForceField
    {
        public virtual string Name => GetType().Name;

        public virtual bool Affects(ISimBody body) => !body.IsStatic;

        public abstract (Vec3 Force, Vec3 Torque) Evaluate(ISimBody body, IBehaviorContext ctx);

        /// <summary>Called once with the field's UI parameters before the first step.</summary>
        public virtual void Configure(IReadOnlyDictionary<string, string> parameters, ForceFieldSpec spec) { }
    }

    /// <summary>An additional air velocity (fan, thermal, vortex). Acts through drag and propeller inflow.</summary>
    public interface IWindSource
    {
        Vec3 WindAt(Vec3 point, double time);
    }

    /// <summary>Custom circuit parts. Each definition's Build creates <see cref="AnalogDevice"/> subclasses (Stamp/Accept).</summary>
    public interface IPartProvider
    {
        IEnumerable<PartDefinition> Parts();

        IEnumerable<PartPreset> Presets();
    }
}
