using UnityEngine;
using System.Collections.Generic;
using System;

namespace RobotTwin.UI.RobotEditor
{
    /// <summary>
    /// Robot Analyzer - Comprehensive Structural & Thermal Analysis
    /// Stress calculations, heat maps, joint analysis, material properties
    /// Weight distribution, movable parts, circuit compatibility
    /// </summary>
    public class RobotAnalyzer : MonoBehaviour
    {
        // Analysis Results
        public StressAnalysisResult StressAnalysis { get; private set; }
        public ThermalAnalysisResult ThermalAnalysis { get; private set; }
        public JointAnalysisResult JointAnalysis { get; private set; }
        public WeightDistributionResult WeightDistribution { get; private set; }
        public CircuitCompatibilityResult CircuitCompatibility { get; private set; }

        // Analysis Configuration
        private AnalysisConfiguration _config;

        // Material Database
        private MaterialDatabase _materialDb;

        public void Initialize()
        {
            _materialDb = new MaterialDatabase();
            _config = new AnalysisConfiguration
            {
                EnableStressAnalysis = true,
                EnableThermalAnalysis = true,
                EnableJointAnalysis = true,
                StressResolution = 0.01f,
                ThermalResolution = 0.5f,
                SafetyFactor = 2.0f,
                MaxOperatingTemp = 85.0f
            };
        }

        /// <summary>
        /// Perform complete structural stress analysis
        /// Uses Finite Element Method (FEM) approximation
        /// </summary>
        public StressAnalysisResult AnalyzeStress(RobotConfiguration robot)
        {
            var result = new StressAnalysisResult();
            result.Timestamp = DateTime.Now;

            // Calculate total forces on structure
            float totalMass = CalculateTotalMass(robot);
            float gravityForce = totalMass * 9.81f; // Newtons

            // Analyze each structural component
            result.ComponentStresses = new Dictionary<string, ComponentStress>();

            foreach (var component in robot.StructuralComponents)
            {
                var stress = new ComponentStress
                {
                    ComponentName = component.Name,
                    Material = component.Material
                };

                // Calculate stress based on load and cross-section
                float crossSectionArea = component.Width * component.Height; // m²
                float appliedLoad = gravityForce * component.LoadFactor; // N

                // Stress = Force / Area (Pa)
                stress.MaxStress = appliedLoad / (crossSectionArea * 1e6f); // Convert to MPa

                // Get material properties
                var material = _materialDb.GetMaterial(component.Material);
                stress.YieldStrength = material.YieldStrength; // MPa
                stress.UltimateTensileStrength = material.UltimateTensileStrength; // MPa

                // Safety factor check
                stress.SafetyFactor = material.YieldStrength / stress.MaxStress;
                stress.IsWithinSafetyLimits = stress.SafetyFactor >= _config.SafetyFactor;

                // Stress concentration at joints
                if (component.HasJoints)
                {
                    stress.StressConcentrationFactor = CalculateStressConcentration(component);
                    stress.MaxStress *= stress.StressConcentrationFactor;
                    stress.SafetyFactor = material.YieldStrength / stress.MaxStress;
                }

                result.ComponentStresses[component.Name] = stress;

                // Track maximum stress
                if (stress.MaxStress > result.MaxOverallStress)
                {
                    result.MaxOverallStress = stress.MaxStress;
                    result.CriticalComponent = component.Name;
                }
            }

            // Overall assessment
            result.IsStructurallySafe = CheckAllComponentsSafe(result.ComponentStresses);
            result.RecommendedMaterialUpgrades = GenerateMaterialRecommendations(result);

            return result;
        }

