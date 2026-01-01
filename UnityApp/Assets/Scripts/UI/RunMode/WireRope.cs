using UnityEngine;

namespace RobotTwin.UI
{
    public class WireRope : MonoBehaviour
    {
        [SerializeField] private WireAnchor _start;
        [SerializeField] private WireAnchor _end;
        [SerializeField] private int _segments = 12;
        [SerializeField] private float _sagStrength = 0.15f;
        [SerializeField] private float _liftStrength = 0.08f;
        [SerializeField] private float _tension = 0.8f;
        [SerializeField] private float _minSag = 0.002f;
        [SerializeField] private float _maxSag = 0.08f;
        [SerializeField] private float _widthScale = 3f;
        [SerializeField] private float _minWidth = 0.0015f;
        [SerializeField] private Color _color = new Color(0.2f, 0.9f, 0.6f);
        [SerializeField] private Color _errorColor = new Color(1f, 0.25f, 0.2f);
        [SerializeField] private float _errorIntensity = 1f;

        private LineRenderer _line;
        private static Material _wireMaterial;
        private bool _errorActive;
        private float _errorSeed;

        public void Initialize(WireAnchor start, WireAnchor end)
        {
            _start = start;
            _end = end;
            EnsureRenderer();
            UpdateLine();
        }

        public void SetColor(Color color)
        {
            _color = color;
            UpdateColor();
        }

        public void SetError(bool active)
        {
            _errorActive = active;
            UpdateColor();
        }

        private void Awake()
        {
            _errorSeed = Random.Range(0f, 10f);
            EnsureRenderer();
        }

        private void LateUpdate()
        {
            UpdateLine();
            UpdateColor();
        }

        private void EnsureRenderer()
        {
            if (_line != null) return;
            _line = GetComponent<LineRenderer>();
            if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = false;
            _line.material = GetWireMaterial();
            _line.startColor = _color;
            _line.endColor = _color;
            _line.numCapVertices = 8;
            _line.numCornerVertices = 8;
            _line.alignment = LineAlignment.View;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.sortingOrder = 5;
        }

        private void UpdateLine()
        {
            if (_line == null || _start == null || _end == null) return;
            int count = Mathf.Max(2, _segments);
            if (_line.positionCount != count) _line.positionCount = count;

            var startPos = _start.transform.localPosition;
            var endPos = _end.transform.localPosition;
            var gravity = Physics.gravity;
            var gravityDir = gravity.sqrMagnitude > 0.0001f ? gravity.normalized : Vector3.down;
            var parent = transform.parent;
            if (parent != null)
            {
                gravityDir = parent.InverseTransformDirection(gravityDir).normalized;
            }
            var upDir = -gravityDir;

            float length = Vector3.Distance(startPos, endPos);
            float gravityScale = gravity.sqrMagnitude > 0.0001f ? gravity.magnitude / 9.81f : 0f;
            float sag = length * Mathf.Max(0f, _sagStrength) * gravityScale;
            sag = Mathf.Clamp(sag, _minSag, _maxSag);
            sag /= (1f + Mathf.Max(0f, _tension));
            float lift = length * Mathf.Max(0f, _liftStrength);

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                var control = (startPos + endPos) * 0.5f + gravityDir * sag + upDir * lift;
                _line.SetPosition(i, QuadraticBezier(startPos, control, endPos, t));
            }

            float minRadius = Mathf.Min(_start.Radius, _end.Radius);
            float width = Mathf.Max(_minWidth, minRadius * _widthScale);
            _line.startWidth = width;
            _line.endWidth = width;
        }

        private void UpdateColor()
        {
            if (_line == null) return;
            if (!_errorActive)
            {
                _line.startColor = _color;
                _line.endColor = _color;
                return;
            }

            float pulse = 0.4f + 0.6f * Mathf.Sin(Time.time * 6f + _errorSeed);
            float strength = Mathf.Clamp01(_errorIntensity * pulse);
            var tinted = Color.Lerp(_color, _errorColor, strength);
            _line.startColor = tinted;
            _line.endColor = tinted;
        }

        private static Vector3 QuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        private static Material GetWireMaterial()
        {
            if (_wireMaterial != null) return _wireMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                Shader.Find("Unlit/Color") ??
                Shader.Find("Sprites/Default");
            _wireMaterial = new Material(shader)
            {
                name = "Circuit3D_Wire"
            };
            return _wireMaterial;
        }
    }
}
