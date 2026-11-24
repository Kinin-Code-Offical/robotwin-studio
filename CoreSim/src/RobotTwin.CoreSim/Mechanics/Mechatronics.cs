using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox.Sensing;

namespace RobotTwin.CoreSim.Mechanics
{
    public sealed class MechEvent
    {
        public MechEvent(double time, string source, string message, bool severe)
        {
            Time = time;
            Source = source;
            Message = message;
            Severe = severe;
        }

        public double Time { get; }
        public string Source { get; }
        public string Message { get; }
        public bool Severe { get; }
    }

    /// <summary>A hard contact the vehicle felt: time, force (N), acceleration (m/s²), where.</summary>
    public readonly struct ImpactRecord
    {
        public ImpactRecord(double time, double force, double acceleration, double speed, string part)
        {
            Time = time;
            Force = force;
            Acceleration = acceleration;
            Speed = speed;
            Part = part;
        }

        public double Time { get; }
        public double Force { get; }
        public double Acceleration { get; }

        /// <summary>Approach speed of the impact (m/s).</summary>
        public double Speed { get; }

        public string Part { get; }
        public double G => Acceleration / 9.80665;
    }

    public sealed class WheelLink
    {
        internal WheelLink(MotorMount mount, Wheel wheel, DcMotor? motor)
        {
            Mount = mount;
            Wheel = wheel;
            Motor = motor;
        }

        public MotorMount Mount { get; }
        public Wheel Wheel { get; }
        public DcMotor? Motor { get; }

        /// <summary>Gear train between the motor output and the wheel (efficiency, backlash).</summary>
        public Gearbox Gearbox { get; } = new Gearbox();

        /// <summary>Spin inertia of the wheel itself (kg·m²).</summary>
        public double WheelInertia { get; internal set; }

        public bool Detached => Wheel.Detached;
        internal double BaseLoadInertia, LastGearSpeed;
    }

    public sealed class PropLink
    {
        internal PropLink(int index, MotorMount? mount, Propeller prop, DcMotor? motor)
        {
            Index = index;
            Mount = mount;
            Prop = prop;
            Motor = motor;
        }

        public int Index { get; }
        public MotorMount? Mount { get; }
        public Propeller Prop { get; }
        public DcMotor? Motor { get; }
        public bool Broken { get; internal set; }

        /// <summary>The arm carrying this motor snapped (motor and propeller hang loose).</summary>
        public bool ArmBroken { get; internal set; }
    }

    public sealed class SensorLink
    {
        internal SensorLink(SensorMount mount, ControlledSource? source, UltrasonicSensor? ranger, Phototransistor? photo = null)
        {
            Mount = mount;
            Source = source;
            Ranger = ranger;
            Photo = photo;
        }

        public SensorMount Mount { get; }
        public ControlledSource? Source { get; }
        public UltrasonicSensor? Ranger { get; }

        /// <summary>
        /// A real optical line module (TCRT5000: IR LED + phototransistor + comparator) reads the floor through its own
        /// phototransistor here too, so the same part works in this vehicle model and in a world.
        /// </summary>
        public Phototransistor? Photo { get; }

        /// <summary>Last reading: floor reflectance (line), distance in metres (obstacle, ultrasonic).</summary>
        public double Value { get; internal set; }

        public bool Triggered { get; internal set; }
    }

    /// <summary>A jointed part of the vehicle (arm segment, gripper finger) and the servo driving it.</summary>
    public sealed class ArticulatedLink
    {
        internal ArticulatedLink(LinkMount mount, SceneBody body, SceneJoint joint, Servo? servo, ServoMotor? motor, string? parentName)
        {
            Mount = mount;
            Body = body;
            Joint = joint;
            Servo = servo;
            ServoMotor = motor;
            ParentName = parentName;
        }

        public LinkMount Mount { get; }
        public SceneBody Body { get; }
        public SceneJoint Joint { get; }
        public Servo? Servo { get; }
        public ServoMotor? ServoMotor { get; }
        public string? ParentName { get; }

        /// <summary>Joint angle in degrees (hinge) or position in mm (slider).</summary>
        public double JointValue => Joint.Kind == JointKind.Slider ? Joint.Position * 1000 : Joint.Angle * 180 / Math.PI;

        public bool Broken => Joint.Broken;

        internal Vec3 RestAnchorBody; // joint anchor in the parent body frame at build time
        internal Quat RestRelative;   // link orientation relative to the chassis
        internal Vec3 RestOffset;     // link COM relative to the chassis COM (chassis frame)
    }

    /// <summary>A module that tore off its fastening and now flies/lies around as its own body.</summary>
    public sealed class DetachedModule
    {
        internal DetachedModule(ModuleMount mount, SceneBody body, MassProperties mass, Vec3 at)
        {
            Mount = mount;
            Body = body;
            Mass = mass;
            At = at;
        }

        public ModuleMount Mount { get; }
        public SceneBody Body { get; }
        internal MassProperties Mass { get; }
        internal Vec3 At { get; }
    }

    /// <summary>
    /// Couples the circuit simulation with the rigid-body world: motor shafts turn
    /// wheels and propellers through gearboxes (ratio, efficiency, backlash, reflected
    /// inertia), the ground and air push back on the shafts as load torque, servos drive
    /// jointed links and draw current according to the real load, sensors read the arena,
    /// hard impacts break parts, and a built-in flight controller drives the FC's MOSFETs.
    /// Call <see cref="Advance"/> after every circuit step.
    /// </summary>
    public sealed class MechatronicSystem
    {
        private const double ControlPeriod = 1e-3;
        private readonly AnalogSimulator? _sim;
        private readonly CompiledCircuit _circuit;
        private readonly List<WheelLink> _wheels = new List<WheelLink>();
        private readonly List<PropLink> _props = new List<PropLink>();
        private readonly List<SensorLink> _sensors = new List<SensorLink>();
        private readonly List<ArticulatedLink> _links = new List<ArticulatedLink>();
        private readonly List<MechEvent> _events = new List<MechEvent>();
        private readonly List<ImpactRecord> _impacts = new List<ImpactRecord>();
        private readonly List<DetachedModule> _detached = new List<DetachedModule>();
        private readonly List<(ModuleMount Mount, MassProperties Mass, Vec3 At)> _moduleMasses = new List<(ModuleMount, MassProperties, Vec3)>();
        private double _pending;
        private double _lastCrash = -10, _upsideSince = -1;
        private bool _flippedReported;
        private string? _fcRef;
        private Vec3 _prevPos;
        private MassProperties? _baseMass;
        private double _armBreakForce = double.PositiveInfinity, _armStiffness = 1e4;
        // Push-fit hub on a D-shaft: pulls off axially at ~25 N; the plastic gearbox mount breaks radially at ~150 N.
        private double _wheelAxialHold = 25, _wheelRadialStrength = 150;
        private int _contactPointCount = -1;
        private const double DefaultHoldForce = 60, MountStiffness = 5e3;

        public MechatronicSystem(AssemblyDocument assembly, Schematic schematic, PartLibrary library, CompiledCircuit circuit, AnalogSimulator? simulator)
        {
            Assembly = assembly;
            _circuit = circuit;
            _sim = simulator;
            Arena = assembly.CreateArena();
            World = new World3D(Arena);
            if (assembly.Arena == "custom" && assembly.World != null) World.Gravity = new Vec3(0, 0, -Math.Abs(assembly.World.Gravity));
            Build(schematic, library);
            Reset();
        }

        public AssemblyDocument Assembly { get; }
        public Arena Arena { get; }
        public World3D World { get; }
        public RigidBody Body => World.Body;
        public VehicleKind Kind => Assembly.Kind;
        public IReadOnlyList<WheelLink> Wheels => _wheels;
        public IReadOnlyList<PropLink> Props => _props;
        public IReadOnlyList<SensorLink> Sensors => _sensors;

        /// <summary>Jointed parts (arms, grippers) and their servos.</summary>
        public IReadOnlyList<ArticulatedLink> Links => _links;

