using System;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [couple] A gear, belt or lead screw between two joints of the world (Mechanics/PhysicsScene.Couplings.cs): a gear pair
    /// or a belt between two hinges, a rack and pinion, a belt-driven carriage or a lead screw between a hinge and a slider.
    /// A constraint, not a motor: the driven side's load comes back to the driving side, less what the mesh loses.
    /// </summary>
    public sealed class WorldCoupling
    {
        public const string Gear = "gear";
        public const string Belt = "belt";
        public const string Screw = "screw";

        /// <summary>[cam] A cam on a shaft (A, a hinge) lifting a follower (B, a slider): <see cref="SizeA"/> its lift (mm), <see cref="SizeB"/> its lobes.</summary>
        public const string Cam = "cam";

        /// <summary>Friction of a bronze or plastic nut on a steel screw, lubricated.</summary>
        public const double NutFriction = 0.15;

        public string Id { get; set; } = string.Empty;

        /// <summary><see cref="Gear"/>, <see cref="Belt"/> or <see cref="Screw"/>.</summary>
        public string Kind { get; set; } = Gear;

        /// <summary>The driving joint: the motor's gear, the pulley, the screw (the hinge, between a hinge and a slider).</summary>
        public string JointA { get; set; } = string.Empty;

        public string JointB { get; set; } = string.Empty;

        /// <summary>
        /// Two hinges: the teeth of A's gear, or A's pulley diameter (mm). A hinge and a slider: the pinion's or the pulley's
        /// pitch diameter (mm), or the screw's lead (mm a turn).
        /// </summary>
        public double SizeA { get; set; } = 20;

        /// <summary>
        /// Two hinges: the teeth of B's gear, or B's pulley diameter (mm). A lead screw: its diameter (mm), which with the
        /// lead sets the thread's angle and so what it loses. Unused for a rack or a belt-driven carriage.
        /// </summary>
        public double SizeB { get; set; } = 40;

        /// <summary>A crossed belt, an idler gear between the two, a left-hand thread (a screw is right-handed): the other way round.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Reverse { get; set; }

        /// <summary>
        /// Share of the power the mesh passes on (0 … 1); 0 = the mechanism's own (<see cref="EfficiencyFor"/>). At 0.5 or
        /// less the driven side cannot turn the driving one back: a self-locking screw.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Efficiency { get; set; }

        /// <summary>Free play at B, all of it: degrees for a gear or pulley, millimetres for a rack, a carriage or a nut; 0 = none.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Backlash { get; set; }

        /// <summary>A belt's stretch: its span's stiffness (N/mm); 0 = rigid.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double BeltStiffness { get; set; }

        /// <summary>[drive] The two parts in it (objects): a belt's pulleys, so the scene can draw it; a pinion and its rack or two gears, kept clear of each other. Null for a coupling between joints alone.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? PulleyA { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? PulleyB { get; set; }

        /// <summary>A hinge and a slider (a rack, a belt-driven carriage, a lead screw) rather than two shafts.</summary>
        public static bool Linear(WorldJointKind a, WorldJointKind b) =>
            a != b && a is (WorldJointKind.Hinge or WorldJointKind.Slider) && b is (WorldJointKind.Hinge or WorldJointKind.Slider);

        /// <summary>Whether this kind can join joints of these kinds: gears and belts two hinges or a hinge and a slider, a screw only the latter.</summary>
        public static bool Fits(string kind, WorldJointKind a, WorldJointKind b) =>
            kind == Cam ? a == WorldJointKind.Hinge && b == WorldJointKind.Slider
                        : Linear(a, b) || (a == WorldJointKind.Hinge && b == WorldJointKind.Hinge && kind != Screw);

        /// <summary>
        /// [cam] The follower's lift (m) and its slope (m/rad) at a shaft angle (rad): a harmonic cam, L/2·(1 − cos nθ), up
        /// and down once a lobe; an eccentric is one lobe. <see cref="Reverse"/> lifts the other way along the slider.
        /// </summary>
        public (double Value, double Slope) CamLift(double angle)
        {
            double l = SizeA / 1000, n = Math.Max(1, Math.Round(SizeB));
            double v = l / 2 * (1 - Math.Cos(n * angle)), s = l / 2 * n * Math.Sin(n * angle);
            return Reverse ? (-v, -s) : (v, s);
        }

        /// <summary>
        /// B's speed over A's (rad/s or m/s each): the teeth or diameters of two shafts (a gear turns the other way), the
        /// metres of travel per radian between a hinge and a slider. Null when the coupling does not fit the joints.
        /// </summary>
        public double? Ratio(WorldJointKind a, WorldJointKind b)
        {
            if (!Fits(Kind, a, b) || SizeA <= 0) return null;
            if (Kind == Cam) return CamLift(0).Slope;   // the profile takes over each step
            double r;
            if (Linear(a, b))
            {
                // A right-hand thread: the screw turning positively about the axis moves its nut back along it.
                double perRadian = Kind == Screw ? -SizeA / 1000 / (2 * Math.PI) : SizeA / 2000;
                r = a == WorldJointKind.Hinge ? perRadian : 1 / perRadian;
            }
            else
            {
                if (SizeB <= 0) return null;
                r = Kind == Gear ? -SizeA / SizeB : SizeA / SizeB;
            }

            return Reverse ? -r : r;
        }

        /// <summary>
        /// What the mesh passes on: <see cref="Efficiency"/> when set, else spur gears 0.98, a rack 0.95, a toothed belt 0.97,
        /// a lead screw from its thread (<see cref="ScrewEfficiency"/>).
        /// </summary>
        public double EfficiencyFor(WorldJointKind a, WorldJointKind b)
        {
            if (Efficiency > 0) return Math.Min(1, Efficiency);
            if (Kind == Screw) return ScrewEfficiency(SizeA, SizeB > 0 ? SizeB : 8);
            if (Kind == Cam) return 0.95;   // a roller follower
            if (Kind == Gear) return Linear(a, b) ? 0.95 : 0.98;
            return 0.97;
        }

        /// <summary>
        /// A trapezoidal thread (30°) of this lead and diameter with a nut of friction <paramref name="mu"/>:
        /// η = tan λ / tan(λ + φ), λ the lead angle at the mean diameter, φ = atan(μ / cos 15°). A T8×8 printer screw
        /// passes about 66 % and its carriage falls with the motor off; a T8×2 about 36 % and holds.
        /// </summary>
        public static double ScrewEfficiency(double leadMm, double diameterMm, double mu = NutFriction)
        {
            if (leadMm <= 0 || diameterMm <= 0) return 1;
            double tanLead = leadMm / (Math.PI * 0.875 * diameterMm);   // the mean diameter of a standard trapezoidal thread
            double lambda = Math.Atan(tanLead), phi = Math.Atan(mu / Math.Cos(15 * Math.PI / 180));
            return tanLead / Math.Tan(lambda + phi);
        }

        /// <summary>The coupling's name: a gear pair, a rack and pinion, a belt, a belt-driven carriage, a lead screw.</summary>
        public string Title(WorldJointKind a, WorldJointKind b) => Kind switch
        {
            Screw => "Vidalı mil",
            Cam => "Kam ve itici",
            Belt => Linear(a, b) ? "Kayışlı taşıyıcı" : "Kayış",
            _ => Linear(a, b) ? "Kremayer ve pinyon" : "Dişli çifti"
        };
    }
}
