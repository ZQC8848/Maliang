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

    public enum ScrollStartMode
    {
        /// <summary>Starts rolled up in the middle of the desk and unrolls after a short pause.</summary>
        UnrollOnStart,
        /// <summary>Stays rolled up (a spare scroll waiting to be laid on the desk).</summary>
        Rolled,
        /// <summary>Starts flat and open.</summary>
        Open,
    }

    /// <summary>
    /// The scroll's state machine (TechPlan §6.1): Rolled → Unrolled → Levitating.
    /// The scroll starts rolled up and unrolls on the desk (<see cref="ScrollUnroll"/>); the seal is the "final" stroke —
    /// once it lands the drawing locks and the scroll rises to hover in front of the player.
    /// </summary>
    public class ScrollRitual : MonoBehaviour
    {
        public InkCanvas canvas;
        [Tooltip("Object that rises (the scroll root). Defaults to the canvas transform.")]
        public Transform scrollRoot;
        [Tooltip("Player head. Defaults to Camera.main.")]
        public Transform head;

        [Header("Unrolling")]
        [Tooltip("Rod / paper animation. Without it the scroll starts open.")]
        public ScrollUnroll unroll;
        public ScrollStartMode startMode = ScrollStartMode.UnrollOnStart;
        [Tooltip("Pause before the scroll unrolls at the start (s).")]
        public float unrollDelay = 1f;

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

        public event Action Unrolled;
        public event Action<SealType> Sealed;
        public event Action Hovering;

        Vector3 _deskPos;
        Quaternion _deskRot;
        Vector3 _hoverPos;
        float _hoverTime;
        Coroutine _rise;
        Coroutine _unrolling;

        Transform Root => scrollRoot != null ? scrollRoot : canvas.transform;
        Transform Head => head != null ? head : (Camera.main != null ? Camera.main.transform : null);

        public bool CanSeal => State == ScrollState.Unrolled && canvas.InkCoverage >= minInkCoverage;
        /// <summary>True once the scroll has left the desk: hovering after its seal, or further on in the ritual.</summary>
        public bool OffDesk => State >= ScrollState.Burning || (State == ScrollState.Levitating && IsHovering);

        void Awake()
        {
            _deskPos = Root.position;
            _deskRot = Root.rotation;
        }

        void Start()
        {
            if (unroll == null) return;
            if (startMode == ScrollStartMode.UnrollOnStart) RollUpAndUnroll(unrollDelay);
            else if (startMode == ScrollStartMode.Rolled)
            {
                State = ScrollState.Rolled;
                canvas.InputLocked = true;
                unroll.SetProgress(0f);
            }
        }

        /// <summary>Where the scroll lies while it is drawn on (and returns to on reset).</summary>
        public void SetDeskPose(Vector3 position, Quaternion rotation)
        {
            _deskPos = position;
            _deskRot = rotation;
        }

        /// <summary>
        /// Rolls the scroll up (animated, or snapped when <paramref name="animateRollUp"/> is false), locks the drawing,
        /// and unrolls it again after <paramref name="delay"/> seconds.
        /// </summary>
        public void RollUpAndUnroll(float delay, bool animateRollUp = false)
        {
            if (unroll == null) return;
            if (_unrolling != null) StopCoroutine(_unrolling);
            State = ScrollState.Rolled;
            canvas.InputLocked = true;
            if (!animateRollUp) unroll.SetProgress(0f);
            _unrolling = StartCoroutine(UnrollRoutine(delay));
        }

        IEnumerator UnrollRoutine(float delay)
        {
            if (unroll.Progress > 0f) yield return unroll.Play(0f);
            if (delay > 0f) yield return new WaitForSeconds(delay);
            yield return unroll.Play(1f);
            _unrolling = null;
            State = ScrollState.Unrolled;
            canvas.InputLocked = false;
            MaliangLog.Info("Ritual", "Scroll unrolled.");
            Unrolled?.Invoke();
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
            RollUpAndUnroll(0.3f, animateRollUp: true); // roll up, then a fresh sheet unrolls again
        }
    }
}
