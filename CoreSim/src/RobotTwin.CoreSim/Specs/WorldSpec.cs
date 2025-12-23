using System.Collections.Generic;

namespace RobotTwin.CoreSim.Specs
{
    public class WorldSpec
    {
        public string Name { get; set; }
        public double Width { get; set; }
        public double Depth { get; set; }
        
        // MVP: Track spline or bitmap
        public List<TrackPoint> TrackPath { get; set; } = new List<TrackPoint>();
        public string FloorTexturePath { get; set; }
    }

    public class TrackPoint
    {
        public double X { get; set; }
        public double Y { get; set; } // In 2D top-down view, Y is usually Z in 3D, but keeping 2D coords for simplicity 
        public double Width { get; set; }
    }
}
