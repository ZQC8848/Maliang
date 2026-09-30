using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Maliang.VR
{
    /// <summary>
    /// A desk drawer that slides along <see cref="openDirection"/> while the player grabs its front (or pull) and moves
    /// the hand. It is a kinematic rigidbody moved in FixedUpdate, so things lying inside ride along with it.
    /// A released drawer keeps its momentum and glides to a stop; hitting either end gives a small haptic bump.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(XRSimpleInteractable))]
    public class Drawer : MonoBehaviour
    {
        [Tooltip("World direction the drawer opens in (towards the player).")]
        public Vector3 openDirection = Vector3.back;
        [Tooltip("How far the drawer can be pulled out (m).")]
        public float maxOpen = 0.42f;
        [Tooltip("How quickly a released drawer's glide dies away (1/s).")]
        public float friction = 8f;
        [Tooltip("How tightly the drawer follows the hand (1/s).")]
        public float followSharpness = 25f;

        public float OpenAmount { get; private set; }
        public float OpenFraction => maxOpen > 0f ? OpenAmount / maxOpen : 0f;
        /// <summary>Speed along <see cref="openDirection"/> (m/s).</summary>
        public float Velocity { get; private set; }
        /// <summary>Acceleration along <see cref="openDirection"/> (m/s²), lightly smoothed. Drives the swinging pulls.</summary>
        public float Acceleration { get; private set; }
        public bool IsHeld => _hand != null;

        Rigidbody _rb;
        XRSimpleInteractable _interactable;
        Vector3 _closedPos;
        Transform _hand;
        float _grabHandAlong, _grabOpen;
        HapticImpulsePlayer _haptics;
        bool _atLimit = true;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _interactable = GetComponent<XRSimpleInteractable>();
            openDirection = openDirection.normalized;
            _closedPos = transform.position;
        }

        void OnEnable()
        {
            _interactable.selectEntered.AddListener(OnSelectEntered);
            _interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            _interactable.selectEntered.RemoveListener(OnSelectEntered);
            _interactable.selectExited.RemoveListener(OnSelectExited);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            // The controller itself, not the attach point: a far (ray) grab then pulls the same way as a near grab.
            _hand = args.interactorObject.transform;
            _haptics = (args.interactorObject as Component)?.GetComponentInParent<HapticImpulsePlayer>();
            _grabHandAlong = Vector3.Dot(_hand.position, openDirection);
            _grabOpen = OpenAmount;
            Haptic(0.15f, 0.03f);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (_interactable.isSelected) return; // still held by the other hand
            _hand = null;
            _haptics = null;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float prev = OpenAmount;
            float next;
            if (_hand != null)
            {
                float target = _grabOpen + Vector3.Dot(_hand.position, openDirection) - _grabHandAlong;
                next = Mathf.Lerp(prev, target, 1f - Mathf.Exp(-followSharpness * dt));
            }
            else
            {
                next = prev + Velocity * Mathf.Exp(-friction * dt) * dt;
            }
            next = Mathf.Clamp(next, 0f, maxOpen);

            float v = (next - prev) / dt;
            bool atLimit = next <= 0f || next >= maxOpen;
            if (atLimit && !_atLimit && Mathf.Abs(Velocity) > 0.05f) Haptic(0.35f, 0.05f);
            _atLimit = atLimit;

            Acceleration = Mathf.Lerp(Acceleration, (v - Velocity) / dt, 0.5f);
            Velocity = v;
            OpenAmount = next;
            _rb.MovePosition(_closedPos + openDirection * next);
        }

        /// <summary>Jumps to an opening (m), e.g. for tests or a scripted reveal.</summary>
        public void SetOpen(float amount)
        {
            OpenAmount = Mathf.Clamp(amount, 0f, maxOpen);
            Velocity = Acceleration = 0f;
            transform.position = _closedPos + openDirection * OpenAmount;
        }

        void Haptic(float amplitude, float duration)
        {
            if (_haptics != null) _haptics.SendHapticImpulse(amplitude, duration);
        }
    }
}