        /// <summary>Modules that tore off in a crash.</summary>
        public IReadOnlyList<DetachedModule> DetachedModules => _detached;

        /// <summary>Hard contacts felt by the vehicle (force, acceleration) since the last reset.</summary>
        public IReadOnlyList<ImpactRecord> Impacts => _impacts;

        /// <summary>Largest acceleration (m/s²) and contact force (N) felt since the last reset.</summary>
        public double PeakAcceleration { get; private set; }

        public double PeakImpactForce { get; private set; }

        public FlightController? Flight { get; private set; }
        public double Time { get; private set; }
        public double TotalMass => Body.Mass + _links.Sum(l => l.Body.Mass);
        public double Distance { get; private set; }

        /// <summary>Deck height above the floor when resting (rover) – also the body origin height.</summary>
        public double RideHeight { get; private set; }

        public double Speed => new Vec3(Body.Velocity.X, Body.Velocity.Y, 0).Length;
        public double Altitude => Body.Position.Z - CenterOfMass.Z - RideHeight;

        public IReadOnlyList<MechEvent> DrainEvents()
        {
            var copy = _events.ToArray();
            _events.Clear();
            return copy;
        }

        // ------------------------------------------------------------------ building

        private void Build(Schematic schematic, PartLibrary library)
        {
            var a = Assembly;
            double mass = 0;
            foreach (var part in schematic.Parts)
            {
                var def = library.Find(part.PartId);
                if (def != null) mass += AssemblyDocument.PartMass(def, schematic.ResolveParameters(library, part));
            }

            var body = World.Body;
            if (a.Kind == VehicleKind.Rover)
            {
                body.Material = a.ChassisMaterial ?? "PLA";
                mass += a.ChassisMass + 2 * 0.025;
                RideHeight = a.WheelRadius + 0.012;
                body.Mass = Math.Max(0.05, mass);
                double h = 0.06;
                body.Inertia = new Vec3(body.Mass / 12 * (a.Width * a.Width + h * h), body.Mass / 12 * (a.Length * a.Length + h * h),
                    body.Mass / 12 * (a.Length * a.Length + a.Width * a.Width));
                // Rolling resistance, drivetrain losses and air drag are modelled explicitly now.
                body.LinearDrag = 0;
                body.AngularDrag = 2e-5;
                body.DragArea = new Vec3(a.Width * h, a.Length * h, a.Length * a.Width) * 1.05;

                double halfTrack = a.Width / 2 + a.WheelWidth / 2 + 0.003;
                double perWheel = body.Mass * 9.81 / 3;
                foreach (var role in new[] { "L", "R" })
                {
                    var mount = a.MotorFor(role) ?? new MotorMount { Role = role };
                    var wheel = new Wheel
                    {
                        Mount = new Vec3(a.AxleX, role == "L" ? halfTrack : -halfTrack, a.WheelRadius - RideHeight),
                        Radius = a.WheelRadius,
                        Width = a.WheelWidth,
                        Material = a.WheelMaterial,
                        SuspensionStiffness = perWheel / 0.0015,
                        SuspensionDamping = 2 * 0.8 * Math.Sqrt(perWheel / 0.0015 * body.Mass / 3),
                        LongitudinalStiffness = 12 * body.Mass * 9.81,
                        LateralStiffness = 18 * body.Mass * 9.81
                    };
                    // Small hard tyres: the tread relaxes within a fraction of the radius.
                    wheel.Tyre.RelaxationLength = Math.Max(0.002, 0.18 * a.WheelRadius);
                    var motor = FindDevice<DcMotor>(mount.RefDes);
                    var link = new WheelLink(mount, wheel, motor) { WheelInertia = 0.025 * a.WheelRadius * a.WheelRadius / 2 };
                    wheel.SpinInertia = link.WheelInertia;
                    if (motor != null)
                    {
                        // Wheel and gear train inertia reflected to the motor; gearbox losses.
                        double gearTrain = 0.2 * motor.Inertia * motor.GearRatio * motor.GearRatio;
                        motor.LoadInertia += link.WheelInertia + gearTrain;
                        link.BaseLoadInertia = motor.LoadInertia;
                        bool metal = mount.RefDes.Length > 0 && IsMetalGearbox(schematic, library, mount.RefDes);
                        double eta = mount.GearEfficiency ?? Gearbox.TypicalEfficiency(motor.GearRatio, metal);
                        motor.GearEfficiency = Math.Max(0.05, Math.Min(1, eta));
                        link.Gearbox.Efficiency = motor.GearEfficiency;
                        double backlashDeg = mount.BacklashDeg ?? (motor.GearRatio > 1.0001 ? (metal ? 1.0 : 2.0) : 0.0);
                        link.Gearbox.Backlash = Math.Max(0, backlashDeg) * Math.PI / 180;
                    }

                    World.Wheels.Add(wheel);
                    _wheels.Add(link);
                }

                body.CasterPoints.Add(new Vec3(a.Length / 2 - 0.025, 0, -RideHeight));
                foreach (double sx in new[] { -1.0, 1.0 })
                {
                    foreach (double sy in new[] { -1.0, 1.0 })
                    {
                        body.ContactPoints.Add(new Vec3(sx * a.Length / 2, sy * a.Width / 2, -0.012));
                        body.ContactPoints.Add(new Vec3(sx * a.Length / 2, sy * a.Width / 2, 0.05));
                    }
                }
            }
            else if (a.Kind == VehicleKind.Quadcopter)
            {
                body.Material = a.ChassisMesh != null ? a.ChassisMaterial ?? "plastic" : "plastic";
                double propMass = 0.0003;
                mass += a.FrameMass + 4 * propMass;
                RideHeight = 0.012;
                body.Mass = Math.Max(0.01, mass);
                double motorMass = 0;
                foreach (var m in a.Motors)
                {
                    var p = schematic.FindPart(m.RefDes);
                    var d = p != null ? library.Find(p.PartId) : null;
                    if (d != null) motorMass += AssemblyDocument.PartMass(d, schematic.ResolveParameters(library, p!));
                }

                double armMass = motorMass / 4 + propMass;
                double centre = body.Mass - 4 * armMass;
                double r2 = a.ArmLength * a.ArmLength;
                double ixy = 4 * armMass * r2 / 2 + centre / 12 * 0.04 * 0.04;
                body.Inertia = new Vec3(ixy, ixy, 4 * armMass * r2 + centre / 12 * 0.04 * 0.04 * 2);
                // Air drag is explicit: Cd·A of the frame and battery, rotor drag (H-force) from the propellers.
                body.LinearDrag = 0;
                body.AngularDrag = 4e-6;
                body.DragArea = new Vec3(0.04 * 0.02, 0.04 * 0.02, 0.04 * 0.04) * 1.1;

                double scale = a.PropDiameter / 0.055;
                double[] angles = { 45, -45, -135, 135 };
                int[] dirs = { -1, 1, -1, 1 };
                for (int i = 0; i < 4; i++)
                {
                    double ang = angles[i] * Math.PI / 180;
                    var mount = a.MotorFor("M" + (i + 1));
                    var prop = new Propeller
                    {
                        Mount = new Vec3(a.ArmLength * Math.Cos(ang), a.ArmLength * Math.Sin(ang), 0.012),
                        Diameter = a.PropDiameter,
                        SpinDirection = dirs[i],
                        ThrustCoefficient = 2.95e-8 * Math.Pow(scale, 4),
                        TorqueCoefficient = 1.4e-10 * Math.Pow(scale, 5)
                    };
                    var motor = mount != null ? FindDevice<DcMotor>(mount.RefDes) : null;
                    if (motor != null) motor.LoadInertia += propMass * a.PropDiameter * a.PropDiameter / 20;
                    World.Propellers.Add(prop);
                    _props.Add(new PropLink(i, mount, prop, motor));
                    body.ContactPoints.Add(new Vec3(prop.Mount.X, prop.Mount.Y, -RideHeight));
                    body.ContactPoints.Add(new Vec3(prop.Mount.X, prop.Mount.Y, 0.02));
                }

                body.ContactPoints.Add(new Vec3(0, 0, -RideHeight));

                // Arms snap in bending at the motor: F = σ·Z/L with Z = b·t²/6 (6 × 4 mm section);
                // the arm's own bending stiffness 3EI/L³ sets how hard a tip impact really hits.
                var mat = Materials.Get(a.ChassisMesh != null ? a.ChassisMaterial : "nylon");
                double b = 0.006, t = 0.004, len = Math.Max(0.02, a.ArmLength);
                _armBreakForce = mat.YieldStrength * b * t * t / 6 / len;
                double youngs = mat.Name == "carbon" ? 70e9 : mat.Name == "aluminium" ? 69e9 : mat.Name == "steel" ? 200e9 : 2.5e9;
                _armStiffness = 3 * youngs * (b * t * t * t / 12) / (len * len * len);

                var fcPart = schematic.Parts.FirstOrDefault(p => p.PartId == "flight-controller");
                if (fcPart != null)
                {
                    _fcRef = fcPart.RefDes;
                    Flight = new FlightController();
                    for (int i = 0; i < 4; i++) Flight.Outputs[i] = _circuit.Device<AveragedPwmDriver>(fcPart.RefDes + ".M" + (i + 1));
                    var anyMotor = _props.Select(p => p.Motor).FirstOrDefault(m => m != null);
                    if (anyMotor != null)
                    {
                        Flight.HoverDuty = EstimateHoverDuty(anyMotor, _props[0].Prop, body.Mass * 9.81 / 4, 3.8);
                    }

                    Flight.RateGain = EstimateRateGain(body, a, anyMotor, _props[0].Prop);
                }
            }

            ApplyMassModel(schematic, library);
            if (Flight != null && _props.Count > 0)
            {
                var anyMotor = _props.Select(x => x.Motor).FirstOrDefault(x => x != null);
                double rho = Arena.AirDensity / 1.225;
                if (anyMotor != null) Flight.HoverDuty = EstimateHoverDuty(anyMotor, _props[0].Prop, World.Body.Mass * Math.Abs(World.Gravity.Z) / 4 / Math.Max(0.05, rho), 3.8);
                Flight.RateGain = EstimateRateGain(World.Body, a, anyMotor, _props[0].Prop);
            }

            BuildLinks(schematic, library);

            foreach (var s in a.Sensors)
            {
                var source = FindDevice<ControlledSource>(s.RefDes);
                var ranger = FindDevice<UltrasonicSensor>(s.RefDes);
                var photo = s.Kind == SensorKind.Line ? FindDevice<Phototransistor>(s.RefDes) : null;
                _sensors.Add(new SensorLink(s, source, ranger, photo));
            }
        }

