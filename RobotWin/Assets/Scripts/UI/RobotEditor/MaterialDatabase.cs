using System;
using System.Collections.Generic;

namespace RobotTwin.UI.RobotEditor
{
    /// <summary>
    /// Database of material properties for structural analysis
    /// </summary>
    public class MaterialDatabase
    {
        private Dictionary<MaterialType, MaterialProperties> _materials;

        public MaterialDatabase()
        {
            _materials = new Dictionary<MaterialType, MaterialProperties>();
            InitializeMaterials();
        }

        private void InitializeMaterials()
        {
            // Aluminum 6061-T6
            _materials[MaterialType.Aluminum] = new MaterialProperties
            {
                Name = "Aluminum 6061-T6",
                Density = 2700f, // kg/m³
                YieldStrength = 276f, // MPa
                UltimateTensileStrength = 310f, // MPa
                YoungsModulus = 68.9f, // GPa
                ThermalConductivity = 167f, // W/m·K
                SpecificHeatCapacity = 896f, // J/kg·K
                CoefficientOfThermalExpansion = 23.6e-6f // 1/K
            };

            // Steel AISI 1045
            _materials[MaterialType.Steel] = new MaterialProperties
            {
                Name = "Steel AISI 1045",
                Density = 7850f,
                YieldStrength = 530f,
                UltimateTensileStrength = 625f,
                YoungsModulus = 200f,
                ThermalConductivity = 49.8f,
                SpecificHeatCapacity = 486f,
                CoefficientOfThermalExpansion = 11.5e-6f
            };

            // ABS Plastic
            _materials[MaterialType.Plastic_ABS] = new MaterialProperties
            {
                Name = "ABS Plastic",
                Density = 1040f,
                YieldStrength = 45f,
                UltimateTensileStrength = 46f,
                YoungsModulus = 2.3f,
                ThermalConductivity = 0.25f,
                SpecificHeatCapacity = 1425f,
                CoefficientOfThermalExpansion = 90e-6f
            };

            // PLA (3D printing material)
            _materials[MaterialType.Plastic_PLA] = new MaterialProperties
            {
                Name = "PLA Plastic",
                Density = 1240f,
                YieldStrength = 50f,
                UltimateTensileStrength = 65f,
                YoungsModulus = 3.5f,
                ThermalConductivity = 0.13f,
                SpecificHeatCapacity = 1800f,
                CoefficientOfThermalExpansion = 68e-6f
            };

            // Carbon Fiber Composite
            _materials[MaterialType.CarbonFiber] = new MaterialProperties
            {
                Name = "Carbon Fiber Composite",
                Density = 1600f,
                YieldStrength = 600f,
                UltimateTensileStrength = 700f,
                YoungsModulus = 70f,
                ThermalConductivity = 5f,
                SpecificHeatCapacity = 1050f,
                CoefficientOfThermalExpansion = 1.5e-6f
            };

            // Titanium Ti-6Al-4V
            _materials[MaterialType.Titanium] = new MaterialProperties
            {
                Name = "Titanium Ti-6Al-4V",
                Density = 4430f,
                YieldStrength = 880f,
                UltimateTensileStrength = 950f,
                YoungsModulus = 114f,
                ThermalConductivity = 6.7f,
                SpecificHeatCapacity = 526f,
                CoefficientOfThermalExpansion = 8.6e-6f
            };

            // Brass
            _materials[MaterialType.Brass] = new MaterialProperties
            {
                Name = "Brass",
                Density = 8500f,
                YieldStrength = 200f,
                UltimateTensileStrength = 400f,
                YoungsModulus = 100f,
                ThermalConductivity = 120f,
                SpecificHeatCapacity = 380f,
                CoefficientOfThermalExpansion = 19.0e-6f
            };

            // Copper
            _materials[MaterialType.Copper] = new MaterialProperties
            {
                Name = "Copper",
                Density = 8960f,
                YieldStrength = 70f,
                UltimateTensileStrength = 220f,
                YoungsModulus = 120f,
                ThermalConductivity = 385f,
                SpecificHeatCapacity = 385f,
                CoefficientOfThermalExpansion = 16.5e-6f
            };

            // Stainless Steel 304
            _materials[MaterialType.StainlessSteel] = new MaterialProperties
            {
                Name = "Stainless Steel 304",
                Density = 8000f,
                YieldStrength = 215f,
                UltimateTensileStrength = 505f,
                YoungsModulus = 193f,
                ThermalConductivity = 16.2f,
                SpecificHeatCapacity = 500f,
                CoefficientOfThermalExpansion = 17.3e-6f
            };

            // Nylon 6/6
            _materials[MaterialType.Nylon] = new MaterialProperties
            {
                Name = "Nylon 6/6",
                Density = 1140f,
                YieldStrength = 75f,
                UltimateTensileStrength = 85f,
                YoungsModulus = 2.8f,
                ThermalConductivity = 0.25f,
                SpecificHeatCapacity = 1670f,
                CoefficientOfThermalExpansion = 80e-6f
            };

            // BLACK ELECTRICAL TAPE - High sensor error material
            _materials[MaterialType.BlackTape] = new MaterialProperties
            {
                Name = "Black Electrical Tape",
                Density = 900f,
                YieldStrength = 5f,
                UltimateTensileStrength = 8f,
                YoungsModulus = 0.5f,
                ThermalConductivity = 0.15f,
                SpecificHeatCapacity = 1800f,
                CoefficientOfThermalExpansion = 120e-6f,
                // Sensor properties
                OpticalReflectivity = 0.02f,      // Very low - absorbs light
                IRReflectivity = 0.05f,            // Poor IR reflection
                UltrasonicAbsorption = 0.8f,       // High absorption
                ColorSensorError = 0.95f,          // 95% error rate
                LineSensorDetectability = 0.1f     // Hard to detect edges
            };

            // FLOOR SURFACE - Multiple types with varying properties
            _materials[MaterialType.FloorTile] = new MaterialProperties
            {
                Name = "Floor Tile",
                Density = 2300f,
                YieldStrength = 40f,
                UltimateTensileStrength = 50f,
                YoungsModulus = 30f,
                ThermalConductivity = 1.2f,
                SpecificHeatCapacity = 840f,
                CoefficientOfThermalExpansion = 8e-6f,
                // Sensor properties
                OpticalReflectivity = 0.4f,
                IRReflectivity = 0.35f,
                UltrasonicAbsorption = 0.2f,
                ColorSensorError = 0.3f,
                LineSensorDetectability = 0.6f
            };

            _materials[MaterialType.FloorCarpet] = new MaterialProperties
            {
                Name = "Floor Carpet",
                Density = 400f,
                YieldStrength = 2f,
                UltimateTensileStrength = 3f,
                YoungsModulus = 0.1f,
                ThermalConductivity = 0.06f,
                SpecificHeatCapacity = 1340f,
                CoefficientOfThermalExpansion = 150e-6f,
                // Sensor properties
                OpticalReflectivity = 0.25f,
                IRReflectivity = 0.2f,
                UltrasonicAbsorption = 0.9f,       // Very high absorption
                ColorSensorError = 0.6f,           // Color varies with texture
                LineSensorDetectability = 0.3f     // Difficult on textured surface
            };

            // PAPER - Multi-layer A4 stacks
            _materials[MaterialType.Paper_A4] = new MaterialProperties
            {
                Name = "A4 Paper (Single Sheet)",
                Density = 700f,
                YieldStrength = 3f,
                UltimateTensileStrength = 5f,
                YoungsModulus = 1.5f,
                ThermalConductivity = 0.05f,
                SpecificHeatCapacity = 1400f,
                CoefficientOfThermalExpansion = 100e-6f,
                // Sensor properties
                OpticalReflectivity = 0.85f,       // High reflectivity (white)
                IRReflectivity = 0.7f,
                UltrasonicAbsorption = 0.5f,
                ColorSensorError = 0.15f,
                LineSensorDetectability = 0.8f
            };

            _materials[MaterialType.Paper_MultiLayer] = new MaterialProperties
            {
                Name = "A4 Paper Stack (Multi-layer)",
                Density = 700f,
                YieldStrength = 8f,
                UltimateTensileStrength = 12f,
                YoungsModulus = 3f,
                ThermalConductivity = 0.08f,
                SpecificHeatCapacity = 1400f,
                CoefficientOfThermalExpansion = 100e-6f,
                // Sensor properties - stacked layers cause issues
                OpticalReflectivity = 0.85f,
                IRReflectivity = 0.65f,
                UltrasonicAbsorption = 0.7f,       // Multiple reflections
                ColorSensorError = 0.25f,          // Shadows between layers
                LineSensorDetectability = 0.65f    // Edge detection harder
            };

            // CARDBOARD - Common packaging material
            _materials[MaterialType.Cardboard] = new MaterialProperties
            {
                Name = "Cardboard",
                Density = 650f,
                YieldStrength = 4f,
                UltimateTensileStrength = 6f,
                YoungsModulus = 2f,
                ThermalConductivity = 0.06f,
                SpecificHeatCapacity = 1500f,
                CoefficientOfThermalExpansion = 110e-6f,
                // Sensor properties
                OpticalReflectivity = 0.6f,
                IRReflectivity = 0.5f,
                UltrasonicAbsorption = 0.6f,
                ColorSensorError = 0.4f,           // Brown color varies
                LineSensorDetectability = 0.5f
            };
        }

