using System;
using System.Threading;
using System.Threading.Tasks;
using Maliang.Api;
using Maliang.Core;
using Maliang.Library;
using Maliang.Loading;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// The summoning without generation (TechPlan §9): no API keys, no network, or the fast mode for 「境」. A bundled
    /// work comes, matched to the drawing when the vision agent can still be asked (it judges the drawing as strictly
    /// as ever, so a scribble still fails), otherwise a random one of the same kind. The burn plays out as usual;
    /// nothing about the fallback is shown to the player, it is only logged.
    /// </summary>
    public class FallbackJob : IBurnJob, IPreparedSummon, IBurnOutcome
    {
        /// <summary>How long the vision agent may take before the fallback stops waiting for it (s).</summary>
        const float VisionTimeout = 20f;

        readonly ScrollRitual _ritual;
        readonly SealType _seal;
        readonly bool _askVision;
        readonly string _why;
        readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        JobVerdict _verdict = JobVerdict.Pending;
        FailReason? _failure;
        float _progress;
        bool _done;

        /// <summary>The bundled work that comes.</summary>
        public LibraryEntry Work { get; private set; }
        public SummonedObject Prepared { get; private set; }

        /// <param name="askVision">Ask the vision agent what the drawing is (needs the vision key and the network).</param>
        /// <param name="why">For the log: why there is no generation.</param>
        public FallbackJob(ScrollRitual ritual, SealType seal, bool askVision, string why)
        {
            _ritual = ritual;
            _seal = seal;
            _askVision = askVision;
            _why = why;
            _ = RunAsync();
        }

        public JobVerdict Verdict => _verdict;
        public FailReason? Reason => _failure;
        public float Progress => _progress;
        public bool Done => _done;
        public bool Succeeded => _done && _failure == null;

        public void Cancel() => _cancel.Cancel();

        async Task RunAsync()
        {
            try
            {
                MaliangLog.Info("Fallback", $"Scroll {_ritual.Tag} sealed {_seal} with no generation ({_why})");
                string subject = null, category = null;
                if (_askVision)
                {
                    var tcs = new TaskCompletionSource<Drawing.InkExport>();
                    _ritual.canvas.Export(r => tcs.TrySetResult(r));
                    var export = await tcs.Task;
                    if (export.Empty) { Fail(FailReason.NearlyBlank); return; }
                    _progress = 0.02f;

                    var read = await ReadAsync(export.Png);
                    if (read.failed != null) { Fail(read.failed.Value); return; }
                    subject = read.subject;
                    category = read.category;
                }

                _verdict = JobVerdict.Ok;
                _progress = 0.3f;
                Work = FallbackMatcher.Match(_seal, subject, category);
                if (Work == null) { Fail(FailReason.Unreachable); return; }
                if (_seal == SealType.Object)
                {
                    Prepared = await Summoning.PrepareAsync(ObjectSpawner.Request.From(Work), _cancel.Token);
                    if (Prepared == null) { Fail(FailReason.Faded); return; }
                }
                _progress = 1f;
                _done = true;
            }
            catch (OperationCanceledException) { Fail(FailReason.Unreachable); }
            catch (Exception e)
            {
                MaliangLog.Error("Fallback", e);
                Fail(FailReason.Collapsed);
            }
        }

        /// <summary>
        /// What the vision agent reads in the drawing: its subject, or its verdict when it rejects the drawing.
        /// If the agent cannot be reached in time, nothing (a random work comes).
        /// </summary>
        async Task<(string subject, string category, FailReason? failed)> ReadAsync(byte[] png)
        {
            var vision = new VisionClient(MaliangConfig.Current);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(VisionTimeout));
            try
            {
                if (_seal == SealType.World)
                {
                    var plan = await vision.PlanWorldAsync(png, timeout.Token);
                    return plan.Ok ? (plan.Subject, null, null) : (null, null, FailReasons.FromVision(plan.Reason));
                }
                var p = await vision.PlanAsync(png, "OBJECT", timeout.Token);
                return p.Ok ? (p.Subject, p.Category, null) : (null, null, FailReasons.FromVision(p.Reason));
            }
            catch (Exception e) when (!_cancel.IsCancellationRequested && (e is ApiException || e is OperationCanceledException))
            {
                MaliangLog.Info("Fallback", $"Vision unavailable ({e.Message}); a random work comes");
                return (null, null, null);
            }
        }

        void Fail(FailReason reason)
        {
            _failure = reason;
            _verdict = JobVerdict.Fail;
            _done = true;
            MaliangLog.Info("Fallback", $"Scroll {_ritual.Tag}: {reason}");
        }

        /// <summary>A bundled world rises once the scroll has burned away (an object is revealed by the ritual).</summary>
        public void Apply(ScrollRitual ritual)
        {
            if (_seal == SealType.World) WorldStage.Instance.Show(Work);
        }

        public override string ToString() => $"fallback {_seal} ({_why}){(Work != null ? $": \"{Work.subject}\"" : "")}";
    }
}