        private static bool IsMetalGearbox(Schematic schematic, PartLibrary library, string refDes)
        {
            var part = schematic.FindPart(refDes);
            var def = part != null ? library.Find(part.PartId) : null;
            if (part == null || def == null) return false;
            var p = schematic.ResolveParameters(library, part);
            string model = p.TryGetValue("model", out var m) ? m : def.Name;
            return model.IndexOf("N20", StringComparison.OrdinalIgnoreCase) >= 0 || model.IndexOf("25GA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   model.IndexOf("JGA", StringComparison.OrdinalIgnoreCase) >= 0 || model.IndexOf("metal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------ mass model

        /// <summary>Centre of mass in the chassis frame; the rigid body's origin sits here.</summary>
        public Vec3 CenterOfMass { get; private set; }

        /// <summary>Mass breakdown of the vehicle (name, kg) for the inspector.</summary>
        public List<(string Name, double Mass)> MassBreakdown { get; } = new List<(string, double)>();

        /// <summary>The imported chassis model in the chassis frame, if any.</summary>
        public MeshModel? ChassisModel { get; private set; }

        /// <summary>
        /// Real mass properties: the chassis (imported model by volume × material density,
        /// or a plate), every mounted board/module/battery as a box with its weight, motors,
        /// wheels/propellers and sensors at their mounts, loose parts at the centre. The total
        /// gives mass, centre of mass and the full inertia tensor; everything mounted on the
        /// body is then expressed relative to the centre of mass.
        /// </summary>
        private void ApplyMassModel(Schematic schematic, PartLibrary library)
        {
            var a = Assembly;
            var parts = new List<MassPart>();
            MassBreakdown.Clear();
            _moduleMasses.Clear();
            void Add(string name, MassProperties mp, Vec3 at)
            {
                if (mp.Mass <= 0) return;
                parts.Add(new MassPart(mp, at));
                MassBreakdown.Add((name, mp.Mass));
            }

            double deck = a.DeckZ;
            if (a.ChassisMesh != null)
            {
                ChassisModel = MeshModel.FromEmbedded(a.ChassisMesh);
                string mat = (a.ChassisMaterial ?? "PLA").ToUpperInvariant();
                bool printed = mat == "PLA" || mat == "PETG" || mat == "ABS" || mat == "NAYLON" || mat == "NYLON";
                double density = printed ? MaterialDensity.Printed(DensityOf(a.ChassisMaterial ?? "PLA"), a.ChassisInfill) : DensityOf(a.ChassisMaterial ?? "PLA");
                var mp = a.ChassisMassOverride is double m && m > 0 ? MassProperties.FromMeshWithMass(ChassisModel, m) : MassProperties.FromMesh(ChassisModel, density);
                parts.Add(new MassPart(mp, Vec3.Zero));
                MassBreakdown.Add(("Gövde modeli (" + (string.IsNullOrEmpty(a.ChassisMeshName) ? "3B" : a.ChassisMeshName) + ")", mp.Mass));
            }
            else if (a.Kind == VehicleKind.Rover)
            {
                Add("Şasi plakası", MassProperties.Box(a.ChassisMass, new Vec3(a.Length, a.Width, 0.003)), new Vec3(0, 0, deck - 0.0015));
            }
            else
            {
                Add("Gövde", MassProperties.Box(a.FrameMass, new Vec3(a.ArmLength * 1.4, 0.012, 0.004)), Vec3.Zero);
            }

            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in a.Modules)
            {
                var part = schematic.FindPart(m.RefDes);
                var def = part != null ? library.Find(part.PartId) : null;
                if (def == null) continue;
                double mass = m.MassKg ?? AssemblyDocument.PartMass(def, schematic.ResolveParameters(library, part!));
                var mp = MassProperties.Box(mass, new Vec3(0.05, 0.035, 0.012));
                var at = new Vec3(m.X, m.Y, a.Kind == VehicleKind.Quadcopter && def.Symbol == SymbolKind.Battery ? -0.008 : deck + 0.006);
                Add(m.RefDes, mp, at);
                if (mass > 0) _moduleMasses.Add((m, mp, at));
                placed.Add(m.RefDes);
            }

            foreach (var w in _wheels)
            {
                Add("Teker " + w.Mount.Role, MassProperties.Cylinder(0.025, w.Wheel.Radius, w.Wheel.Width), w.Wheel.Mount);
                if (string.IsNullOrEmpty(w.Mount.RefDes)) continue;
                var part = schematic.FindPart(w.Mount.RefDes);
                var def = part != null ? library.Find(part.PartId) : null;
                if (def != null) Add(w.Mount.RefDes, MassProperties.Box(AssemblyDocument.PartMass(def, schematic.ResolveParameters(library, part!)), new Vec3(0.065, 0.022, 0.019)),
                    new Vec3(w.Wheel.Mount.X + 0.015, w.Wheel.Mount.Y * 0.75, w.Wheel.Mount.Z + 0.004));
                placed.Add(w.Mount.RefDes);
            }

            foreach (var p in _props)
            {
                double motor = 0;
                if (p.Mount != null && !string.IsNullOrEmpty(p.Mount.RefDes))
                {
                    var part = schematic.FindPart(p.Mount.RefDes);
                    var def = part != null ? library.Find(part.PartId) : null;
                    if (def != null) motor = AssemblyDocument.PartMass(def, schematic.ResolveParameters(library, part!));
                    placed.Add(p.Mount.RefDes);
                }

                Add("Motor + pervane " + (p.Index + 1), MassProperties.Cylinder(motor + 0.0003, 0.0045, 0.02), p.Prop.Mount);
            }

            foreach (var s in a.Sensors)
            {
                var part = schematic.FindPart(s.RefDes);
                var def = part != null ? library.Find(part.PartId) : null;
                if (def == null) continue;
                Add(s.RefDes, MassProperties.Point(AssemblyDocument.PartMass(def, schematic.ResolveParameters(library, part!))), new Vec3(s.X, s.Y, s.Z + deck));
                placed.Add(s.RefDes);
            }

            // Servos driving links are carried by the chassis (the link bodies have their own mass).
            double loose = 0;
            foreach (var part in schematic.Parts)
            {
                if (placed.Contains(part.RefDes)) continue;
                var def = library.Find(part.PartId);
                if (def != null) loose += AssemblyDocument.PartMass(def, schematic.ResolveParameters(library, part));
            }

            if (loose > 0) Add("Diğer parçalar", MassProperties.Point(loose), new Vec3(0, 0, deck));
            if (parts.Count == 0) return;

            var total = MassProperties.Combine(parts);
            _baseMass = total;
            var body = World.Body;
            body.SetMassProperties(total);
            var com = total.CenterOfMass;
            CenterOfMass = com;

            // Everything attached to the body is now relative to the centre of mass.
            foreach (var w in _wheels) w.Wheel.Mount = w.Wheel.Mount - com;
            foreach (var p in _props) p.Prop.Mount = p.Prop.Mount - com;
            if (ChassisModel != null)
            {
                var hull = ContactGeometry.ContactPointsFromMesh(ChassisModel, 16);
                body.ContactPoints.Clear();
                foreach (var c in hull) body.ContactPoints.Add(c - com);
            }
            else
            {
                for (int i = 0; i < body.ContactPoints.Count; i++) body.ContactPoints[i] = body.ContactPoints[i] - com;
            }

            for (int i = 0; i < body.CasterPoints.Count; i++) body.CasterPoints[i] = body.CasterPoints[i] - com;

            // Suspension and tyre stiffness follow the real weight.
            double perWheel = body.Mass * 9.81 / 3;
            foreach (var w in _wheels)
            {
                w.Wheel.SuspensionStiffness = perWheel / 0.0015;
                w.Wheel.SuspensionDamping = 2 * 0.8 * Math.Sqrt(perWheel / 0.0015 * body.Mass / 3);
                w.Wheel.LongitudinalStiffness = 12 * body.Mass * 9.81;
                w.Wheel.LateralStiffness = 18 * body.Mass * 9.81;
            }
        }

        public static double DensityOf(string material) => (material ?? string.Empty).ToUpperInvariant() switch
        {
            "PETG" => MaterialDensity.Petg,
            "ABS" => MaterialDensity.Abs,
            "NAYLON" or "NYLON" => MaterialDensity.Nylon,
            "AKRİLİK" or "AKRILIK" or "ACRYLIC" => MaterialDensity.Acrylic,
            "KONTRPLAK" or "PLYWOOD" => MaterialDensity.Plywood,
            "MDF" => MaterialDensity.Mdf,
            "FR4" => MaterialDensity.Fr4,
            "ALÜMİNYUM" or "ALUMINIUM" or "ALUMINUM" => MaterialDensity.Aluminium,
            "ÇELİK" or "STEEL" => MaterialDensity.Steel,
            "KARBON" or "CARBON" => MaterialDensity.CarbonFibre,
            "AHŞAP" or "AHSAP" or "WOOD" => MaterialDensity.Wood,
            "BALSA" => MaterialDensity.Balsa,
            "KURŞUN" or "KURSUN" or "LEAD" => MaterialDensity.Lead,
            "KAUÇUK" or "KAUCUK" or "RUBBER" => MaterialDensity.Rubber,
            "BAKIR" or "COPPER" => MaterialDensity.Copper,
            "CAM" or "GLASS" => MaterialDensity.Glass,
            _ => MaterialDensity.Pla
        };

        private T? FindDevice<T>(string refDes) where T : AnalogDevice =>
            string.IsNullOrEmpty(refDes) ? null : _circuit.DevicesOf(refDes).OfType<T>().FirstOrDefault();

        // ------------------------------------------------------------------ articulated links

        private void BuildLinks(Schematic schematic, PartLibrary library)
        {
            var a = Assembly;
            if (a.Links.Count == 0) return;
            var chassis = World.VehicleBody;
            var byName = new Dictionary<string, (SceneBody Body, LinkMount Mount)>(StringComparer.OrdinalIgnoreCase);
            var pending = new List<LinkMount>(a.Links);
            int guard = 0;
            while (pending.Count > 0 && guard++ < 64)
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    var lm = pending[i];
                    bool onChassis = string.IsNullOrEmpty(lm.Parent);
                    if (!onChassis && !byName.ContainsKey(lm.Parent)) continue;
                    pending.RemoveAt(i--);

                    // Joint point and axis in the parent body's frame.
                    SceneBody parent;
                    Vec3 anchorParent, axisParent;
                    if (onChassis)
                    {
                        parent = chassis;
                        anchorParent = new Vec3(lm.X, lm.Y, lm.Z + a.DeckZ) - CenterOfMass;
                        axisParent = new Vec3(lm.AxisX, lm.AxisY, lm.AxisZ);
                    }
                    else
                    {
                        var (pb, pm) = byName[lm.Parent];
                        parent = pb;
                        anchorParent = new Vec3(lm.X - pm.Length / 2, lm.Y, lm.Z);
                        axisParent = new Vec3(lm.AxisX, lm.AxisY, lm.AxisZ);
                    }

                    if (axisParent.Length < 1e-9) axisParent = Vec3.UnitY;
                    double len = Math.Max(0.005, lm.Length), wid = Math.Max(0.002, lm.Width), th = Math.Max(0.001, lm.Thickness);
                    string mat = Materials.Canonical(lm.Material);
                    double mass = lm.Mass > 0 ? lm.Mass : DensityOf(lm.Material) * len * wid * th;
                    var size = new Vec3(len, wid, th);
                    // At rest the link frame is parallel to its parent frame; the box extends along +x from the joint.
                    var body = SceneBody.Box(lm.Name, size, Math.Max(1e-4, mass), Vec3.Zero, mat);
                    body.DragArea = new Vec3(wid * th, len * th, len * wid) * 1.1;
                    var anchorWorld = parent.ToWorld(anchorParent);
                    body.Orientation = parent.Orientation;
                    body.Position = anchorWorld + parent.Orientation.Rotate(new Vec3(len / 2, 0, 0));
                    World.Scene.AddBody(body);
                    var axisWorld = parent.Orientation.Rotate(axisParent.Normalized());
                    var joint = World.Scene.AddJoint(lm.Joint, parent, body, anchorWorld, axisWorld, lm.Name);
                    joint.Friction = Math.Max(0, lm.Friction);
                    double unit = lm.Joint == JointKind.Slider ? 1.0 : Math.PI / 180;
                    if (lm.Lower.HasValue) joint.Lower = lm.Lower.Value * unit;
                    if (lm.Upper.HasValue) joint.Upper = lm.Upper.Value * unit;

                    Servo? servo = null;
                    ServoMotor? motor = null;
                    if (!string.IsNullOrEmpty(lm.ServoRefDes))
                    {
                        servo = FindDevice<Servo>(lm.ServoRefDes);
                        var part = schematic.FindPart(lm.ServoRefDes);
                        var p = part != null ? schematic.ResolveParameters(library, part) : null;
                        motor = ServoMotor.ForModel(p != null && p.TryGetValue("model", out var model) ? model : null);
                        if (servo != null)
                        {
                            motor.StallCurrent = servo.StallCurrent;
                            motor.MovingCurrent = servo.MovingCurrent;
                            motor.IdleCurrent = servo.IdleCurrent;
                            motor.NoLoadSpeed = servo.SpeedDegPerSec * Math.PI / 180;
                            servo.MechanicallyDriven = true;
                        }
                    }

                    // Breakage: the servo's plastic gears strip at ~4× stall torque; a link root
                    // fails in bending at σ·w·t²/6; a pinned joint pulls out at ~200 N.
                    var strength = Materials.Get(mat).YieldStrength;
                    double rootMoment = strength * wid * th * th / 6;
                    joint.BreakTorque = lm.BreakTorque ?? (motor != null ? Math.Min(4 * motor.StallTorque, rootMoment) : rootMoment);
                    joint.BreakForce = lm.BreakForce ?? 200;
                    if (!Assembly.Breakable)
                    {
                        joint.BreakTorque = double.PositiveInfinity;
                        joint.BreakForce = double.PositiveInfinity;
                    }

                    var link = new ArticulatedLink(lm, body, joint, servo, motor, onChassis ? null : lm.Parent)
                    {
                        RestAnchorBody = anchorParent,
                        RestRelative = chassis.Orientation.Conjugate * body.Orientation,
                        RestOffset = chassis.Orientation.InverseRotate(body.Position - chassis.Position)
                    };
                    _links.Add(link);
                    byName[lm.Name] = (body, lm);
                    MassBreakdown.Add((lm.Name, body.Mass));
                }
            }
        }