        public MaterialProperties GetMaterial(MaterialType type)
        {
            if (_materials.TryGetValue(type, out var material))
                return material;

            return _materials[MaterialType.Aluminum]; // Default
        }

        public MaterialProperties GetStrongerMaterial(MaterialProperties current)
        {
            // Find material with higher yield strength
            MaterialProperties best = current;

            foreach (var material in _materials.Values)
            {
                if (material.YieldStrength > best.YieldStrength)
                {
                    best = material;
                }
            }

            return best;
        }

        public List<MaterialType> GetAllMaterialTypes()
        {
            return new List<MaterialType>(_materials.Keys);
        }
    }

    [Serializable]
    public class MaterialProperties
    {
        public string Name;
        public float Density; // kg/m³
        public float YieldStrength; // MPa
        public float UltimateTensileStrength; // MPa
        public float YoungsModulus; // GPa
        public float ThermalConductivity; // W/m·K
        public float SpecificHeatCapacity; // J/kg·K
        public float CoefficientOfThermalExpansion; // 1/K

        // Sensor Detection Properties
        public float OpticalReflectivity = 0.5f;      // 0-1: Optical sensor visibility
        public float IRReflectivity = 0.5f;           // 0-1: Infrared reflectivity
        public float UltrasonicAbsorption = 0.3f;     // 0-1: Sound absorption (higher = harder to detect)
        public float ColorSensorError = 0.1f;         // 0-1: Color sensor error rate
        public float LineSensorDetectability = 0.7f;  // 0-1: Line sensor edge detection quality
    }

    public enum MaterialType
    {
        Aluminum,
        Steel,
        Plastic_ABS,
        Plastic_PLA,
        CarbonFiber,
        Titanium,
        Brass,
        Copper,
        StainlessSteel,
        Nylon,
        // High sensor error materials
        BlackTape,
        FloorTile,
        FloorCarpet,
        Paper_A4,
        Paper_MultiLayer,
        Cardboard
    }

    public enum VehicleType
    {
        GroundRover,
        Drone,
        Walker,
        Arm
    }

    public enum BoardType
    {
        Arduino_Uno,
        Arduino_Mega,
        RaspberryPi
    }

    public enum JointType
    {
        Revolute,
        Prismatic,
        Fixed,
        Spherical,
        Universal
    }
}
