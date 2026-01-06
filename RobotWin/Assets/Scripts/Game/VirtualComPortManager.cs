using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace RobotTwin.Game
{
    public sealed class VirtualComPortManager : IDisposable
    {
        private sealed class ComPortPair
        {
            public string BoardId;
            public string AppPort;
            public string IdePort;
            public object Port;
        }

        private readonly Dictionary<string, ComPortPair> _pairs =
            new Dictionary<string, ComPortPair>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _usbConnected =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string> _log;
        private bool _disposed;

        public string SetupcPath { get; set; }
        public string InstallScriptPath { get; set; }
        public int PortBase { get; set; } = 30;
        public int BaudRate { get; set; } = 9600;
        public string StatusMessage { get; private set; } = "Virtual COM: idle";

        public VirtualComPortManager(Action<string> log = null)
        {
            _log = log;
        }

        public string BuildStatusPayloadJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"port_base\":{PortBase},");
            sb.Append($"\"status\":\"{EscapeJson(StatusMessage)}\",");
            sb.Append("\"pairs\":[");

            bool first = true;
            foreach (var pair in _pairs.Values.OrderBy(p => p.BoardId, StringComparer.OrdinalIgnoreCase))
            {
                if (!first) sb.Append(",");
                first = false;

                bool usbConnected = _usbConnected.Contains(pair.BoardId);
                sb.Append("{");
                sb.Append($"\"board_id\":\"{EscapeJson(pair.BoardId)}\",");
                sb.Append($"\"ide_port\":\"{EscapeJson(pair.IdePort)}\",");
                sb.Append($"\"app_port\":\"{EscapeJson(pair.AppPort)}\",");
                sb.Append($"\"usb_connected\":{(usbConnected ? "true" : "false")}");
                sb.Append("}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        public void SetStatus(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            StatusMessage = message;
        }

        public void ConfigureBoards(IEnumerable<string> boardIds)
        {
            if (_disposed) return;

            var ids = boardIds == null
                ? new List<string>()
                : boardIds.Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            var keep = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _pairs.Values.Where(p => !keep.Contains(p.BoardId)).ToList())
            {
                ClosePort(pair);
                _pairs.Remove(pair.BoardId);
                _usbConnected.Remove(pair.BoardId);
            }

            int index = 0;
            foreach (var id in ids)
            {
                var appPort = $"COM{PortBase + index * 2}";
                var idePort = $"COM{PortBase + index * 2 + 1}";
                if (!_pairs.TryGetValue(id, out var pair))
                {
                    pair = new ComPortPair { BoardId = id };
                    _pairs[id] = pair;
                }

                pair.AppPort = appPort;
                pair.IdePort = idePort;
                index++;
            }

            StatusMessage = _pairs.Count == 0 ? "Virtual COM: no boards" : StatusMessage;
        }

        public bool TryGetIdePort(string boardId, out string idePort)
        {
            idePort = string.Empty;
            if (string.IsNullOrWhiteSpace(boardId)) return false;
            if (!_pairs.TryGetValue(boardId, out var pair)) return false;
            idePort = pair.IdePort;
            return !string.IsNullOrWhiteSpace(idePort);
        }

        public bool TryGetPorts(string boardId, out string appPort, out string idePort)
        {
            appPort = string.Empty;
            idePort = string.Empty;
            if (string.IsNullOrWhiteSpace(boardId)) return false;
            if (!_pairs.TryGetValue(boardId, out var pair)) return false;
            appPort = pair.AppPort;
            idePort = pair.IdePort;
            return !string.IsNullOrWhiteSpace(appPort) && !string.IsNullOrWhiteSpace(idePort);
        }

        public string[] GetAvailablePorts()
        {
            return GetPortNames();
        }

        public bool EnsurePortsInstalled(bool requestAdmin)
        {
            if (_pairs.Count == 0)
            {
                StatusMessage = "Virtual COM: no boards";
                _log?.Invoke("[VirtualCOM] No boards configured.");
                return true;
            }

            if (!HasSerialPortSupport())
            {
                StatusMessage = "Virtual COM: SerialPort unavailable";
                _log?.Invoke("[VirtualCOM] SerialPort unavailable (missing assembly).");
                return false;
            }

            var missing = _pairs.Values.Where(p => !PortsExist(p)).ToList();
            if (missing.Count == 0)
            {
                StatusMessage = "Virtual COM: ready";
                _log?.Invoke("[VirtualCOM] Ports already installed.");
                return true;
            }

            if (string.IsNullOrWhiteSpace(SetupcPath) || !File.Exists(SetupcPath))
            {
                StatusMessage = "Virtual COM: setupc.exe missing";
                _log?.Invoke("[VirtualCOM] setupc.exe missing.");
                return false;
            }

            if (!requestAdmin)
            {
                StatusMessage = "Virtual COM: ports missing";
                _log?.Invoke($"[VirtualCOM] Missing ports (admin needed): {FormatPairs(missing)}");
                return false;
            }

            bool installed = InstallPortPairs(missing);
            if (!installed)
            {
                StatusMessage = "Virtual COM: install failed";
                _log?.Invoke("[VirtualCOM] Install failed.");
                return false;
            }

            var stillMissing = _pairs.Values.Where(p => !PortsExist(p)).ToList();
            if (stillMissing.Count == 0)
            {
                StatusMessage = "Virtual COM: installed";
                _log?.Invoke("[VirtualCOM] Ports installed.");
                return true;
            }

            StatusMessage = "Virtual COM: partial install";
            _log?.Invoke($"[VirtualCOM] Ports still missing: {FormatPairs(stillMissing)}");
            return false;
        }

        public bool ForceInstallPorts(bool requestAdmin)
        {
            if (_pairs.Count == 0)
            {
                StatusMessage = "Virtual COM: no boards";
                _log?.Invoke("[VirtualCOM] No boards configured.");
                return true;
            }

            if (string.IsNullOrWhiteSpace(SetupcPath) || !File.Exists(SetupcPath))
            {
                StatusMessage = "Virtual COM: setupc.exe missing";
                _log?.Invoke("[VirtualCOM] setupc.exe missing.");
                return false;
            }

            if (!requestAdmin)
            {
                StatusMessage = "Virtual COM: admin required";
                _log?.Invoke("[VirtualCOM] Admin required to install ports.");
                return false;
            }

            var pairs = _pairs.Values.ToList();
            _log?.Invoke($"[VirtualCOM] Verifying port pairs: {FormatPairs(pairs)}");
            var missing = pairs.Where(p => !PortsExist(p)).ToList();
            if (missing.Count == 0)
            {
                StatusMessage = "Virtual COM: ready";
                _log?.Invoke("[VirtualCOM] Ports already present; skipping install.");
                return true;
            }

            bool installed = InstallPortPairs(missing);

            var stillMissing = _pairs.Values.Where(p => !PortsExist(p)).ToList();
            if (stillMissing.Count == 0)
            {
                StatusMessage = "Virtual COM: ready";
                _log?.Invoke("[VirtualCOM] Ports verified.");
                return true;
            }

            if (!installed)
            {
                StatusMessage = "Virtual COM: install failed";
                _log?.Invoke("[VirtualCOM] Install failed.");
                return false;
            }

            StatusMessage = "Virtual COM: partial install";
            _log?.Invoke($"[VirtualCOM] Ports still missing: {FormatPairs(stillMissing)}");
            return false;
        }

        public void SetUsbConnected(string boardId, bool connected)
        {
            if (string.IsNullOrWhiteSpace(boardId)) return;
            if (!_pairs.TryGetValue(boardId, out var pair)) return;

            if (connected)
            {
                _usbConnected.Add(boardId);
                EnsurePortOpen(pair);
            }
            else
            {
                _usbConnected.Remove(boardId);
                ClosePort(pair);
            }
        }

        public void RefreshConnections()
        {
            foreach (var pair in _pairs.Values)
            {
                if (_usbConnected.Contains(pair.BoardId))
                {
                    EnsurePortOpen(pair);
                }
                else
                {
                    ClosePort(pair);
                }
            }
        }

        public void PublishSerial(string boardId, string text)
        {
            if (string.IsNullOrWhiteSpace(boardId) || string.IsNullOrEmpty(text)) return;
            if (!_pairs.TryGetValue(boardId, out var pair)) return;
            if (pair.Port == null || !IsPortOpen(pair.Port)) return;

            string payload = text.Replace("\r\n", "\n").Replace("\r", "\n");
            if (!WritePort(pair.Port, payload))
            {
                _log?.Invoke($"[VirtualCOM] Write failed for {pair.AppPort}.");
            }
        }

        public void CloseAll()
        {
            foreach (var pair in _pairs.Values)
            {
                ClosePort(pair);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            CloseAll();
            _disposed = true;
        }

        private static bool PortsExist(ComPortPair pair)
        {
            if (pair == null) return false;
            var ports = new HashSet<string>(GetPortNames(), StringComparer.OrdinalIgnoreCase);
            return ports.Contains(pair.AppPort) && ports.Contains(pair.IdePort);
        }

        private static string FormatPairs(IEnumerable<ComPortPair> pairs)
        {
            if (pairs == null) return string.Empty;
            return string.Join(", ", pairs.Select(p => $"{p.AppPort}<->{p.IdePort}"));
        }

        private bool InstallPortPairs(IReadOnlyList<ComPortPair> pairs)
        {
            string pairArg = string.Join(";", pairs.Select(p => $"{p.AppPort},{p.IdePort}"));
            var psi = BuildInstallProcess(pairArg);
            if (psi == null)
            {
                StatusMessage = "Virtual COM: install command missing";
                _log?.Invoke("[VirtualCOM] Install command not available.");
                return false;
            }

            try
            {
                _log?.Invoke($"[VirtualCOM] Installing port pairs: {pairArg}");
                var process = Process.Start(psi);
                if (process == null)
                {
                    StatusMessage = "Virtual COM: admin canceled";
                    _log?.Invoke("[VirtualCOM] Install canceled.");
                    return false;
                }
                process.WaitForExit();
                _log?.Invoke($"[VirtualCOM] Install exit code: {process.ExitCode}");
                return process.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223 || ex.NativeErrorCode == 5)
            {
                StatusMessage = "Virtual COM: admin denied";
                _log?.Invoke("[VirtualCOM] Admin permission denied.");
                return false;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[VirtualCOM] Install failed: {ex.Message}");
                return false;
            }
        }

        private ProcessStartInfo BuildInstallProcess(string pairArg)
        {
            string workingDir = string.Empty;
            if (!string.IsNullOrWhiteSpace(SetupcPath))
            {
                workingDir = Path.GetDirectoryName(SetupcPath);
            }

            if (!string.IsNullOrWhiteSpace(InstallScriptPath) && File.Exists(InstallScriptPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{InstallScriptPath}\" -SetupcPath \"{SetupcPath}\" -Pairs \"{pairArg}\"",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                if (!string.IsNullOrWhiteSpace(workingDir) && Directory.Exists(workingDir))
                {
                    psi.WorkingDirectory = workingDir;
                }
                return psi;
            }

            string command = BuildSetupcChain(pairArg);
            if (string.IsNullOrWhiteSpace(command)) return null;

            var fallback = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = true,
                Verb = "runas"
            };
            if (!string.IsNullOrWhiteSpace(workingDir) && Directory.Exists(workingDir))
            {
                fallback.WorkingDirectory = workingDir;
            }
            return fallback;
        }

        private string BuildSetupcChain(string pairArg)
        {
            if (string.IsNullOrWhiteSpace(pairArg)) return string.Empty;
            var pairs = pairArg.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var commands = new List<string>();
            foreach (var pair in pairs)
            {
                var ports = pair.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (ports.Length < 2) continue;
                string appPort = ports[0].Trim();
                string idePort = ports[1].Trim();
                commands.Add($"\"{SetupcPath}\" install PortName={appPort} PortName={idePort}");
            }
            return string.Join(" & ", commands);
        }

        private void EnsurePortOpen(ComPortPair pair)
        {
            if (pair.Port != null && IsPortOpen(pair.Port)) return;

            var ports = GetPortNames();
            if (!ports.Any(port => string.Equals(port, pair.AppPort, StringComparison.OrdinalIgnoreCase)))
            {
                StatusMessage = $"Virtual COM: {pair.AppPort} missing";
                return;
            }

            try
            {
                var port = CreateSerialPort(pair.AppPort, BaudRate);
                if (port == null)
                {
                    StatusMessage = "Virtual COM: SerialPort unavailable";
                    return;
                }
                pair.Port = port;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Virtual COM: open {pair.AppPort} failed";
                _log?.Invoke($"[VirtualCOM] Open failed for {pair.AppPort}: {ex.Message}");
            }
        }

        private static void ClosePort(ComPortPair pair)
        {
            if (pair == null || pair.Port == null) return;
            try
            {
                if (IsPortOpen(pair.Port)) InvokeIfExists(pair.Port, "Close");
            }
            catch
            {
                // Ignore shutdown errors.
            }
            finally
            {
                (pair.Port as IDisposable)?.Dispose();
                pair.Port = null;
            }
        }

        private static bool HasSerialPortSupport()
        {
            return GetSerialPortType() != null;
        }

        private static Type GetSerialPortType()
        {
            return Type.GetType("System.IO.Ports.SerialPort, System.IO.Ports") ??
                   Type.GetType("System.IO.Ports.SerialPort, System");
        }

        private static string[] GetPortNames()
        {
            var type = GetSerialPortType();
            if (type == null) return Array.Empty<string>();
            var method = type.GetMethod("GetPortNames", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null) return Array.Empty<string>();
            return method.Invoke(null, null) as string[] ?? Array.Empty<string>();
        }

        private object CreateSerialPort(string portName, int baudRate)
        {
            var type = GetSerialPortType();
            if (type == null) return null;
            var port = Activator.CreateInstance(type, portName, baudRate);
            SetPropertyIfExists(port, "NewLine", "\n");
            SetPropertyIfExists(port, "DtrEnable", false);
            SetPropertyIfExists(port, "RtsEnable", false);
            InvokeIfExists(port, "Open");
            return port;
        }

        private static bool IsPortOpen(object port)
        {
            if (port == null) return false;
            var prop = port.GetType().GetProperty("IsOpen");
            return prop != null && prop.GetValue(port) is bool open && open;
        }

        private static bool WritePort(object port, string payload)
        {
            if (port == null) return false;
            try
            {
                var method = port.GetType().GetMethod("Write", new[] { typeof(string) });
                if (method == null) return false;
                method.Invoke(port, new object[] { payload });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SetPropertyIfExists(object target, string name, object value)
        {
            var prop = target?.GetType().GetProperty(name);
            if (prop == null || !prop.CanWrite) return;
            prop.SetValue(target, value);
        }

        private static void InvokeIfExists(object target, string method)
        {
            var info = target?.GetType().GetMethod(method, Type.EmptyTypes);
            info?.Invoke(target, null);
        }
    }
}
