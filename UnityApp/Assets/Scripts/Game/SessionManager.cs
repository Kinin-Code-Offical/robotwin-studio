using UnityEngine;
using CoreSim.Specs;
using CoreSim.Catalogs;

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
<<<<<<< HEAD
        public TemplateSpec ActiveTemplate { get; private set; }
=======
        public TemplateDefinition ActiveTemplate { get; private set; }
>>>>>>> 970ef08 (feat: implement template-driven project wizard and session management components)

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

<<<<<<< HEAD
        public void InitializeSession(TemplateSpec template)
=======
        public void InitializeSession(TemplateDefinition template)
>>>>>>> 970ef08 (feat: implement template-driven project wizard and session management components)
        {
            ActiveTemplate = template;
            CurrentCircuit = template.DefaultCircuit;
            CurrentRobot = template.DefaultRobot;
            CurrentWorld = template.DefaultWorld;

            Debug.Log($"Session Initialized: {template.Name}");
        }
    }
}
