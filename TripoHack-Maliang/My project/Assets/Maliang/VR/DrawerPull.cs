using UnityEngine;

namespace Maliang.VR
{
    /// <summary>
    /// A hanging drop pull on a drawer front, simulated as a damped pendulum about its pin (this transform is the pin).
    /// While the drawer is held the pull lifts towards the hand; released, it drops back against the front with a
    /// small bounce, and it swings out when the drawer stops suddenly (and clacks against the front when it is slammed).
    /// </summary>
    public class DrawerPull : MonoBehaviour
    {
        public Drawer drawer;
        [Tooltip("Distance from the pin to the middle of the pull (m).")]
        public float length = 0.021f;
        [Tooltip("Swing damping (1/s).")]
        public float damping = 5f;
        [Tooltip("Angle the pull is lifted to while the drawer is held (degrees, outwards from hanging).")]
        public float heldAngle = 75f;
        [Tooltip("How quickly the pull lifts to the held angle.")]
        public float heldStiffness = 250f;
        public float maxAngle = 110f;
        [Tooltip("Energy kept when the pull knocks against the drawer front.")]
        [Range(0f, 1f)] public float bounce = 0.3f;

        const float SubStep = 0.002f;

        Quaternion _rest;
        Vector3 _axis;
        float _angle, _speed; // rad, rad/s

        public float AngleDegrees => _angle * Mathf.Rad2Deg;

        void Awake()
        {
            _rest = transform.localRotation;
            // Horizontal axis across the drawer front; a positive angle swings the pull out towards the player.
            var dir = drawer != null ? drawer.openDirection.normalized : Vector3.back;
            var worldAxis = Vector3.Cross(Vector3.down, dir).normalized;
            _axis = transform.parent != null ? transform.parent.InverseTransformDirection(worldAxis).normalized : worldAxis;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / SubStep));
            float h = dt / steps;
            bool held = drawer != null && drawer.IsHeld;
            float a = drawer != null ? drawer.Acceleration : 0f;
            float g = Physics.gravity.magnitude;
            float maxRad = maxAngle * Mathf.Deg2Rad;

            for (int i = 0; i < steps; i++)
            {
                float acc;
                if (held)
                {
                    float target = heldAngle * Mathf.Deg2Rad;
                    acc = heldStiffness * (target - _angle) - 2f * Mathf.Sqrt(heldStiffness) * _speed;
                }
                else
                {
                    // Pendulum in the drawer's accelerating frame: gravity pulls it down, the drawer's acceleration
                    // pushes it the other way.
                    acc = (-g * Mathf.Sin(_angle) - a * Mathf.Cos(_angle)) / Mathf.Max(0.001f, length) - damping * _speed;
                }
                _speed += acc * h;
                _angle += _speed * h;

                if (_angle < 0f) { _angle = 0f; if (_speed < 0f) _speed = -_speed * bounce; } // knocks against the front
                if (_angle > maxRad) { _angle = maxRad; if (_speed > 0f) _speed = 0f; }
            }
            transform.localRotation = Quaternion.AngleAxis(_angle * Mathf.Rad2Deg, _axis) * _rest;
        }
    }
}
