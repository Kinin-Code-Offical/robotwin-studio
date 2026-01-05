using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Serialization;
using RobotTwin.CoreSim.Host;
using RobotTwin.CoreSim.Specs;
using RobotTwin.CoreSim.IPC;
using System.Collections.Generic;
using System.Threading.Tasks;

public class CircuitController : MonoBehaviour
{
    [Tooltip("Path to the firmware host executable if not in PATH")]
    [FormerlySerializedAs("MockEnginePath")]
    public string FirmwareHostPath = "Default"; // Logic to find it relative to project

    private SimHost _simHost;
    private IFirmwareClient _firmwareClient;
    private VisualElement _root;
    private Button _btnRun;
    private Button _btnStop;
    private VisualElement _ledVisual; // Visualization for Blinky

    void OnEnable()
    {
        var uiDoc = GetComponent<UIDocument>();
        if (uiDoc != null)
        {
            _root = uiDoc.rootVisualElement;
            _btnRun = _root.Q<Button>("toolbar-run");
            _btnStop = _root.Q<Button>("toolbar-stop");

            if (_btnRun != null) _btnRun.clicked += OnRunClicked;
            if (_btnStop != null) _btnStop.clicked += OnStopClicked;

            // Find placeholder LED in the canvas (hardcoded for now based on CircuitStudio.uxml)
            // In real app, we would query by ID from the CircuitSpec
            var canvas = _root.Q("CanvasContainer");
            if (canvas != null)
            {
                // We added a generic element in UXML, let's try to find it or just assume connection
                // For MVP vertical slice, we'll toggle the color of the "LED" element we hardcoded
                // In UXML: <VisualElement class="component-item" name="comp-led"> is in palette
                // In CanvasContainer: <VisualElement ... background-color: #cc3333 ... > 
                // We didn't give the canvas LED a name in UXML, so let's find it by style or modify UXML later.
                // For now, let's query children.
                if (canvas.childCount > 2)
                    _ledVisual = canvas[2]; // 3rd child was the LED circle in UXML
            }
        }
    }

    void OnDisable()
    {
        Shutdown();
    }

    private void OnRunClicked()
    {
        Debug.Log("Starting Simulation...");
        // 1. Create Spec (Mock Blinky)
        var spec = new CircuitSpec
        {
            Id = "BlinkySpec",
            Mode = RobotTwin.CoreSim.Specs.SimulationMode.Fast,
            Components = new List<ComponentSpec>
            {
                new ComponentSpec { Id = "U1", Type = "ArduinoUno" },
                new ComponentSpec { Id = "D1", Type = "LED" }
            }
        };

        // 2. Init IPC & Host
        _firmwareClient = new FirmwareClient();
        // TODO: Start firmware host process here if needed
        // SimulationManager.LaunchEngine(FirmwareHostPath);

        _simHost = new SimHost(spec, _firmwareClient);

        // MVP: Launch Mock Engine if path is provided and valid
        if (!string.IsNullOrEmpty(FirmwareHostPath) && FirmwareHostPath != "Default" && System.IO.File.Exists(FirmwareHostPath))
        {
            _simHost.StartFirmwareProcess(FirmwareHostPath);
        }

        _simHost.OnTickComplete += HandleTick;
        _simHost.Start();
    }

    private void OnStopClicked()
    {
        Debug.Log("Stopping Simulation...");
        Shutdown();
    }

    private void Shutdown()
    {
        if (_simHost != null)
        {
            _simHost.OnTickComplete -= HandleTick;
            _simHost.Stop();
            _simHost = null;
        }
        if (_firmwareClient != null)
        {
            _firmwareClient.Disconnect();
            _firmwareClient = null;
        }
    }

    private void HandleTick(double simTime)
    {
        // Thread safety: This callback comes from SimHost thread.
        // We need to dispatch to Unity Main Thread.
        // Simple MainThreadDispatcher pattern or just standard Queue.
        // Unity doesn't marshal this automatically; keep main-thread dispatch.
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            UpdateUI(simTime);
        });
    }

    private void UpdateUI(double simTime)
    {
        // Mock Visualization Logic:
        // Use SimTime to blink because we haven't wired up "State" return fully in SimHost yet,
        // OR rely on FastSolver logic if we implemented it.
        // FastSolver.Solve was empty MVP.
        // Let's cheat for Vertical Slice: Toggle LED every second.

        // Wait, SimHost calls Tick() -> FirmwareClient -> returns Result.
        // We should expose Result in SimHost to read it here?
        // Or store sim state in SimHost.

        if (_ledVisual != null)
        {
            // Visual feedback
            bool on = (simTime % 1.0) < 0.5;
            _ledVisual.style.backgroundColor = on ? new StyleColor(Color.red) : new StyleColor(new Color(0.5f, 0, 0));
        }
    }
}

// Simple Dispatcher helper
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private readonly Queue<System.Action> _executionQueue = new Queue<System.Action>();

    public static UnityMainThreadDispatcher Instance()
    {
        if (!_instance)
        {
            var go = new GameObject("UnityMainThreadDispatcher");
            _instance = go.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(go);
        }
        return _instance;
    }

    public void Enqueue(System.Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    public void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }
}
