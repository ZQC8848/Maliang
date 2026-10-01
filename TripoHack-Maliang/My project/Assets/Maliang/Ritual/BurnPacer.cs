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
    /// Replaying a library work (<see cref="ReplayJob"/>, Phase3Design 8.5): a fixed <see cref="replayDuration"/> with no
    /// hold point; loading starts as the fire catches, and if it is not done by the end the fire waits in its last
    /// embers. A failed load is the failure sequence (faded).
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
        [Tooltip("While the job runs, the fire may creep up to this much burned share beyond the job's progress...")]
        public float creepMax = 0.12f;
        [Tooltip("...approaching it over about this many seconds (a real generation takes one to three minutes).")]
        public float creepTime = 60f;
        [Tooltip("Furthest the fire burns before the job is done.")]
        [Range(0f, 1f)] public float maxBeforeDone = 0.95f;
        [Tooltip("A failure shows only after this much has burned, so the fire is seen to catch first.")]
        public float minBurnBeforeFail = 0.06f;

        [Header("Replay (library)")]
        [Tooltip("Seconds a replayed scroll takes to burn (no API, no hold point).")]
        public float replayDuration = 10f;
        [Tooltip("Burned share where a replay waits in its embers if the files are still loading.")]
        [Range(0f, 1f)] public float emberPoint = 0.97f;

        public enum Phase { Idle, Burning, Holding, Loading, Embers, Finishing, Failed }
        public Phase Current { get; private set; } = Phase.Idle;

        float _target, _creepTime;

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
            _creepTime = 0f;
            if (ritual.Job is ReplayJob replay) replay.BeginLoading(); // the files load while it burns
            Set(Phase.Burning);
        }

        /// <summary>Back to unlit (reset).</summary>
        public void ResetPacing()
        {
            _target = 0f;
            _creepTime = 0f;
            Current = Phase.Idle;
            burn.Holding = false;
        }

        void Update()
        {
            if (Current == Phase.Idle || Current == Phase.Failed || !burn.IsBurning) return;

            var job = ritual.Job;
            float cap;
            float duration = baseDuration;
            float band = slowBand;
            if (job is ReplayJob)
            {
                // Fixed length, no hold point; embers at the very end if the files are not loaded yet.
                duration = replayDuration;
                band = 0.03f;
                if (job.Done && !job.Succeeded)
                {
                    if (burn.Progress >= minBurnBeforeFail)
                    {
                        Set(Phase.Failed);
                        burn.Holding = false;
                        ritual.Fail(job.Reason ?? FailReason.Faded);
                        return;
                    }
                    cap = minBurnBeforeFail + 0.02f;
                }
                else if (job.Done)
                {
                    cap = 1f;
                    Set(Phase.Finishing);
                }
                else
                {
                    cap = emberPoint;
                    Set(_target >= emberPoint - 0.01f ? Phase.Embers : Phase.Burning);
                }
            }
            else if (job == null)
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
                _creepTime += Time.deltaTime;
                float creep = creepMax * (1f - Mathf.Exp(-_creepTime / Mathf.Max(1f, creepTime)));
                float jobShare = Mathf.InverseLerp(0.1f, 1f, job.Progress);
                cap = Mathf.Min(maxBeforeDone, holdPoint + (maxBeforeDone - holdPoint - creepMax) * jobShare + creep);
                cap = Mathf.Max(cap, holdPoint);
                Set(Phase.Loading);
            }

            // Normal pace, easing into the limit so the fire settles into a smoulder rather than stopping dead.
            float room = cap - _target;
            float ease = cap >= 1f ? 1f : Mathf.Clamp01(room / Mathf.Max(0.001f, band));
            _target = Mathf.Min(cap, _target + Time.deltaTime / Mathf.Max(0.1f, duration) * ease);
            burn.TargetProgress = _target;
            burn.Holding = Current == Phase.Holding || Current == Phase.Embers;
        }

        void Set(Phase phase)
        {
            if (Current == phase) return;
            Current = phase;
            string what = phase switch
            {
                Phase.Holding => $"holding at {holdPoint:P0}, waiting for the verdict",
                Phase.Embers => "embers: waiting for the saved work to load",
                Phase.Loading => "verdict ok: the fire follows the generation",
                Phase.Finishing => "burning to the end",
                Phase.Failed => "the job failed",
                _ => phase.ToString().ToLowerInvariant(),
            };
            MaliangLog.Info("Burn", $"{what} (burned {burn.Progress:P0})");
        }
    }
}
