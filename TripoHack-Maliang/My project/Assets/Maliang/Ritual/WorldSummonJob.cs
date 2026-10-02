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
    /// <summary>What happens when a scroll has burned away, besides an object appearing (a world rising, the sky returning).</summary>
    public interface IBurnOutcome
    {
        void Apply(ScrollRitual ritual);
    }

    /// <summary>
    /// The real 「境」 summoning (Phase 6): at the seal the layers go to the library's pending folder and the drawing
    /// to the <see cref="WorldAgent"/> (vision, realistic landscape, World Labs, splats). When the world is ready it is
    /// saved to the library; once the scroll has burned away it rises around the lotus throne (<see cref="WorldStage"/>).
    /// If the APIs fail (network, quota, a collapsed generation), the closest bundled world rises instead.
    /// </summary>
    public class WorldSummonJob : IBurnJob, IBurnOutcome
    {
        readonly ScrollRitual _ritual;
        readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        PendingWork _work;
        FailReason? _failure;
        bool _done;
        bool _fallingBack;

        public WorldJob Agent { get; private set; }
        public LibraryEntry Entry { get; private set; }
        /// <summary>The bundled world raised instead, when the APIs failed.</summary>
        public LibraryEntry Fallback { get; private set; }

        public WorldSummonJob(ScrollRitual ritual)
        {
            _ritual = ritual;
            _ = RunAsync();
        }

        public JobVerdict Verdict => _fallingBack || AgentFallsBack ? JobVerdict.Ok
            : _failure != null && (Agent == null || Agent.Verdict == JobVerdict.Pending) ? JobVerdict.Fail
            : Agent?.Verdict ?? JobVerdict.Pending;

        public FailReason? Reason => _failure ?? (_fallingBack || AgentFallsBack ? null : Agent?.Reason);

        bool AgentFallsBack => Agent != null && Agent.Done && !Agent.Succeeded
                               && FallbackMatcher.Covers(Agent.Reason) && FallbackMatcher.Available(SealType.World);
        public float Progress => Agent == null ? 0f : Mathf.Min(Agent.Progress, 0.97f);
        public bool Done => _done;
        public bool Succeeded => _done && _failure == null;

        public void Cancel() => _cancel.Cancel();

        async Task RunAsync()
        {
            try
            {
                var canvas = _ritual.canvas;
                _work = ArtLibrary.BeginPending(SealType.World);
                var tcsLayers = new TaskCompletionSource<(byte[], byte[])>();
                canvas.ReadLayers((ink, seal) => tcsLayers.TrySetResult((ink, seal)));
                var (inkLayer, sealLayer) = await tcsLayers.Task;
                if (inkLayer == null) throw new Exception("could not read the drawing");
                _work.WriteLayers(inkLayer, sealLayer);

                var tcsExport = new TaskCompletionSource<Drawing.InkExport>();
                canvas.Export(r => tcsExport.TrySetResult(r));
                var export = await tcsExport.Task;
                if (export.Empty) { Finish(FailReason.NearlyBlank); return; }

                Agent = new WorldAgent(MaliangConfig.Current).Start(export.Png, _work.AgentDir, _cancel.Token);
                MaliangLog.Info("Summon", $"World agent started ({_work.Id})");
                while (!Agent.Done) await Task.Yield();
                if (!Agent.Succeeded)
                {
                    if (AgentFallsBack) FallBack(Agent.Plan?.Ok == true ? Agent.Plan.Subject : null);
                    else Finish(Agent.Reason ?? FailReason.Collapsed);
                    return;
                }

                Entry = _work.Commit(Agent);
                _work.DiscardScratch();
                Finish(null);
            }
            catch (OperationCanceledException) { Finish(FailReason.Unreachable); }
            catch (Exception e)
            {
                MaliangLog.Error("Summon", e);
                Finish(FailReason.Collapsed);
            }
        }

        void FallBack(string subject)
        {
            _fallingBack = true;
            MaliangLog.Info("Summon", $"World agent failed ({Agent.Reason}); falling back to a bundled world");
            _work.Discard();
            Fallback = FallbackMatcher.Match(SealType.World, subject);
            Finish(Fallback != null ? (FailReason?)null : Agent.Reason ?? FailReason.Collapsed);
        }

        void Finish(FailReason? failure)
        {
            _failure = failure;
            _done = true;
            if (failure != null)
            {
                MaliangLog.Info("Summon", $"World failed: {failure}");
                if (Entry == null) _work?.Discard();
            }
        }

        /// <summary>The scroll has burned away: the world rises around the throne.</summary>
        public void Apply(ScrollRitual ritual) => WorldStage.Instance.Show(Entry ?? Fallback);

        public override string ToString() => Fallback != null ? $"summoning (fell back to \"{Fallback.subject}\")" : "summoning (world agent)";
    }
}
