using Maliang.Api;
using Maliang.Core;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// Paces the burn against the summoning job (Phase3Design D11, D12). Sets <see cref="ScrollBurn.TargetProgress"/>
    /// every frame:
    /// - Verdict pending: burns at the normal pace up to the hold point (40%), slowing from 30% into a smoulder that
    ///   waits there ("gathering").
    /// - Verdict ok: the fire becomes the loading bar for the generation. It never runs ahead of the job (plus a slow
    ///   creep so it never looks stuck) and burns to the end once the model is ready.
    /// - Verdict fail, or a technical failure later: the failure sequence (<see cref="ScrollRitual.Fail"/>), once the
    ///   fire has had a moment to catch.
    /// Without a job it simply burns at the normal pace.
    /// </summary>
    [RequireComponent(typeof(ScrollBurn))]
    public class BurnPacer : MonoBehaviour
    {
        public ScrollRitual ritual;
        public ScrollBurn burn;

        [Tooltip("Seconds to burn the whole scroll at the normal pace (nothing to wait for).")]
        public float baseDuration = 14f;
        [Tooltip("Burned share where the fire waits for the verdict.")]
        [Range(0f, 1f)] public float holdPoint = 0.4f;
        [Tooltip("The fire slows down over this much burned share before a limit (the hold point, the job's progress).")]
        public float slowBand = 0.1f;
        [Tooltip("While the job runs, the fire may creep this much burned share per second beyond the job's progress.")]
        public float creepRate = 0.003f;
        [Tooltip("Furthest the fire burns before the job is done.")]
        [Range(0f, 1f)] public float maxBeforeDone = 0.95f;
        [Tooltip("A failure shows only after this much has burned, so the fire is seen to catch first.")]
        public float minBurnBeforeFail = 0.06f;

        public enum Phase { Idle, Burning, Holding, Loading, Finishing, Failed }
        public Phase Current { get; private set; } = Phase.Idle;

        float _target, _creep;

        void Awake()
        {
            if (ritual == null) ritual = GetComponent<ScrollRitual>();
            if (burn == null) burn = GetComponent<ScrollBurn>();
            burn.Ignited += OnIgnited;
        }

        void OnDestroy()
        {
            if (burn != null) burn.Ignited -= OnIgnited;
        }

        void OnIgnited(ScrollBurn b)
        {
            _target = 0f;
            _creep = 0f;
            Set(Phase.Burning);
        }

        /// <summary>Back to unlit (reset).</summary>
        public void ResetPacing()
        {
            _target = 0f;
            _creep = 0f;
            Current = Phase.Idle;
            burn.Holding = false;
        }

        void Update()
        {
            if (Current == Phase.Idle || Current == Phase.Failed || !burn.IsBurning) return;

            var job = ritual.Job;
            float cap;
            if (job == null)
            {
                cap = 1f;
                Set(Phase.Finishing);
            }
            else if (job.Verdict == JobVerdict.Fail || (job.Done && !job.Succeeded))
            {
                if (burn.Progress >= minBurnBeforeFail)
                {
                    Set(Phase.Failed);
                    burn.Holding = false;
                    ritual.Fail(job.Reason ?? FailReason.Collapsed);
                    return;
                }
                cap = minBurnBeforeFail + 0.02f; // let it catch, then fail
            }
            else if (job.Verdict == JobVerdict.Pending)
            {
                cap = holdPoint;
                Set(_target >= holdPoint - slowBand * 0.5f ? Phase.Holding : Phase.Burning);
            }
            else if (job.Done)
            {
                cap = 1f;
                Set(Phase.Finishing);
            }
            else
            {
                // Generation running: the fire follows the job (its progress after the verdict maps onto the rest).
                _creep += creepRate * Time.deltaTime;
                float jobShare = Mathf.InverseLerp(0.1f, 1f, job.Progress);
                cap = Mathf.Min(maxBeforeDone, holdPoint + (maxBeforeDone - holdPoint) * jobShare + _creep);
                cap = Mathf.Max(cap, holdPoint);
                Set(Phase.Loading);
            }

            // Normal pace, easing into the limit so the fire settles into a smoulder rather than stopping dead.
            float room = cap - _target;
            float ease = cap >= 1f ? 1f : Mathf.Clamp01(room / Mathf.Max(0.001f, slowBand));
            _target = Mathf.Min(cap, _target + Time.deltaTime / Mathf.Max(0.1f, baseDuration) * ease);
            burn.TargetProgress = _target;
            burn.Holding = Current == Phase.Holding;
        }

        void Set(Phase phase)
        {
            if (Current == phase) return;
            Current = phase;
            string what = phase switch
            {
                Phase.Holding => $"holding at {holdPoint:P0}, waiting for the verdict",
                Phase.Loading => "verdict ok: the fire follows the generation",
                Phase.Finishing => "burning to the end",
                Phase.Failed => "the job failed",
                _ => phase.ToString().ToLowerInvariant(),
            };
            MaliangLog.Info("Burn", $"{what} (burned {burn.Progress:P0})");
        }
    }
}
