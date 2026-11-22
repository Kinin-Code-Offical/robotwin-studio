using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;

namespace RobotTwin.CoreSim.Sandbox
{
    public static partial class WorldExamples
    {
        /// <summary>
        /// Hollow parts collide as their real shape: a 3D-printed tray catches steel balls and wooden cubes
        /// on its inner floor, and a ball dropped into an upright tube falls down the bore to the ground.
        /// Both parts are modeller meshes (2.5 mm walls); the physics splits each into convex pieces.
        /// </summary>
        public static WorldDocument Containers(PartLibrary lib)
        {
            _ = lib;
            var w = new WorldDocument { Title = "Kap ve bilyeler" };

            // tray: 90 × 60 × 30 mm, open top, 2.5 mm walls
            var tray = PrimitiveBuilder.RoundedBox(90, 60, 30, 0, 1, false).Translate(new Vec3(0, 0, 15));
            int top = Enumerable.Range(0, tray.Faces.Count).First(f => tray.FaceNormal(f).Z > 0.9);
            MeshEdit.DeleteFaces(tray, new[] { top });
            MeshCut.Solidify(tray, 2.5);
            var t = w.AddModel(tray.ToMeshModel(), "Baskı tepsi", new WorldPose { X = -0.04 });
            t.Color = "#3B82C4";

            // tube: Ø40 / Ø30 mm, 70 mm tall, standing
            var tube = PrimitiveBuilder.Tube(20, 15, 70, 32).Translate(new Vec3(0, 0, 35));
            var u = w.AddModel(tube.ToMeshModel(), "Boru", new WorldPose { X = 0.07 });
            u.Color = "#D9A441";

            // steel balls and wooden cubes falling into the tray
            for (int i = 0; i < 3; i++)
            {
                var ball = w.AddBody("sphere", 0.012, 0.012, 0.012, new WorldPose { X = -0.06 + i * 0.02, Y = (i - 1) * 0.008, Z = 0.06 + i * 0.015 }, "ÇELİK");
                ball.Name = "Çelik bilye " + (i + 1);
            }

            for (int i = 0; i < 2; i++)
            {
                var cube = w.AddBody("box", 0.012, 0.012, 0.012, new WorldPose { X = -0.05 + i * 0.03, Y = 0.012, Z = 0.1 + i * 0.02, Yaw = 25 * i }, "AHŞAP");
                cube.Name = "Tahta küp " + (i + 1);
            }

            // one ball into the tube's bore
            var drop = w.AddBody("sphere", 0.012, 0.012, 0.012, new WorldPose { X = 0.07, Z = 0.12 }, "ÇELİK");
            drop.Name = "Boruya düşen bilye";
            return w;
        }
    }
}
