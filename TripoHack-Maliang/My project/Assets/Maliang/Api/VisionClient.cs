using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Maliang.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Maliang.Api
{
    /// <summary>
    /// The vision agent (Phase3Design section 4): one GPT call with the ink drawing returns a strict
    /// <see cref="VisionPlan"/> (status ok or fail). The system prompt, the capability table and the JSON schema live
    /// in StreamingAssets/Prompts and are shared with Tools/agent/agent_spike.py.
    /// </summary>
    public class VisionClient
    {
        const string Endpoint = "https://api.openai.com/v1/responses";
        static string PromptDir => Path.Combine(Application.streamingAssetsPath, "Prompts");

        readonly MaliangConfig _config;
        public ObjectCapabilities Capabilities { get; }

        public VisionClient(MaliangConfig config)
        {
            _config = config;
            Capabilities = ObjectCapabilities.Load();
        }

        public async Task<VisionPlan> PlanAsync(byte[] inkPng, string seal = "OBJECT", CancellationToken cancel = default)
        {
            string system = File.ReadAllText(Path.Combine(PromptDir, "vision_object.txt"))
                .Replace("{{CAPABILITIES}}", Capabilities.PromptJson);
            var schema = JObject.Parse(File.ReadAllText(Path.Combine(PromptDir, "vision_object.schema.json")));
            var started = Time.realtimeSinceStartup;
            var plan = JsonConvert.DeserializeObject<VisionPlan>(await RequestAsync(system, schema, inkPng, seal, cancel));
            Validate(plan);
            MaliangLog.Info("Vision", $"{plan.Status} in {Time.realtimeSinceStartup - started:F1}s: " +
                (plan.Ok ? $"{plan.Subject} [{plan.Category}]" : $"{plan.Reason}") + $" @ {plan.Confidence:F2} (seen: {plan.Seen})");
            return plan;
        }

        /// <summary>The 「境」 reading (Phase 6): which place the drawing shows, as a realistic landscape to grow a world from.</summary>
        public async Task<WorldPlan> PlanWorldAsync(byte[] inkPng, CancellationToken cancel = default)
        {
            string system = File.ReadAllText(Path.Combine(PromptDir, "vision_world.txt"));
            var schema = JObject.Parse(File.ReadAllText(Path.Combine(PromptDir, "vision_world.schema.json")));
            var started = Time.realtimeSinceStartup;
            var plan = JsonConvert.DeserializeObject<WorldPlan>(await RequestAsync(system, schema, inkPng, "WORLD", cancel));
            float min = _config.vision.minConfidence;
            if (plan.Ok && (plan.Confidence ?? 1f) < min)
            {
                MaliangLog.Info("Vision", $"'{plan.Subject}' at confidence {plan.Confidence:F2} < {min:F2}: unrecognizable");
                plan.Status = "fail";
                plan.Reason = "unrecognizable";
            }
            MaliangLog.Info("Vision", $"world {plan.Status} in {Time.realtimeSinceStartup - started:F1}s: " +
                (plan.Ok ? plan.Subject : plan.Reason) + $" @ {plan.Confidence:F2} (seen: {plan.Seen})");
            return plan;
        }

        async Task<string> RequestAsync(string system, JObject schema, byte[] inkPng, string seal, CancellationToken cancel)
        {
            var body = new JObject
            {
                ["model"] = _config.vision.model,
                ["input"] = new JArray
                {
                    new JObject { ["role"] = "system", ["content"] = new JArray { new JObject { ["type"] = "input_text", ["text"] = system } } },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JArray
                        {
                            new JObject { ["type"] = "input_text", ["text"] = "Seal: " + seal },
                            new JObject { ["type"] = "input_image", ["image_url"] = "data:image/png;base64," + Convert.ToBase64String(inkPng) },
                        },
                    },
                },
                ["text"] = new JObject
                {
                    ["format"] = new JObject { ["type"] = "json_schema", ["name"] = "vision_plan", ["strict"] = true, ["schema"] = schema },
                },
            };

            string reply = await Http.PostJsonTextAsync(Endpoint, Auth, body.ToString(Formatting.None), 90, cancel);
            return ExtractOutputText(JObject.Parse(reply));
        }

        Dictionary<string, string> Auth => new Dictionary<string, string> { ["Authorization"] = "Bearer " + _config.vision.apiKey };

        static string ExtractOutputText(JObject reply)
        {
            foreach (var item in reply["output"] ?? new JArray())
            {
                if ((string)item["type"] != "message") continue;
                foreach (var c in item["content"] ?? new JArray())
                {
                    if ((string)c["type"] == "output_text") return (string)c["text"];
                    if ((string)c["type"] == "refusal") throw new ApiException(FailReason.Forbidden, "Vision refused: " + (string)c["refusal"]);
                }
            }
            throw new ApiException(FailReason.Collapsed, "Vision reply had no output text");
        }

        /// <summary>Client-side checks (Phase3Design 4.3): anything outside the allow-lists is dropped.</summary>
        public void Validate(VisionPlan plan)
        {
            if (!plan.Ok) return;
            float min = _config.vision.minConfidence;
            if ((plan.Confidence ?? 1f) < min)
            {
                MaliangLog.Info("Vision", $"'{plan.Subject}' at confidence {plan.Confidence:F2} < {min:F2}: unrecognizable");
                plan.Status = "fail";
                plan.Reason = "unrecognizable";
                return;
            }
            var cat = Capabilities.Get(plan.Category);
            var anim = plan.Animate;
            if (anim.Wanted)
            {
                var kept = (anim.Animations ?? new string[0]).Where(a => cat != null && cat.Presets.Contains(a)).Take(2).ToArray();
                if (cat == null || !cat.Riggable || kept.Length == 0)
                {
                    MaliangLog.Info("Vision", $"Animation off for '{plan.Category}' (not riggable or no valid preset)");
                    anim.Wanted = false;
                }
                anim.Animations = kept;
            }
            if (plan.Sound.Wanted)
            {
                plan.Sound.DurationS = Mathf.Clamp(plan.Sound.DurationS ?? 3f, 0.5f, 30f);
                if (string.IsNullOrWhiteSpace(plan.Sound.Prompt)) plan.Sound.Wanted = false;
            }
            plan.SizeM = Mathf.Clamp(plan.SizeM ?? 0.4f, 0.15f, 1.2f);
        }
    }

    /// <summary>Tripo rig and animation capabilities per category (StreamingAssets/Prompts/object_capabilities.json).</summary>
    public class ObjectCapabilities
    {
        public class Category
        {
            [JsonProperty("riggable")] public bool Riggable;
            [JsonProperty("rigType")] public string RigType;
            [JsonProperty("rigModel")] public string RigModel;
            [JsonProperty("examples")] public string Examples;
            [JsonProperty("presets")] public List<string> Presets = new List<string>();
        }

        public Dictionary<string, Category> Categories = new Dictionary<string, Category>();
        public string PromptJson { get; private set; }

        public Category Get(string name) => name != null && Categories.TryGetValue(name, out var c) ? c : null;

        public static ObjectCapabilities Load()
        {
            var json = JObject.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Prompts", "object_capabilities.json")));
            json.Remove("_comment");
            return new ObjectCapabilities
            {
                Categories = json["categories"].ToObject<Dictionary<string, Category>>(),
                PromptJson = json.ToString(Formatting.Indented),
            };
        }
    }
}