        /// <summary>Steady-state duty that makes one motor carry <paramref name="thrust"/> newtons.</summary>
        private static double EstimateHoverDuty(DcMotor motor, Propeller prop, double thrust, double supply)
        {
            double w = Math.Sqrt(thrust / prop.ThrustCoefficient);
            double torque = prop.TorqueCoefficient * w * w + motor.ViscousFriction * w + motor.CoulombFriction;
            double v = motor.MotorConstant * w + motor.Resistance * torque / motor.MotorConstant;
            return AnalogMath.Clamp(v / supply, 0.05, 0.95);
        }

        /// <summary>Scales the rate-loop gains with how much angular acceleration one unit of command produces.</summary>
        private static double EstimateRateGain(RigidBody body, AssemblyDocument a, DcMotor? motor, Propeller prop)
        {
            if (motor == null) return 1.0;
            double wHover = Math.Sqrt(body.Mass * 9.81 / 4 / prop.ThrustCoefficient);
            double dwdd = 3.8 / motor.MotorConstant * 0.8;
            double dTdd = 2 * prop.ThrustCoefficient * wHover * dwdd;
            double torquePerUnit = 4 * dTdd * a.ArmLength / Math.Sqrt(2);
            double accel = torquePerUnit / body.Inertia.X;
            // Reference airframe (30 g, 8520, 45 mm arms) gives ~2300 rad/s² per unit.
            return AnalogMath.Clamp(2300.0 / Math.Max(1.0, accel), 0.1, 10.0);
        }

