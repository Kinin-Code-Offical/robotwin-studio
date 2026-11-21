using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [hull] Collision shapes view (Görünüm → Çarpışma şekilleri): every rigid body of the physics
    /// drawn as the engine sees it — box, sphere, cylinder or the convex hull of the part's model —
    /// translucent over the models and following the bodies while the world runs.
    /// </summary>
    public sealed partial class WorldScene
    {
        private readonly ModelVisual3D _collisionVisual = new ModelVisual3D();
        private readonly List<(SceneBody Body, MatrixTransform3D Transform)> _collisionBodies = new List<(SceneBody, MatrixTransform3D)>();
        private bool _collisionAttached;

        /// <summary>Shows the bodies of <paramref name="physics"/>, or hides the view when null.</summary>
        public void SetCollisionShapes(WorldPhysics? physics)
        {
            if (!_collisionAttached)
            {
                _view.Children.Add(_collisionVisual);
                _collisionAttached = true;
            }

            _collisionBodies.Clear();
            if (physics == null)
            {
                _collisionVisual.Content = null;
                return;
            }

            var group = new Model3DGroup();
            var moving = Material(Color.FromArgb(0x66, 0x3C, 0xD0, 0xFF));
            var fixedMat = Material(Color.FromArgb(0x55, 0xF2, 0xA3, 0x3B));
            foreach (var b in physics.Scene.Bodies)
            {
                if (b.IsStatic && b.Tag is not WorldObject) continue;   // floor, obstacles and terrain are not parts
                var mesh = ShapeMesh(b.EffectiveShape);
                if (mesh == null) continue;
                var tf = new MatrixTransform3D(BodyMatrix(b));
                var mat = b.IsStatic ? fixedMat : moving;
                group.Children.Add(new GeometryModel3D(mesh, mat) { BackMaterial = mat, Transform = tf });
                _collisionBodies.Add((b, tf));
            }

            _collisionVisual.Content = group;
        }

        /// <summary>Moves the drawn shapes to the bodies' current poses.</summary>
        public void UpdateCollisionShapes()
        {
            foreach (var (b, tf) in _collisionBodies) tf.Matrix = BodyMatrix(b);
        }

        public bool CollisionShapesShown => _collisionVisual.Content != null;

        /// <summary>[gas] A nozzle's jet for this frame (exhaust, water or cold gas particles).</summary>
        public void Jet(Point3D at, Vector3D dir, double speed, double massFlow, Assembly3D.ParticleKind kind, double dt) =>
            _effects.EmitJet(at, dir, speed, massFlow, kind, dt);

        private static Material Material(Color c)
        {
            var mat = new MaterialGroup { Children = { new DiffuseMaterial(new SolidColorBrush(c)), new EmissiveMaterial(new SolidColorBrush(Color.FromArgb((byte)(c.A / 2), c.R, c.G, c.B))) } };
            mat.Freeze();
            return mat;
        }

        private static Matrix3D BodyMatrix(SceneBody b)
        {
            var q = b.Orientation;
            var m = Matrix3D.Identity;
            m.Scale(new Vector3D(1.004, 1.004, 1.004));   // a hair outside the model's own surface: no z-fighting
            m.Rotate(new Quaternion(q.X, q.Y, q.Z, q.W));
            m.Translate(new Vector3D(b.Position.X, b.Position.Y, b.Position.Z));
            return m;
        }

        /// <summary>The collision shape as a mesh in the body frame.</summary>
        private static MeshGeometry3D? ShapeMesh(CollisionShape? s)
        {
            if (s == null) return null;
            switch (s.Kind)
            {
                case ShapeKind.Box:
                {
                    var h = s.HalfExtents;
                    return Frozen(M.Box(0, 0, 0, 2 * h.X, 2 * h.Y, 2 * h.Z));
                }

                case ShapeKind.Sphere:
                    return Frozen(M.Sphere(s.Radius, 0, 0, 0, 20));
                case ShapeKind.Cylinder:
                    return Frozen(M.Cylinder(s.Radius, 2 * s.HalfHeight, 2, 40));
                case ShapeKind.ConvexHull:
                case ShapeKind.Points:
                {
                    if (s.Vertices.Count < 4) return null;
                    var hull = ConvexHull.Compute(s.Vertices);
                    if (hull.IsDegenerate) return null;
                    var hm = hull.ToMesh();
                    var mesh = new MeshGeometry3D();
                    // Flat-shaded: each triangle its own corners, so the hull's facets read as facets.
                    for (int i = 0; i + 2 < hm.Indices.Count; i += 3)
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            var v = hm.Vertices[hm.Indices[i + k]];
                            mesh.Positions.Add(new Point3D(v.X, v.Y, v.Z));
                            mesh.TriangleIndices.Add(mesh.Positions.Count - 1);
                        }
                    }

                    return Frozen(mesh);
                }

                default:
                    return null;
            }
        }

        private static MeshGeometry3D Frozen(MeshGeometry3D m)
        {
            m.Freeze();
            return m;
        }
    }
}
