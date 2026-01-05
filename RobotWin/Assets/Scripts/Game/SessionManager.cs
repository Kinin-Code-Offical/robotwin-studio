using UnityEngine;
using RobotTwin.CoreSim.Specs;
using System.IO;


namespace RobotTwin.Game
{
    // Manages the active simulation session. 
    // Persists across scenes (DontDestroyOnLoad).
    public class SessionManager : MonoBehaviour
    {
        public static SessionManager Instance { get; private set; }

        public CircuitSpec CurrentCircuit { get; private set; }
        public RobotSpec CurrentRobot { get; private set; }
        public WorldSpec CurrentWorld { get; private set; }
        public TemplateSpec ActiveTemplate { get; private set; }

        public ProjectManifest CurrentProject { get; private set; }
        public string CurrentProjectPath { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void InitializeSession(TemplateSpec template)
        {
            TemplateSpecValidator.ValidateOrThrow(template);
            ActiveTemplate = template;
            CurrentCircuit = template.DefaultCircuit;
            CurrentRobot = template.DefaultRobot;
            CurrentWorld = template.DefaultWorld;
            CurrentProject = null; // Template mode, not project mode

            LogNoStack(LogType.Log, $"Session Initialized: {template.Name}");
        }

        public void StartSession(CircuitSpec circuit)
        {
            CurrentCircuit = circuit;
            CurrentRobot = new RobotSpec { Name = "TestRobot" };
            CurrentWorld = new WorldSpec { Name = "TestWorld" };
            ActiveTemplate = null;
            CurrentProject = null;
            var circuitId = string.IsNullOrWhiteSpace(circuit.Id) ? "<unnamed>" : circuit.Id;
            LogNoStack(LogType.Log, $"Session Started explicitly: {circuitId}");
        }

        public void StartSession(ProjectManifest project)
        {
            CurrentProject = project;
            CurrentCircuit = project.Circuit;
            CurrentRobot = project.Robot;
            CurrentWorld = project.World;
            ActiveTemplate = null;
            CurrentProjectPath = null;
            
            LogNoStack(LogType.Log, $"Session Started from Project: {project.ProjectName}");
        }

        public void StartSession(ProjectManifest project, string projectPath)
        {
            StartSession(project);
            CurrentProjectPath = projectPath;
        }
        public string FirmwarePath { get; private set; }
        public bool UseVirtualMcu { get; set; } = true;
        public bool UseNativeEnginePins { get; set; } = true;

        public void FindFirmware()
        {
            string resolvedPath = string.Empty;
#if UNITY_EDITOR
            // Relative to project path: ../builds/firmware/RoboTwinFirmwareHost.exe
            var projectRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
            var firmwarePath = Path.Combine(projectRoot, "builds", "firmware", "RoboTwinFirmwareHost.exe");
            if (File.Exists(firmwarePath))
            {
                resolvedPath = firmwarePath;
                Debug.Log($"[SessionManager] Firmware found: {firmwarePath}");
            }
            else
            {
                Debug.LogWarning($"[SessionManager] Firmware NOT found at {firmwarePath}");
            }
#else
            // In build, expect next to executable or in data
            resolvedPath = Path.Combine(Application.streamingAssetsPath, "RoboTwinFirmwareHost.exe");
#endif
            FirmwarePath = resolvedPath;
            if (UseVirtualMcu)
            {
                Debug.Log("[SessionManager] VirtualMcu enabled. External firmware will be ignored.");
            }
        }

        private void OnApplicationQuit()
        {
             // Cleanup if needed
        }

        private static void LogNoStack(LogType type, string message)
        {
            var prev = Application.GetStackTraceLogType(type);
            Application.SetStackTraceLogType(type, StackTraceLogType.None);
            Debug.unityLogger.Log(type, message);
            Application.SetStackTraceLogType(type, prev);
        }
    }
}
