using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Maliang.Api;
using Maliang.Core;
using Maliang.Library;
using Maliang.Loading;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>A job that ends with an object built out of sight, to be revealed when the scroll has burned away.</summary>
    public interface IPreparedSummon
    {
        SummonedObject Prepared { get; }
    }

    /// <summary>
    /// The real summoning for a sealed scroll (Phase 5). At the seal it keeps the drawing and seal layers for the
    /// library, exports the drawing and starts the <see cref="ObjectAgent"/>. Once the model is ready it saves the work
    /// to the library (waiting a few seconds for the sound, which is added later if it is slower) and builds the object
    /// hidden; only then is the job done, so the burn ends the moment the object can appear.
    /// If the APIs fail (network, quota, a collapsed generation; not a drawing the agent rejects), a bundled work
    /// matched to what the agent read comes instead (<see cref="FallbackMatcher"/>) and the burn never shows a failure.
    /// </summary>
    public class SummonJob : IBurnJob, IPreparedSummon
    {
        const float SoundWait = 8f;

        readonly ScrollRitual _ritual;
        readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        PendingWork _work;
        FailReason? _failure;
        bool _done;
        float _finishing; // progress of the last steps (save, load), so the fire keeps moving
        bool _fallingBack;

        public ObjectJob Agent { get; private set; }
        public LibraryEntry Entry { get; private set; }
        public SummonedObject Prepared { get; private set; }
        /// <summary>The bundled work summoned instead, when the APIs failed.</summary>
        public LibraryEntry Fallback { get; private set; }

        public SummonJob(ScrollRitual ritual)
        {
            _ritual = ritual;
            _ = RunAsync();
        }

        public JobVerdict Verdict => _fallingBack || AgentFallsBack ? JobVerdict.Ok
            : _failure != null && (Agent == null || Agent.Verdict == JobVerdict.Pending) ? JobVerdict.Fail
            : Agent?.Verdict ?? JobVerdict.Pending;

        public FailReason? Reason => _failure ?? (_fallingBack || AgentFallsBack ? null : Agent?.Reason);

        /// <summary>The agent has failed in a way the fallback covers (true from that frame on, before it starts).</summary>
        bool AgentFallsBack => Agent != null && Agent.Done && !Agent.Succeeded
                               && FallbackMatcher.Covers(Agent.Reason) && FallbackMatcher.Available(SealType.Object);
        public float Progress => Agent == null ? 0f : Mathf.Min(Agent.Progress, 0.95f) + 0.05f * _finishing;
        public bool Done => _done;
        public bool Succeeded => _done && _failure == null;

        public void Cancel() => _cancel.Cancel();

        async Task RunAsync()
        {
            try
            {
                var canvas = _ritual.canvas;
                // 1. The library keeps the scroll exactly as it was sealed (both layers, full resolution).
                _work = ArtLibrary.BeginPending(_ritual.Seal ?? SealType.Object);
                var layers = await ReadLayers(canvas);
                if (layers.ink == null) throw new Exception("could not read the drawing");
                _work.WriteLayers(layers.ink, layers.seal);

                // 2. The drawing alone, cropped, for the vision agent.
                var export = await Export(canvas);
                if (export.Empty) { Finish(FailReason.NearlyBlank); return; }

                // 3. The agent: verdict, refine, model, rig, animation, sound.
                var config = MaliangConfig.Current;
                Agent = new ObjectAgent(config).Start(export.Png, _work.AgentDir, _cancel.Token);
                MaliangLog.Info("Summon", $"Agent started ({_work.Id})");
                while (!Agent.Done) await Task.Yield();
                if (!Agent.Succeeded)
                {
                    if (AgentFallsBack) await FallBackAsync(Agent.Plan?.Ok == true ? Agent.Plan : null);
                    else Finish(Agent.Reason ?? FailReason.Collapsed);
                    return;
                }

                // 4. Save the work (give the sound a few seconds to arrive), then build the object out of sight.
                float waited = 0f;
                while (!Agent.SoundDone && waited < SoundWait)
                {
                    await Task.Yield();
                    waited += Time.unscaledDeltaTime;
                    _finishing = 0.3f * waited / SoundWait;
                }
                Entry = _work.Commit(Agent);
                if (Agent.SoundDone) _work.DiscardScratch();
                else Agent.SoundReady += OnLateSound;
                _finishing = 0.5f;

                Prepared = await Summoning.PrepareAsync(ObjectSpawner.Request.From(Entry), _cancel.Token);
                if (Prepared == null) { Finish(FailReason.Collapsed); return; }
                _finishing = 1f;
                Finish(null);
            }
            catch (OperationCanceledException)
            {
                Finish(FailReason.Unreachable);
            }
            catch (Exception e)
            {
                MaliangLog.Error("Summon", e);
                Finish(FailReason.Collapsed);
            }
        }

        /// <summary>A bundled work instead of the failed generation; the player's drawing is not kept (nothing was made of it).</summary>
        async Task FallBackAsync(VisionPlan plan)
        {
            _fallingBack = true;
            MaliangLog.Info("Summon", $"Agent failed ({Agent.Reason}); falling back to a bundled work");
            _work.Discard();
            var entry = FallbackMatcher.Match(SealType.Object, plan?.Subject, plan?.Category);
            _finishing = 0.3f;
            Prepared = entry != null ? await Summoning.PrepareAsync(ObjectSpawner.Request.From(entry), _cancel.Token) : null;
            if (Prepared == null) { Finish(Agent.Reason ?? FailReason.Collapsed); return; }
            Fallback = entry;
            _finishing = 1f;
            Finish(null);
        }

        void Finish(FailReason? failure)
        {
            _failure = failure;
            _done = true;
            if (failure != null)
            {
                MaliangLog.Info("Summon", $"Failed: {failure}");
                if (Entry == null) _work?.Discard();
            }
        }

        void OnLateSound(ObjectJob job)
        {
            ArtLibrary.AddSound(Entry, job.SoundPath);
            if (Prepared != null && File.Exists(job.SoundPath)) _ = AttachSound(job.SoundPath, job.Plan?.Sound?.Trigger);
            _work.DiscardScratch();
        }

        async Task AttachSound(string path, string trigger)
        {
            var clip = await SoundClient.LoadClipAsync(path, _cancel.Token);
            if (Prepared != null) Prepared.SetSound(clip, trigger);
        }

        static Task<(byte[] ink, byte[] seal)> ReadLayers(Drawing.InkCanvas canvas)
        {
            var tcs = new TaskCompletionSource<(byte[], byte[])>();
            canvas.ReadLayers((ink, seal) => tcs.TrySetResult((ink, seal)));
            return tcs.Task;
        }

        static Task<Drawing.InkExport> Export(Drawing.InkCanvas canvas)
        {
            var tcs = new TaskCompletionSource<Drawing.InkExport>();
            canvas.Export(r => tcs.TrySetResult(r));
            return tcs.Task;
        }

        public override string ToString() => Fallback != null ? $"summoning (fell back to \"{Fallback.subject}\")" : "summoning (agent)";
    }
}
