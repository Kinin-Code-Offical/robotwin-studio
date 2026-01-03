using System;
using System.IO;
using UnityEngine;
using RobotTwin.Game;

namespace RobotTwin.Debugging
{
    public class BridgeLogWriter : MonoBehaviour
    {
        [SerializeField] private bool _enabled = true;
        [SerializeField] private float _intervalSeconds = 1.0f;
        [SerializeField] private string _logFileName = "bridge.log";

        private float _nextTime;
        private StreamWriter _writer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            var go = new GameObject("BridgeLogWriter");
            go.AddComponent<BridgeLogWriter>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (!_enabled) return;
            string projectRoot = Directory.GetParent(Application.dataPath)?.Parent?.FullName
                                 ?? Directory.GetParent(Application.dataPath).FullName;
            string logDir = Path.Combine(projectRoot, "logs", "native");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, _logFileName);
            _writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
            _writer.WriteLine($"[SESSION START] {DateTime.UtcNow:O}");
        }

        private void OnDestroy()
        {
            if (_writer == null) return;
            _writer.WriteLine($"[SESSION END] {DateTime.UtcNow:O}");
            _writer.Dispose();
            _writer = null;
        }

        private void Update()
        {
            if (!_enabled || _writer == null) return;
            if (Time.unscaledTime < _nextTime) return;
            _nextTime = Time.unscaledTime + Mathf.Max(0.1f, _intervalSeconds);
            WriteSnapshot();
        }

        private void WriteSnapshot()
        {
            var host = SimHost.Instance;
            var physics = NativePhysicsWorld.Instance;

            string timestamp = DateTime.UtcNow.ToString("O");
            bool running = host != null && host.IsRunning;
            bool useNative = host != null && host.UseNativeEngine;
            bool nativeReady = host != null && host.NativePinsReady;
            int tick = host?.TickCount ?? 0;
            int signals = host?.LastTelemetry?.Signals?.Count ?? 0;
            int validations = host?.LastTelemetry?.ValidationMessages?.Count ?? 0;
            int physicsBodies = physics?.BodyCount ?? 0;

            _writer.WriteLine(
                $"{timestamp} running={running} native={useNative} native_ready={nativeReady} " +
                $"tick={tick} signals={signals} validations={validations} physics_bodies={physicsBodies}");
        }
    }
}
