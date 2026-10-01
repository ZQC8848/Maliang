using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maliang.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace Maliang.Api
{
    public enum JobVerdict { Pending, Ok, Fail }

    /// <summary>
    /// One sealed drawing on its way to becoming an object (Phase3Design section 9). The ritual watches it:
    /// <see cref="VerdictReady"/> decides whether the burn continues or fails (D11, D12), <see cref="Completed"/>
    /// tells it the model is on disk (or that a technical failure happened after the verdict), and
    /// <see cref="SoundReady"/> arrives independently.
    /// </summary>
    public class ObjectJob : Maliang.Ritual.IBurnJob
    {
        public string Id;
        public string WorkDir;                 // all files of this job (ink, plan, refined, model, animations, sound)

        public JobVerdict Verdict { get; internal set; } = JobVerdict.Pending;
        public FailReason? Reason { get; internal set; }
        public VisionPlan Plan { get; internal set; }

        public string Stage { get; internal set; } = "queued";
        /// <summary>Rough 0..1 estimate across all stages, for the burn after the hold point.</summary>
        public float Progress { get; internal set; }
        public bool Done { get; internal set; }
        public bool Succeeded => Done && Verdict == JobVerdict.Ok && Reason == null;

        public string ModelPath { get; internal set; }           // GLB with the geometry (and the first clip, if animated)
        public string[] ExtraClipPaths { get; internal set; } = new string[0];  // further clips on the same rig
        public bool Animated { get; internal set; }
        public string SoundPath { get; internal set; }
        /// <summary>The sound has arrived, failed, or was never wanted (it never blocks the object).</summary>
        public bool SoundDone { get; internal set; }

        public event Action<ObjectJob> VerdictReady;
        public event Action<ObjectJob> Completed;
        public event Action<ObjectJob> SoundReady;

        internal void RaiseVerdict() => VerdictReady?.Invoke(this);
        internal void RaiseCompleted() => Completed?.Invoke(this);
        internal void RaiseSound() => SoundReady?.Invoke(this);
    }

    /// <summary>
    /// Runs the object pipeline (Phase3Design section 2): vision plan, then image refine, Tripo generation and the rig
    /// gate, with sound generated in parallel. Every step after the vision verdict degrades instead of failing, except
    /// the 3D generation itself.
    /// </summary>
    public class ObjectAgent
    {
        readonly MaliangConfig _config;
        readonly VisionClient _vision;
        readonly ImageRefineClient _refine;
        readonly TripoClient _tripo;
        readonly SoundClient _sound;

        public ObjectAgent(MaliangConfig config)
        {
            _config = config;
            _vision = new VisionClient(config);
            _refine = new ImageRefineClient(config);
            _tripo = new TripoClient(config);
            _sound = new SoundClient(config);
        }

        public ObjectCapabilities Capabilities => _vision.Capabilities;

        /// <summary>Generations started this session (all agents); capped by limits.maxGenerationsPerSession.</summary>
        public static int SessionCount { get; private set; }

        /// <summary>Starts a job for an exported ink drawing; returns at once.</summary>
        public ObjectJob Start(byte[] inkPng, string workDir, CancellationToken cancel = default)
        {
            Directory.CreateDirectory(workDir);
            File.WriteAllBytes(Path.Combine(workDir, "ink.png"), inkPng);
            var job = new ObjectJob { Id = Path.GetFileName(workDir.TrimEnd('/', '\\')), WorkDir = workDir };
            int cap = _config.limits.maxGenerationsPerSession;
            if (cap > 0 && SessionCount >= cap)
            {
                MaliangLog.Warn("Agent", $"Session limit reached ({cap} generations)");
                _ = FailLaterAsync(job, FailReason.Exhausted); // a frame later, so callers can subscribe first
                return job;
            }
            SessionCount++;
            _ = RunAsync(job, inkPng, cancel);
            return job;
        }

        async Task RunAsync(ObjectJob job, byte[] inkPng, CancellationToken cancel)
        {
            float started = Time.realtimeSinceStartup;
            try
            {
                // 1. Vision verdict
                Set(job, "vision", 0.02f);
                var plan = await _vision.PlanAsync(inkPng, "OBJECT", cancel);
                job.Plan = plan;
                File.WriteAllText(Path.Combine(job.WorkDir, "plan.json"), JsonConvert.SerializeObject(plan, Formatting.Indented));
                if (!plan.Ok)
                {
                    Fail(job, FailReasons.FromVision(plan.Reason));
                    return;
                }
                job.Verdict = JobVerdict.Ok;
                job.RaiseVerdict();

                // 5. Sound, in parallel; never blocks the object
                if (plan.Sound.Wanted && _sound.Enabled) _ = SoundAsync(job, plan.Sound, cancel);
                else job.SoundDone = true;

                // 2. Image refine (skipped on error)
                Set(job, "refine", 0.1f);
                byte[] reference = inkPng;
                if (_refine.Enabled && !string.IsNullOrWhiteSpace(plan.RefinePrompt))
                {
                    try
                    {
                        reference = await _refine.RefineAsync(inkPng, plan.RefinePrompt, cancel);
                        File.WriteAllBytes(Path.Combine(job.WorkDir, "refined.png"), reference);
                    }
                    catch (ApiException e) { MaliangLog.Warn("Agent", $"Refine skipped: {e.Message}"); }
                }

                // 3. Tripo generation (the one step whose failure fails the job)
                var cat = _vision.Capabilities.Get(plan.Category);
                bool animate = plan.Animate.Wanted && cat != null && cat.Riggable;
                string model = animate ? _config.tripo.creatureModel : _config.tripo.staticModel;
                int faceLimit = animate ? 0 : _config.tripo.staticFaceLimit;
                Set(job, "generate", 0.3f);
                string token = await _tripo.UploadAsync(reference, "reference.png", cancel);
                string genTask = await _tripo.GenerateAsync(token, model, faceLimit, cancel);
                var gen = await _tripo.WaitAsync(genTask, p => job.Progress = Mathf.Lerp(0.3f, 0.8f, p), _config.limits.objectTimeoutSec, cancel);
                string staticPath = Path.Combine(job.WorkDir, "model.glb");
                await TripoClient.DownloadAsync(TripoClient.ModelUrl(gen), staticPath, cancel);
                job.ModelPath = staticPath;

                // 4. Rig gate (degrades to the static model)
                if (animate)
                {
                    Set(job, "rig", 0.82f);
                    try { await RigAndAnimateAsync(job, genTask, plan, cancel); }
                    catch (ApiException e) { MaliangLog.Warn("Agent", $"Rig/animation skipped, static model: {e.Message}"); }
                }

                job.Done = true;
                Set(job, "done", 1f);
                MaliangLog.Info("Agent", $"Job {job.Id}: {plan.Subject} ready in {Time.realtimeSinceStartup - started:F0}s " +
                                         $"({(job.Animated ? "animated" : "static")}, {model})");
                job.RaiseCompleted();
            }
            catch (ApiException e)
            {
                MaliangLog.Warn("Agent", $"Job {job.Id} failed at {job.Stage}: {e.Message}");
                Fail(job, e.Reason);
            }
            catch (OperationCanceledException)
            {
                MaliangLog.Info("Agent", $"Job {job.Id} cancelled at {job.Stage}");
                Fail(job, FailReason.Unreachable);
            }
            catch (Exception e)
            {
                MaliangLog.Error("Agent", e);
                Fail(job, FailReason.Collapsed);
            }
        }

        async Task RigAndAnimateAsync(ObjectJob job, string genTask, VisionPlan plan, CancellationToken cancel)
        {
            var (riggable, rigType) = await _tripo.RigCheckAsync(genTask, cancel);
            if (!riggable)
            {
                MaliangLog.Info("Agent", $"Rig-check: not riggable (suggested {rigType}); static model");
                return;
            }
            // The rig-check's type wins over the plan's (Phase3Design 5.3).
            var cat = _vision.Capabilities.Categories.Values.FirstOrDefault(c => c.RigType == rigType && c.Riggable)
                      ?? _vision.Capabilities.Get(plan.Category);
            var presets = plan.Animate.Animations.Where(a => cat.Presets.Contains(a)).ToList();
            if (presets.Count == 0) presets.Add(cat.Presets.Contains("preset:biped:idle") ? "preset:biped:idle" : cat.Presets[0]);

            string rigTask = await _tripo.RigAsync(genTask, cat.RigModel, rigType, cancel);
            await _tripo.WaitAsync(rigTask, p => job.Progress = Mathf.Lerp(0.82f, 0.9f, p), 180, cancel);

            var paths = new List<string>();
            for (int i = 0; i < presets.Count; i++)
            {
                Set(job, "animate", 0.9f + 0.1f * i / presets.Count);
                string ret = await _tripo.RetargetAsync(rigTask, presets[i], cancel);
                var data = await _tripo.WaitAsync(ret, null, 180, cancel);
                string path = Path.Combine(job.WorkDir, $"anim_{i}.glb");
                await TripoClient.DownloadAsync(TripoClient.ModelUrl(data), path, cancel);
                paths.Add(path);
            }
            job.ModelPath = paths[0];
            job.ExtraClipPaths = paths.Skip(1).ToArray();
            job.Animated = true;
        }

        async Task SoundAsync(ObjectJob job, SoundPlan plan, CancellationToken cancel)
        {
            try
            {
                var mp3 = await _sound.GenerateAsync(plan, cancel);
                string path = Path.Combine(job.WorkDir, "sound.mp3");
                File.WriteAllBytes(path, mp3);
                job.SoundPath = path;
                job.RaiseSound();
            }
            catch (Exception e) { MaliangLog.Warn("Agent", $"Sound skipped: {e.Message}"); }
            finally { job.SoundDone = true; }
        }

        static async Task FailLaterAsync(ObjectJob job, FailReason reason)
        {
            await Task.Yield();
            Fail(job, reason);
        }

        static void Set(ObjectJob job, string stage, float progress)
        {
            job.Stage = stage;
            job.Progress = Mathf.Max(job.Progress, progress);
        }

        static void Fail(ObjectJob job, FailReason reason)
        {
            bool beforeVerdict = job.Verdict == JobVerdict.Pending;
            if (beforeVerdict) job.SoundDone = true; // no sound was started
            job.Reason = reason;
            job.Verdict = JobVerdict.Fail;
            job.Done = true;
            MaliangLog.Info("Agent", $"Job {job.Id} failed: {reason} (\"{FailReasons.Line(reason)}\")");
            if (beforeVerdict) job.RaiseVerdict();
            job.RaiseCompleted();
        }
    }
}
