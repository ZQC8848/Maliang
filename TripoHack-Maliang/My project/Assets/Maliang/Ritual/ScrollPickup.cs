using Maliang.VR;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// Lets a rolled-up scroll be picked up and laid on the desk's drawing spot (<see cref="ScrollStation"/>).
    /// Released over a free drawing spot, the station takes it; released anywhere else, it glides back to where it lay.
    /// The scroll on the desk (and one that has been sealed) is not pickable.
    /// </summary>
    [RequireComponent(typeof(ScrollRitual))]
    public class ScrollPickup : GrabbableTool
    {
        public ScrollStation station;
        [Tooltip("Pickable when the scene starts (the spare), or not (the scroll already on the desk).")]
        public bool pickableOnStart = true;
        [Tooltip("Collider around the rolled-up scroll, used for grabbing.")]
        public Collider grabCollider;

        public ScrollRitual Ritual { get; private set; }

        bool _overSpot;

        protected override void Awake()
        {
            base.Awake();
            Ritual = GetComponent<ScrollRitual>();
        }

        void Start() => SetPickable(pickableOnStart);

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

        protected override void OnReleased()
        {
            bool accepted = station != null && station.TryAccept(this);
            returnOnRelease = !accepted; // not taken: glide back to where it lay
        }
    }
}
