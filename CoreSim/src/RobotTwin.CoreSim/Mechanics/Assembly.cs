using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Mechanics
{
    public enum VehicleKind
    {
        None,

        /// <summary>Two driven wheels plus a front ball caster (differential drive robot car).</summary>
        Rover,

        /// <summary>Quadcopter in X layout.</summary>
        Quadcopter
    }

    public enum SensorKind
    {
        /// <summary>Downward IR reflectance (TCRT5000 / line sensor).</summary>
        Line,

        /// <summary>Forward IR proximity switch.</summary>
        Obstacle,

        /// <summary>HC-SR04 time-of-flight ranger.</summary>
        Ultrasonic,

        /// <summary>Accelerometer / gyroscope.</summary>
        Imu
    }

    /// <summary>A motor of the schematic attached to a wheel or propeller slot.</summary>
    public sealed class MotorMount
    {
        public string RefDes { get; set; } = string.Empty;

        /// <summary>"L" / "R" for a rover, "M1".."M4" for a quadcopter.</summary>
        public string Role { get; set; } = string.Empty;

        /// <summary>Mounted the other way round: positive motor rotation drives backwards.</summary>
        public bool Reverse { get; set; }

        /// <summary>Gearbox efficiency override (0..1); null = typical for the ratio (≈0.9 per 4:1 plastic stage).</summary>
        public double? GearEfficiency { get; set; }

        /// <summary>Gear backlash at the output shaft in degrees; null = typical (2° plastic gearhead, 0 direct drive).</summary>
        public double? BacklashDeg { get; set; }
    }

    /// <summary>
    /// A rigid part attached to the chassis (or to another link) by a joint: robot arm
    /// segment, gripper finger, flap, lifting fork. Its box extends from the joint along the
    /// link's +x axis; at rest the link frame is parallel to the chassis frame. A servo of the
    /// schematic can drive a hinge.
    /// </summary>
    public sealed class LinkMount
    {
        public string Name { get; set; } = "kol";

        /// <summary>Name of the parent link; empty = the chassis.</summary>
        public string Parent { get; set; } = string.Empty;

        public JointKind Joint { get; set; } = JointKind.Hinge;

        /// <summary>Joint position in the parent frame (chassis frame for the chassis, metres).</summary>
        public double X { get; set; }

        public double Y { get; set; }
        public double Z { get; set; }

        /// <summary>Joint axis in the parent frame (default: pitch about y).</summary>
        public double AxisX { get; set; }

        public double AxisY { get; set; } = 1;
        public double AxisZ { get; set; }

        public double Length { get; set; } = 0.08;
        public double Width { get; set; } = 0.02;
        public double Thickness { get; set; } = 0.006;

        /// <summary>Mass in kg; 0 = from the material density and the box volume.</summary>
        public double Mass { get; set; }

        public string Material { get; set; } = "PLA";

        /// <summary>Joint limits (degrees for a hinge, metres for a slider).</summary>
        public double? Lower { get; set; }

        public double? Upper { get; set; }

        /// <summary>Servo of the schematic driving this joint (empty = passive).</summary>
        public string ServoRefDes { get; set; } = string.Empty;

        /// <summary>Servo angle (degrees) at which the joint angle is zero.</summary>
        public double ServoOffsetDeg { get; set; } = 90;

        /// <summary>+1, or −1 for a mirrored finger driven by the same servo.</summary>
        public double ServoScale { get; set; } = 1;

        /// <summary>Joint friction torque (N·m).</summary>
        public double Friction { get; set; }

        /// <summary>The joint snaps above this force (N) / torque (N·m); null = from the link's material strength.</summary>
        public double? BreakForce { get; set; }

        public double? BreakTorque { get; set; }
    }

    /// <summary>A sensor of the schematic placed on the vehicle (body frame, metres, yaw in degrees).</summary>
    public sealed class SensorMount
    {
        public string RefDes { get; set; } = string.Empty;
        public SensorKind Kind { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Yaw { get; set; }

        /// <summary>Switching distance of a proximity sensor (m).</summary>
        public double Range { get; set; } = 0.12;
    }

    /// <summary>A board, battery or module placed on the chassis deck (body frame, metres).</summary>
    public sealed class ModuleMount
    {
        public string RefDes { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public int Rotation { get; set; }

        /// <summary>Weight override in kg (null = estimated from the part).</summary>
        public double? MassKg { get; set; }

        /// <summary>
        /// Force (N) the fastening holds before the module tears off (double-sided tape ≈ 20 N,
        /// zip ties ≈ 80 N, screws ≈ 300 N); null = 60 N (foam tape).
        /// </summary>
        public double? HoldForce { get; set; }
    }

    /// <summary>
    /// Mechanical build of the project: what kind of vehicle the circuits sit on,
    /// which motors turn which wheels or propellers, where sensors look and where
    /// boards are mounted. Saved inside the schematic file.
    /// </summary>
    public sealed class AssemblyDocument
    {
        public VehicleKind Kind { get; set; } = VehicleKind.None;
        public string Arena { get; set; } = "oval";

        /// <summary>Imported chassis model (any shape), in the chassis frame: x forward, z up, top at the deck.</summary>
        public EmbeddedMesh? ChassisMesh { get; set; }

        public string ChassisMeshName { get; set; } = string.Empty;

        /// <summary>PLA, PETG, ABS, Akrilik, Alüminyum, Çelik, Karbon, MDF, Kontrplak, FR4 …</summary>
        public string ChassisMaterial { get; set; } = "PLA";

        /// <summary>3D-print infill (0..1); solid for non-printed materials.</summary>
        public double ChassisInfill { get; set; } = 0.3;

        /// <summary>Weighed chassis mass in kg; overrides volume × density.</summary>
        public double? ChassisMassOverride { get; set; }

        /// <summary>The user's own world, used when <see cref="Arena"/> is "custom".</summary>
        public WorldSpec? World { get; set; }

        // Rover chassis
        public double Length { get; set; } = 0.20;
        public double Width { get; set; } = 0.13;
        public double ChassisMass { get; set; } = 0.08;
        public double WheelRadius { get; set; } = 0.033;
        public double WheelWidth { get; set; } = 0.026;

        /// <summary>Axle position along the chassis (negative = rear).</summary>
        public double AxleX { get; set; } = -0.035;

        // Quadcopter frame
        public double ArmLength { get; set; } = 0.045;
        public double FrameMass { get; set; } = 0.006;
        public double PropDiameter { get; set; } = 0.055;

        public List<MotorMount> Motors { get; set; } = new List<MotorMount>();
        public List<SensorMount> Sensors { get; set; } = new List<SensorMount>();
        public List<ModuleMount> Modules { get; set; } = new List<ModuleMount>();

        /// <summary>Articulated parts (arms, grippers) attached by joints.</summary>
        public List<LinkMount> Links { get; set; } = new List<LinkMount>();

        /// <summary>Tyre material (rubber, foam, plastic …).</summary>
        public string WheelMaterial { get; set; } = "rubber";

        /// <summary>Hard impacts can break parts (propellers, arms, wheels, modules, joints).</summary>
        public bool Breakable { get; set; } = true;

        /// <summary>Start pose (m, m, degrees); null uses the arena's default.</summary>
        public double? StartX { get; set; }

        public double? StartY { get; set; }
        public double? StartYaw { get; set; }

        /// <summary>Height of the deck top above the body origin.</summary>
        public double DeckZ => Kind == VehicleKind.Quadcopter ? 0.006 : 0.004;

        public MotorMount? MotorFor(string role) =>
            Motors.FirstOrDefault(m => string.Equals(m.Role, role, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------------ auto configuration

        /// <summary>
        /// Builds a sensible assembly from the schematic: a flight controller makes a
        /// quadcopter whose motor slots follow the FC outputs they are wired to;
        /// otherwise two motors make a rover. Sensors and modules are placed on the deck.
        /// Existing mounts that still refer to parts in the schematic are kept.
        /// </summary>
        public static AssemblyDocument AutoConfigure(Schematic schematic, PartLibrary library, AssemblyDocument? existing = null)
        {
            var doc = existing ?? new AssemblyDocument();
            var netlist = schematic.BuildNetlist(library);
            var parts = schematic.Parts.Select(p => (Part: p, Def: library.Find(p.PartId))).Where(x => x.Def != null).ToList();
            var motors = parts.Where(x => x.Def!.Symbol == SymbolKind.Motor).Select(x => x.Part).ToList();
            var fc = parts.FirstOrDefault(x => x.Def!.Id == "flight-controller").Part;

            doc.Motors.RemoveAll(m => schematic.FindPart(m.RefDes) == null);
            doc.Sensors.RemoveAll(m => schematic.FindPart(m.RefDes) == null);
            doc.Modules.RemoveAll(m => schematic.FindPart(m.RefDes) == null);

            if (fc != null)
            {
                doc.Kind = VehicleKind.Quadcopter;
                if (doc.Arena == "oval" && existing == null) doc.Arena = "empty";
                foreach (var role in new[] { "M1", "M2", "M3", "M4" })
                {
                    if (doc.MotorFor(role) != null) continue;
                    var net = netlist.NetOf(fc.RefDes, role);
                    var motor = net == null ? null : motors.FirstOrDefault(m => net.Connections.Any(c => c.RefDes == m.RefDes));
                    if (motor != null && doc.Motors.All(x => x.RefDes != motor.RefDes))
                    {
                        doc.Motors.Add(new MotorMount { RefDes = motor.RefDes, Role = role });
                    }
                }
            }
            else if (motors.Count >= 2)
            {
                doc.Kind = VehicleKind.Rover;
                var ordered = motors.OrderBy(m => m.Position.X).ThenBy(m => m.Position.Y).ToList();
                if (doc.MotorFor("L") == null && doc.Motors.All(x => x.RefDes != ordered[0].RefDes))
                    doc.Motors.Add(new MotorMount { RefDes = ordered[0].RefDes, Role = "L" });
                var right = ordered.FirstOrDefault(m => doc.Motors.All(x => x.RefDes != m.RefDes));
                if (doc.MotorFor("R") == null && right != null) doc.Motors.Add(new MotorMount { RefDes = right.RefDes, Role = "R" });
            }
            else if (doc.Kind == VehicleKind.None)
            {
                return doc;
            }

            // Sensors
            int line = 0, prox = 0;
            var lineSensors = new List<SensorMount>();
            foreach (var (part, def) in parts)
            {
                if (doc.Sensors.Any(s => s.RefDes == part.RefDes)) continue;
                var kind = SensorKindOf(def!, schematic.ResolveParameters(library, part));
                if (kind == null) continue;
                var mount = new SensorMount { RefDes = part.RefDes, Kind = kind.Value };
                if (doc.Kind == VehicleKind.Rover)
                {
                    double front = doc.Length / 2;
                    switch (kind.Value)
                    {
                        case SensorKind.Line:
                            mount.X = front + 0.004;
                            mount.Z = -(doc.WheelRadius + 0.004) + 0.008 - doc.DeckZ;
                            lineSensors.Add(mount);
                            line++;
                            break;
                        case SensorKind.Obstacle:
                            mount.X = front;
                            mount.Y = (prox % 2 == 0 ? 1 : -1) * 0.03 * ((prox + 1) / 2);
                            mount.Yaw = mount.Y * 600;
                            mount.Z = 0.01;
                            prox++;
                            break;
                        case SensorKind.Ultrasonic:
                            mount.X = front - 0.01;
                            mount.Z = 0.025;
                            break;
                        default:
                            mount.Z = 0.01;
                            break;
                    }
                }
                else
                {
                    mount.Z = kind.Value == SensorKind.Ultrasonic ? -0.01 : 0.01;
                    mount.Yaw = 0;
                }

                doc.Sensors.Add(mount);
            }

            // Line sensors straddle the tape symmetrically, left (+y) to right.
            if (lineSensors.Count > 0)
            {
                var sorted = lineSensors.OrderBy(s => schematic.FindPart(s.RefDes)!.Position.X).ToList();
                double spacing = sorted.Count == 2 ? 0.03 : 0.016;
                for (int i = 0; i < sorted.Count; i++)
                {
                    sorted[i].Y = (sorted.Count - 1) / 2.0 * spacing - i * spacing;
                }
            }

            // Modules on the deck in a simple grid (boards first, then the rest).
            var mountable = parts.Where(x => IsModule(x.Def!) && doc.Sensors.All(s => s.RefDes != x.Part.RefDes) &&
                                             doc.Motors.All(m => m.RefDes != x.Part.RefDes))
                .OrderBy(x => x.Def!.Symbol == SymbolKind.Board ? 0 : x.Def!.Symbol == SymbolKind.Battery ? 1 : 2)
                .Select(x => x.Part).ToList();
            double gx = doc.Kind == VehicleKind.Quadcopter ? 0.0 : doc.Length / 2 - 0.05, gy = 0.0;
            int idx = doc.Modules.Count;
            foreach (var part in mountable)
            {
                if (doc.Modules.Any(m => m.RefDes == part.RefDes)) continue;
                if (doc.Kind == VehicleKind.Quadcopter)
                {
                    doc.Modules.Add(new ModuleMount { RefDes = part.RefDes, X = 0, Y = 0 });
                    continue;
                }

                int col = idx % 2, row = idx / 2;
                gx = doc.Length / 2 - 0.045 - row * 0.06;
                gy = col == 0 ? 0.03 : -0.03;
                doc.Modules.Add(new ModuleMount { RefDes = part.RefDes, X = gx, Y = gy });
                idx++;
            }

            return doc;
        }

        public static SensorKind? SensorKindOf(PartDefinition def, IReadOnlyDictionary<string, string> parameters)
        {
            if (def.Id == "hc-sr04") return SensorKind.Ultrasonic;
            if (def.Id.IndexOf("mpu", StringComparison.OrdinalIgnoreCase) >= 0) return SensorKind.Imu;
            if (def.Id != "sensor-module") return null;
            parameters.TryGetValue("quantity", out var q);
            switch (q)
            {
                case "reflectance": return SensorKind.Line;
                case "obstacle": return SensorKind.Obstacle;
                default: return null;
            }
        }

        private static bool IsModule(PartDefinition def) =>
            def.Symbol == SymbolKind.Board || def.Symbol == SymbolKind.Battery || def.Symbol == SymbolKind.Module ||
            def.Id == "motor-driver" || def.Id == "breadboard";

        // ------------------------------------------------------------------ masses

        /// <summary>Approximate mass of a part in kilograms (for the vehicle's weight and inertia).</summary>
        public static double PartMass(PartDefinition def, IReadOnlyDictionary<string, string> parameters)
        {
            string model = parameters.TryGetValue("model", out var m) ? m : def.Name;
            switch (def.Symbol)
            {
                case SymbolKind.Board:
                    if (def.Id == "flight-controller") return 0.004;
                    if (def.Id.IndexOf("nano", StringComparison.OrdinalIgnoreCase) >= 0) return 0.007;
                    if (def.Id.IndexOf("esp32", StringComparison.OrdinalIgnoreCase) >= 0) return 0.010;
                    return 0.025;
                case SymbolKind.Battery:
                {
                    double cap = Num(parameters, "capacity", 1.0);
                    int cells = (int)Num(parameters, "cells", 1);
                    parameters.TryGetValue("chemistry", out var chem);
                    double perAh = chem == "LithiumIon" ? 0.024 : chem == "LiFePO4" ? 0.035 : chem == "LeadAcid" ? 0.33 : chem == "NiMH" ? 0.012 : 0.010;
                    if (chem == "LithiumIon" && cap >= 1.5) perAh = 0.019; // 18650 class cells
                    return Math.Max(0.003, perAh * cap * cells * (chem == "LithiumIon" || chem == "LiFePO4" || chem == "LeadAcid" ? 1.0 : 2.0));
                }

                case SymbolKind.Motor:
                {
                    if (model.IndexOf("Coreless", StringComparison.OrdinalIgnoreCase) >= 0) return model.StartsWith("07") ? 0.0021 : 0.0045;
                    if (model.IndexOf("N20", StringComparison.OrdinalIgnoreCase) >= 0) return 0.010;
                    if (model.IndexOf("TT", StringComparison.OrdinalIgnoreCase) >= 0) return 0.030;
                    if (model.IndexOf("25", StringComparison.OrdinalIgnoreCase) >= 0) return 0.095;
                    return 0.020;
                }

                case SymbolKind.Module:
                    return def.Id == "hc-sr04" ? 0.009 : 0.003;
                default:
                    if (def.Id == "motor-driver") return model.IndexOf("L298", StringComparison.OrdinalIgnoreCase) >= 0 ? 0.030 : 0.003;
                    return 0.0;
            }
        }

        private static double Num(IReadOnlyDictionary<string, string> p, string key, double fallback) =>
            p.TryGetValue(key, out var s) && SiValue.TryParse(s, out var v) ? v : fallback;
    }
}
