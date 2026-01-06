using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using RobotTwin.Game;

namespace RobotTwin.Debugging
{
    public class RemoteCommandServer : MonoBehaviour
    {
        private static RemoteCommandServer _instance;
        private HttpListener _listener;
        private Thread _listenerThread;
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private bool _isRunning = true;
        private const int BasePort = 8085;
        private const int MaxPortAttempts = 10;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (_instance != null) return;
            var go = new GameObject("RemoteCommandServer");
            go.AddComponent<RemoteCommandServer>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (_listener != null) return;

            for (int i = 0; i < MaxPortAttempts; i++)
            {
                int port = BasePort + i;
                string url = $"http://localhost:{port}/";
                _listener = new HttpListener();
                _listener.Prefixes.Add(url);
                try
                {
                    _listener.Start();
                    Debug.Log($"[RemoteCommandServer] Listening on {url}");
                    _listenerThread = new Thread(ListenLoop);
                    _listenerThread.Start();
                    return;
                }
                catch (Exception)
                {
                    _listener.Close();
                    _listener = null;
                }
            }

            Debug.LogWarning($"[RemoteCommandServer] Could not bind to any port between {BasePort} and {BasePort + MaxPortAttempts - 1}. Server disabled.");
            _isRunning = false;
        }

        private void OnDestroy()
        {
            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();
            if (_listenerThread != null && _listenerThread.IsAlive)
            {
                _listenerThread.Join(500);
            }
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            while (!_mainThreadActions.IsEmpty)
            {
                if (_mainThreadActions.TryDequeue(out var action))
                {
                    action?.Invoke();
                }
            }
        }

        private void ListenLoop()
        {
            while (_isRunning && _listener.IsListening)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem((_) => HandleRequest(context));
                }
                catch (HttpListenerException)
                {
                    // Listener stopped
                    break;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RemoteCommandServer] Error: {e.Message}");
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            string responseString = "OK";
            int statusCode = 200;

            try
            {
                string rawUrl = context.Request.RawUrl;
                string command = rawUrl.Split('?')[0].Trim('/');
                var queryParams = ParseQuery(context.Request.Url?.Query);

                switch (command)
                {
                    case "screenshot":
                        Enqueue(() =>
                        {
                            string filename = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots");
                            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

                            string fullPath = Path.Combine(path, filename);
                            ScreenCapture.CaptureScreenshot(Path.Combine("Screenshots", filename));
                            Debug.Log($"[RemoteCommandServer] Screenshot captured: {fullPath}");
                        });
                        break;

                    case "run-tests":
                        Enqueue(() =>
                        {
                            Debug.Log("[RemoteCommandServer] Triggering Tests...");
                            if (AutoPilot.Instance != null)
                                AutoPilot.Instance.StartSmokeTest();
                            else
                                Debug.LogError("[RemoteCommandServer] AutoPilot Instance is NULL");
                        });
                        break;

                    case "action":
                        queryParams.TryGetValue("type", out string actionType);
                        queryParams.TryGetValue("target", out string target);
                        Enqueue(() =>
                        {
                            Debug.Log($"[RemoteCommandServer] ACTION: {actionType} on {target}");
                            // TODO: Implement actual UI Toolkit event simulation
                        });
                        break;

                    case "query":
                        queryParams.TryGetValue("target", out string selector);
                        Debug.Log($"[RemoteCommandServer] QUERY: {selector}");
                        // Mock Response for now
                        if (selector == "CurrentScene") responseString = "{\"value\": \"CircuitStudio\"}";
                        else if (selector == "#RunMode") responseString = "{\"value\": true}";
                        else responseString = "{\"value\": null}";
                        break;

                    case "reset":
                        Enqueue(() =>
                        {
                            Debug.Log("[RemoteCommandServer] Resetting Scene...");
                            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                        });
                        break;

                    case "status":
                        int engineVer = -1;
                        try { engineVer = RobotTwin.Core.NativeBridge.GetVersion(); } catch { }

                        string engineStatus = (engineVer > 0) ? "connected" : "disconnected";
                        responseString = $"{{\"engine\": \"{engineStatus}\", \"version\": {engineVer}}}";
                        break;

                    case "telemetry":
                        responseString = BuildTelemetryPayload();
                        break;

                    case "bridge":
                        responseString = BuildBridgePayload();
                        break;

                    case "firmware-mode":
                        queryParams.TryGetValue("mode", out string modeValue);
                        string mode = (modeValue ?? string.Empty).Trim().ToLowerInvariant();
                        bool lockstep = mode != "realtime";
                        bool hasSession = SessionManager.Instance != null;
                        bool hasSimHost = SimHost.Instance != null;
                        bool requiresRestart = hasSimHost && SimHost.Instance.UseExternalFirmware;
                        Enqueue(() =>
                        {
                            var session = SessionManager.Instance;
                            if (session != null)
                            {
                                session.FirmwareHostLockstep = lockstep;
                            }
                            SimHost.Instance?.SetFirmwareHostMode(lockstep);
                        });
                        responseString = $"{{\"mode\":\"{(lockstep ? "lockstep" : "realtime")}\",\"applied\":{BoolJson(hasSession || hasSimHost)},\"requires_restart\":{BoolJson(requiresRestart)}}}";
                        break;

                    default:
                        statusCode = 404;
                        responseString = "Unknown Command";
                        break;
                }
            }
            catch (Exception ex)
            {
                statusCode = 500;
                responseString = $"Error: {ex.Message}";
            }

