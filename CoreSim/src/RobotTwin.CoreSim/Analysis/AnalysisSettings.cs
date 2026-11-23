using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Analysis
{
    /// <summary>How finely the offline run integrates (much finer than the real-time mode).</summary>
    public enum AccuracyPreset
    {
        /// <summary>Real-time settings (1 ms physics, analog 0.2 ms, rel. tol 1e-3) — for previews.</summary>
        Fast,

        /// <summary>0.5 ms physics, analog 0.1 ms, tol 5e-4.</summary>
        Standard,

        /// <summary>0.1 ms physics, 24 solver passes, analog 20 µs, tol 1e-4, no sleeping.</summary>
        Fine,

        /// <summary>50 µs physics, 32 passes, analog 10 µs, tol 1e-5.</summary>
        Reference
    }

    /// <summary>What the analysis computes in addition to the recorded time series.</summary>
    public enum AnalysisPreset
    {
        Stress,
        Thermal,
        Aero,
        Full,
        Custom
    }

    /// <summary>[analysis] Everything that defines an offline analysis run (saved with the results).</summary>
    public sealed class AnalysisSettings
    {
        public AnalysisPreset Preset { get; set; } = AnalysisPreset.Full;

        /// <summary>Simulated duration (s).</summary>
        public double Duration { get; set; } = 2.0;

        /// <summary>Recorded samples per second.</summary>
        public double OutputRate { get; set; } = 500;

        public AccuracyPreset Accuracy { get; set; } = AccuracyPreset.Fine;
        public int Seed { get; set; }
        public bool Firmware { get; set; } = true;

        /// <summary>[replay] Inputs of a recorded live run (controller reports, serial, switches …), applied at their recorded times.</summary>
        public RobotTwin.CoreSim.Automation.InputJournal? Inputs { get; set; }

        public bool RecordBodies { get; set; } = true;
        public bool RecordJoints { get; set; } = true;
        public bool RecordCircuit { get; set; } = true;
        public bool RecordNodes { get; set; } = true;

        // ------------------------------------------------ structural
        public bool Stress { get; set; }

        /// <summary>World object ids to analyse (empty = every movable body).</summary>
        public List<string> StressObjects { get; set; } = new List<string>();

        /// <summary>"auto" (beams for slender links), "tet" or "beam".</summary>
        public string StressMethod { get; set; } = "auto";

        /// <summary>Element size (m); 0 = automatic from the target cell count.</summary>
        public double StressElementSize { get; set; }

        /// <summary>Target number of grid cells per part when the size is automatic (each → 6 tetrahedra).</summary>
        public int StressTargetCells { get; set; } = 1500;

        /// <summary>Number of time instants solved per part (half evenly spaced, half at load peaks).</summary>
        public int StressInstants { get; set; } = 12;

        // ------------------------------------------------ thermal
        public bool Thermal { get; set; }

        /// <summary>Boards / components to solve spatially (empty = all boards and powered parts).</summary>
        public List<string> ThermalObjects { get; set; } = new List<string>();

        /// <summary>Grid cell size (m).</summary>
        public double ThermalCell { get; set; } = 0.002;

        /// <summary>Natural-convection coefficient (W/m²K).</summary>
        public double ConvectionH { get; set; } = 10;

        /// <summary>Extra time (s) held at the run's mean power after the recording, to approach steady state.</summary>
        public double ThermalExtend { get; set; }

        public int ThermalFrames { get; set; } = 40;

        // ------------------------------------------------ aerodynamics
        public bool Aero { get; set; }
        public string? AeroObject { get; set; }

        /// <summary>Free-stream air speed relative to the body (m/s).</summary>
        public double AeroSpeed { get; set; } = 2.0;

        /// <summary>"+X", "-X", "+Y", "-Y", "+Z", "-Z": direction the body moves (air flows opposite).</summary>
        public string AeroAxis { get; set; } = "+X";

        public bool Aero3D { get; set; }

        /// <summary>
        /// Extra runs with every part's values drawn from its tolerance band (clones from their wider
        /// spread): the result keeps the min–max envelope of every channel, so a reading shows how far
        /// the real hardware may be from the model. 0 = off.
        /// </summary>
        public int UncertaintyRuns { get; set; }

        /// <summary>Re-solve each part's critical instant on a finer mesh and report how much the stress moved.</summary>
        public bool StressMeshCheck { get; set; }

        /// <summary>Lattice cells across the body's frontal size.</summary>
        public int AeroResolution { get; set; } = 32;

        /// <summary>Simulated flow length in body lengths (how far the air travels past the body).</summary>
        public double AeroPasses { get; set; } = 30;

        public double AeroSmagorinsky { get; set; } = 0.17;

        public (double PhysicsStep, double MaxSubstep, int Iterations, double AnalogMaxStep, double RelTol, bool Sleep) AccuracyValues => Accuracy switch
        {
            AccuracyPreset.Fast => (1e-3, 5e-4, 16, 2e-4, 1e-3, true),
            AccuracyPreset.Standard => (5e-4, 2.5e-4, 20, 1e-4, 5e-4, false),
            AccuracyPreset.Reference => (5e-5, 5e-5, 32, 1e-5, 1e-5, false),
            _ => (1e-4, 1e-4, 24, 2e-5, 1e-4, false)
        };

        public int SampleCount => Math.Max(1, (int)Math.Ceiling(Duration * OutputRate - 1e-9));

        /// <summary>Settings of one of the named presets ("Gerilme", "Isı", "Aerodinamik", "Tam analiz") for a world.</summary>
        public static AnalysisSettings ForPreset(AnalysisPreset preset, WorldDocument? world)
        {
            var s = new AnalysisSettings { Preset = preset };
            var movable = world?.Objects.Where(o => !o.IsStatic && o.Kind != WorldObjectKind.Board).ToList() ?? new List<WorldObject>();
            switch (preset)
            {
                case AnalysisPreset.Stress:
                    s.Stress = true;
                    s.Duration = 2;
                    s.OutputRate = 1000;
                    s.StressObjects = movable.Where(o => o.Kind == WorldObjectKind.Body || o.Kind == WorldObjectKind.Model).Select(o => o.Id).Take(4).ToList();
                    break;
                case AnalysisPreset.Thermal:
                    s.Thermal = true;
                    s.Duration = 10;
                    s.OutputRate = 100;
                    s.Accuracy = AccuracyPreset.Standard;
                    s.ThermalExtend = 120;
                    break;
                case AnalysisPreset.Aero:
                    s.Aero = true;
                    s.Duration = 1;
                    s.OutputRate = 200;
                    s.Accuracy = AccuracyPreset.Standard;
                    s.AeroObject = (movable.FirstOrDefault(o => o.Kind == WorldObjectKind.Body || o.Kind == WorldObjectKind.Model) ?? movable.FirstOrDefault())?.Id;
                    break;
                case AnalysisPreset.Full:
                    s.Stress = s.Thermal = s.Aero = true;
                    s.Duration = 3;
                    s.OutputRate = 1000;
                    s.ThermalExtend = 60;
                    s.StressObjects = movable.Where(o => o.Kind == WorldObjectKind.Body || o.Kind == WorldObjectKind.Model).Select(o => o.Id).Take(3).ToList();
                    s.AeroObject = (movable.FirstOrDefault(o => o.Kind == WorldObjectKind.Body || o.Kind == WorldObjectKind.Model) ?? movable.FirstOrDefault())?.Id;
                    break;
            }

            return s;
        }

        public static string PresetName(AnalysisPreset p) => p switch
        {
            AnalysisPreset.Stress => "Gerilme",
            AnalysisPreset.Thermal => "Isı",
            AnalysisPreset.Aero => "Aerodinamik",
            AnalysisPreset.Full => "Tam analiz",
            _ => "Özel"
        };

        public static string AccuracyName(AccuracyPreset a) => a switch
        {
            AccuracyPreset.Fast => "Hızlı (gerçek zaman ayarları)",
            AccuracyPreset.Standard => "Standart (0,5 ms)",
            AccuracyPreset.Fine => "Hassas (0,1 ms)",
            _ => "Referans (50 µs)"
        };

        public AnalysisSettings Clone()
        {
            var c = FromJson(ToJson());
            c.Inputs = Inputs;   // [replay] shared, read-only during a run
            return c;
        }

        public JsonObject ToJson()
        {
            static JsonArray Strs(IEnumerable<string> list) => new JsonArray(list.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
            return new JsonObject
            {
                ["preset"] = Preset.ToString(),
                ["duration"] = Duration,
                ["output_rate"] = OutputRate,
                ["accuracy"] = Accuracy.ToString(),
                ["seed"] = Seed,
                ["firmware"] = Firmware,
                ["record_bodies"] = RecordBodies,
                ["record_joints"] = RecordJoints,
                ["record_circuit"] = RecordCircuit,
                ["record_nodes"] = RecordNodes,
                ["stress"] = Stress,
                ["stress_objects"] = Strs(StressObjects),
                ["stress_method"] = StressMethod,
                ["stress_element_size"] = StressElementSize,
                ["stress_target_cells"] = StressTargetCells,
                ["stress_instants"] = StressInstants,
                ["thermal"] = Thermal,
                ["thermal_objects"] = Strs(ThermalObjects),
                ["thermal_cell"] = ThermalCell,
                ["convection_h"] = ConvectionH,
                ["thermal_extend"] = ThermalExtend,
                ["thermal_frames"] = ThermalFrames,
                ["aero"] = Aero,
                ["aero_object"] = AeroObject,
                ["aero_speed"] = AeroSpeed,
                ["aero_axis"] = AeroAxis,
                ["aero_3d"] = Aero3D,
                ["aero_resolution"] = AeroResolution,
                ["aero_passes"] = AeroPasses,
                ["aero_smagorinsky"] = AeroSmagorinsky,
                ["uncertainty_runs"] = UncertaintyRuns,
                ["stress_mesh_check"] = StressMeshCheck
            };
        }

        public static AnalysisSettings FromJson(JsonObject j)
        {
            double D(string k, double d) => j[k] is JsonValue v && v.TryGetValue(out double x) ? x : d;
            int I(string k, int d) => j[k] is JsonValue v && v.TryGetValue(out int x) ? x : d;
            bool B(string k, bool d) => j[k] is JsonValue v && v.TryGetValue(out bool x) ? x : d;
            string? S(string k) => j[k] is JsonValue v && v.TryGetValue(out string? x) ? x : null;
            List<string> L(string k) => j[k] is JsonArray a ? a.Select(n => n?.GetValue<string>() ?? string.Empty).Where(x => x.Length > 0).ToList() : new List<string>();
            var s = new AnalysisSettings
            {
                Preset = Enum.TryParse<AnalysisPreset>(S("preset"), out var p) ? p : AnalysisPreset.Custom,
                Duration = D("duration", 2),
                OutputRate = D("output_rate", 500),
                Accuracy = Enum.TryParse<AccuracyPreset>(S("accuracy"), out var a) ? a : AccuracyPreset.Fine,
                Seed = I("seed", 0),
                Firmware = B("firmware", true),
                RecordBodies = B("record_bodies", true),
                RecordJoints = B("record_joints", true),
                RecordCircuit = B("record_circuit", true),
                RecordNodes = B("record_nodes", true),
                Stress = B("stress", false),
                StressObjects = L("stress_objects"),
                StressMethod = S("stress_method") ?? "auto",
                StressElementSize = D("stress_element_size", 0),
                StressTargetCells = I("stress_target_cells", 1500),
                StressInstants = I("stress_instants", 12),
                Thermal = B("thermal", false),
                ThermalObjects = L("thermal_objects"),
                ThermalCell = D("thermal_cell", 0.002),
                ConvectionH = D("convection_h", 10),
                ThermalExtend = D("thermal_extend", 0),
                ThermalFrames = I("thermal_frames", 40),
                Aero = B("aero", false),
                AeroObject = S("aero_object"),
                AeroSpeed = D("aero_speed", 2),
                AeroAxis = S("aero_axis") ?? "+X",
                Aero3D = B("aero_3d", false),
                AeroResolution = I("aero_resolution", 32),
                AeroPasses = D("aero_passes", 30),
                AeroSmagorinsky = D("aero_smagorinsky", 0.17),
                UncertaintyRuns = I("uncertainty_runs", 0),
                StressMeshCheck = B("stress_mesh_check", false)
            };
            return s;
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.###} s @ {2:0} Hz, {3}", PresetName(Preset), Duration, OutputRate, AccuracyName(Accuracy));
    }
}