        /// <summary>Fault injection: snaps a propeller off its motor.</summary>
        public void BreakProp(int index)
        {
            if (index < 0 || index >= _props.Count || _props[index].Broken) return;
            _props[index].Broken = true;
            _events.Add(new MechEvent(Time, _props[index].Mount?.RefDes ?? "M" + (index + 1), "pervane kırıldı (arıza deneyi)", true));
        }

        /// <summary>Fault injection: snaps a quadcopter arm (its motor and propeller stop producing thrust).</summary>
        public void BreakArm(int index)
        {
            if (index < 0 || index >= _props.Count || _props[index].ArmBroken) return;
            SnapArm(_props[index], "kol kırıldı (arıza deneyi)");
        }

        /// <summary>Fault injection: a wheel comes off its shaft.</summary>
        public void DetachWheel(int index)
        {
            if (index < 0 || index >= _wheels.Count || _wheels[index].Wheel.Detached) return;
            LoseWheel(_wheels[index], "teker yerinden çıktı (arıza deneyi)");
        }

        public void Reset()
        {
            var a = Assembly;
            double x = a.StartX ?? Arena.StartX;
            double y = a.StartY ?? Arena.StartY;
            double yaw = a.StartYaw.HasValue ? a.StartYaw.Value * Math.PI / 180 : Arena.StartYaw;
            var b = Body;

            // Undo structural damage.
            foreach (var d in _detached) World.Scene.RemoveBody(d.Body);
            if (_detached.Count > 0 && _baseMass != null) RestoreMass();
            _detached.Clear();
            if (_contactPointCount >= 0 && b.ContactPoints.Count > _contactPointCount)
                b.ContactPoints.RemoveRange(_contactPointCount, b.ContactPoints.Count - _contactPointCount);
            _contactPointCount = b.ContactPoints.Count;
            foreach (var p in _props)
            {
                p.Broken = false;
                p.ArmBroken = false;
                p.Prop.Detached = false;
            }

            foreach (var w in _wheels)
            {
                w.Wheel.Detached = false;
                w.Wheel.Tyre.Reset();
                w.Gearbox.Reset();
                w.LastGearSpeed = 0;
                if (w.Motor != null) w.Motor.LoadInertia = w.BaseLoadInertia;
            }

            b.Orientation = Quat.FromYaw(yaw);
            double ground = Arena.GroundHeight(x, y);
            b.Position = new Vec3(x, y, ground + RideHeight + 0.0005) + b.Orientation.Rotate(CenterOfMass);
            b.Velocity = Vec3.Zero;
            b.AngularVelocity = Vec3.Zero;
            foreach (var l in _links)
            {
                l.Joint.Broken = false;
                Array.Clear(l.Joint.Impulse, 0, l.Joint.Impulse.Length);
                l.Joint.PointImpulse = Vec3.Zero;
                l.Joint.Angle = 0;
                l.Joint.PrevWrapped = 0;
                l.Body.Orientation = b.Orientation * l.RestRelative;
                l.Body.Position = b.Position + b.Orientation.Rotate(l.RestOffset);
                l.Body.Velocity = Vec3.Zero;
                l.Body.AngularVelocity = Vec3.Zero;
            }

            World.ResetMovables();
            Flight?.Reset();
            _prevPos = b.Position;
            Distance = 0;
            _flippedReported = false;
            _upsideSince = -1;
            _impacts.Clear();
            PeakAcceleration = 0;
            PeakImpactForce = 0;
        }

        private void RestoreMass() => Body.SetMassProperties(_baseMass!);

        // ------------------------------------------------------------------ stepping

        public void Advance(double dt)
        {
            if (Kind == VehicleKind.None || dt <= 0) return;
            _pending += dt;
            while (_pending >= ControlPeriod * 0.999)
            {
                double h = Math.Min(_pending, ControlPeriod);
                StepOnce(h);
                _pending -= h;
            }
        }

