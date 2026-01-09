using UnityEngine;
using RobotTwin.Game;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RobotTwin.Debugging
{
    public class PhysicsDiagnosticsOverlay : MonoBehaviour
    {
        [SerializeField] private bool _visible = true;
        [SerializeField] private KeyCode _toggleKey = KeyCode.F3;
        [SerializeField] private Rect _panelRect = new Rect(12f, 12f, 260f, 110f);
        [SerializeField] private int _fontSize = 11;
        [SerializeField] private Color _textColor = Color.white;
        [SerializeField] private Color _backgroundColor = new Color(0f, 0f, 0f, 0.7f);

        private GUIStyle _labelStyle;
        private Texture2D _backgroundTex;

        private void Update()
        {
            if (WasTogglePressedThisFrame())
            {
                _visible = !_visible;
            }
        }

        private bool WasTogglePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            // When the new Input System is enabled, UnityEngine.Input throws.
            // Map KeyCode -> InputSystem Key via name matching (works for common keys like F3).
            if (Keyboard.current == null) return false;

            if (System.Enum.TryParse<Key>(_toggleKey.ToString(), out var key))
            {
                var control = Keyboard.current[key];
                return control != null && control.wasPressedThisFrame;
            }

            return false;
#else
            return Input.GetKeyDown(_toggleKey);
#endif
        }

        private void OnGUI()
        {
            if (!_visible) return;
            var world = NativePhysicsWorld.Instance;
            if (world == null) return;

            EnsureStyle();
            GUI.color = _backgroundColor;
            GUI.DrawTexture(_panelRect, _backgroundTex);
            GUI.color = _textColor;

            GUILayout.BeginArea(_panelRect);
            GUILayout.Label("Physics Diagnostics", _labelStyle);
            GUILayout.Label($"Bodies: {world.BodyCount}", _labelStyle);
            GUILayout.Label($"Step dt: {world.LastStepDt:0.0000}s", _labelStyle);
            GUILayout.Label($"Substeps: {world.LastStepSubsteps}", _labelStyle);
            GUILayout.Label($"Step ms: {world.LastStepMs:0.00}", _labelStyle);
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
