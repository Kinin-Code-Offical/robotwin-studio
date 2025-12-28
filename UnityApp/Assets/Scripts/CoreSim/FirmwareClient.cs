using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using UnityEngine;

namespace RobotTwin.CoreSim
{
    public class FirmwareClient : MonoBehaviour
    {
        public static FirmwareClient Instance { get; private set; }

        private Process _firmwareProcess;
        private NamedPipeClientStream _pipeClient;
        private StreamWriter _pipeWriter;

        private void Awake()
        {
            if (Instance != null) Destroy(gameObject);
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void LaunchFirmware(string executablePath)
        {
            if (_firmwareProcess != null && !_firmwareProcess.HasExited)
            {
                UnityEngine.Debug.LogWarning("[FirmwareClient] Process already running.");
                return;
            }

            if (!File.Exists(executablePath))
            {
                UnityEngine.Debug.LogError($"[FirmwareClient] Firmware executable not found at: {executablePath}");
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "--pipe RoboTwinFirmware", // Example arg
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                _firmwareProcess = Process.Start(startInfo);
                UnityEngine.Debug.Log($"[FirmwareClient] Launched Firmware PID: {_firmwareProcess.Id}");
                
                // Connect via IPC (Async to avoid blocking Unity main thread)
                // In a real robust app, wait for signal. Here we just wait a bit or start connect.
                ConnectPipeOnThread();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[FirmwareClient] Failed to launch: {ex.Message}");
            }
        }

        private async void ConnectPipeOnThread()
        {
            await System.Threading.Tasks.Task.Run(() => 
            {
                try
                {
                    // Assuming Firmware creates a NamedPipeServer named "RoboTwinFirmware"
                    _pipeClient = new NamedPipeClientStream(".", "RoboTwinFirmware", PipeDirection.Out);
                    UnityEngine.Debug.Log("[FirmwareClient] Connecting to pipe...");
                    _pipeClient.Connect(5000); // 5s timeout
                    _pipeWriter = new StreamWriter(_pipeClient) { AutoFlush = true };
                    UnityEngine.Debug.Log("[FirmwareClient] Connected to Firmware Pipe!");
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[FirmwareClient] Pipe connection failed (Mock mode?): {ex.Message}");
                }
            });
        }

        public void SendIOState(string pin, int value)
        {
            if (_pipeWriter != null && _pipeClient.IsConnected)
            {
                try
                {
                    _pipeWriter.WriteLine($"IO:{pin}={value}");
                }
                catch
                {
                    UnityEngine.Debug.LogError("[FirmwareClient] Pipe disconnected.");
                }
            }
        }

        public void StopFirmware()
        {
            if (_pipeClient != null)
            {
                _pipeClient.Dispose();
                _pipeClient = null;
            }

            if (_firmwareProcess != null && !_firmwareProcess.HasExited)
            {
                _firmwareProcess.Kill();
                _firmwareProcess = null;
                UnityEngine.Debug.Log("[FirmwareClient] Firmware stopped.");
            }
        }

        private void OnDestroy()
        {
            StopFirmware();
        }
    }
}
