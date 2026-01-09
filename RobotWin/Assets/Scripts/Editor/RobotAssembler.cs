using UnityEngine;
using UnityEditor;
using RobotWin.Robotics;

namespace RobotWin.EditorTools
{
    /// <summary>
    /// Automates the tedious task of assembling the 7 parts into a Physics Robot.
    /// "God Tier" automation: You give it meshes, it gives you a driving robot.
    /// </summary>
    public class RobotAssembler : EditorWindow
    {
        // UI Variables
        GameObject chassisMesh;
        GameObject wheelL, wheelR;
        GameObject armBase, armClaw;

        [MenuItem("RobotWin/Assembler/Create Alp Robot")]
        public static void ShowWindow()
        {
            GetWindow<RobotAssembler>("Alp Assembler");
        }

        void OnGUI()
        {
            GUILayout.Label("1. Drag & Drop Meshes", EditorStyles.boldLabel);
            chassisMesh = (GameObject)EditorGUILayout.ObjectField("Chassis", chassisMesh, typeof(GameObject), false);
            wheelL = (GameObject)EditorGUILayout.ObjectField("Wheel Left", wheelL, typeof(GameObject), false);
            wheelR = (GameObject)EditorGUILayout.ObjectField("Wheel Right", wheelR, typeof(GameObject), false);

            if (GUILayout.Button("2. Assemble Digital Twin"))
            {
                Assemble();
            }
        }

        void Assemble()
        {
            // 1. Root Object
            GameObject root = new GameObject("AlpRobot_DigitalTwin");
            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 0.8f; // Estimated from Batteries + Motors

            // 2. Chassis Visuals
            if (chassisMesh) Instantiate(chassisMesh, root.transform);

            // 3. Setup Wheels
            CreateWheel(root, wheelL, new Vector3(-0.06f, 0.03f, -0.05f), "Left");
            CreateWheel(root, wheelR, new Vector3(0.06f, 0.03f, -0.05f), "Right");

            // 4. Attach The Brain
            root.AddComponent<AnalogSignalProcessor>(); // For Sensors
            // root.AddComponent<FirmwareBridge>(); // Later...

            Debug.Log("Robot Assembled! Now just tune the colliders.");
        }

        void CreateWheel(GameObject parent, GameObject mesh, Vector3 pos, string side)
        {
            GameObject wheelObj = new GameObject($"Wheel_{side}");
            wheelObj.transform.parent = parent.transform;
            wheelObj.transform.localPosition = pos;

            if (mesh) Instantiate(mesh, wheelObj.transform);

            // Physics Logic
            HingeJoint hinge = wheelObj.AddComponent<HingeJoint>();
            hinge.connectedBody = parent.GetComponent<Rigidbody>();
            hinge.axis = Vector3.right;
            hinge.useMotor = true;

            // Attach our Custom Controller
            MotorController ctrl = wheelObj.AddComponent<MotorController>();
            if (side == "Left") ctrl.efficiency = 0.65f; // The "Broken" motor
        }
    }
}
