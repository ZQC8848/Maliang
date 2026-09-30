using System;
using System.Collections;
using System.Collections.Generic;
using Maliang.Core;
using Maliang.Drawing;
using Maliang.VR;
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

        [Header("Hover clearance")]
        [Tooltip("Before rising, the hover spot is checked for objects and other hovering scrolls. If it is taken the " +
                 "scroll moves up in these steps (m) first, then sideways.")]
        public float clearanceStep = 0.05f;
        [Tooltip("Furthest it moves up to find room (m).")]
        public float maxLiftUp = 0.6f;
        [Tooltip("Furthest it moves sideways to find room (m), when moving up is not enough.")]
        public float maxShiftSide = 0.9f;
        [Tooltip("Extra room kept around the scroll (m).")]
        public float clearanceMargin = 0.02f;

        /// <summary>World box this scroll has claimed for hovering (set when it starts to rise).</summary>
        public Bounds HoverReservation { get; private set; }
        public bool HasHoverReservation { get; private set; }

        static readonly List<ScrollRitual> All = new List<ScrollRitual>();
        static readonly Collider[] Hits = new Collider[64];

        public ScrollState State { get; private set; } = ScrollState.Unrolled;
        public SealType? Seal { get; private set; }
        /// <summary>True once the rise animation has finished and the scroll is hovering.</summary>
        public bool IsHovering { get; private set; }
        /// <summary>Held by the player while hovering: the bobbing pauses and it hovers where it is let go.</summary>
        public bool IsHeld { get; private set; }

        public event Action Unrolled;
        public event Action<SealType> Sealed;
        public event Action Hovering;
        /// <summary>Put back on the desk (reset).</summary>
        public event Action ReturnedToDesk;

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

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

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
            _hoverPos = FindClearHover(_hoverPos, hoverRot);

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

        // ------------------------------------------------------------------ hover clearance

        /// <summary>
        /// The nearest free hover spot to <paramref name="desired"/>: straight up first (in <see cref="clearanceStep"/>s,
        /// up to <see cref="maxLiftUp"/>), then alternately right and left (up to <see cref="maxShiftSide"/>), each
        /// sideways spot again trying the lowest height first. Claims the spot so later scrolls avoid it.
        /// </summary>
        Vector3 FindClearHover(Vector3 desired, Quaternion rot)
        {
            Vector3 half = HoverHalfExtents();
            Vector3 up = Vector3.up;
            Vector3 side = Vector3.ProjectOnPlane(rot * Vector3.right, Vector3.up).normalized;
            float step = Mathf.Max(0.01f, clearanceStep);
            int ups = Mathf.FloorToInt(maxLiftUp / step);
            int sides = Mathf.FloorToInt(maxShiftSide / step);

            for (int s = 0; s <= sides; s++)
            {
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    if (s == 0 && sign < 0) continue;
                    for (int u = 0; u <= ups; u++)
                    {
                        Vector3 p = desired + up * (u * step) + side * (sign * s * step);
                        if (!IsClear(p, rot, half)) continue;
                        Reserve(p, rot, half);
                        if (s > 0 || u > 0)
                            MaliangLog.Info("Ritual", $"Hover spot taken; moved up {u * step:F2} m, sideways {sign * s * step:F2} m.");
                        return p;
                    }
                }
            }
            MaliangLog.Warn("Ritual", "No free hover spot nearby; hovering at the default spot.");
            Reserve(desired, rot, half);
            return desired;
        }

        /// <summary>The open scroll as a box in its own frame: width along X (paper + rods), depth along Z, thin along Y.</summary>
        Vector3 HoverHalfExtents() =>
            new Vector3(canvas.size.x * 0.5f + 0.03f, 0.025f, canvas.size.y * 0.5f + 0.03f) + Vector3.one * clearanceMargin;

        /// <summary>
        /// The player picked up (true) or let go of (false) the hovering scroll. Let go, it hovers where it is: no
        /// gravity, no return, the gentle bob resumes there and that spot is claimed for later scrolls.
        /// </summary>
        public void SetHeld(bool held)
        {
            IsHeld = held;
            if (held || !IsHovering) return;
            _hoverPos = Root.position;
            _hoverTime = 0f; // bob starts from its middle, so there is no jump
            Reserve(Root.position, Root.rotation, HoverHalfExtents());
        }

        bool IsClear(Vector3 centre, Quaternion rot, Vector3 half)
        {
            int n = Physics.OverlapBoxNonAlloc(centre, half, Hits, rot, ~0, QueryTriggerInteraction.Ignore);
            var rig = Head != null ? Head.root : null;
            for (int i = 0; i < n; i++)
            {
                var t = Hits[i].transform;
                if (t.IsChildOf(transform)) continue;                    // this scroll
                if (rig != null && t.IsChildOf(rig)) continue;           // the player
                var tool = Hits[i].GetComponentInParent<GrabbableTool>();
                if (tool != null && tool.IsHeld) continue;               // something in the player's hand
                return false;
            }
            // Hovering scrolls have no active colliders: check the spots they have claimed.
            var box = WorldBox(centre, rot, half);
            foreach (var other in All)
                if (other != this && other.HasHoverReservation && other.HoverReservation.Intersects(box)) return false;
            return true;
        }

        void Reserve(Vector3 centre, Quaternion rot, Vector3 half)
        {
            HoverReservation = WorldBox(centre, rot, half);
            HasHoverReservation = true;
        }

        static Bounds WorldBox(Vector3 centre, Quaternion rot, Vector3 half)
        {
            var b = new Bounds(centre, Vector3.zero);
            for (int i = 0; i < 8; i++)
                b.Encapsulate(centre + rot * Vector3.Scale(half, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
            return b;
        }

        void Update()
        {
            if (!IsHovering || State != ScrollState.Levitating || IsHeld) return;
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
            IsHeld = false;
            HasHoverReservation = false;
            State = ScrollState.Unrolled;
            MaliangLog.Info("Ritual", "Scroll reset.");
            ReturnedToDesk?.Invoke();
            RollUpAndUnroll(0.3f, animateRollUp: true); // roll up, then a fresh sheet unrolls again
        }
    }
}
