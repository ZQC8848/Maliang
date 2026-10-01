using System;
using System.Threading;
using System.Threading.Tasks;
using Maliang.Api;
using Maliang.Core;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// What the burn waits on: the summoning job started when the scroll was sealed (Phase3Design section 3).
    /// <see cref="ObjectJob"/> is the real one; <see cref="FakeJob"/> stands in for testing without the API;
    /// <see cref="ReplayJob"/> replays a work from the library (only local files to load).
    /// </summary>
    public interface IBurnJob
    {
        JobVerdict Verdict { get; }
        FailReason? Reason { get; }
        /// <summary>Rough 0..1 over all stages.</summary>
        float Progress { get; }
        bool Done { get; }
        bool Succeeded { get; }
    }

    public enum FakeOutcome
    {
        /// <summary>Recognised, then the model is ready after the generation time.</summary>
        Success,
        /// <summary>The vision agent fails it (the reason below).</summary>
        FailAtVerdict,
        /// <summary>Recognised, then the 3D generation fails halfway (collapsed).</summary>
        FailAfterVerdict,
    }

    [Serializable]
    public class FakeJobSettings
    {
        public FakeOutcome outcome = FakeOutcome.Success;
        [Tooltip("Seconds from the seal to the verdict (the real vision call takes 5-15 s).")]
        public float verdictDelay = 6f;
        [Tooltip("Seconds from the verdict to the model being ready (the real generation takes 1-3 min).")]
        public float generationTime = 30f;
        [Tooltip("Reason when the vision agent fails it.")]
        public FailReason failReason = FailReason.Unrecognizable;

        public FakeJobSettings Clone() => (FakeJobSettings)MemberwiseClone();
    }

    /// <summary>A job that plays out on a timer from <see cref="FakeJobSettings"/> (no network).</summary>
    public class FakeJob : IBurnJob
    {
        /// <summary>Settings for new fake jobs (set by <see cref="DeskDebugKeys"/>).</summary>
        public static FakeJobSettings Settings = new FakeJobSettings();

        readonly FakeJobSettings _s;
        readonly float _start;

        public FakeJob(FakeJobSettings settings, float startTime)
        {
            _s = settings.Clone();
            _start = startTime;
        }

        float Elapsed => Time.time - _start;
        float VerdictAt => _s.verdictDelay;
        float FailAt => _s.verdictDelay + _s.generationTime * 0.5f;
        float ReadyAt => _s.verdictDelay + _s.generationTime;

        public JobVerdict Verdict => Elapsed < VerdictAt ? JobVerdict.Pending
            : _s.outcome == FakeOutcome.FailAtVerdict ? JobVerdict.Fail : JobVerdict.Ok;

        public FailReason? Reason =>
            _s.outcome == FakeOutcome.FailAtVerdict && Elapsed >= VerdictAt ? _s.failReason
            : _s.outcome == FakeOutcome.FailAfterVerdict && Elapsed >= FailAt ? FailReason.Collapsed
            : (FailReason?)null;

        public float Progress => Elapsed < VerdictAt ? 0.02f
            : 0.1f + 0.9f * Mathf.Clamp01((Elapsed - VerdictAt) / Mathf.Max(0.1f, _s.generationTime));

        public bool Done => _s.outcome switch
        {
            FakeOutcome.FailAtVerdict => Elapsed >= VerdictAt,
            FakeOutcome.FailAfterVerdict => Elapsed >= FailAt,
            _ => Elapsed >= ReadyAt,
        };

        public bool Succeeded => Done && _s.outcome == FakeOutcome.Success;

        public override string ToString() => $"fake {_s.outcome} (verdict {_s.verdictDelay:F0}s, generation {_s.generationTime:F0}s)";
    }

    /// <summary>
    /// Replaying a work from the library (Phase3Design 8.5, D19): nothing to decide and no API, only the saved files to
    /// load. Loading starts when the scroll catches fire (<see cref="BeginLoading"/>); the burn takes a fixed 10 s and
    /// waits in its embers if loading takes longer. A load that fails (files missing or broken) fails as
    /// <see cref="FailReason.Faded"/>.
    /// </summary>
    public class ReplayJob : IBurnJob
    {
        readonly Func<CancellationToken, Task<bool>> _load;
        readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        Task<bool> _task;

        /// <param name="load">Loads the work; returns false if its files are missing or broken.</param>
        public ReplayJob(Func<CancellationToken, Task<bool>> load) => _load = load;

        /// <summary>Starts loading (once).</summary>
        public void BeginLoading()
        {
            if (_task == null) _task = Run();
        }

        async Task<bool> Run()
        {
            try { return await _load(_cancel.Token); }
            catch (OperationCanceledException) { return false; }
            catch (Exception e)
            {
                MaliangLog.Warn("Replay", "Load failed: " + e.Message);
                return false;
            }
        }

        public void Cancel() => _cancel.Cancel();

        public bool Loading => _task != null && !_task.IsCompleted;
        public JobVerdict Verdict => JobVerdict.Ok;
        public FailReason? Reason => Done && !Succeeded ? FailReason.Faded : (FailReason?)null;
        public float Progress => Done ? 1f : _task != null ? 0.5f : 0f;
        public bool Done => _task != null && _task.IsCompleted;
        public bool Succeeded => Done && _task.Status == TaskStatus.RanToCompletion && _task.Result;

        /// <summary>A stand-in load for testing: takes <paramref name="seconds"/>, then succeeds unless <paramref name="faded"/>.</summary>
        public static ReplayJob Fake(float seconds, bool faded = false) => new ReplayJob(async cancel =>
        {
            await Task.Delay(Mathf.RoundToInt(seconds * 1000f), cancel);
            return !faded;
        });

        public override string ToString() => "replay";
    }
}
