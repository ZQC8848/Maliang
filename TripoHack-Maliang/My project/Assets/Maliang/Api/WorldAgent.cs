using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Maliang.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Maliang.Api
{
    /// <summary>One sealed 「境」 drawing on its way to becoming a world around the lotus throne (Phase 6).</summary>
    public class WorldJob
    {
        public string Id;
        public string WorkDir;                 // ink, plan, refined, world.json, world.spz

        public JobVerdict Verdict { get; internal set; } = JobVerdict.Pending;
        public FailReason? Reason { get; internal set; }
        public WorldPlan Plan { get; internal set; }

        public string Stage { get; internal set; } = "queued";
        public float Progress { get; internal set; }
        public bool Done { get; internal set; }
        public bool Succeeded => Done && Verdict == JobVerdict.Ok && Reason == null;

        public string SpzPath { get; internal set; }
        public string SpzSize { get; internal set; }
        public string RefinedPath { get; internal set; }
        /// <summary>The place's looping ambience (MP3); null when sound is off or failed.</summary>
        public string AmbiencePath { get; internal set; }
        public string WorldId { get; internal set; }
        public string MarbleUrl { get; internal set; }
        public string Caption { get; internal set; }
        public string Model { get; internal set; }
        /// <summary>World Labs semantics (null when the model gives none, e.g. the draft model).</summary>
        public float? MetricScale { get; internal set; }
        public float? GroundOffset { get; internal set; }
    }

    /// <summary>
    /// The 「境」 pipeline: vision (which place) → refine into a realistic landscape (1536x1024) → World Labs (upload,
    /// generate, poll) → download the splats. In parallel, the place's ambience loop (ElevenLabs). The refine step
    /// degrades (the raw drawing goes up instead) and the ambience is optional; everything else fails the job.
    /// Mirrors <see cref="ObjectAgent"/>.
    /// </summary>
    public class WorldAgent
    {
        readonly MaliangConfig _config;
        readonly VisionClient _vision;
        readonly ImageRefineClient _refine;
        readonly WorldLabsClient _worlds;
        readonly SoundClient _sound;

        /// <summary>Length of the generated ambience loop (s; ElevenLabs allows up to 30).</summary>
        const float AmbienceSeconds = 22f;

        public WorldAgent(MaliangConfig config)
        {
            _config = config;
            _vision = new VisionClient(config);
            _refine = new ImageRefineClient(config);
            _worlds = new WorldLabsClient(config);
            _sound = new SoundClient(config);
        }

        public WorldJob Start(byte[] inkPng, string workDir, CancellationToken cancel = default)
        {
            Directory.CreateDirectory(workDir);
            File.WriteAllBytes(Path.Combine(workDir, "ink.png"), inkPng);
            var job = new WorldJob { Id = Path.GetFileName(workDir.TrimEnd('/', '\\')), WorkDir = workDir, Model = _worlds.Model };
            _ = RunAsync(job, inkPng, cancel);
            return job;
        }

        async Task RunAsync(WorldJob job, byte[] inkPng, CancellationToken cancel)
        {
            float started = Time.realtimeSinceStartup;
            try
            {
                Set(job, "vision", 0.02f);
                var plan = await _vision.PlanWorldAsync(inkPng, cancel);
                job.Plan = plan;
                File.WriteAllText(Path.Combine(job.WorkDir, "plan.json"), JsonConvert.SerializeObject(plan, Formatting.Indented));
                if (!plan.Ok) { Fail(job, FailReasons.FromVision(plan.Reason)); return; }
                job.Verdict = JobVerdict.Ok;
                var ambience = AmbienceAsync(job, plan.AmbiencePrompt, cancel); // takes seconds; the world takes far longer

                Set(job, "refine", 0.08f);
                byte[] reference = inkPng;
                if (_refine.Enabled && !string.IsNullOrWhiteSpace(plan.RefinePrompt))
                {
                    try
                    {
                        reference = await _refine.RefineAsync(inkPng, plan.RefinePrompt, cancel, "1536x1024");
                        job.RefinedPath = Path.Combine(job.WorkDir, "refined.png");
                        File.WriteAllBytes(job.RefinedPath, reference);
                    }
                    catch (ApiException e) { MaliangLog.Warn("World", $"Refine skipped: {e.Message}"); }
                }

                Set(job, "upload", 0.25f);
                string asset = await _worlds.UploadAsync(reference, "reference.png", cancel);
                Set(job, "generate", 0.3f);
                string op = await _worlds.GenerateAsync(asset, plan.WorldPrompt, plan.Subject, cancel);
                float expected = _config.worldLabs.draft ? 30f : 300f;
                var world = await _worlds.WaitAsync(op, p => job.Progress = Mathf.Lerp(0.3f, 0.85f, p), expected, 1800, cancel);
                File.WriteAllText(Path.Combine(job.WorkDir, "world.json"), world.ToString(Formatting.Indented));

                job.WorldId = (string)world["world_id"];
                job.MarbleUrl = (string)world["world_marble_url"];
                job.Caption = (string)world["assets"]?["caption"];
                var semantics = world["assets"]?["splats"]?["semantics_metadata"] as JObject;
                job.MetricScale = (float?)semantics?["metric_scale_factor"];
                job.GroundOffset = (float?)semantics?["ground_plane_offset"];

                Set(job, "download", 0.88f);
                string url = _worlds.SpzUrl(world, out var size);
                if (url == null) throw new ApiException(FailReason.Collapsed, "World Labs world has no splats");
                job.SpzSize = size;
                job.SpzPath = Path.Combine(job.WorkDir, "world.spz");
                await WorldLabsClient.DownloadAsync(url, job.SpzPath, cancel);
                await Task.WhenAny(ambience, Task.Delay(15000, cancel)); // never hold the world back for long

                job.Done = true;
                Set(job, "done", 1f);
                MaliangLog.Info("World", $"Job {job.Id}: \"{plan.Subject}\" ready in {Time.realtimeSinceStartup - started:F0}s " +
                                         $"({job.Model}, {size}, {new FileInfo(job.SpzPath).Length / 1048576f:F1} MB, scale {job.MetricScale?.ToString("F2") ?? "-"})");
            }
            catch (ApiException e)
            {
                MaliangLog.Warn("World", $"Job {job.Id} failed at {job.Stage}: {e.Message}");
                Fail(job, e.Reason);
            }
            catch (OperationCanceledException)
            {
                Fail(job, FailReason.Unreachable);
            }
            catch (Exception e)
            {
                MaliangLog.Error("World", e);
                Fail(job, FailReason.Collapsed);
            }
        }

        /// <summary>Generates the place's ambience loop; any failure just leaves the world silent.</summary>
        async Task AmbienceAsync(WorldJob job, string prompt, CancellationToken cancel)
        {
            if (!_sound.Enabled || string.IsNullOrWhiteSpace(prompt)) return;
            try
            {
                var mp3 = await GenerateAmbienceAsync(_sound, prompt, cancel);
                string path = Path.Combine(job.WorkDir, "ambience.mp3");
                File.WriteAllBytes(path, mp3);
                job.AmbiencePath = path;
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                MaliangLog.Warn("World", $"Ambience skipped: {e.Message}");
            }
        }

        /// <summary>A seamless, quiet background loop of the place described by <paramref name="prompt"/>.</summary>
        public static Task<byte[]> GenerateAmbienceAsync(SoundClient sound, string prompt, CancellationToken cancel = default) =>
            sound.GenerateAsync(new SoundPlan
            {
                Wanted = true,
                Kind = "loop",
                Trigger = "loop",
                DurationS = AmbienceSeconds,
                Prompt = prompt.TrimEnd('.') + ". Calm continuous ambience, seamless loop, no music, no voices.",
            }, cancel);

        static void Set(WorldJob job, string stage, float progress)
        {
            job.Stage = stage;
            job.Progress = Mathf.Max(job.Progress, progress);
        }

        static void Fail(WorldJob job, FailReason reason)
        {
            job.Reason = reason;
            job.Verdict = JobVerdict.Fail;
            job.Done = true;
            MaliangLog.Info("World", $"Job {job.Id} failed: {reason} (\"{FailReasons.Line(reason)}\")");
        }
    }
}