        private void StepOnce(double h)
        {
            // Shafts drive the mechanics through the gearboxes.
            foreach (var w in _wheels)
            {
                double gear = w.Motor == null ? 0 : (w.Mount.Reverse ? -1 : 1) * w.Motor.AngularVelocity / w.Motor.GearRatio;
                if (w.Motor == null || w.Gearbox.Coupled)
                {
                    w.Wheel.Drive = WheelDrive.Kinematic;
                    w.Wheel.AngularVelocity = gear;
                }
                else
                {
                    // Teeth apart: the wheel spins on its own inertia.
                    w.Wheel.Drive = WheelDrive.Torque;
                    w.Wheel.DriveTorque = 0;
                }
            }

            foreach (var p in _props)
            {
                double omega = p.Motor?.AngularVelocity ?? 0;
                if (p.Mount?.Reverse == true) omega = -omega;
                p.Prop.AngularVelocity = p.Broken ? 0 : omega;
                p.Prop.Detached = p.Broken || p.ArmBroken;
            }

            foreach (var l in _links) DriveServo(l);

            if (Flight != null)
            {
                Flight.Powered = FcVoltage() > 3.0;
                Flight.Update(Body, h);
                foreach (var msg in Flight.DrainMessages()) _events.Add(new MechEvent(Time, _fcRef ?? "FC", msg, false));
            }

            World.Step(h);
            Time += h;

            // Ground and air push back on the shafts.
            foreach (var w in _wheels)
            {
                if (w.Motor == null) continue;
                double sign = w.Mount.Reverse ? -1 : 1;
                double gear = sign * w.Motor.AngularVelocity / w.Motor.GearRatio;
                var gb = w.Gearbox;
                bool wasCoupled = gb.Coupled;
                double reaction = w.Wheel.Detached ? 0 : w.Wheel.ReactionTorque;
                gb.Update(gear, w.LastGearSpeed, w.Wheel.AngularVelocity, reaction, w.WheelInertia, h);
                w.LastGearSpeed = gear;
                if (gb.Coupled && wasCoupled)
                {
                    // Power flowing back from the wheel passes the gears with losses the other way.
                    double load = reaction * gear >= 0 ? reaction : reaction * gb.Efficiency * gb.Efficiency;
                    w.Motor.LoadTorque = sign * load;
                    w.Motor.LoadInertia = w.BaseLoadInertia;
                }
                else
                {
                    w.Motor.LoadTorque = 0;
                    w.Motor.LoadInertia = Math.Max(0, w.BaseLoadInertia - w.WheelInertia);
                    if (gb.Coupled) w.Wheel.AngularVelocity = gear;
                }
            }

            foreach (var p in _props)
            {
                if (p.Motor == null) continue;
                double sign = p.Mount?.Reverse == true ? -1 : 1;
                p.Motor.LoadTorque = p.Broken ? 0 : sign * p.Prop.DragTorque;
            }

            foreach (var l in _links) FeedBackServo(l);

            UpdateSensors();
            DetectIncidents();

            var pos = Body.Position;
            Distance += new Vec3(pos.X - _prevPos.X, pos.Y - _prevPos.Y, 0).Length;
            _prevPos = pos;
        }

        private void DriveServo(ArticulatedLink l)
        {
            if (l.ServoMotor == null) return;
            var m = l.ServoMotor;
            if (l.Servo != null)
            {
                m.SupplyVoltage = _sim == null ? 5.0 : _circuit.PinVoltage(_sim, l.Mount.ServoRefDes, "V+") - _circuit.PinVoltage(_sim, l.Mount.ServoRefDes, "GND");
                double target = l.Servo.TargetAngleDeg;
                m.CommandAngle = double.IsNaN(target) ? double.NaN : (target - l.Mount.ServoOffsetDeg) * l.Mount.ServoScale * Math.PI / 180;
            }

            if (l.Joint.Broken)
            {
                l.Joint.Drive = JointDrive.None;
                return;
            }

            m.Drive(l.Joint);
        }

        private void FeedBackServo(ArticulatedLink l)
        {
            if (l.ServoMotor == null || l.Servo == null) return;
            double current = l.Joint.Broken ? l.ServoMotor.IdleCurrent : l.ServoMotor.UpdateCurrent(l.Joint);
            l.Servo.DrivenCurrent = current;
            double scale = Math.Abs(l.Mount.ServoScale) < 1e-9 ? 1 : l.Mount.ServoScale;
            l.Servo.DrivenAngleDeg = l.Mount.ServoOffsetDeg + l.Joint.Angle * 180 / Math.PI / scale;
        }

        private double FcVoltage()
        {
            if (_fcRef == null || _sim == null) return 3.7;
            return _circuit.PinVoltage(_sim, _fcRef, "VBAT") - _circuit.PinVoltage(_sim, _fcRef, "GND");
        }

        private bool IsOwnBody(SceneBody b)
        {
            if (b == Body) return true;
            foreach (var l in _links)
                if (l.Body == b)
                    return true;
            foreach (var d in _detached)
                if (d.Body == b)
                    return false;
            return false;
        }

        private void UpdateSensors()
        {
            foreach (var s in _sensors)
            {
                var m = s.Mount;
                var origin = Body.ToWorld(new Vec3(m.X, m.Y, m.Z + Assembly.DeckZ) - CenterOfMass);
                double yaw = m.Yaw * Math.PI / 180;
                var dir = Body.Orientation.Rotate(new Vec3(Math.Cos(yaw), Math.Sin(yaw), 0));
                switch (m.Kind)
                {
                    case SensorKind.Line:
                    {
                        // Average reflectance over the ~6 mm spot of whatever surface is below (floor,
                        // ramp, box …), fading with the distance to it.
                        double r, height;
                        var down = Body.Orientation.Rotate(-Vec3.UnitZ);
                        if (World.Scene.Raycast(origin, down, 0.05, out var hit, b => !IsOwnBody(b), false) && hit.Body != null)
                        {
                            r = Reflectance(hit.Body.Material);
                            height = hit.Distance;
                        }
                        else
                        {
                            r = 0;
                            const double spot = 0.003;
                            r += Arena.Reflectance(origin.X, origin.Y);
                            r += Arena.Reflectance(origin.X + spot, origin.Y);
                            r += Arena.Reflectance(origin.X - spot, origin.Y);
                            r += Arena.Reflectance(origin.X, origin.Y + spot);
                            r += Arena.Reflectance(origin.X, origin.Y - spot);
                            r /= 5;
                            height = Math.Max(0, origin.Z - Arena.GroundHeight(origin.X, origin.Y));
                        }

                        double falloff = AnalogMath.Clamp((0.03 - height) / 0.02, 0, 1);
                        double seen = r * falloff;
                        s.Value = seen;
                        s.Triggered = seen < 0.4;
                        if (s.Source != null)
                        {
                            double level = AnalogMath.Clamp(0.97 - seen, 0.02, 0.97);
                            if (Math.Abs(level - s.Source.Level) > 0.004) s.Source.Level = level;
                        }

                        if (s.Photo != null)
                        {
                            // The module's own phototransistor: photocurrent from the reflected IR (white ≈ full, black ≈ dark).
                            var spec = ReflectiveSensorSpec.Tcrt5000;
                            s.Photo.Photocurrent = Math.Max(0, spec.PeakCurrent * seen + spec.DarkCurrent);
                        }

                        break;
                    }

                    case SensorKind.Obstacle:
                    {
                        double d = World.Raycast(origin, dir, 2.0, IsOwnBody);
                        s.Value = d;
                        bool on = s.Triggered ? d < m.Range * 1.1 : d < m.Range;
                        s.Triggered = on;
                        if (s.Source != null)
                        {
                            double level = on ? 0.0 : 1.0;
                            if (s.Source.Level != level) s.Source.Level = level;
                        }

                        break;
                    }

                    case SensorKind.Ultrasonic:
                    {
                        double best = double.PositiveInfinity;
                        foreach (double off in new[] { -0.13, 0.0, 0.13 })
                        {
                            double a = yaw + off;
                            var d2 = Body.Orientation.Rotate(new Vec3(Math.Cos(a), Math.Sin(a), 0));
                            best = Math.Min(best, World.Raycast(origin, d2, 4.5, IsOwnBody));
                        }

                        s.Value = best;
                        s.Triggered = best < 0.2;
                        if (s.Ranger != null) s.Ranger.Distance = best;
                        break;
                    }
                }
            }
        }

        /// <summary>Infrared reflectance of an object's surface (white paper ≈ 0.85, black rubber ≈ 0.08).</summary>
        public static double Reflectance(string material) => Materials.Canonical(material) switch
        {
            "paper" => 0.85,
            "wood" or "mdf" => 0.6,
            "pla" or "petg" or "abs" or "nylon" or "plastic" or "acrylic" => 0.7,
            "aluminium" or "steel" or "brass" => 0.7,
            "rubber" or "foam" => 0.08,
            "carpet" or "grass" => 0.25,
            "asphalt" => 0.12,
            "concrete" or "tile" => 0.4,
            "ice" or "glass" => 0.5,
            "carbon" or "fr4" => 0.15,
            _ => 0.5
        };

