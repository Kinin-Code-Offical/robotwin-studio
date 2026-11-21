using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [hull] Görünüm → Çarpışma şekilleri: the rigid bodies the physics really collides — the running
    /// simulation's, or (stopped) the ones a run would build from the parts' 3D models.
    /// </summary>
    public partial class WorldPage
    {
        public bool CollisionView { get; private set; }

        private object? _collisionSource;

        public void SetCollisionView(bool on)
        {
            CollisionView = on;
            _collisionSource = null;
            if (!on) Scene.SetCollisionShapes(null);
            else UpdateCollisionView();
        }

        /// <summary>Rebuilds the drawn bodies (world loaded, edited, or the run stopped).</summary>
        private void RefreshCollisionView()
        {
            _collisionSource = null;
            UpdateCollisionView();
        }

        private void UpdateCollisionView()
        {
            if (!CollisionView || World is not { } world) return;
            var running = _session.State == SimState.Stopped ? null : _session.Physics;
            object source = (object?)running ?? world;
            if (!ReferenceEquals(source, _collisionSource))
            {
                _collisionSource = source;
                Scene.SetCollisionShapes(running ?? RobotTwin.CoreSim.Sandbox.WorldPhysics.Build(world, Scene.GeometryOf, null));
            }
            else
            {
                Scene.UpdateCollisionShapes();
            }
        }
    }
}
