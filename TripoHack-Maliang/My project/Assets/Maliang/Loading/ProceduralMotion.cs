using UnityEngine;

namespace Maliang.Loading
{
    /// <summary>
    /// Life for summoned objects without skeletal animation (Phase3Design 5.5): a gentle hover and slow turn; for birds
    /// (avian category, never rigged) an added wing-beat sway and a small drifting circle. Pauses while held.
    /// </summary>
    public class ProceduralMotion : MonoBehaviour
    {
        public bool flight;              // avian
        public float bobAmplitude = 0.015f;
        public float bobPeriod = 3.5f;
        public float turnSpeed = 8f;     // degrees per second
        public float flapAngle = 6f;     // degrees of roll for the wing-beat sway
        public float flapPeriod = 0.9f;
        public float driftRadius = 0.06f;
        public float driftPeriod = 9f;

        public bool Paused { get; set; }

        Vector3 _anchor;
        float _t, _yaw, _phase;

        void Start() => Rebase();

        /// <summary>Takes the current pose as the new centre of the motion (after the player lets go).</summary>
        public void Rebase()
        {
            _anchor = transform.position;
            _yaw = transform.eulerAngles.y;
            _t = 0f;
            _phase = Random.value * 10f;
        }

        void Update()
        {
            if (Paused) return;
            _t += Time.deltaTime;
            float bob = Mathf.Sin((_t + _phase) * Mathf.PI * 2f / bobPeriod) * bobAmplitude;
            Vector3 pos = _anchor + Vector3.up * bob;
            float yaw = _yaw + _t * turnSpeed;
            float roll = 0f;
            if (flight)
            {
                float a = _t * Mathf.PI * 2f / driftPeriod;
                pos += new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * driftRadius;
                yaw = _yaw - a * Mathf.Rad2Deg;   // face along the circle
                roll = Mathf.Sin((_t + _phase) * Mathf.PI * 2f / flapPeriod) * flapAngle;
            }
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, roll));
        }
    }
}