        // ------------------------------------------------------------------ incidents

        private void DetectIncidents()
        {
            double weight = Body.Mass * Math.Max(0.1, World.Gravity.Length);
            var body = World.VehicleBody;

            // Physics events of the vehicle's own parts (joint breaks, link impacts).
            foreach (var e in World.Scene.DrainEvents())
            {
                if (e.Kind == PhysicsEventKind.JointBroken)
                {
                    var link = _links.FirstOrDefault(l => l.Joint.Id == e.Source);
                    string what = link?.ServoMotor != null ? "servo dişlisi sıyırdı / bağlantı koptu" : "bağlantı koptu";
                    _events.Add(new MechEvent(Time, e.Source, $"{what} ({e.Message})", true));
                }
                else if (e.Kind == PhysicsEventKind.BodyBroken)
                {
                    _events.Add(new MechEvent(Time, e.Source, e.Message, true));
                }
            }

            // Largest load of this step: chassis contacts, wheel springs, link contacts.
            double rigid = World.PeakImpact;
            foreach (var l in _links) rigid = Math.Max(rigid, l.Body.PeakContactForce);
            double springs = _wheels.Count > 0 ? _wheels.Max(w => w.Wheel.PeakLoad) : 0;
            double force = Math.Max(rigid, springs);
            double accel = Math.Max(body.PeakAcceleration, force / Math.Max(1e-6, TotalMass));
            PeakAcceleration = Math.Max(PeakAcceleration, accel);
            PeakImpactForce = Math.Max(PeakImpactForce, force);

            // A crash: the (soft) wheel springs carry over 20× the weight, or a hard contact does
            // so at a real collision speed (> 2 m/s, i.e. more than a ~20 cm drop). A stiff
            // touchdown at walking pace briefly reaches many g without being a crash.
            bool crash = springs > 20 * weight || (rigid > 10 * weight && body.PeakImpactSpeed > 2.0) || rigid > 100 * weight;
            if (crash && Time - _lastCrash > 0.5)
            {
                _lastCrash = Time;
                double speed = Math.Max(Body.Velocity.Length, body.PeakImpactSpeed);
                _impacts.Add(new ImpactRecord(Time, force, accel, body.PeakImpactSpeed, "gövde"));
                _events.Add(new MechEvent(Time, "gövde", $"sert çarpma ({force:F1} N, {accel / 9.81:F0} g, {speed:F2} m/s)", true));
                foreach (var p in _props)
                {
                    if (p.Broken || Math.Abs(p.Prop.AngularVelocity) < 400) continue;
                    var at = Body.ToWorld(p.Prop.Mount);
                    bool hit = World.IsInsideSolid(at, 0.01) ||
                               Math.Abs(at.X) > Arena.Width / 2 - 0.03 || Math.Abs(at.Y) > Arena.Depth / 2 - 0.03;
                    if (!hit) continue;
                    p.Broken = true;
                    _events.Add(new MechEvent(Time, p.Mount?.RefDes ?? "M" + (p.Index + 1), "pervane kırıldı", true));
                }
            }

            if (Assembly.Breakable) DetectStructuralFailures(body, accel);

            var up = Body.Orientation.Rotate(Vec3.UnitZ);
            if (up.Z < 0.1)
            {
                if (_upsideSince < 0) _upsideSince = Time;
                if (!_flippedReported && Time - _upsideSince > 0.4)
                {
                    _flippedReported = true;
                    _events.Add(new MechEvent(Time, "gövde", Kind == VehicleKind.Quadcopter ? "drone ters döndü" : "araç devrildi", true));
                    if (Flight != null) Flight.Armed = false;
                }
            }
            else
            {
                _upsideSince = -1;
            }
        }

        private void DetectStructuralFailures(SceneBody body, double accel)
        {
            // Quadcopter arms: a tip impact loads the arm through its own bending stiffness,
            // peak force ≈ v·√(k_arm·m_eff); it snaps when that exceeds σ·Z/L.
            if (_props.Count > 0 && body.PeakImpactForce > 0)
            {
                var local = Body.Orientation.InverseRotate(body.PeakImpactPoint - Body.Position);
                foreach (var p in _props)
                {
                    if (p.ArmBroken) continue;
                    var tip = p.Prop.Mount;
                    double dxy = Math.Sqrt((local.X - tip.X) * (local.X - tip.X) + (local.Y - tip.Y) * (local.Y - tip.Y));
                    if (dxy > Assembly.ArmLength * 0.4) continue;
                    double peak = body.PeakImpactSpeed * Math.Sqrt(_armStiffness * Math.Max(1e-5, body.PeakImpactMass));
                    if (peak > _armBreakForce) SnapArm(p, $"kol kırıldı ({peak:F0} N > {_armBreakForce:F0} N)");
                }
            }

            // Wheels: pulled off the shaft axially, or the gearbox mount breaks under radial load.
            foreach (var w in _wheels)
            {
                if (w.Wheel.Detached) continue;
                if (w.Wheel.PeakAxialLoad > _wheelAxialHold)
                    LoseWheel(w, $"teker milden çıktı (yanal {w.Wheel.PeakAxialLoad:F0} N > {_wheelAxialHold:F0} N)");
                else if (w.Wheel.PeakLoad > _wheelRadialStrength)
                    LoseWheel(w, $"teker bağlantısı kırıldı ({w.Wheel.PeakLoad:F0} N > {_wheelRadialStrength:F0} N)");
            }

            // Modules tear off when the force in their fastening exceeds what it holds. The mount
            // (foam tape, k ≈ 5 kN/m) is a spring: an impact pulse of duration τ much shorter than
            // the mount period only transmits ≈ (2/π)·ω_mount·τ of the quasi-static m·a.
            double a = accel;
            if (a > 50)
            {
                double tau = body.PeakImpactDuration > 0 ? body.PeakImpactDuration : 0.01;
                foreach (var (mount, mp, at) in _moduleMasses)
                {
                    if (_detached.Any(d => d.Mount == mount)) continue;
                    double hold = mount.HoldForce ?? DefaultHoldForce;
                    double wMount = Math.Sqrt(MountStiffness / mp.Mass);
                    double f = mp.Mass * a * Math.Min(1, 2 / Math.PI * wMount * tau);
                    if (f <= hold) continue;
                    DetachModule(mount, mp, at, f);
                }
            }
        }

        private void SnapArm(PropLink p, string message)
        {
            p.ArmBroken = true;
            p.Broken = true;
            p.Prop.Detached = true;
            _events.Add(new MechEvent(Time, p.Mount?.RefDes ?? "M" + (p.Index + 1), message, true));
        }

        private void LoseWheel(WheelLink w, string message)
        {
            w.Wheel.Detached = true;
            if (w.Motor != null)
            {
                w.Motor.LoadTorque = 0;
                w.Motor.LoadInertia = Math.Max(0, w.BaseLoadInertia - w.WheelInertia);
            }

            // The chassis now rests on its corner where the wheel was.
            var hub = w.Wheel.Mount;
            Body.ContactPoints.Add(new Vec3(hub.X, hub.Y * 0.8, hub.Z - w.Wheel.Radius * 0.3));
            _events.Add(new MechEvent(Time, string.IsNullOrEmpty(w.Mount.RefDes) ? "teker " + w.Mount.Role : w.Mount.RefDes, message, true));
        }

