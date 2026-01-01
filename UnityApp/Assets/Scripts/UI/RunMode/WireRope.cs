using UnityEngine;

namespace RobotTwin.UI
{
    public class WireRope : MonoBehaviour
    {
        [SerializeField] private WireAnchor _start;
        [SerializeField] private WireAnchor _end;
        [SerializeField] private int _segments = 12;
        [SerializeField] private float _sagStrength = 0.15f;
        [SerializeField] private float _widthScale = 2f;
        [SerializeField] private float _minWidth = 0.0005f;
        [SerializeField] private Color _color = new Color(0.2f, 0.9f, 0.6f);

        private LineRenderer _line;

        public void Initialize(WireAnchor start, WireAnchor end)
        {
            _start = start;
            _end = end;
            EnsureRenderer();
            UpdateLine();
        }

        private void Awake()
        {
            EnsureRenderer();
        }

        private void LateUpdate()
        {
            UpdateLine();
        }

        private void EnsureRenderer()
        {
            if (_line != null) return;
            _line = GetComponent<LineRenderer>();
            if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = false;
            _line.material = new Material(Shader.Find("Sprites/Default"));
            _line.startColor = _color;
            _line.endColor = _color;
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

            float length = Vector3.Distance(startPos, endPos);
            float gravityScale = gravity.sqrMagnitude > 0.0001f ? gravity.magnitude / 9.81f : 0f;
            float sag = length * Mathf.Max(0f, _sagStrength) * gravityScale;

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                var basePos = Vector3.Lerp(startPos, endPos, t);
                float curve = Mathf.Sin(Mathf.PI * t);
                _line.SetPosition(i, basePos + gravityDir * (curve * sag));
            }

            float minRadius = Mathf.Min(_start.Radius, _end.Radius);
            float width = Mathf.Max(_minWidth, minRadius * _widthScale);
            _line.startWidth = width;
            _line.endWidth = width;
        }
    }
}
