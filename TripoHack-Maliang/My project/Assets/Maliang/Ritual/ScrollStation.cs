using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// The drawing spot on the desk. It holds the scroll being worked on (<see cref="Active"/>); the brush and seals
    /// draw and stamp on whichever scroll is active. Once the active scroll is sealed and hovering, the spare rolled
    /// scroll can be laid here: it slides to the middle of the desk and unrolls, becomes the active scroll, and a new
    /// spare appears at the back of the desk.
    /// </summary>
    public class ScrollStation : MonoBehaviour
    {
        [Tooltip("The scroll on the desk (or the last one, now hovering).")]
        public ScrollRitual active;
        [Tooltip("The rolled scroll waiting at the back of the desk.")]
        public ScrollPickup spare;
        [Tooltip("Prefab for new spare scrolls.")]
        public ScrollRitual scrollPrefab;
        [Tooltip("Where new spares lie (back of the desk).")]
        public Transform spareSpot;
        [Tooltip("Player head, handed to new scrolls for their hover position.")]
        public Transform head;

        [Header("Laying a scroll down")]
        [Tooltip("A scroll released within this horizontal distance (m) of the drawing spot is laid down there.")]
        public float acceptRadius = 0.3f;
        [Tooltip("...and no higher than this above the desk (m).")]
        public float acceptHeight = 0.45f;
        [Tooltip("Time to slide into place before unrolling (s).")]
        public float placeDuration = 0.5f;

        readonly List<ScrollRitual> _retired = new List<ScrollRitual>();
        bool _placing;

        public ScrollRitual Active => active;
        public event Action<ScrollRitual> ActiveChanged;

        /// <summary>The desk spot is free once the active scroll has left it (sealed and hovering, or further on).</summary>
        public bool SlotFree => !_placing && (active == null || active.OffDesk);

        public bool WouldAccept(ScrollPickup pickup)
        {
            // Only a rolled-up scroll can be laid down (not a sealed one carried back, nor the one already here).
            if (pickup == null || !SlotFree || pickup.Ritual == active || pickup.Ritual.State != ScrollState.Rolled) return false;
            Vector3 d = pickup.transform.position - transform.position;
            return new Vector2(d.x, d.z).magnitude <= acceptRadius && d.y > -0.05f && d.y <= acceptHeight;
        }

        /// <summary>Called when a scroll is released: lays it down here if it is close enough and the spot is free.</summary>
        public bool TryAccept(ScrollPickup pickup)
        {
            if (!WouldAccept(pickup)) return false;
            StartCoroutine(Place(pickup));
            return true;
        }

        IEnumerator Place(ScrollPickup pickup)
        {
            _placing = true;
            pickup.SetPickable(false);
            if (pickup == spare) spare = null;

            // Slide (and turn) into the drawing spot, still rolled up. (A scroll from a drawer stops riding with it.)
            var t = pickup.transform;
            t.SetParent(null, true);
            Vector3 p0 = t.position;
            Quaternion r0 = t.rotation;
            for (float k = 0f; k < 1f; k += Time.deltaTime / Mathf.Max(0.01f, placeDuration))
            {
                float s = Mathf.SmoothStep(0f, 1f, k);
                t.SetPositionAndRotation(Vector3.Lerp(p0, transform.position, s), Quaternion.Slerp(r0, transform.rotation, s));
                yield return null;
            }
            t.SetPositionAndRotation(transform.position, transform.rotation);

            if (active != null)
            {
                active.name = "Scroll (Sealed)";
                _retired.Add(active);
            }
            active = pickup.Ritual;
            active.name = "Scroll";
            active.SetDeskPose(transform.position, transform.rotation);
            active.RollUpAndUnroll(0f);
            _placing = false;
            ActiveChanged?.Invoke(active);

            SpawnSpare();
        }

        /// <summary>Puts a fresh rolled scroll at the back of the desk (if there is none).</summary>
        public void SpawnSpare()
        {
            if (spare != null || scrollPrefab == null || spareSpot == null) return;
            var ritual = Instantiate(scrollPrefab, spareSpot.position, spareSpot.rotation);
            ritual.name = "Scroll (Spare)";
            ritual.head = head;
            ritual.startMode = ScrollStartMode.Rolled;
            spare = ritual.GetComponent<ScrollPickup>();
            spare.station = this;
            spare.pickableOnStart = true;
        }

        /// <summary>Testing / new round: clears away the earlier scrolls and resets the active one.</summary>
        public void ResetAll()
        {
            foreach (var r in _retired) if (r != null) Destroy(r.gameObject);
            _retired.Clear();
            if (active != null) active.ResetScroll();
        }
    }
}