        private void DetachModule(ModuleMount mount, MassProperties mp, Vec3 at, double force)
        {
            // Remove the module's mass from the chassis (keeping the body origin) and let it fly off.
            var b = World.VehicleBody;
            var current = MassProperties.FromTensor(b.Mass, Vec3.Zero, b.InertiaTensor());
            var rel = at - CenterOfMass;
            double m = Math.Min(mp.Mass, b.Mass * 0.9);
            var parallel = (Mat3.Identity * rel.Dot(rel) - Mat3.Outer(rel, rel)) * m;
            var remaining = MassProperties.FromTensor(Math.Max(1e-3, b.Mass - m), Vec3.Zero, current.Inertia - mp.WithMass(m).Inertia - parallel);
            b.SetMassProperties(remaining);
            var world = b.ToWorld(rel);
            var piece = SceneBody.Box(mount.RefDes, new Vec3(0.05, 0.035, 0.012), m, world, "fr4");
            piece.Orientation = b.Orientation;
            piece.Velocity = b.PointVelocity(world);
            piece.AngularVelocity = b.AngularVelocity;
            piece.SensorVisible = true;
            World.Scene.AddBody(piece);
            _detached.Add(new DetachedModule(mount, piece, mp, at));
            _events.Add(new MechEvent(Time, mount.RefDes, $"modül yerinden koptu ({force:F0} N > {mount.HoldForce ?? DefaultHoldForce:F0} N tutma)", true));
        }
    }

    public enum FlightMode
    {
        /// <summary>Stick = tilt angle, throttle = collective.</summary>
        Angle,

        /// <summary>Stick = tilt angle, throttle stick moves the altitude set-point.</summary>
        AltitudeHold
    }

    /// <summary>
    /// Firmware of the brushed flight controller: attitude (angle → rate) cascade
    /// PID with an IMU, optional altitude hold, and an X-quad mixer writing the
    /// duty of the four MOSFET channels. Motor layout seen from above, nose = +x:
    /// M1 front-left (CW), M2 front-right (CCW), M3 rear-right (CW), M4 rear-left (CCW).
    /// </summary>
    public sealed class FlightController
    {
        private readonly List<string> _messages = new List<string>();
        private double _ix, _iy, _iz, _ialt;
        private double _lastPx, _lastPy;
        private bool _armed;
        private readonly Random _noise = new Random(7);

        public AveragedPwmDriver?[] Outputs { get; } = new AveragedPwmDriver?[4];
        public double[] Command { get; } = new double[4];

        public bool Powered { get; set; }
        public FlightMode Mode { get; set; } = FlightMode.AltitudeHold;

        public bool Armed
        {
            get => _armed;
            set
            {
                if (_armed == value) return;
                _armed = value;
                ResetIntegrators();
                _messages.Add(value ? "motorlar kuruldu (ARM)" : "motorlar kapatıldı (DISARM)");
            }
        }

        // Pilot inputs (−1..1, throttle 0..1)
        public double Throttle { get; set; }
        public double RollInput { get; set; }
        public double PitchInput { get; set; }
        public double YawInput { get; set; }

        public double TargetAltitude { get; set; } = 1.0;
        public double MaxAngle { get; set; } = 25 * Math.PI / 180;
        public double MaxYawRate { get; set; } = 3.0;

        /// <summary>Duty that roughly holds the craft in a hover (estimated from the motor model).</summary>
        public double HoverDuty { get; set; } = 0.4;

        /// <summary>Airframe-dependent multiplier on the roll/pitch rate gains.</summary>
        public double RateGain { get; set; } = 1.0;

        public double AngleP { get; set; } = 4.0;
        public double RateP { get; set; } = 0.0045;
        public double RateI { get; set; } = 0.006;
        public double RateD { get; set; } = 0.00012;
        public double YawP { get; set; } = 0.12;
        public double YawI { get; set; } = 0.05;
        /// <summary>Climb rate per metre of altitude error (1/s).</summary>
        public double AltP { get; set; } = 1.6;

        /// <summary>Collective per m/s of climb-rate error, and its integral gain.</summary>
        public double AltD { get; set; } = 0.12;

        public double AltI { get; set; } = 0.15;
        public double MaxClimbRate { get; set; } = 0.8;

        /// <summary>Gyro noise (rad/s, 1σ) — the IMU is not perfect.</summary>
        public double GyroNoise { get; set; } = 0.004;

        public double Roll { get; private set; }
        public double Pitch { get; private set; }
        public double Yaw { get; private set; }

        public IReadOnlyList<string> DrainMessages()
        {
            var copy = _messages.ToArray();
            _messages.Clear();
            return copy;
        }

        public void Reset()
        {
            _armed = false;
            ResetIntegrators();
            Throttle = RollInput = PitchInput = YawInput = 0;
            for (int i = 0; i < 4; i++) Command[i] = 0;
            Apply();
        }

        private void ResetIntegrators()
        {
            _ix = _iy = _iz = _ialt = 0;
            _lastPx = _lastPy = 0;
        }

        public void Update(RigidBody body, double dt)
        {
            if (!Powered || !Armed)
            {
                for (int i = 0; i < 4; i++) Command[i] = 0;
                Apply();
                return;
            }

            var (roll, pitch, yaw) = body.Orientation.ToEuler();
            Roll = roll;
            Pitch = pitch;
            Yaw = yaw;
            if (Math.Abs(roll) > 1.3 || Math.Abs(pitch) > 1.3)
            {
                Armed = false;
                _messages.Add("aşırı eğim: güvenlik için motorlar durduruldu");
                Update(body, dt);
                return;
            }

            var rates = body.BodyRates;
            double px = rates.X + Gauss() * GyroNoise, py = rates.Y + Gauss() * GyroNoise, pz = rates.Z + Gauss() * GyroNoise;

            // Outer loop: angle → rate
            double spX = AngleP * (RollInput * MaxAngle - roll);
            double spY = AngleP * (PitchInput * MaxAngle - pitch);
            double spZ = YawInput * MaxYawRate;

            // Inner loop: rate PID
            double ex = spX - px, ey = spY - py, ez = spZ - pz;
            _ix = Clamp(_ix + ex * dt, -2, 2);
            _iy = Clamp(_iy + ey * dt, -2, 2);
            _iz = Clamp(_iz + ez * dt, -2, 2);
            double g = RateGain;
            double ur = g * (RateP * ex + RateI * _ix - RateD * (px - _lastPx) / dt);
            double up = g * (RateP * ey + RateI * _iy - RateD * (py - _lastPy) / dt);
            double uy = YawP * ez + YawI * _iz;
            _lastPx = px;
            _lastPy = py;

            // Collective
            double t;
            double tilt = Math.Max(0.5, Math.Cos(roll) * Math.Cos(pitch));
            if (Mode == FlightMode.AltitudeHold)
            {
                TargetAltitude = Clamp(TargetAltitude + (Throttle - 0.5) * 2.0 * 0.8 * dt, 0, 4);
                // Altitude → climb-rate set-point → collective (PI on climb rate).
                double z = body.Position.Z;
                double vzSp = Clamp(AltP * (TargetAltitude - z), -MaxClimbRate, MaxClimbRate);
                double ev = vzSp - body.Velocity.Z;
                _ialt = Clamp(_ialt + ev * dt, -2, 2);
                t = (HoverDuty + AltD * ev + AltI * _ialt) / tilt;
            }
            else
            {
                t = Throttle;
            }

            ur = Clamp(ur, -0.3, 0.3);
            up = Clamp(up, -0.3, 0.3);
            uy = Clamp(uy, -0.15, 0.15);
            Command[0] = t + ur - up + uy;
            Command[1] = t - ur - up - uy;
            Command[2] = t - ur + up + uy;
            Command[3] = t + ur + up - uy;
            for (int i = 0; i < 4; i++) Command[i] = Clamp(Command[i], 0.04, 1.0);
            Apply();
        }

        private void Apply()
        {
            for (int i = 0; i < 4; i++)
            {
                var o = Outputs[i];
                if (o != null && Math.Abs(o.Duty - Command[i]) > 1e-4) o.Duty = Command[i];
            }
        }

        private double Gauss()
        {
            double u1 = 1.0 - _noise.NextDouble(), u2 = _noise.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
