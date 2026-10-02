using System;
using System.Collections.Generic;
using System.Linq;
using Maliang.Api;
using Maliang.Core;
using UnityEngine;

namespace Maliang.Library
{
    /// <summary>
    /// The fallback (TechPlan §9): when the APIs cannot summon (no keys, offline, quota, network, a generation that
    /// collapsed), a bundled work comes instead and the ritual plays out the same. The closest one is chosen by the
    /// words of the subject the vision agent read (against each work's keywords, subject and category); without a
    /// subject, or when nothing matches, a random one of the same kind.
    /// </summary>
    public static class FallbackMatcher
    {
        static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "a", "an", "the", "of", "with", "and", "in", "on", "at", "by", "to", "from", "over", "under", "above", "below",
            "its", "his", "her", "their", "some", "two", "three", "small", "big", "large", "little", "tiny", "single",
            "solitary", "whole", "quiet", "calm", "old", "young", "traditional", "simple", "open", "near", "far",
            // colours say little about what a thing is
            "red", "orange", "yellow", "golden", "gold", "green", "blue", "purple", "pink", "white", "black", "grey", "gray",
            "brown", "dark", "light", "pale", "bright", "tricolour", "tricolor", "colourful", "colorful",
        };

        public static bool Enabled => MaliangConfig.Current.fallback.enabled;

        /// <summary>Failures that are the APIs' (not the ritual's judgement of the drawing): these fall back.</summary>
        public static bool Covers(FailReason? reason) =>
            reason == FailReason.Unreachable || reason == FailReason.Exhausted || reason == FailReason.Collapsed;

        /// <summary>Whether a bundled work of this kind exists to fall back on.</summary>
        public static bool Available(SealType seal) => Enabled && ArtLibrary.Bundled(seal).Count > 0;

        /// <summary>The bundled work closest to <paramref name="subject"/> (null subject: random); null if there is none.</summary>
        public static LibraryEntry Match(SealType seal, string subject, string category = null)
        {
            var works = ArtLibrary.Bundled(seal);
            if (works.Count == 0) return null;

            var words = Words(subject);
            LibraryEntry best = null;
            float bestScore = 0f;
            string why = "random";
            foreach (var w in works.OrderBy(_ => UnityEngine.Random.value)) // ties go to a random one
            {
                var keys = new HashSet<string>(Words(string.Join(" ", w.keywords ?? new string[0])));
                var named = new HashSet<string>(Words(w.subject));
                var hits = words.Where(x => keys.Contains(x) || named.Contains(x)).Distinct().ToList();
                float score = hits.Sum(x => keys.Contains(x) ? 2f : 1f);
                if (!string.IsNullOrEmpty(category) && string.Equals(category, w.category, StringComparison.OrdinalIgnoreCase))
                    score += 0.5f;
                if (score > bestScore)
                {
                    best = w;
                    bestScore = score;
                    why = hits.Count > 0 ? "matched " + string.Join(", ", hits) : "same category " + category;
                }
            }
            if (best == null) best = works[UnityEngine.Random.Range(0, works.Count)];
            MaliangLog.Info("Fallback", $"{seal} \"{subject ?? "(no subject)"}\" -> bundled \"{best.subject}\" ({why})");
            return best;
        }

        /// <summary>Lower-case content words, plurals folded ("fishes" -> "fish", "cats" -> "cat").</summary>
        static List<string> Words(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (var raw in text.ToLowerInvariant().Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw.Length < 2 || Ignored.Contains(raw)) continue;
                string w = raw;
                if (w.EndsWith("es") && w.Length > 4 && (w.EndsWith("shes") || w.EndsWith("ches") || w.EndsWith("xes"))) w = w.Substring(0, w.Length - 2);
                else if (w.EndsWith("s") && !w.EndsWith("ss") && w.Length > 3) w = w.Substring(0, w.Length - 1);
                result.Add(w);
            }
            return result;
        }

        static readonly char[] Separators = " ,.;:!?'\"()-/\t\n".ToCharArray();
    }
}
