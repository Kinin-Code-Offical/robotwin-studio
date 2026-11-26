using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Automation
{
    /// <summary>
    /// [variants-code] A circuit run headless with its boards' code, as the Circuit page runs it: the schematic compiled,
    /// the solver started, each Uno / Nano / ESP32 given its code (the project's own, a file, or an override) with its I²C,
    /// SPI and LED-strip devices, then stepped in lockstep. No mechanics: there is nothing to move.
    /// </summary>
    public sealed class CircuitRunner : SimRunner
    {
        public CircuitRunner(Schematic schematic, ProjectPackage? package = null, RunnerOptions? options = null, double ambientC = 25.0, bool asGiven = false)
            : base(options ?? new RunnerOptions())
        {
            Schematic = schematic ?? throw new ArgumentNullException(nameof(schematic));
            Package = package;
            Ambient = ambientC;
            AsGiven = asGiven;
            Reset();
        }

        public Schematic Schematic { get; }
        public ProjectPackage? Package { get; }
        public double Ambient { get; }

        /// <summary>Run the schematic exactly as given, not its active alternative (a comparison resolves each alternative itself).</summary>
        public bool AsGiven { get; }

        private Schematic Effective => AsGiven ? Schematic : Variants.Active(Schematic);
        public override string Kind => "circuit";
        public override string Title => Schematic.Title;
        protected override double AmbientC => Ambient;

        protected override void Build()
        {
            try
            {
                Compiled = CircuitCompiler.Compile(Effective, Library, new CompileOptions { AmbientC = Ambient });
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                throw new ApiException(ApiException.InvalidParams, "Devre derlenemedi: " + ex.Message);
            }

            foreach (var w in Compiled.Warnings) AddEvent(new RunnerEvent(0, "derleyici", RunnerEventKind.Warning, w));
            Simulator = StartSolver(Compiled.Circuit, Ambient);
            AttachBoards(Effective.Parts, Package);
        }

        protected override IEnumerable<(string FullRef, PartDefinition Def, IReadOnlyDictionary<string, string> Parameters)> AllParts()
        {
            var active = Effective;
            foreach (var p in active.Parts)
            {
                var def = Library.Find(p.PartId);
                if (def != null) yield return (p.RefDes, def, active.ResolveParameters(Library, p));
            }
        }

        protected override void AdvanceMechanics(double dt)
        {
        }

        public override IEnumerable<string> ObjectIds() => Enumerable.Empty<string>();

        public override bool TryGetBody(string objectId, out BodyState state)
        {
            state = default!;
            return false;
        }

        public override bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, string? ignoreObject, out SceneRayHit hit)
        {
            hit = default!;
            return false;
        }

        public override Vec3 Gravity => new Vec3(0, 0, -9.81);

        public override JsonObject Describe()
        {
            var boards = new JsonArray();
            foreach (var b in Boards) boards.Add(new JsonObject { ["refdes"] = b.RefDes, ["mcu"] = b.Mcu, ["firmware"] = b.Firmware });
            return new JsonObject { ["kind"] = Kind, ["title"] = Title, ["boards"] = boards };
        }

        public override JsonArray ListActuators() => new JsonArray();

        public override JsonObject SetActuator(string name, JsonObject p) => throw new ApiException(ApiException.Unsupported, "Devrede sürülecek mekanik yok.");

        public override void ReleaseActuator(string name)
        {
        }

        public override JsonObject DriveDifferential(double v, double w, JsonObject p) => throw new ApiException(ApiException.Unsupported, "Devrede araç yok.");

        public override void SetPose(string objectId, Vec3? position, Quat? orientation, bool zeroVelocity)
        {
        }

        public override void Randomize(NoiseSource noise, double positionSigma, double yawSigmaRad)
        {
        }
    }
}
