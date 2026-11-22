using System;
using System.Globalization;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Sandbox
{
    public static partial class WorldExamples
    {
        private static string Inv(double v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// [gas] A 2 L PET bottle water rocket on its fins: <paramref name="waterLitres"/> of water and air
        /// pumped to <paramref name="gaugeBar"/> bar; the launcher holds the 21.5 mm neck closed for half
        /// a second, then lets go. Three balsa fins (single-panel lifting surfaces) keep it pointing into
        /// the wind; a foam nose cone on top. Everything — the water jet, the air burst after it, the
        /// falling mass, the drag and the fins' restoring lift — comes from the shared physics.
        /// </summary>
        public static WorldDocument WaterRocket(PartLibrary lib, double waterLitres = 0.6, double gaugeBar = 4.0)
        {
            _ = lib;
            var w = new WorldDocument { Title = "Su roketi" };
            const double d = 0.105, len = 0.30, finSpan = 0.07, finChord = 0.10, finLift = 0.04;
            // The bottle stands on its fins: their trailing edges reach 4 cm below its neck.
            var bottle = w.AddBody("cylinder", d, d, len, new WorldPose { Z = finLift }, "PET");
            bottle.Name = "2 L PET şişe";
            bottle.MassKg = 0.05;
            bottle.Color = "#A9D8F0";   // clear PET
            bottle.Parameters["vessel"] = "true";
            bottle.Parameters["volume"] = "2.0";
            bottle.Parameters["wall"] = "0.3";
            bottle.Parameters["water"] = Inv(waterLitres);
            bottle.Parameters["pressure"] = Inv(gaugeBar);
            bottle.Parameters["nozzle"] = "21.5";
            bottle.Parameters["nozzle.axis"] = "-z";
            bottle.Parameters["valve.at"] = "0.5";
            var nose = w.AddBody("sphere", d, d, d, new WorldPose { Z = finLift + len - d / 2 }, "PLA");
            nose.Name = "Burun konisi (köpük + 30 g kil)";
            nose.Color = "#E5484D";
            nose.MassKg = 0.06;   // clay in the nose keeps the centre of mass ahead of the fins' centre of pressure
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = nose.Id, ObjectB = bottle.Id });
            for (int k = 0; k < 3; k++)
            {
                double a = k * 120.0;
                double ca = Math.Cos(a * Math.PI / 180), sa = Math.Sin(a * Math.PI / 180);
                // A single panel: chord along its x (leading edge up), span along its y straight out from the bottle.
                var fin = w.AddBody("wing", finChord, finSpan, 0.003, new WorldPose { X = ca * d / 2, Y = sa * d / 2, Z = finChord / 2, Pitch = -90, Yaw = a - 90 }, "BALSA");
                fin.Name = "Kanatçık " + (k + 1);
                fin.MassKg = 0.012;
                fin.Color = "#F2A33B";
                fin.Parameters["symmetric"] = "false";
                fin.Parameters["airfoil"] = "flat";
                fin.Parameters["tipchord"] = "0.06";
                fin.Parameters["sweep"] = "25";
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = fin.Id, ObjectB = bottle.Id });
            }

            return w;
        }

        /// <summary>
        /// [gas] A KNSB ("rocket candy") motor on a static test stand: 6061 aluminium casing Ø32 × 160 mm,
        /// two BATES grains Ø25 / core 8 × 60 mm, a 6 mm throat expanding to 14 mm. Ignited at 0.1 s; the
        /// chamber pressure, thrust and burn time come out of the propellant's burn-rate law and the
        /// nozzle flow — nothing is read from a thrust curve. <paramref name="throatMm"/> lets a test
        /// choke the nozzle down until the casing bursts.
        /// </summary>
        public static WorldDocument SugarMotorStand(PartLibrary lib, double throatMm = 6.0)
        {
            _ = lib;
            var w = new WorldDocument { Title = "Katı yakıtlı motor (test standı)" };
            var motor = w.AddBody("cylinder", 0.032, 0.032, 0.16, new WorldPose { Z = 0.05 }, "ALÜMİNYUM");
            motor.Name = "KNSB motor";
            motor.MassKg = 0.12;
            motor.Color = "#B8BCC4";
            motor.IsStatic = true;   // clamped in the stand
            motor.Parameters["vessel"] = "true";
            motor.Parameters["wall"] = "1.5";
            motor.Parameters["grain"] = "KNSB";
            motor.Parameters["grain.diameter"] = "25";
            motor.Parameters["grain.core"] = "8";
            motor.Parameters["grain.length"] = "60";
            motor.Parameters["grain.segments"] = "2";
            motor.Parameters["nozzle"] = Inv(throatMm);
            motor.Parameters["nozzle.exit"] = "14";
            motor.Parameters["nozzle.axis"] = "-z";
            motor.Parameters["ignite.at"] = "0.1";
            return w;
        }
    }
}
