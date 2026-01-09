// Minimal FPS controller for World Run mode.

using UnityEngine;

namespace RobotTwin.Gameplay
{
    [RequireComponent(typeof(CharacterController))]
    public class FpsPlayerController : MonoBehaviour
    {
        [SerializeField] private float _moveSpeed = 4.5f;
        [SerializeField] private float _runSpeed = 7.5f;
        [SerializeField] private float _lookSensitivity = 2.2f;
        [SerializeField] private float _jumpSpeed = 4.5f;
        [SerializeField] private float _gravity = -18f;
        [SerializeField] private bool _lockCursor = true;

        private CharacterController _controller;
        private float _yaw;
        private float _pitch;
        private float _verticalVelocity;

        public Transform CameraRoot { get; set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (CameraRoot == null)
            {
                var cam = GetComponentInChildren<Camera>();
                if (cam != null) CameraRoot = cam.transform;
            }
            ApplyCursorState(_lockCursor);
        }

        private void Update()
        {
            if (CameraRoot == null || _controller == null) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _lockCursor = !_lockCursor;
                ApplyCursorState(_lockCursor);
            }

            Look();
            Move();
        }

        private void Look()
        {
            if (!_lockCursor) return;
            float mouseX = Input.GetAxis("Mouse X") * _lookSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * _lookSensitivity;
            _yaw += mouseX;
            _pitch = Mathf.Clamp(_pitch - mouseY, -85f, 85f);
            transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            CameraRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void Move()
        {
            float speed = Input.GetKey(KeyCode.LeftShift) ? _runSpeed : _moveSpeed;
            float moveX = Input.GetAxisRaw("Horizontal");
            float moveZ = Input.GetAxisRaw("Vertical");
            Vector3 move = (transform.right * moveX + transform.forward * moveZ) * speed;

            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f;
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    _verticalVelocity = _jumpSpeed;
                }
            }
            else
            {
                _verticalVelocity += _gravity * Time.deltaTime;
            }

            move.y = _verticalVelocity;
            _controller.Move(move * Time.deltaTime);
        }

        private static void ApplyCursorState(bool locked)
        {
            Cursor.visible = !locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        }
    }
}
