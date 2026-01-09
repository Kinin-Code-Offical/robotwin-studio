using UnityEngine;
using System.Collections.Generic;
using System;

namespace RobotTwin.UI.RobotEditor
{
    /// <summary>
    /// Complete robot configuration with all components
    /// </summary>
    [Serializable]
    public class RobotConfiguration
    {
        public string Name;
        public VehicleType VehicleType;
        public BoardType BoardType;

        // Physical dimensions
        public float VehicleWidth = 0.3f; // meters
        public float VehicleLength = 0.4f; // meters
        public float VehicleHeight = 0.2f; // meters
        public float TotalMass = 5.0f; // kg
        public Vector3 CenterOfMass;

        // Power specifications
        public float OperatingVoltage = 12.0f; // volts
        public float BatteryCapacity = 5000f; // mAh

        // Component lists
        public List<Motor> Motors = new List<Motor>();
        public List<Servo> Servos = new List<Servo>();
        public List<Sensor> Sensors = new List<Sensor>();
        public List<StructuralComponent> StructuralComponents = new List<StructuralComponent>();
        public List<Joint> Joints = new List<Joint>();
        public List<Connector> RequiredConnectors = new List<Connector>();

        // Derived property for all components
        public List<Component> AllComponents
        {
            get
            {
                var all = new List<Component>();
                all.AddRange(Motors);
                all.AddRange(Servos);
                all.AddRange(Sensors);
                all.AddRange(StructuralComponents);
                return all;
            }
        }
    }

    [Serializable]
    public class Component
    {
        public string Name;
        public Vector3 Position;
        public float Mass; // kg
    }

    [Serializable]
    public class Motor : Component
    {
        public float Voltage;
        public float Current;
        public float Efficiency = 0.8f;
        public int RequiredPins = 2;
    }

    [Serializable]
    public class Servo : Component
    {
        public float PowerConsumption = 1.0f;
        public float MaxTorque;
        public int RequiredPins = 1;
    }

    [Serializable]
    public class Sensor : Component
    {
        public string Type;
        public float PowerConsumption = 0.05f;
        public int RequiredPins = 1;
    }

    [Serializable]
    public class StructuralComponent : Component
    {
        public MaterialType Material;
        public float Width; // meters
        public float Height; // meters
        public float Thickness; // meters
        public float LoadFactor = 1.0f;
        public bool HasJoints;
        public float JointHoleDiameter;
    }

    [Serializable]
    public class Joint
    {
        public string Name;
        public JointType Type;
        public Vector3 Position;
        public float Diameter; // meters
        public float MinAngle = -180f;
        public float MaxAngle = 180f;
        public float MaxLoadCapacity = 100f; // Newtons
        public float EstimatedLifeCycles = 1000000;
        public float TotalCycles = 0;
        public float RequiredFlexibility = 0.5f;
        public float ActualFlexibility = 1.0f;
        public float Backlash = 0.2f; // degrees
        public float PositionAccuracy = 0.1f; // degrees
    }

    [Serializable]
    public class Connector
    {
        public string Name;
        public string Type; // "USB", "JST", "GPIO", etc.
        public string Gender; // "Male", "Female"
        public int PinCount;
    }

    /// <summary>
    /// Circuit configuration with components and connections
    /// </summary>
    [Serializable]
    public class CircuitConfiguration
    {
        public string Name;
        public BoardType BoardType;

        // Power specifications
        public float SupplyVoltage = 12.0f; // volts
        public float TotalPowerSupply = 20.0f; // watts

        // Components
        public List<CircuitComponent> Components = new List<CircuitComponent>();
        public List<Connection> Connections = new List<Connection>();
        public List<Connector> AvailableConnectors = new List<Connector>();
    }

    [Serializable]
    public class CircuitComponent
    {
        public string Name;
        public string Type; // "Resistor", "Capacitor", "IC", etc.
        public Vector2 Position; // 2D schematic position
        public float Voltage;
        public float Current;
        public float PowerDissipation;
        public float Temperature;
    }

    [Serializable]
    public class Connection
    {
        public string FromComponent;
        public string ToComponent;
        public string SignalType; // "Power", "Ground", "Digital", "Analog"
        public float Length; // meters
        public float Impedance; // ohms
    }
}