        /// <summary>
        /// Generate thermal heat map for robot
        /// Simulates heat dissipation from motors, electronics, batteries
        /// </summary>
        public ThermalAnalysisResult AnalyzeThermal(RobotConfiguration robot, float ambientTemp)
        {
            var result = new ThermalAnalysisResult();
            result.AmbientTemperature = ambientTemp;
            result.Timestamp = DateTime.Now;

            // Heat map grid (10x10x10 voxel grid for 3D analysis)
            result.HeatMap = new float[10, 10, 10];
            result.ComponentTemperatures = new Dictionary<string, float>();

            // Calculate heat sources
            float totalPowerDissipation = 0f;

            // Motor heat
            foreach (var motor in robot.Motors)
            {
                float motorPower = motor.Voltage * motor.Current; // Watts
                float efficiency = motor.Efficiency; // 0.0 - 1.0
                float heatDissipated = motorPower * (1f - efficiency); // Watts

                totalPowerDissipation += heatDissipated;

                // Temperature rise: ΔT = P * θ_JA (thermal resistance)
                float thermalResistance = 5.0f; // K/W (typical for small motor)
                float tempRise = heatDissipated * thermalResistance;
                float motorTemp = ambientTemp + tempRise;

                result.ComponentTemperatures[motor.Name] = motorTemp;

                // Propagate heat to nearby components (simplified diffusion)
                Vector3Int gridPos = WorldToGridPosition(motor.Position);
                PropagateHeat(result.HeatMap, gridPos, heatDissipated, 0.1f);
            }

            // Electronics heat (MCU, sensors, etc.)
            float electronicsPower = CalculateElectronicsPower(robot);
            totalPowerDissipation += electronicsPower;
            float electronicsTemp = ambientTemp + electronicsPower * 10.0f; // K/W
            result.ComponentTemperatures["Electronics"] = electronicsTemp;

            // Battery heat
            float batteryPower = robot.BatteryCapacity * 0.001f; // Simplified
            float batteryTemp = ambientTemp + batteryPower * 8.0f;
            result.ComponentTemperatures["Battery"] = batteryTemp;
            totalPowerDissipation += batteryPower;

            // Find maximum temperature
            result.MaxTemperature = ambientTemp;
            result.HotSpotLocation = Vector3.zero;

            foreach (var kvp in result.ComponentTemperatures)
            {
                if (kvp.Value > result.MaxTemperature)
                {
                    result.MaxTemperature = kvp.Value;
                    result.HotSpotComponent = kvp.Key;
                }
            }

            // Thermal safety check
            result.IsThermalSafe = result.MaxTemperature < _config.MaxOperatingTemp;
            result.TotalPowerDissipation = totalPowerDissipation;
            result.CoolingRecommendations = GenerateCoolingRecommendations(result);

            return result;
        }

        /// <summary>
        /// Analyze all joints for strength, range of motion, wear
        /// </summary>
        public JointAnalysisResult AnalyzeJoints(RobotConfiguration robot)
        {
            var result = new JointAnalysisResult();
            result.Timestamp = DateTime.Now;
            result.JointDetails = new List<JointDetail>();

            foreach (var joint in robot.Joints)
            {
                var detail = new JointDetail
                {
                    JointName = joint.Name,
                    JointType = joint.Type,
                    Position = joint.Position
                };

                // Range of motion analysis
                detail.MinAngle = joint.MinAngle;
                detail.MaxAngle = joint.MaxAngle;
                detail.TotalRangeOfMotion = joint.MaxAngle - joint.MinAngle;

                // Torque capacity
                float jointRadius = joint.Diameter / 2f; // meters
                detail.MaxTorque = CalculateJointTorque(joint, robot);

                // Load analysis
                float appliedLoad = CalculateJointLoad(joint, robot);
                detail.AppliedLoad = appliedLoad;
                detail.LoadCapacity = joint.MaxLoadCapacity;
                detail.LoadSafetyFactor = joint.MaxLoadCapacity / appliedLoad;

                // Wear estimation (based on cycles and load)
                detail.EstimatedCycles = (int)joint.EstimatedLifeCycles;
                detail.WearFactor = CalculateWearFactor(joint, appliedLoad);
                detail.MaintenanceInterval = EstimateMaintenanceInterval(joint, detail.WearFactor);

                // Flexibility check
                detail.RequiredFlexibility = joint.RequiredFlexibility;
                detail.ActualFlexibility = joint.ActualFlexibility;
                detail.IsFlexibilitySufficient = joint.ActualFlexibility >= joint.RequiredFlexibility;

                // Backlash and precision
                detail.Backlash = joint.Backlash; // degrees or mm
                detail.PositionAccuracy = joint.PositionAccuracy;
                detail.IsPrecisionSufficient = joint.Backlash < 0.5f; // < 0.5 degrees acceptable

                result.JointDetails.Add(detail);

                // Track critical joints
                if (detail.LoadSafetyFactor < 2.0f)
                {
                    if (result.CriticalJoints == null)
                        result.CriticalJoints = new List<string>();
                    result.CriticalJoints.Add(joint.Name);
                }
            }

            result.TotalJoints = robot.Joints.Count;
            result.AllJointsSafe = (result.CriticalJoints == null || result.CriticalJoints.Count == 0);
            result.RecommendedUpgrades = GenerateJointUpgradeRecommendations(result);

            return result;
        }

