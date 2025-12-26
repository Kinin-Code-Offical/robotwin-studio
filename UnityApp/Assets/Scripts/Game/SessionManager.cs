using UnityEngine;
using RobotTwin.CoreSim.Specs;


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
            ActiveTemplate = template;
            CurrentCircuit = template.DefaultCircuit;
            CurrentRobot = template.DefaultRobot;
            CurrentWorld = template.DefaultWorld;
            CurrentProject = null; // Template mode, not project mode

            Debug.Log($"Session Initialized: {template.Name}");
        }

        public void StartSession(CircuitSpec circuit)
        {
            CurrentCircuit = circuit;
            CurrentRobot = new RobotSpec { Name = "TestRobot" };
            CurrentWorld = new WorldSpec { Name = "TestWorld" };
            ActiveTemplate = null;
            CurrentProject = null;
            var circuitId = string.IsNullOrWhiteSpace(circuit.Id) ? "<unnamed>" : circuit.Id;
            Debug.Log($"Session Started explicitly: {circuitId}");
        }

        public void StartSession(ProjectManifest project)
        {
            CurrentProject = project;
            CurrentCircuit = project.Circuit;
            CurrentRobot = project.Robot;
            CurrentWorld = project.World;
            ActiveTemplate = null;
            
            Debug.Log($"Session Started from Project: {project.ProjectName}");
        }
    }
}
