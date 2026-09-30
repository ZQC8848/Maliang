using System;
using System.Collections;
using Maliang.Core;
using Maliang.Drawing;
using UnityEngine;

namespace Maliang.Ritual
{
    public enum ScrollState
    {
        Rolled,
        /// <summary>Flat on the desk: draw and stamp.</summary>
        Unrolled,
        /// <summary>Stamped; rising / hovering in front of the player, waiting for the candle.</summary>
        Levitating,
        Burning,
        Materializing,
        Done,
        Failed,
    }

    /// <summary>
    /// The scroll's state machine (TechPlan §6.1). Phase 2 covers Unrolled → Levitating:
    /// the seal is the "final" stroke — once it lands the drawing locks and the scroll rises to hover in front of the player.
    /// </summary>
    public class ScrollRitual : MonoBehaviour
    {
        public InkCanvas canvas;
        [Tooltip("Object that rises (the scroll root). Defaults to the canvas transform.")]
        public Transform scrollRoot;
        [Tooltip("Player head. Defaults to Camera.main.")]
        public Transform head;

        [Header("Seal rules")]
        [Tooltip("Minimum inked fraction of the canvas before a seal is accepted (TechPlan §5.5).")]
        [Range(0f, 0.2f)] public float minInkCoverage = 0.01f;

        [Header("Levitation")]
        [Tooltip("Pause after stamping so the player sees the seal land (s).")]
        public float holdBeforeRise = 0.8f;
        public float riseDuration = 2.4f;
        [Tooltip("Hover point: this far in front of the head (m, horizontal)...")]
        public float hoverDistance = 0.6f;
        [Tooltip("...and this far below eye height (m). Keep within arm's reach for the candle.")]
        public float hoverBelowEyes = 0.18f;
        [Tooltip("Tilt back from vertical (degrees) so the scroll faces slightly upwards to the eyes.")]
        public float hoverTiltBack = 12f;
        public float bobAmplitude = 0.012f;
        public float bobPeriod = 3.2f;

        public ScrollState State { get; private set; } = ScrollState.Unrolled;
        public SealType? Seal { get; private set; }
        /// <summary>True once the rise animation has finished and the scroll is hovering.</summary>
        public bool IsHovering { get; private set; }

        public event Action<SealType> Sealed;
        public event Action Hovering;

        Vector3 _deskPos;
        Quaternion _deskRot;
        Vector3 _hoverPos;
        float _hoverTime;
        Coroutine _rise;

        Transform Root => scrollRoot != null ? scrollRoot : canvas.transform;
        Transform Head => head != null ? head : (Camera.main != null ? Camera.main.transform : null);

        public bool CanSeal => State == ScrollState.Unrolled && canvas.InkCoverage >= minInkCoverage;

        void Awake()
        {
            _deskPos = Root.position;
            _deskRot = Root.rotation;
        }

        /// <summary>Called by a seal after it printed. Locks the drawing and starts the rise.</summary>
        public void OnSealed(SealType type)
        {
            if (State != ScrollState.Unrolled) return;
            Seal = type;
            canvas.InputLocked = true;
            State = ScrollState.Levitating;
            MaliangLog.Info("Ritual", $"Sealed as {type} (ink coverage {canvas.InkCoverage:P1}); scroll rising.");
            Sealed?.Invoke(type);
            _rise = StartCoroutine(Rise());
        }

        IEnumerator Rise()
        {
            yield return new WaitForSeconds(holdBeforeRise);

            var head = Head;
            Vector3 fwd = head != null ? Vector3.ProjectOnPlane(head.forward, Vector3.up) : Vector3.forward;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 eye = head != null ? head.position : Root.position + Vector3.up * 0.5f;

            _hoverPos = eye + fwd * hoverDistance + Vector3.down * hoverBelowEyes;
            // Canvas normal (+Y) faces the player; the drawing's top (+Z) points up, tilted back a little.
            Vector3 toPlayer = -fwd;
            Quaternion upright = Quaternion.LookRotation(Vector3.up, toPlayer);
            Quaternion hoverRot = Quaternion.AngleAxis(-hoverTiltBack, Vector3.Cross(Vector3.up, toPlayer)) * upright;

            Vector3 p0 = Root.position;
            Quaternion r0 = Root.rotation;
            // Lift straight up first so it clears the desk items, then glide to the hover point.
            Vector3 lift = p0 + Vector3.up * 0.15f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / riseDuration)
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                Vector3 a = Vector3.Lerp(p0, lift, k), b = Vector3.Lerp(lift, _hoverPos, k);
                Root.SetPositionAndRotation(Vector3.Lerp(a, b, k), Quaternion.Slerp(r0, hoverRot, k));
                yield return null;
            }
            Root.SetPositionAndRotation(_hoverPos, hoverRot);

            _hoverTime = 0f;
            IsHovering = true;
            _rise = null;
            MaliangLog.Info("Ritual", "Scroll hovering; waiting for the candle.");
            Hovering?.Invoke();
        }

        void Update()
        {
            if (!IsHovering || State != ScrollState.Levitating) return;
            _hoverTime += Time.deltaTime;
            float bob = Mathf.Sin(_hoverTime * Mathf.PI * 2f / bobPeriod) * bobAmplitude;
            Root.position = _hoverPos + Vector3.up * bob;
        }

        /// <summary>Back to a blank scroll on the desk (testing / new round).</summary>
        [ContextMenu("Reset Scroll")]
        public void ResetScroll()
        {
            if (_rise != null) { StopCoroutine(_rise); _rise = null; }
            Root.SetPositionAndRotation(_deskPos, _deskRot);
            canvas.ClearAll();
            Seal = null;
            IsHovering = false;
            State = ScrollState.Unrolled;
            MaliangLog.Info("Ritual", "Scroll reset.");
        }
    }
}
