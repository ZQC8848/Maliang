using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Maliang.VR
{
    /// <summary>
    /// Shared behaviour for desk tools (brush, seals, candle): tracks who is holding it, sends haptics to that hand,
    /// and glides back to its rest pose when released so nothing ends up on the floor.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class GrabbableTool : MonoBehaviour
    {
        [Tooltip("Where the tool returns when released. Defaults to its pose at startup.")]
        public Transform restPose;
        public bool returnOnRelease = true;
        public float returnDuration = 0.45f;

        XRGrabInteractable _grab;
        HapticImpulsePlayer _haptics;
        Vector3 _restPos;
        Quaternion _restRot;
        Coroutine _returning;

        public bool IsHeld { get; private set; }
        public XRGrabInteractable Grab => _grab;

        protected virtual void Awake()
        {
            _grab = GetComponent<XRGrabInteractable>();
            var t = restPose != null ? restPose : transform;
            _restPos = t.position;
            _restRot = t.rotation;
        }

        protected virtual void OnEnable()
        {
            _grab.selectEntered.AddListener(OnSelectEntered);
            _grab.selectExited.AddListener(OnSelectExited);
        }

        protected virtual void OnDisable()
        {
            _grab.selectEntered.RemoveListener(OnSelectEntered);
            _grab.selectExited.RemoveListener(OnSelectExited);
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (_returning != null) { StopCoroutine(_returning); _returning = null; }
            IsHeld = true;
            _haptics = (args.interactorObject as Component)?.GetComponentInParent<HapticImpulsePlayer>();
            OnGrabbed();
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (_grab.isSelected) return; // still held by the other hand
            IsHeld = false;
            _haptics = null;
            OnReleased();
            if (returnOnRelease && isActiveAndEnabled) _returning = StartCoroutine(ReturnToRest());
        }

        protected virtual void OnGrabbed() { }
        protected virtual void OnReleased() { }

        public void Haptic(float amplitude, float duration)
        {
            if (_haptics != null) _haptics.SendHapticImpulse(amplitude, duration);
        }

        /// <summary>Changes the rest pose (e.g. after the scene is re-laid out).</summary>
        public void SetRestPose(Vector3 position, Quaternion rotation)
        {
            _restPos = position;
            _restRot = rotation;
        }

        public void SnapToRest()
        {
            if (_returning != null) { StopCoroutine(_returning); _returning = null; }
            transform.SetPositionAndRotation(RestPosition, RestRotation);
        }

        // A rest pose transform is followed live (it may move, like a slot in a drawer); otherwise the pose at start.
        Vector3 RestPosition => restPose != null ? restPose.position : _restPos;
        Quaternion RestRotation => restPose != null ? restPose.rotation : _restRot;

        IEnumerator ReturnToRest()
        {
            var rb = GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; }
            Vector3 p0 = transform.position;
            Quaternion r0 = transform.rotation;
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, returnDuration))
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                transform.SetPositionAndRotation(Vector3.Lerp(p0, RestPosition, k), Quaternion.Slerp(r0, RestRotation, k));
                yield return null;
            }
            transform.SetPositionAndRotation(RestPosition, RestRotation);
            _returning = null;
        }
    }
}
