using UnityEngine;

namespace RobotTwin.Game
{
    public class NativeRcCarController : MonoBehaviour
    {
        [SerializeField] private NativeVehicle _vehicle;

        [Header("Input")]
        [SerializeField] private bool _useInputAxes = true;
        [SerializeField] private string _steerAxis = "Horizontal";
        [SerializeField] private string _driveAxis = "Vertical";
        [SerializeField] private KeyCode _accelerateKey = KeyCode.W;
        [SerializeField] private KeyCode _reverseKey = KeyCode.S;
        [SerializeField] private KeyCode _leftKey = KeyCode.A;
        [SerializeField] private KeyCode _rightKey = KeyCode.D;
        [SerializeField] private KeyCode _brakeKey = KeyCode.Space;

        [Header("Tuning")]
        [SerializeField] private float _maxSteer = 0.6f;
        [SerializeField] private float _maxDriveTorque = 30f;
        [SerializeField] private float _maxBrakeTorque = 22f;
        [SerializeField] private float _steerResponse = 6f;
        [SerializeField] private float _driveResponse = 8f;
        [SerializeField] private float _brakeResponse = 10f;

        [Header("Wheel Mapping")]
        [SerializeField] private int[] _steerWheels = { 0, 1 };
        [SerializeField] private int[] _driveWheels = { 2, 3 };
        [SerializeField] private int[] _brakeWheels = { };

        private float _steer;
        private float _throttle;
        private float _brake;

        private void Reset()
        {
            if (_vehicle == null)
            {
                _vehicle = GetComponent<NativeVehicle>();
            }
        }

        private void FixedUpdate()
        {
            if (_vehicle == null || _vehicle.VehicleId == 0)
            {
                return;
            }

            float targetSteer = GetSteerInput();
            float targetThrottle = GetThrottleInput();
            float targetBrake = GetBrakeInput();

            _steer = Mathf.MoveTowards(_steer, targetSteer, _steerResponse * Time.fixedDeltaTime);
            _throttle = Mathf.MoveTowards(_throttle, targetThrottle, _driveResponse * Time.fixedDeltaTime);
            _brake = Mathf.MoveTowards(_brake, targetBrake, _brakeResponse * Time.fixedDeltaTime);

            float steerValue = _steer * _maxSteer;
            float driveTorque = _throttle * _maxDriveTorque;
            float brakeTorque = _brake * _maxBrakeTorque;

            int wheelCount = _vehicle.WheelCount;
            for (int i = 0; i < wheelCount; i++)
            {
                float steer = ShouldApplyToWheel(_steerWheels, i) ? steerValue : 0f;
                float drive = ShouldApplyToWheel(_driveWheels, i) ? driveTorque : 0f;
                float brake = ShouldApplyToWheel(_brakeWheels, i) ? brakeTorque : 0f;
                _vehicle.SetWheelInput(i, steer, drive, brake);
            }
        }

        private float GetSteerInput()
        {
            if (_useInputAxes)
            {
                return Mathf.Clamp(Input.GetAxisRaw(_steerAxis), -1f, 1f);
            }

            float steer = 0f;
            if (Input.GetKey(_leftKey)) steer -= 1f;
            if (Input.GetKey(_rightKey)) steer += 1f;
            return steer;
        }

        private float GetThrottleInput()
        {
            if (_useInputAxes)
            {
                return Mathf.Clamp(Input.GetAxisRaw(_driveAxis), -1f, 1f);
            }

            float throttle = 0f;
            if (Input.GetKey(_accelerateKey)) throttle += 1f;
            if (Input.GetKey(_reverseKey)) throttle -= 1f;
            return throttle;
        }

        private float GetBrakeInput()
        {
            return Input.GetKey(_brakeKey) ? 1f : 0f;
        }

        private static bool ShouldApplyToWheel(int[] mapping, int index)
        {
            if (mapping == null || mapping.Length == 0) return true;
            for (int i = 0; i < mapping.Length; i++)
            {
                if (mapping[i] == index) return true;
            }
            return false;
        }
    }
}
