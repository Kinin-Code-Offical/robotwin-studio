using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sdk;

namespace RobotWin.Studio.Examples
{
    /// <summary>[sdk] Example world for the environment and the SDK (listed in WorldTemplates.All as "wind-lab").</summary>
    public static class SdkWorldTemplates
    {
        public static WorldDocument WindLab()
        {
            var w = new WorldDocument { Title = "Rüzgâr ve kuvvet alanları" };
            w.Environment.Width = 3;
            w.Environment.Depth = 2;
            var s = w.Sdk;
            s.Wind.Speed = 3;
            s.Wind.DirectionDeg = 30;
            s.Wind.Turbulence = "dryden";
            s.Wind.Intensity = 0.6;
            s.Wind.LengthScale = 8;
            s.Wind.Gusts = true;
            s.Wind.GustAmplitude = 2.5;
            s.Wind.GustInterval = 5;

            // A desk fan blows a ping-pong ball across the floor.
            var ball = w.AddBody("sphere", 0.04, 0.04, 0.04, new WorldPose { X = -0.9, Y = 0.5 });
            ball.Name = "Pinpon topu";
            ball.MassKg = 0.0027;
            s.Fields.Add(new ForceFieldSpec { Id = s.NextId("F"), Name = "Masa fanı", Kind = "fan", X = -0.75, Y = 0.5, Z = 0.1, SizeX = 0.9, SizeY = 0.3, SizeZ = 0.2, Strength = 6 });

            // A conveyor zone carries a parcel.
            var parcel = w.AddBody("box", 0.08, 0.08, 0.06, new WorldPose { X = -0.65, Y = -0.5 }, "AHŞAP");
            parcel.Name = "Koli";
            s.Fields.Add(new ForceFieldSpec { Id = s.NextId("F"), Name = "Konveyör", Kind = "conveyor", X = 0, Y = -0.5, Z = 0.05, SizeX = 1.6, SizeY = 0.2, SizeZ = 0.15, Strength = 0.25 });

            // An expression holds a plate at 30 cm (a PD height controller written as a force law).
            var plate = w.AddBody("box", 0.12, 0.12, 0.01, new WorldPose { X = 0.45, Y = 0.45 });
            plate.Name = "Süzülen plaka";
            s.Behaviors.Add(new BehaviorAttachment
            {
                Id = s.NextId("B"), ObjectId = plate.Id, Name = "Yükseklik tutucu",
                Expression = "# yükseklik PD denetleyicisi\nF.z = m*g + k*(0.3 - z) - c*vz",
                Parameters = { ["k"] = "3", ["c"] = "0.6" }
            });

            // A fixed magnet pulls a steel ball (built-in behaviour).
            var magnet = w.AddBody("box", 0.03, 0.03, 0.03, new WorldPose { X = 0.95, Y = -0.4 }, "ÇELİK");
            magnet.Name = "Mıknatıs";
            magnet.IsStatic = true;
            var steel = w.AddBody("sphere", 0.02, 0.02, 0.02, new WorldPose { X = 1.05, Y = -0.4 }, "ÇELİK");
            steel.Name = "Çelik bilye";
            s.Behaviors.Add(new BehaviorAttachment
            {
                Id = s.NextId("B"), ObjectId = steel.Id, Type = "builtin:RobotTwin.CoreSim.Sdk.Builtins.MagneticAttraction",
                Parameters = { ["Target"] = magnet.Name, ["Strength"] = "2", ["Exponent"] = "2", ["Range"] = "0.4" }
            });
            return w;
        }
    }
}