        /// <summary>
        /// Analyze weight distribution and center of mass
        /// </summary>
        public WeightDistributionResult AnalyzeWeightDistribution(RobotConfiguration robot)
        {
            var result = new WeightDistributionResult();
            result.Timestamp = DateTime.Now;

            // Calculate center of mass
            Vector3 totalMoment = Vector3.zero;
            float totalMass = 0f;

            result.ComponentWeights = new Dictionary<string, float>();

            // Accumulate all component masses and moments
            foreach (var component in robot.AllComponents)
            {
                float componentMass = component.Mass; // kg
                Vector3 componentPosition = component.Position; // meters

                totalMass += componentMass;
                totalMoment += componentPosition * componentMass;

                result.ComponentWeights[component.Name] = componentMass;
            }

            result.TotalMass = totalMass;
            result.CenterOfMass = totalMoment / totalMass;

            // Check stability (CoM should be low and centered)
            float baseWidth = robot.VehicleWidth;
            float baseLength = robot.VehicleLength;

            result.IsStable = CheckStability(result.CenterOfMass, baseWidth, baseLength);
            result.StabilityMargin = CalculateStabilityMargin(result.CenterOfMass, baseWidth, baseLength);

            // Weight distribution per wheel/leg
            if (robot.VehicleType == VehicleType.GroundRover)
            {
                result.WheelLoads = CalculateWheelLoads(result.CenterOfMass, robot);
                result.IsBalanced = CheckWheelLoadBalance(result.WheelLoads);
            }

            // Inertia tensor (for rotation dynamics)
            result.InertiaTensor = CalculateInertiaTensor(robot);

            result.Recommendations = GenerateWeightRecommendations(result);

            return result;
        }

        /// <summary>
        /// Check circuit compatibility with robot structure
        /// Verify power requirements, connector compatibility, signal integrity
        /// </summary>
        public CircuitCompatibilityResult CheckCircuitCompatibility(RobotConfiguration robot, CircuitConfiguration circuit)
        {
            var result = new CircuitCompatibilityResult();
            result.Timestamp = DateTime.Now;
            result.Issues = new List<CompatibilityIssue>();

            // Power compatibility check
            float robotPowerRequirement = CalculateRobotPowerRequirement(robot);
            float circuitPowerSupply = circuit.TotalPowerSupply;

            if (circuitPowerSupply < robotPowerRequirement)
            {
                result.Issues.Add(new CompatibilityIssue
                {
                    Severity = IssueSeverity.Critical,
                    Component = "Power Supply",
                    Description = $"Circuit provides {circuitPowerSupply:F1}W but robot needs {robotPowerRequirement:F1}W",
                    Recommendation = $"Upgrade power supply or reduce robot power consumption by {(robotPowerRequirement - circuitPowerSupply):F1}W"
                });
            }

            result.PowerCompatible = circuitPowerSupply >= robotPowerRequirement;
            result.PowerMargin = circuitPowerSupply - robotPowerRequirement;

            // Voltage compatibility
            float robotVoltage = robot.OperatingVoltage;
            float circuitVoltage = circuit.SupplyVoltage;

            if (Math.Abs(robotVoltage - circuitVoltage) > 0.5f)
            {
                result.Issues.Add(new CompatibilityIssue
                {
                    Severity = IssueSeverity.Warning,
                    Component = "Voltage Level",
                    Description = $"Robot operates at {robotVoltage:F1}V but circuit supplies {circuitVoltage:F1}V",
                    Recommendation = "Add voltage regulator or adjust circuit voltage"
                });
            }

            result.VoltageCompatible = Math.Abs(robotVoltage - circuitVoltage) <= 0.5f;

            // Connector compatibility
            result.ConnectorMatches = CheckConnectorCompatibility(robot, circuit);
            result.AllConnectorsCompatible = result.ConnectorMatches.Count == robot.RequiredConnectors.Count;

            // Signal integrity (check wire lengths, impedance matching)
            result.SignalIntegrityScore = AnalyzeSignalIntegrity(robot, circuit);
            result.SignalIntegrityOK = result.SignalIntegrityScore > 0.8f;

            // Pin count check
            int requiredPins = CountRequiredPins(robot);
            int availablePins = circuit.BoardType == BoardType.Arduino_Mega ? 54 : 14;

            if (requiredPins > availablePins)
            {
                result.Issues.Add(new CompatibilityIssue
                {
                    Severity = IssueSeverity.Critical,
                    Component = "Pin Count",
                    Description = $"Robot needs {requiredPins} pins but board has only {availablePins}",
                    Recommendation = "Use pin multiplexer or upgrade to larger board"
                });
            }

            result.PinCountCompatible = requiredPins <= availablePins;
            result.AvailablePins = availablePins - requiredPins;

            // Overall compatibility
            result.IsFullyCompatible = result.PowerCompatible && result.VoltageCompatible &&
                                       result.AllConnectorsCompatible && result.PinCountCompatible;

            result.CompatibilityScore = CalculateOverallCompatibility(result);

            return result;
        }

