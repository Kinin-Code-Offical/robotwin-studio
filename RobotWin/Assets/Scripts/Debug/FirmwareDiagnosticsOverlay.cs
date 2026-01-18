using System.Collections.Generic;
using UnityEngine;
using RobotTwin.CoreSim;
using RobotTwin.Game;

namespace RobotTwin.Debugging
{
    public sealed class FirmwareDiagnosticsOverlay : MonoBehaviour
    {
        [SerializeField] private bool _visible = true;
        [SerializeField] private KeyCode _toggleKey = KeyCode.F5;
        [SerializeField] private Rect _panelRect = new Rect(12f, 130f, 420f, 360f);
        [SerializeField] private int _fontSize = 11;
        [SerializeField] private Color _textColor = Color.white;
        [SerializeField] private Color _backgroundColor = new Color(0f, 0f, 0f, 0.72f);
        [SerializeField] private string _boardId = string.Empty;
        [SerializeField] private bool _showBitFields = true;
        [SerializeField] private int _maxBitFields = 12;

        private GUIStyle _labelStyle;
        private Texture2D _backgroundTex;
        private SimHost _host;
        private readonly List<string> _boardIds = new List<string>();

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                _visible = !_visible;
            }

            if (_host == null)
            {
                _host = SimHost.Instance ?? FindFirstObjectByType<SimHost>();
            }

            if (_host == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_boardId))
            {
                if (_host.GetFirmwareBoardIds(_boardIds) > 0)
                {
                    _boardId = _boardIds[0];
                }
            }
        }

        private void OnGUI()
        {
            if (!_visible) return;

            EnsureStyle();
            GUI.color = _backgroundColor;
            GUI.DrawTexture(_panelRect, _backgroundTex);
            GUI.color = _textColor;

            GUILayout.BeginArea(_panelRect);
            GUILayout.Label("Firmware Diagnostics", _labelStyle);

            if (_host == null)
            {
                GUILayout.Label("SimHost not available.", _labelStyle);
                GUILayout.EndArea();
                GUI.color = Color.white;
                return;
            }

            if (string.IsNullOrWhiteSpace(_boardId))
            {
                GUILayout.Label("No firmware boards detected.", _labelStyle);
                GUILayout.EndArea();
                GUI.color = Color.white;
                return;
            }

            GUILayout.Label($"Board: {_boardId}", _labelStyle);

            if (_host.TryGetFirmwareDebugCounters(_boardId, out var debug))
            {
                GUILayout.Label($"PC: 0x{debug.ProgramCounter:X4}  SP: 0x{debug.StackPointer:X4}  SREG: 0x{debug.StatusRegister:X2}", _labelStyle);
                GUILayout.Label($"CPU: {debug.CpuHz} Hz  Flash: {FormatBytes(debug.FlashBytes)}", _labelStyle);
                GUILayout.Label($"SRAM: {FormatBytes(debug.SramBytes)}  EEPROM: {FormatBytes(debug.EepromBytes)}  IO: {FormatBytes(debug.IoBytes)}", _labelStyle);
                GUILayout.Label($"Stack High: 0x{debug.StackHighWater:X4}  Heap Top: 0x{debug.HeapTopAddress:X4}", _labelStyle);
                GUILayout.Label($"Stack Min: 0x{debug.StackMinAddress:X4}  Data End: 0x{debug.DataSegmentEnd:X4}", _labelStyle);
                GUILayout.Label($"Interrupts: {debug.InterruptCount}  Max Latency: {debug.InterruptLatencyMax}", _labelStyle);
                GUILayout.Label($"Timing Violations: {debug.TimingViolations}  Critical Cycles: {debug.CriticalSectionCycles}", _labelStyle);
                GUILayout.Label($"Sleep Cycles: {debug.SleepCycles}  Flash Cycles: {debug.FlashAccessCycles}", _labelStyle);
                GUILayout.Label($"UART Ovr: {debug.UartOverflows}  Timer Ovr: {debug.TimerOverflows}", _labelStyle);
                GUILayout.Label($"Brown Out: {debug.BrownOutResets}  GPIO Changes: {debug.GpioStateChanges}", _labelStyle);
                GUILayout.Label($"PWM Cycles: {debug.PwmCycles}  I2C: {debug.I2cTransactions}  SPI: {debug.SpiTransactions}", _labelStyle);
            }
            else
            {
                GUILayout.Label("Firmware debug counters unavailable.", _labelStyle);
            }

            if (_showBitFields && _host.TryGetFirmwareDebugBits(_boardId, out var bits) && bits.Fields.Count > 0)
            {
                int shown = 0;
                GUILayout.Space(4f);
                GUILayout.Label($"Bit Fields ({bits.BitCount} bits)", _labelStyle);
                foreach (var field in bits.Fields)
                {
                    if (_maxBitFields > 0 && shown >= _maxBitFields)
                    {
                        break;
                    }
                    GUILayout.Label($"{field.Name}: {field.Value} [{field.Bits}]", _labelStyle);
                    shown++;
                }

                int remaining = bits.Fields.Count - shown;
                if (remaining > 0)
                {
                    GUILayout.Label($"+{remaining} more...", _labelStyle);
                }
            }

            GUILayout.EndArea();
            GUI.color = Color.white;
        }

        private void EnsureStyle()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(9, _fontSize),
                    normal = { textColor = _textColor }
                };
            }
            if (_backgroundTex == null)
            {
                _backgroundTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                _backgroundTex.SetPixel(0, 0, Color.white);
                _backgroundTex.Apply();
            }
        }

        private static string FormatBytes(uint bytes)
        {
            const float kb = 1024f;
            const float mb = kb * 1024f;
            if (bytes >= mb)
            {
                return $"{bytes / mb:0.00} MB";
            }
            if (bytes >= kb)
            {
                return $"{bytes / kb:0.0} KB";
            }
            return $"{bytes} B";
        }

        private void OnDestroy()
        {
            if (_backgroundTex != null)
            {
                Destroy(_backgroundTex);
                _backgroundTex = null;
            }
        }
    }
}
