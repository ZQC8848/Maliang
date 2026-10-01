using System;
using System.IO;
using System.Linq;
using Maliang.Api;
using Maliang.Core;
using Maliang.Loading;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Maliang.EditorTools
{
    /// <summary>
    /// Runs the object agent on a PNG without the headset (Phase3Design section 11). Each run writes the plan,
    /// refined image, model(s) and sound to TestData/Agent/unity/&lt;name&gt;_&lt;time&gt;/ and logs every stage.
    /// </summary>
    public static class ObjectAgentMenu
    {
        static ObjectAgent _agent;
        static ObjectJob _job;
        static string _lastDir;

        public static ObjectJob LastJob => _job;
        public static string LastDir => _lastDir;

        [MenuItem("Maliang/Agent/Run Object Agent On PNG...")]
        static void RunFromPanel()
        {
            string start = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestData", "Agent", "testset"));
            string png = EditorUtility.OpenFilePanel("Ink drawing", Directory.Exists(start) ? start : "", "png");
            if (!string.IsNullOrEmpty(png)) Run(png);
        }

        /// <summary>Starts a run; returns the job (also kept in <see cref="LastJob"/>).</summary>
        public static ObjectJob Run(string pngPath)
        {
            MaliangConfig.Reload();
            var config = MaliangConfig.Current;
            if (!config.HasObjectKeys)
            {
                Debug.LogError("[Agent] maliang.config.json needs tripo.apiKey and vision.apiKey");
                return null;
            }
            _agent = new ObjectAgent(config);
            string name = Path.GetFileNameWithoutExtension(pngPath);
            _lastDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestData", "Agent", "unity", $"{name}_{DateTime.Now:HHmmss}"));
            var job = _agent.Start(File.ReadAllBytes(pngPath), _lastDir);
            _job = job;
            float t0 = Time.realtimeSinceStartup;
            string Elapsed() => $"{Time.realtimeSinceStartup - t0:F0}s";
            job.VerdictReady += j => Debug.Log($"[Agent] {Elapsed()} verdict {j.Verdict}" +
                                               (j.Plan != null && j.Plan.Ok ? $": {j.Plan.Subject} [{j.Plan.Category}]" : $": {j.Reason}"));
            job.SoundReady += j => Debug.Log($"[Agent] {Elapsed()} sound ready: {j.SoundPath}");
            job.Completed += j => Debug.Log($"[Agent] {Elapsed()} " + (j.Succeeded
                ? $"done: {(j.Animated ? "animated" : "static")} {j.ModelPath} (+{j.ExtraClipPaths.Length} clips)"
                : $"failed: {j.Reason} \"{FailReasons.Line(j.Reason ?? FailReason.Collapsed)}\""));
            Debug.Log($"[Agent] started on {name} -> {_lastDir}");
            return job;
        }

        [MenuItem("Maliang/Agent/Spawn Last Result (Play Mode)")]
        static void SpawnLast()
        {
            if (_lastDir == null) { Debug.Log("[Agent] no run yet"); return; }
            _ = SpawnFromFolder(_lastDir);
        }

        /// <summary>Spawns a finished run (its folder) 0.6 m in front of the camera. Play mode only.</summary>
        public static async System.Threading.Tasks.Task<SummonedObject> SpawnFromFolder(string dir)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[Agent] enter play mode first"); return null; }
            var plan = JsonConvert.DeserializeObject<VisionPlan>(File.ReadAllText(Path.Combine(dir, "plan.json")));
            var anims = Directory.GetFiles(dir, "anim_*.glb").OrderBy(f => f).ToArray();
            string sound = Path.Combine(dir, "sound.mp3");
            var req = new ObjectSpawner.Request
            {
                ModelPath = anims.Length > 0 ? anims[0] : Path.Combine(dir, "model.glb"),
                ExtraClipPaths = anims.Skip(1).ToArray(),
                Animated = anims.Length > 0,
                SoundPath = File.Exists(sound) ? sound : null,
                SoundTrigger = plan.Sound.Wanted ? plan.Sound.Trigger : null,
                Category = plan.Category,
                SizeM = plan.SizeM ?? 0.4f,
                Name = plan.Subject,
            };
            var cam = Camera.main != null ? Camera.main.transform : null;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized : Vector3.forward;
            Vector3 at = (cam != null ? cam.position : new Vector3(0f, 1.3f, 0f)) + fwd * 0.6f + Vector3.down * 0.15f;
            return await ObjectSpawner.SpawnAsync(req, at, Quaternion.LookRotation(-fwd, Vector3.up));
        }

        [MenuItem("Maliang/Agent/Log Last Job Status")]
        static void LogStatus()
        {
            if (_job == null) { Debug.Log("[Agent] no job"); return; }
            Debug.Log($"[Agent] stage {_job.Stage}, progress {_job.Progress:P0}, verdict {_job.Verdict}, done {_job.Done}, reason {_job.Reason}");
        }
    }
}
