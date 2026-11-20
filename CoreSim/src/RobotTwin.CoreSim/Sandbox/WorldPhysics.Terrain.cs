using System;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    // [terrain] The world's sculpted terrain and static props in the physics scene.
    public sealed partial class WorldPhysics
    {
        /// <summary>The world's terrain becomes the ground (shared with the editor: sculpting acts at once).</summary>
        private void ConfigureTerrain(WorldDocument world)
        {
            if (world.Terrain is { } map) Scene.Terrain = new TerrainSurface(map);
        }

        /// <summary>
        /// Static props (stairs, pipes, trees, walls …) collide as their convex pieces instead of
        /// one convex hull of the whole mesh. Moving props (cones, boxes) take the normal path.
        /// </summary>
        private bool TryAddProp(WorldObject o)
        {
            if (!o.IsStatic || o.Kind != WorldObjectKind.Model || !o.Parameters.TryGetValue(TerrainProps.Key, out var id)) return false;
            var def = TerrainProps.Find(id);
            if (def == null) return false;
            var size = TerrainProps.SizeOf(o.Parameters, def);
            var q = o.Pose.Orientation;
            string material = MaterialOf(o);
            int k = 0;
            foreach (var c in TerrainProps.Colliders(def.Id, size, WorldBuilding.SeedOf(o)))
            {
                SceneBody body;
                if (c.IsBox)
                {
                    body = SceneBody.StaticBox(o.Id + "#" + k, c.Size, o.Pose.ToWorld(c.Center), q * c.Orientation, material);
                }
                else
                {
                    var pts = new Vec3[c.Points!.Count];
                    for (int i = 0; i < pts.Length; i++) pts[i] = o.Pose.ToWorld(c.Points[i]);
                    body = SceneBody.StaticHull(o.Id + "#" + k, pts, material);
                }

                body.Tag = o;
                Scene.AddBody(body);
                k++;
            }

            return true;
        }
    }
}