        // Helper methods
        private float CalculateTotalMass(RobotConfiguration robot)
        {
            float total = 0f;
            foreach (var component in robot.AllComponents)
                total += component.Mass;
            return total;
        }

        private float CalculateStressConcentration(StructuralComponent component)
        {
            // Simplified stress concentration factor (real FEM would be more complex)
            // Kt = 1 + 2 * sqrt(a/r) where a = notch depth, r = notch radius
            float holeDiameter = component.JointHoleDiameter;
            float thickness = component.Thickness;

            if (holeDiameter > 0)
            {
                float ratio = holeDiameter / thickness;
                return 1.0f + 2.0f * Mathf.Sqrt(ratio);
            }

            return 1.0f;
        }

        private bool CheckAllComponentsSafe(Dictionary<string, ComponentStress> stresses)
        {
            foreach (var stress in stresses.Values)
            {
                if (!stress.IsWithinSafetyLimits)
                    return false;
            }
            return true;
        }

        private List<string> GenerateMaterialRecommendations(StressAnalysisResult result)
        {
            var recommendations = new List<string>();

            foreach (var kvp in result.ComponentStresses)
            {
                if (kvp.Value.SafetyFactor < 2.0f)
                {
                    var material = _materialDb.GetMaterial(kvp.Value.Material);
                    var betterMaterial = _materialDb.GetStrongerMaterial(material);
                    recommendations.Add($"{kvp.Key}: Upgrade from {material.Name} to {betterMaterial.Name}");
                }
            }

            return recommendations;
        }

        private Vector3Int WorldToGridPosition(Vector3 worldPos)
        {
            // Convert world position to heat map grid coordinates
            int x = Mathf.Clamp((int)((worldPos.x + 0.5f) * 10f), 0, 9);
            int y = Mathf.Clamp((int)((worldPos.y + 0.5f) * 10f), 0, 9);
            int z = Mathf.Clamp((int)((worldPos.z + 0.5f) * 10f), 0, 9);
            return new Vector3Int(x, y, z);
        }

