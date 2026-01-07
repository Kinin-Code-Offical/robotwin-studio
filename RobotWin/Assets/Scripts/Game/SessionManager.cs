using UnityEngine;
using RobotTwin.CoreSim.Specs;
using System;
using System.IO;
using System.Linq;
using RobotTwin.Game.RaspberryPi;


namespace RobotTwin.Game
{
    // Manages the active simulation session. 
    // Persists across scenes (DontDestroyOnLoad).
    public class SessionManager : MonoBehaviour
    {
        private const string DefaultFirmwareHostExe = "RoboTwinFirmwareHost.exe";

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
            ConfigureFirmwareMode(circuit);
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
            ConfigureFirmwareMode(CurrentCircuit);
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
        public string FirmwareHostExecutableName { get; set; } = DefaultFirmwareHostExe;
        public string FirmwareHostPathOverride { get; set; }
        public bool UseVirtualMcu { get; set; } = true;
        public bool UseNativeEnginePins { get; set; } = true;
        public bool FirmwareHostLockstep { get; set; } = true;

        public void ConfigureFirmwareMode(CircuitSpec circuit)
        {
            bool hasVirtualFirmware = false;
            bool hasHexFirmware = false;
            bool hasBvmFirmware = false;

            if (circuit?.Components != null)
            {
                foreach (var comp in circuit.Components)
                {
                    if (comp?.Properties == null) continue;

                    if (comp.Properties.TryGetValue("virtualFirmware", out var virtualFirmware) &&
                        !string.IsNullOrWhiteSpace(virtualFirmware))
                    {
                        hasVirtualFirmware = true;
                    }

                    if (comp.Properties.TryGetValue("firmwarePath", out var firmwarePath) &&
                        !string.IsNullOrWhiteSpace(firmwarePath) &&
                        File.Exists(firmwarePath) &&
                        (firmwarePath.EndsWith(".hex", StringComparison.OrdinalIgnoreCase) ||
                         firmwarePath.EndsWith(".ihx", StringComparison.OrdinalIgnoreCase)))
                    {
                        hasHexFirmware = true;
                    }

                    if (comp.Properties.TryGetValue("firmware", out var firmwareValue) &&
                        !string.IsNullOrWhiteSpace(firmwareValue) &&
                        File.Exists(firmwareValue) &&
                        (firmwareValue.EndsWith(".hex", StringComparison.OrdinalIgnoreCase) ||
                         firmwareValue.EndsWith(".ihx", StringComparison.OrdinalIgnoreCase)))
                    {
                        hasHexFirmware = true;
                    }

                    if (comp.Properties.TryGetValue("bvmPath", out var bvmPath) &&
                        !string.IsNullOrWhiteSpace(bvmPath) &&
                        File.Exists(bvmPath))
                    {
                        hasBvmFirmware = true;
                    }
                }
            }

            // If there's no real firmware, keep VirtualMcu on (placeholder/demo programs).
            if (!hasVirtualFirmware && !hasHexFirmware && !hasBvmFirmware)
            {
                UseVirtualMcu = true;
                UseNativeEnginePins = false;
                return;
            }

            // Prefer real firmware backends over VirtualMcu.
            UseVirtualMcu = false;

            // If bvm exists and firmware host exists, use external firmware host.
            if (hasBvmFirmware)
            {
                FindFirmware();
                bool hostExists = !string.IsNullOrWhiteSpace(FirmwarePath) && File.Exists(FirmwarePath);
                if (hostExists)
                {
                    UseNativeEnginePins = false;
                    Debug.Log("[SessionManager] Firmware backend: FirmwareHost (.bvm). Reason: bvmPath present and host executable found.");
                    return;
                }

                // If we can't run the .bvm (host missing) and we don't have a HEX fallback,
                // we must fall back to VirtualMcu to avoid ending up with no backend.
                if (!hasHexFirmware)
                {
                    Debug.LogWarning("[SessionManager] .bvm firmware detected but firmware host is missing. Falling back to VirtualMcu.");
                    UseVirtualMcu = true;
                    UseNativeEnginePins = false;
                    return;
                }
            }

            // Otherwise, prefer NativeEngine AVR when a HEX exists.
            UseNativeEnginePins = hasHexFirmware;

            // If only virtual firmware exists (no hex/bvm), fall back to VirtualMcu.
            if (hasVirtualFirmware && !hasHexFirmware && !hasBvmFirmware)
            {
                UseVirtualMcu = true;
                UseNativeEnginePins = false;
            }

            if (UseVirtualMcu)
            {
                Debug.Log("[SessionManager] Firmware backend: VirtualMcu. Reason: virtualFirmware/no-host fallback.");
            }
            else if (UseNativeEnginePins)
            {
                Debug.Log("[SessionManager] Firmware backend: NativeEngine AVR (.hex/.ihx).");
            }
        }

        public string ResolveFirmwareHostOverride()
        {
            if (!string.IsNullOrWhiteSpace(FirmwareHostPathOverride))
            {
                return FirmwareHostPathOverride;
            }

            return Environment.GetEnvironmentVariable("ROBOTWIN_FIRMWARE_HOST");
        }

        public void FindFirmware()
        {
            string resolvedPath = string.Empty;
            var overridePath = ResolveFirmwareHostOverride();
            if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
            {
                resolvedPath = overridePath;
                Debug.Log($"[SessionManager] Firmware host override found: {resolvedPath}");
                FirmwarePath = resolvedPath;
                if (UseVirtualMcu)
                {
                    Debug.Log("[SessionManager] VirtualMcu enabled. External firmware will be ignored.");
                }
                return;
            }

            var hostExe = string.IsNullOrWhiteSpace(FirmwareHostExecutableName)
                ? DefaultFirmwareHostExe
                : FirmwareHostExecutableName;
#if UNITY_EDITOR
            // Relative to project path: ../builds/firmware/<firmware host exe>
            var projectRoot = Directory.GetParent(Application.dataPath).Parent.FullName;
            var firmwarePath = Path.Combine(projectRoot, "builds", "firmware", hostExe);
            if (File.Exists(firmwarePath))
            {
                resolvedPath = firmwarePath;
                Debug.Log($"[SessionManager] Firmware host found: {firmwarePath}");
            }
            else
            {
                Debug.LogWarning($"[SessionManager] Firmware host NOT found at {firmwarePath}");
            }
#else
            // In build, expect next to executable or in data
            resolvedPath = Path.Combine(Application.streamingAssetsPath, hostExe);
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
