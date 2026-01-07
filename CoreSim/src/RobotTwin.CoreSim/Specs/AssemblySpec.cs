using System.Collections.Generic;

namespace RobotTwin.CoreSim.Specs
{
    public class AssemblySpec
    {
        public List<AssemblyPartSpec> Parts { get; set; } = new List<AssemblyPartSpec>();
        public List<AssemblyWireSpec> Wires { get; set; } = new List<AssemblyWireSpec>();
        public List<BreadboardSpec> Breadboards { get; set; } = new List<BreadboardSpec>();
    }

    public class AssemblyPartSpec
    {
        public string InstanceId { get; set; } = string.Empty;
        public string ComponentType { get; set; } = string.Empty;
        public string CatalogId { get; set; } = string.Empty;
        public Vec3 Position { get; set; } = new Vec3();
        public Vec3 Rotation { get; set; } = new Vec3();
        public Vec3 Scale { get; set; } = new Vec3 { X = 1, Y = 1, Z = 1 };
        public bool Pinned { get; set; }
        public double DensityKgPerM3 { get; set; } = 1200.0;
        public double MassKg { get; set; }
        public double Friction { get; set; } = 0.6;
    }

    public class AssemblyWireSpec
    {
        public string Id { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public double LengthMeters { get; set; }
        public double YoungModulus { get; set; } = 2.0e9;
        public double MaxStress { get; set; } = 2.0e8;
        public double Damping { get; set; } = 0.2;
    }

    public class BreadboardSpec
    {
        public string Id { get; set; } = string.Empty;
        public int PinCount { get; set; } = 400;
        public int Columns { get; set; } = 10;
        public int Rows { get; set; } = 40;
        public double Pitch { get; set; } = 0.00254;
        public Vec3 Position { get; set; } = new Vec3();
        public Vec3 Rotation { get; set; } = new Vec3();
        public Vec3 Scale { get; set; } = new Vec3 { X = 1, Y = 1, Z = 1 };
    }

    public struct Vec3
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}
