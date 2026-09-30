using Maliang.VR;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// Picking up a scroll.
    /// Rolled up (the spare): released over a free drawing spot (<see cref="ScrollStation"/>) it is laid down there;
    /// released anywhere else it glides back to where it lay.
    /// Sealed and hovering: it can be carried anywhere; let go, it stays there and keeps hovering (no gravity).
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

        bool _overSpot;

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
        }

        protected override void OnReleased()
        {
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
