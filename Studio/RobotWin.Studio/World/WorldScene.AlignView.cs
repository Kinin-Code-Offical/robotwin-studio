using System;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [align-view] Unity's Align With View and Align View to Selected: where the scene camera stands and looks, and a
    /// camera put at a point looking along a direction (a part's own +X, the way a camera or sensor part looks).
    /// </summary>
    public partial class WorldScene
    {
        /// <summary>The scene camera: its eye, and its yaw and pitch (radians; pitch is how far it looks down).</summary>
        public (Point3D Eye, double Yaw, double Pitch) ViewPose => (_camera.Position, _yaw, _pitch);

        /// <summary>The camera at an eye looking along a direction; what it orbits about lies the present distance ahead.</summary>
        public void LookFrom(Point3D eye, Vector3D forward)
        {
            if (forward.Length < 1e-9) return;
            forward.Normalize();
            _yaw = Math.Atan2(forward.Y, forward.X);
            _pitch = Math.Asin(Math.Max(-1, Math.Min(1, -forward.Z)));
            double d = Math.Max(0.05, _distance);
            _target = eye + forward * d;
            UpdateCamera();
        }
    }
}
