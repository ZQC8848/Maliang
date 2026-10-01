using System;
using Newtonsoft.Json;

namespace Maliang.Api
{
    /// <summary>
    /// Why an object could not be summoned (Phase3Design 7.1). The first four come from the vision agent, the rest
    /// from technical problems; each maps to one fixed English line shown in the failure sequence.
    /// </summary>
    public enum FailReason
    {
        Unrecognizable,
        TooAbstract,
        NearlyBlank,
        Crowded,
        Unreachable,   // network error, timeout
        Exhausted,     // auth or quota
        Forbidden,     // content refused by a provider
        Collapsed,     // the 3D generation failed
        Faded,         // a library entry could not be loaded (replay)
    }

    public static class FailReasons
    {
        /// <summary>The in-game line for each reason (English only; no CJK font in the game).</summary>
        public static string Line(FailReason reason) => reason switch
        {
            FailReason.Unrecognizable => "The ink found no shape to become.",
            FailReason.TooAbstract => "The strokes wander. No form answers them.",
            FailReason.NearlyBlank => "Too little ink to hold a spirit.",
            FailReason.Crowded => "Too many forms in one breath. None could rise.",
            FailReason.Unreachable => "The spirit could not cross into this world. Try again.",
            FailReason.Exhausted => "The ink's power is spent for now.",
            FailReason.Forbidden => "This form may not be summoned.",
            FailReason.Collapsed => "The form collapsed before it could take shape.",
            FailReason.Faded => "This memory has faded.",
            _ => "The ink found no shape to become.",
        };

        public static FailReason FromVision(string reason) => reason switch
        {
            "too_abstract" => FailReason.TooAbstract,
            "nearly_blank" => FailReason.NearlyBlank,
            "crowded" => FailReason.Crowded,
            _ => FailReason.Unrecognizable,
        };
    }

    /// <summary>An API call that failed for good (after retries), already classified.</summary>
    public class ApiException : Exception
    {
        public FailReason Reason { get; }
        public long Status { get; }

        public ApiException(FailReason reason, string message, long status = 0) : base(message)
        {
            Reason = reason;
            Status = status;
        }
    }

    // ------------------------------------------------------------------ vision plan (Phase3Design 4.3)

    public class VisionPlan
    {
        [JsonProperty("status")] public string Status;
        [JsonProperty("reason")] public string Reason;
        [JsonProperty("seen")] public string Seen;
        [JsonProperty("subject")] public string Subject;
        [JsonProperty("category")] public string Category;
        [JsonProperty("refine_prompt")] public string RefinePrompt;
        [JsonProperty("model_prompt")] public string ModelPrompt;
        [JsonProperty("animate")] public AnimationPlan Animate = new AnimationPlan();
        [JsonProperty("sound")] public SoundPlan Sound = new SoundPlan();
        [JsonProperty("size_m")] public float? SizeM;

        [JsonIgnore] public bool Ok => Status == "ok";
    }

    public class AnimationPlan
    {
        [JsonProperty("wanted")] public bool Wanted;
        [JsonProperty("rig_type")] public string RigType;
        [JsonProperty("animations")] public string[] Animations = new string[0];
    }

    public class SoundPlan
    {
        [JsonProperty("wanted")] public bool Wanted;
        [JsonProperty("prompt")] public string Prompt;
        [JsonProperty("kind")] public string Kind;        // oneshot | loop
        [JsonProperty("trigger")] public string Trigger;  // on_spawn | on_grab | loop
        [JsonProperty("duration_s")] public float? DurationS;

        [JsonIgnore] public bool Loop => Kind == "loop";
    }
}