            try
            {
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
                context.Response.StatusCode = statusCode;
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
            catch
            {
                // Ignored
            }
        }

        private void Enqueue(Action action)
        {
            _mainThreadActions.Enqueue(action);
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query))
            {
                return dict;
            }

            // query may start with '?'
            if (query.StartsWith("?", StringComparison.Ordinal))
            {
                query = query.Substring(1);
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                return dict;
            }

            var parts = query.Split('&');
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                var kv = part.Split(new[] { '=' }, 2);
                var key = Uri.UnescapeDataString(kv[0] ?? string.Empty);
                if (string.IsNullOrEmpty(key)) continue;
                var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1] ?? string.Empty) : string.Empty;
                dict[key] = value;
            }

            return dict;
        }

        private static string BuildTelemetryPayload()
        {
            var host = SimHost.Instance;
            if (host == null)
            {
                return "{\"running\":false,\"reason\":\"SimHost not ready\"}";
            }

            var telemetry = host.LastTelemetry;
            var circuit = host.Circuit;
            var sb = new StringBuilder();

            sb.Append("{");
            sb.Append($"\"running\":{BoolJson(host.IsRunning)},");
            sb.Append($"\"scene\":\"{EscapeJson(SceneManager.GetActiveScene().name)}\",");
            sb.Append($"\"tick\":{host.TickCount},");
            sb.Append($"\"time\":{host.SimTime.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"signals\":{(telemetry?.Signals?.Count ?? 0)},");
            sb.Append($"\"validation\":{(telemetry?.ValidationMessages?.Count ?? 0)},");
            sb.Append("\"components\":[");

            if (circuit?.Components != null)
            {
                bool first = true;
                foreach (var comp in circuit.Components)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{");
                    sb.Append($"\"id\":\"{EscapeJson(comp.Id)}\",");
                    sb.Append($"\"type\":\"{EscapeJson(comp.Type)}\",");

                    bool powered = host.BoardPowerById != null &&
                                   host.BoardPowerById.TryGetValue(comp.Id, out var isPowered) &&
                                   isPowered;
                    sb.Append($"\"powered\":{BoolJson(powered)},");

                    sb.Append("\"values\":{");
                    AppendSignal(sb, telemetry, $"COMP:{comp.Id}:V", "v");
                    AppendSignal(sb, telemetry, $"COMP:{comp.Id}:I", "i");
                    AppendSignal(sb, telemetry, $"COMP:{comp.Id}:P", "p");
                    AppendSignal(sb, telemetry, $"COMP:{comp.Id}:T", "t");
                    AppendSignal(sb, telemetry, $"COMP:{comp.Id}:R", "r");
                    AppendSignal(sb, telemetry, $"COMP:{comp.Id}:L", "l");
                    AppendSignal(sb, telemetry, $"SRC:{comp.Id}:V", "src_v");
                    AppendSignal(sb, telemetry, $"SRC:{comp.Id}:I", "src_i");
                    AppendSignal(sb, telemetry, $"SRC:{comp.Id}:SOC", "soc");
                    AppendSignal(sb, telemetry, $"SRC:{comp.Id}:RINT", "rint");
                    TrimTrailingComma(sb);
                    sb.Append("}");

                    sb.Append("}");
                }
            }

            sb.Append("]");
            AppendFirmwarePerfPayload(sb, telemetry);
            AppendFirmwareHostPayload(sb, host);
            AppendRealtimeFlagsPayload(sb, host);
            AppendRealtimeBudgetPayload(sb, host);
            AppendTimingPayload(sb, host);
            AppendTickTracePayload(sb, host);
            sb.Append("}");
            return sb.ToString();
        }

        private static string BuildBridgePayload()
        {
            var host = SimHost.Instance;
            if (host == null)
            {
                return "{\"ready\":false,\"reason\":\"SimHost not ready\"}";
            }

            var telemetry = host.LastTelemetry;
            int signalCount = telemetry?.Signals?.Count ?? 0;
            int validationCount = telemetry?.ValidationMessages?.Count ?? 0;
            var physicsWorld = NativePhysicsWorld.Instance;
            bool physicsRunning = physicsWorld != null && physicsWorld.IsRunning;
            int physicsBodies = physicsWorld?.BodyCount ?? 0;
            var firmwareInfo = host.GetFirmwareHostTelemetry();

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"ready\":true,");
            sb.Append($"\"running\":{BoolJson(host.IsRunning)},");
            sb.Append($"\"use_native\":{BoolJson(host.UseNativeEngine)},");
            sb.Append($"\"use_firmware\":{BoolJson(host.UseExternalFirmware)},");
            sb.Append($"\"native_ready\":{BoolJson(host.NativePinsReady)},");
            sb.Append($"\"external_firmware_sessions\":{host.ExternalFirmwareSessionCount},");
            sb.Append($"\"firmware_host\":\"{EscapeJson(firmwareInfo.ExecutableName)}\",");
            sb.Append($"\"firmware_mode\":\"{EscapeJson(firmwareInfo.Mode)}\",");
            sb.Append($"\"firmware_pipe\":\"{EscapeJson(firmwareInfo.PipeName)}\",");
            sb.Append($"\"virtual_boards\":{host.VirtualBoardCount},");
            sb.Append($"\"powered_boards\":{host.PoweredBoardCount},");
            sb.Append($"\"physics_running\":{BoolJson(physicsRunning)},");
            sb.Append($"\"physics_bodies\":{physicsBodies},");
            sb.Append($"\"signals\":{signalCount},");
            sb.Append($"\"validation\":{validationCount},");

            string virtualCom = host.VirtualComStatusJson;
            if (string.IsNullOrWhiteSpace(virtualCom))
            {
                sb.Append("\"virtual_com\":null,");
            }
            else
            {
                sb.Append("\"virtual_com\":");
                sb.Append(virtualCom);
                sb.Append(",");
            }

            sb.Append("\"contract\":{");
            sb.Append("\"control\":[\"tick_index\",\"dt_seconds\",\"actuators\",\"power\"],");
            sb.Append("\"physics\":[\"tick_index\",\"dt_seconds\",\"bodies\",\"sensors\",\"constraints\"]");
            sb.Append("}");
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendSignal(StringBuilder sb, RobotTwin.CoreSim.Runtime.TelemetryFrame telemetry, string key, string label)
        {
            if (telemetry?.Signals == null) return;
            if (!telemetry.Signals.TryGetValue(key, out var value)) return;
            sb.Append($"\"{label}\":{value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},");
        }

        private static void AppendFirmwarePerfPayload(StringBuilder sb, RobotTwin.CoreSim.Runtime.TelemetryFrame telemetry)
        {
            sb.Append(",\"firmware\":[");
            if (telemetry?.Signals == null || telemetry.Signals.Count == 0)
            {
                sb.Append("]");
                return;
            }

            var byBoard = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in telemetry.Signals)
            {
                if (!entry.Key.StartsWith("FW:", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = entry.Key.Split(':');
                if (parts.Length < 3) continue;
                string boardId = parts[1];
                string metric = parts[2];
                if (!byBoard.TryGetValue(boardId, out var metrics))
                {
                    metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    byBoard[boardId] = metrics;
                }
                metrics[metric] = entry.Value;
            }

            bool firstBoard = true;
            string[] orderedKeys =
            {
                "cycles",
                "adc_samples",
                "uart_tx0",
                "uart_tx1",
                "uart_tx2",
                "uart_tx3",
                "uart_rx0",
                "uart_rx1",
                "uart_rx2",
                "uart_rx3",
                "spi_transfers",
                "twi_transfers",
                "wdt_resets",
                "drops"
            };

            foreach (var board in byBoard)
            {
                if (!firstBoard) sb.Append(",");
                firstBoard = false;
                sb.Append("{");
                sb.Append($"\"id\":\"{EscapeJson(board.Key)}\",");
                sb.Append("\"metrics\":{");

                bool firstMetric = true;
                foreach (var key in orderedKeys)
                {
                    if (!board.Value.TryGetValue(key, out var value)) continue;
                    if (!firstMetric) sb.Append(",");
                    firstMetric = false;
                    sb.Append($"\"{key}\":{value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}");
                }

                sb.Append("}");
                sb.Append("}");
            }

            sb.Append("]");
        }

        private static void AppendTickTracePayload(StringBuilder sb, SimHost host)
        {
            if (host == null) return;
            var trace = host.GetTickTraceSnapshot();
            sb.Append(",\"trace\":[");
            for (int i = 0; i < trace.Count; i++)
            {
                var sample = trace[i];
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"tick\":{sample.TickIndex},");
                sb.Append($"\"dt\":{sample.DtSeconds.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)},");
                sb.Append($"\"solve_ms\":{sample.SolveMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},");
                sb.Append($"\"native\":{BoolJson(sample.UsedNativePins)},");
                sb.Append($"\"external_fw\":{BoolJson(sample.UsedExternalFirmware)}");
                sb.Append("}");
            }
            sb.Append("]");
        }

        private static void AppendFirmwareHostPayload(StringBuilder sb, SimHost host)
        {
            if (host == null) return;
            var info = host.GetFirmwareHostTelemetry();
            sb.Append(",\"firmware_host\":{");
            sb.Append($"\"exe\":\"{EscapeJson(info.ExecutableName)}\",");
            sb.Append($"\"path\":\"{EscapeJson(info.ResolvedPath)}\",");
            sb.Append($"\"override\":\"{EscapeJson(info.OverridePath)}\",");
            sb.Append($"\"pipe\":\"{EscapeJson(info.PipeName)}\",");
            sb.Append($"\"mode\":\"{EscapeJson(info.Mode)}\",");
            sb.Append($"\"external\":{BoolJson(info.ExternalEnabled)}");
            sb.Append("}");
        }

        private static void AppendRealtimeFlagsPayload(StringBuilder sb, SimHost host)
        {
            if (host == null) return;
            var session = SessionManager.Instance;
            bool virtualMcu = session == null || session.UseVirtualMcu;
            sb.Append(",\"realtime\":{");
            sb.Append($"\"external_firmware\":{BoolJson(host.UseExternalFirmware)},");
            sb.Append($"\"native\":{BoolJson(host.UseNativeEngine)},");
            sb.Append($"\"virtual_mcu\":{BoolJson(virtualMcu)},");
            sb.Append($"\"firmware_mode\":\"{EscapeJson(host.GetFirmwareHostTelemetry().Mode)}\"");
            sb.Append("}");
        }

        private static void AppendTimingPayload(StringBuilder sb, SimHost host)
        {
            if (host == null) return;
            var timing = host.GetTimingStatsSnapshot();
            sb.Append(",\"timing\":{");
            sb.Append($"\"tick_samples\":{timing.TickSamples},");
            sb.Append($"\"jitter_samples\":{timing.JitterSamples},");
            sb.Append($"\"overruns\":{timing.Overruns},");
            sb.Append($"\"avg_jitter_ms\":{timing.AvgJitterMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"max_jitter_ms\":{timing.MaxJitterMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"last_jitter_ms\":{timing.LastJitterMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"avg_tick_ms\":{timing.AvgTickMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"max_tick_ms\":{timing.MaxTickMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)},");
            sb.Append($"\"last_tick_ms\":{timing.LastTickMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}");
            sb.Append("}");
        }

        private static void AppendRealtimeBudgetPayload(StringBuilder sb, SimHost host)
        {
            if (host == null) return;
            var stats = host.GetRealtimeBudgetStatsSnapshot();
            sb.Append(",\"realtime_stats\":{");
            sb.Append($"\"fast_path\":{stats.FastPathTicks},");
            sb.Append($"\"corrective\":{stats.CorrectiveTicks},");
            sb.Append($"\"budget_overruns\":{stats.BudgetOverruns}");
            sb.Append("}");
        }

        private static void TrimTrailingComma(StringBuilder sb)
        {
            if (sb.Length == 0) return;
            if (sb[sb.Length - 1] == ',') sb.Length -= 1;
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        private static string BoolJson(bool value) => value ? "true" : "false";
    }
}
