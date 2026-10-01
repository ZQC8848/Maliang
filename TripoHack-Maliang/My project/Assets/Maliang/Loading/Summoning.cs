using System.Threading;
using System.Threading.Tasks;
using Maliang.Core;
using UnityEngine;

namespace Maliang.Loading
{
    /// <summary>
    /// The moment a summoned object appears (Phase3Design 5.5): it is loaded and built out of sight while the scroll
    /// burns (<see cref="PrepareAsync"/>), and wakes up where the paper was the moment it has burned away
    /// (<see cref="Reveal"/>), turned towards the player, growing out of the last embers with a soft bloom of sound.
    /// </summary>
    public static class Summoning
    {
        static readonly Vector3 OutOfSight = new Vector3(0f, -50f, 0f);

        /// <summary>Loads and builds the object, inactive, ready to be revealed. Null if the model cannot be loaded.</summary>
        public static Task<SummonedObject> PrepareAsync(ObjectSpawner.Request req, CancellationToken cancel = default) =>
            ObjectSpawner.SpawnAsync(req, OutOfSight, Quaternion.identity, cancel, hidden: true);

        /// <summary>Places the prepared object at <paramref name="centre"/>, facing <paramref name="head"/>, and wakes it.</summary>
        public static void Reveal(SummonedObject obj, Vector3 centre, Transform head)
        {
            if (obj == null) return;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            Vector3 toPlayer = head != null ? Vector3.ProjectOnPlane(head.position - centre, Vector3.up) : Vector3.back;
            if (toPlayer.sqrMagnitude < 1e-4f) toPlayer = Vector3.back;
            obj.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(toPlayer.normalized, Vector3.up));
            obj.gameObject.AddComponent<MaterializeIn>();
            obj.gameObject.SetActive(true);
            Sfx.Play(SfxId.Materialize, centre);
            MaliangLog.Info("Spawn", $"{obj.name} appeared");
        }
    }

    /// <summary>Grows the object from a speck to full size with a slight overshoot as it appears.</summary>
    public class MaterializeIn : MonoBehaviour
    {
        public float duration = 0.8f;
        Vector3 _scale;
        float _t;

        void Awake()
        {
            _scale = transform.localScale;
            transform.localScale = _scale * 0.02f;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / duration);
            // Ease out with a gentle overshoot (back easing).
            const float s = 1.4f;
            float e = 1f + (s + 1f) * Mathf.Pow(k - 1f, 3f) + s * Mathf.Pow(k - 1f, 2f);
            transform.localScale = _scale * Mathf.Max(0.02f, e);
            if (k >= 1f)
            {
                transform.localScale = _scale;
                Destroy(this);
            }
        }
    }
}
