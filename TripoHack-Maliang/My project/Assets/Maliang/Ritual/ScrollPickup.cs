using Maliang.Core;
using Maliang.VR;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Maliang.Ritual
{
    /// <summary>
    /// Picking up a scroll.
    /// Rolled up (the spare): released over a free drawing spot (<see cref="ScrollStation"/>) it is laid down there;
    /// released anywhere else it glides back to where it lay.
    /// Sealed and hovering: it can be carried anywhere; let go, it stays there and keeps hovering (no gravity).
    /// Failed: a remnant under gravity that can be picked up and thrown (<see cref="BecomeRemnant"/>).
    /// The scroll being drawn on is not pickable.
    /// </summary>
    [RequireComponent(typeof(ScrollRitual))]
    public class ScrollPickup : GrabbableTool
    {
        public ScrollStation station;
        [Tooltip("Pickable when the scene starts (the spare), or not (the scroll already on the desk).")]
        public bool pickableOnStart = true;
        [Tooltip("Grab box; sized to the rolled-up bundle, or to the whole scroll once it hovers open.")]
        public Collider grabCollider;

        public ScrollRitual Ritual { get; private set; }
        public bool IsRemnant { get; private set; }

        bool _overSpot, _landed;

        protected override void Awake()
        {
            base.Awake();
            Ritual = GetComponent<ScrollRitual>();
            Ritual.Hovering += OnHovering;
            Ritual.ReturnedToDesk += OnReturnedToDesk;
        }

        void OnDestroy()
        {
            if (Ritual == null) return;
            Ritual.Hovering -= OnHovering;
            Ritual.ReturnedToDesk -= OnReturnedToDesk;
        }

        void Start()
        {
            FitCollider(open: false);
            SetPickable(pickableOnStart);
        }

        void OnHovering()
        {
            FitCollider(open: true);
            SetPickable(true);
        }

        void OnReturnedToDesk()
        {
            FitCollider(open: false);
            SetPickable(false);
        }

        /// <summary>Sizes the grab box to the rolled-up bundle, or to the whole open scroll.</summary>
        public void FitCollider(bool open)
        {
            if (!(grabCollider is BoxCollider box) || Ritual.unroll == null) return;
            var u = Ritual.unroll;
            float depth = Ritual.canvas.size.y + 0.05f; // rods overhang the paper
            if (open)
            {
                box.center = new Vector3(0f, u.rodRadius - u.restDrop, 0f);
                box.size = new Vector3(Ritual.canvas.size.x + u.rodRadius * 4f + 0.004f, u.rodRadius * 2f + 0.01f, depth);
            }
            else
            {
                box.center = new Vector3(0f, u.rolledRadius - u.restDrop, 0f);
                box.size = new Vector3(u.rolledRadius * 4f + 0.004f, u.rolledRadius * 2f + 0.002f, depth);
            }
        }

        /// <summary>
        /// The failed scroll drops under gravity and becomes a physical remnant: grabbed by the whole open scroll,
        /// thrown when let go, never returned anywhere.
        /// </summary>
        public void BecomeRemnant()
        {
            IsRemnant = true;
            _landed = false;
            returnOnRelease = false;
            FitCollider(open: true);
            SetPickable(true);
            Grab.throwOnDetach = true;
            if (IsHeld)
            {
                // The magic leaves it in the player's hand: a buzz, and it slips out (re-grabbing then throws normally).
                Haptic(0.5f, 0.12f);
                if (Grab.interactionManager != null) Grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)Grab);
            }
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.linearDamping = 0.6f;   // paper drifts a little as it falls
                rb.angularDamping = 1.5f;
            }
        }

        /// <summary>The remnant crumbles where it lies: let go of it, no more grabbing, held still (its collider stays).</summary>
        public void FreezeRemnant()
        {
            if (IsHeld && Grab.interactionManager != null) Grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)Grab);
            Grab.enabled = false;
            var rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }
        }

        /// <summary>Back to a held-in-place scroll (reset).</summary>
        public void EndRemnant()
        {
            if (!IsRemnant) return;
            IsRemnant = false;
            Grab.throwOnDetach = false;
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.linearDamping = 0f;
                rb.angularDamping = 0.05f;
                rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }
        }

        void OnCollisionEnter(Collision collision)
        {
            if (!IsRemnant || _landed || collision.relativeVelocity.magnitude < 0.3f) return;
            _landed = true; // the first landing (and after each throw): a dull thud
            Sfx.Play(SfxId.ScrollThud, collision.GetContact(0).point, Mathf.Clamp01(collision.relativeVelocity.magnitude / 2.5f));
        }

        public void SetPickable(bool pickable)
        {
            Grab.enabled = pickable;
            if (grabCollider != null) grabCollider.enabled = pickable;
        }

        void Update()
        {
            // A tick in the hand when the scroll is over a spot that will take it.
            bool over = IsHeld && station != null && station.WouldAccept(this);
            if (over && !_overSpot) Haptic(0.25f, 0.05f);
            _overSpot = over;
        }

        protected override void OnGrabbed()
        {
            if (Ritual.OffDesk) Ritual.SetHeld(true); // pauses the hover bob
            if (IsRemnant) _landed = false;           // thrown again: thud again
        }

        protected override void OnReleased()
        {
            if (IsRemnant)
            {
                returnOnRelease = false; // thrown or dropped; physics takes it from here
                Ritual.SetHeld(false);
                return;
            }
            if (Ritual.OffDesk)
            {
                returnOnRelease = false; // stays where it was let go
                Ritual.SetHeld(false);
                return;
            }
            bool accepted = station != null && station.TryAccept(this);
            returnOnRelease = !accepted; // not taken: glide back to where it lay
        }
    }
}