        private void PropagateHeat(float[,,] heatMap, Vector3Int source, float power, float diffusionRate)
        {
            // Simplified 3D heat diffusion
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        int x = source.x + dx;
                        int y = source.y + dy;
                        int z = source.z + dz;

                        if (x >= 0 && x < 10 && y >= 0 && y < 10 && z >= 0 && z < 10)
                        {
                            float distance = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                            if (distance > 0)
                            {
                                float heatContribution = power * diffusionRate / (distance * distance);
                                heatMap[x, y, z] += heatContribution;
                            }
                        }
                    }
                }
            }
        }

        private float CalculateElectronicsPower(RobotConfiguration robot)
        {
            float power = 0f;
            power += robot.BoardType == BoardType.Arduino_Mega ? 0.5f : 0.2f; // Board consumption
            power += robot.Sensors.Count * 0.05f; // Each sensor ~50mW
            power += 0.3f; // Misc electronics
            return power;
        }

        private List<string> GenerateCoolingRecommendations(ThermalAnalysisResult result)
        {
            var recommendations = new List<string>();

            if (result.MaxTemperature > 70f)
            {
                recommendations.Add("Add passive heatsinks to hot components");
            }

            if (result.MaxTemperature > 80f)
            {
                recommendations.Add("Consider active cooling (fan) for critical components");
            }

            if (result.TotalPowerDissipation > 10f)
            {
                recommendations.Add("Improve thermal design: increase surface area or add thermal vias");
            }

            return recommendations;
        }

        private float CalculateJointTorque(Joint joint, RobotConfiguration robot)
        {
            // T = F * r where F = load, r = joint radius
            float load = CalculateJointLoad(joint, robot);
            float radius = joint.Diameter / 2f;
            return load * radius;
        }

        private float CalculateJointLoad(Joint joint, RobotConfiguration robot)
        {
            // Calculate load on joint based on connected components
            float load = 0f;

            foreach (var component in robot.AllComponents)
            {
                if (IsComponentSupportedByJoint(component, joint))
                {
                    load += component.Mass * 9.81f; // Weight in Newtons
                }
            }

            return load;
        }

        private bool IsComponentSupportedByJoint(Component component, Joint joint)
        {
            // Check if component is structurally supported by this joint
            // Simplified: check if component is "downstream" of joint
            return Vector3.Distance(component.Position, joint.Position) < 0.5f;
        }

        private float CalculateWearFactor(Joint joint, float appliedLoad)
        {
            // Archard wear equation: Wear ∝ Load * Distance / Hardness
            float normalizedLoad = appliedLoad / joint.MaxLoadCapacity;
            float usageFactor = joint.TotalCycles / joint.EstimatedLifeCycles;
            return normalizedLoad * usageFactor;
        }

        private int EstimateMaintenanceInterval(Joint joint, float wearFactor)
        {
            // Maintenance cycles = Base cycles / (1 + wear factor)
            int baseCycles = 100000;
            return (int)(baseCycles / (1f + wearFactor));
        }

        private List<string> GenerateJointUpgradeRecommendations(JointAnalysisResult result)
        {
            var recommendations = new List<string>();

            foreach (var joint in result.JointDetails)
            {
                if (joint.LoadSafetyFactor < 2.0f)
                {
                    recommendations.Add($"{joint.JointName}: Upgrade to higher load capacity joint");
                }

                if (joint.Backlash > 1.0f)
                {
                    recommendations.Add($"{joint.JointName}: Replace with precision joint (current backlash: {joint.Backlash:F2}°)");
                }
            }

            return recommendations;
        }

        private bool CheckStability(Vector3 centerOfMass, float baseWidth, float baseLength)
        {
            // CoM should be within stability polygon
            float halfWidth = baseWidth / 2f;
            float halfLength = baseLength / 2f;

            return (Mathf.Abs(centerOfMass.x) < halfWidth * 0.8f &&
                    Mathf.Abs(centerOfMass.z) < halfLength * 0.8f &&
                    centerOfMass.y < baseWidth); // CoM height < width for stability
        }

        private float CalculateStabilityMargin(Vector3 centerOfMass, float baseWidth, float baseLength)
        {
            // Distance from CoM to tipping edge
            float halfWidth = baseWidth / 2f;
            float halfLength = baseLength / 2f;

            float marginX = halfWidth - Mathf.Abs(centerOfMass.x);
            float marginZ = halfLength - Mathf.Abs(centerOfMass.z);

            return Mathf.Min(marginX, marginZ);
        }

        private Dictionary<string, float> CalculateWheelLoads(Vector3 centerOfMass, RobotConfiguration robot)
        {
            var loads = new Dictionary<string, float>();

            // Simplified 4-wheel load distribution
            float totalWeight = robot.TotalMass * 9.81f; // Newtons
            float wheelbase = robot.VehicleLength;
            float track = robot.VehicleWidth;

            // Load transfer based on CoM offset
            float frontBias = 0.5f + centerOfMass.z / wheelbase;
            float rearBias = 1.0f - frontBias;
            float leftBias = 0.5f - centerOfMass.x / track;
            float rightBias = 1.0f - leftBias;

            loads["FrontLeft"] = totalWeight * frontBias * leftBias;
            loads["FrontRight"] = totalWeight * frontBias * rightBias;
            loads["RearLeft"] = totalWeight * rearBias * leftBias;
            loads["RearRight"] = totalWeight * rearBias * rightBias;

            return loads;
        }

        private bool CheckWheelLoadBalance(Dictionary<string, float> wheelLoads)
        {
            float maxLoad = 0f;
            float minLoad = float.MaxValue;

            foreach (var load in wheelLoads.Values)
            {
                maxLoad = Mathf.Max(maxLoad, load);
                minLoad = Mathf.Min(minLoad, load);
            }

            // Load imbalance < 30% is acceptable
            float imbalance = (maxLoad - minLoad) / maxLoad;
            return imbalance < 0.3f;
        }

        private Matrix4x4 CalculateInertiaTensor(RobotConfiguration robot)
        {
            // Simplified inertia tensor calculation
            Matrix4x4 inertia = Matrix4x4.identity;

            float Ixx = 0f, Iyy = 0f, Izz = 0f;
            Vector3 com = robot.CenterOfMass;

            foreach (var component in robot.AllComponents)
            {
                float m = component.Mass;
                Vector3 r = component.Position - com;

                // Parallel axis theorem
                Ixx += m * (r.y * r.y + r.z * r.z);
                Iyy += m * (r.x * r.x + r.z * r.z);
                Izz += m * (r.x * r.x + r.y * r.y);
            }

            inertia[0, 0] = Ixx;
            inertia[1, 1] = Iyy;
            inertia[2, 2] = Izz;

            return inertia;
        }

        private List<string> GenerateWeightRecommendations(WeightDistributionResult result)
        {
            var recommendations = new List<string>();

            if (!result.IsStable)
            {
                recommendations.Add("Lower center of mass by relocating heavy components");
            }

            if (!result.IsBalanced)
            {
                recommendations.Add("Redistribute weight for better balance");
            }

            if (result.StabilityMargin < 0.05f)
            {
                recommendations.Add("Increase wheelbase or track width for better stability");
            }

            return recommendations;
        }

        private float CalculateRobotPowerRequirement(RobotConfiguration robot)
        {
            float power = 0f;

            foreach (var motor in robot.Motors)
                power += motor.Voltage * motor.Current;

            foreach (var servo in robot.Servos)
                power += servo.PowerConsumption;

            foreach (var sensor in robot.Sensors)
                power += sensor.PowerConsumption;

            power += 1.0f; // Board + misc

            return power * 1.2f; // 20% margin
        }

        private Dictionary<string, string> CheckConnectorCompatibility(RobotConfiguration robot, CircuitConfiguration circuit)
        {
            var matches = new Dictionary<string, string>();

            foreach (var required in robot.RequiredConnectors)
            {
                bool found = false;
                foreach (var available in circuit.AvailableConnectors)
                {
                    if (available.Type == required.Type && available.Gender != required.Gender)
                    {
                        matches[required.Name] = available.Name;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    matches[required.Name] = "NO_MATCH";
                }
            }

            return matches;
        }

        private float AnalyzeSignalIntegrity(RobotConfiguration robot, CircuitConfiguration circuit)
        {
            float score = 1.0f;

            // Check wire lengths (long wires = signal degradation)
            foreach (var connection in circuit.Connections)
            {
                float length = connection.Length;
                if (length > 0.5f) // >50cm
                    score -= 0.1f;
            }

            // Check impedance matching for high-speed signals
            // (Simplified - real analysis would check transmission line effects)

            return Mathf.Clamp01(score);
        }

        private int CountRequiredPins(RobotConfiguration robot)
        {
            int pins = 0;

            foreach (var motor in robot.Motors)
                pins += motor.RequiredPins;

            foreach (var servo in robot.Servos)
                pins += 1; // PWM pin

            foreach (var sensor in robot.Sensors)
                pins += sensor.RequiredPins;

            return pins;
        }

        private float CalculateOverallCompatibility(CircuitCompatibilityResult result)
        {
            float score = 0f;
            int factors = 0;

            if (result.PowerCompatible) { score += 1f; factors++; }
            if (result.VoltageCompatible) { score += 1f; factors++; }
            if (result.AllConnectorsCompatible) { score += 1f; factors++; }
            if (result.PinCountCompatible) { score += 1f; factors++; }
            if (result.SignalIntegrityOK) { score += 1f; factors++; }

            return factors > 0 ? score / factors : 0f;
        }
    }

    // Data structures for analysis results
    [Serializable]
    public class StressAnalysisResult
    {
        public DateTime Timestamp;
        public Dictionary<string, ComponentStress> ComponentStresses;
        public float MaxOverallStress;
        public string CriticalComponent;
        public bool IsStructurallySafe;
        public List<string> RecommendedMaterialUpgrades;
    }

    [Serializable]
    public class ComponentStress
    {
        public string ComponentName;
        public MaterialType Material;
        public float MaxStress; // MPa
        public float YieldStrength; // MPa
        public float UltimateTensileStrength; // MPa
        public float SafetyFactor;
        public bool IsWithinSafetyLimits;
        public float StressConcentrationFactor;
    }

    [Serializable]
    public class ThermalAnalysisResult
    {
        public DateTime Timestamp;
        public float AmbientTemperature;
        public float[,,] HeatMap; // 3D temperature distribution
        public Dictionary<string, float> ComponentTemperatures;
        public float MaxTemperature;
        public string HotSpotComponent;
        public Vector3 HotSpotLocation;
        public bool IsThermalSafe;
        public float TotalPowerDissipation;
        public List<string> CoolingRecommendations;
    }

    [Serializable]
    public class JointAnalysisResult
    {
        public DateTime Timestamp;
        public int TotalJoints;
        public List<JointDetail> JointDetails;
        public List<string> CriticalJoints;
        public bool AllJointsSafe;
        public List<string> RecommendedUpgrades;
    }

    [Serializable]
    public class JointDetail
    {
        public string JointName;
        public JointType JointType;
        public Vector3 Position;
        public float MinAngle;
        public float MaxAngle;
        public float TotalRangeOfMotion;
        public float MaxTorque;
        public float AppliedLoad;
        public float LoadCapacity;
        public float LoadSafetyFactor;
        public int EstimatedCycles;
        public float WearFactor;
        public int MaintenanceInterval;
        public float RequiredFlexibility;
        public float ActualFlexibility;
        public bool IsFlexibilitySufficient;
        public float Backlash;
        public float PositionAccuracy;
        public bool IsPrecisionSufficient;
    }

    [Serializable]
    public class WeightDistributionResult
    {
        public DateTime Timestamp;
        public float TotalMass;
        public Vector3 CenterOfMass;
        public Dictionary<string, float> ComponentWeights;
        public bool IsStable;
        public float StabilityMargin;
        public Dictionary<string, float> WheelLoads;
        public bool IsBalanced;
        public Matrix4x4 InertiaTensor;
        public List<string> Recommendations;
    }

    [Serializable]
    public class CircuitCompatibilityResult
    {
        public DateTime Timestamp;
        public bool IsFullyCompatible;
        public float CompatibilityScore;
        public bool PowerCompatible;
        public float PowerMargin;
        public bool VoltageCompatible;
        public Dictionary<string, string> ConnectorMatches;
        public bool AllConnectorsCompatible;
        public float SignalIntegrityScore;
        public bool SignalIntegrityOK;
        public bool PinCountCompatible;
        public int AvailablePins;
        public List<CompatibilityIssue> Issues;
    }

    [Serializable]
    public class CompatibilityIssue
    {
        public IssueSeverity Severity;
        public string Component;
        public string Description;
        public string Recommendation;
    }

    public enum IssueSeverity
    {
        Info,
        Warning,
        Critical
    }

    [Serializable]
    public class AnalysisConfiguration
    {
        public bool EnableStressAnalysis;
        public bool EnableThermalAnalysis;
        public bool EnableJointAnalysis;
        public float StressResolution;
        public float ThermalResolution;
        public float SafetyFactor;
        public float MaxOperatingTemp;
    }
}
