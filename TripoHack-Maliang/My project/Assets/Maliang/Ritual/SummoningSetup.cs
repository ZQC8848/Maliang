using Maliang.Core;
using Maliang.Library;
using UnityEngine;

namespace Maliang.Ritual
{
    /// <summary>
    /// Connects sealing to the summoning at start-up. A sealed scroll runs, in order of preference:
    /// <list type="bullet">
    /// <item>「境」 in the fast mode (<c>fallback.fastMode</c>): a bundled world (<see cref="FallbackJob"/>), no wait.</item>
    /// <item>With the config online, the keys set and the network up: the real summoning, <see cref="SummonJob"/> for
    /// 「物」 and <see cref="WorldSummonJob"/> for 「境」 (both fall back to a bundled work if the APIs fail).</item>
    /// <item>Otherwise (no keys, offline): a bundled work (<see cref="FallbackJob"/>), matched by the vision agent when
    /// it alone is available.</item>
    /// <item>With no bundled work of that kind either: a <see cref="FakeJob"/> (nothing appears).</item>
    /// </list>
    /// Also clears leftovers of interrupted summonings.
    /// </summary>
    public static class SummoningSetup
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            ArtLibrary.CleanUp();
            var config = MaliangConfig.Current;
            ScrollRitual.StartJob = Start;
            MaliangLog.Info("Summon", $"Summoning: {(config.IsOnline ? "online" : "offline")}, object keys {config.HasObjectKeys}, " +
                                      $"world keys {config.HasWorldKeys}{(config.worldLabs.draft ? " (draft worlds)" : "")}, " +
                                      $"fallback {(config.fallback.enabled ? "on" : "off")}{(config.fallback.fastMode ? ", fast mode" : "")} " +
                                      $"(library at {ArtLibrary.Root})");
        }

        static IBurnJob Start(ScrollRitual r)
        {
            var config = MaliangConfig.Current;
            var seal = r.Seal ?? SealType.Object;
            bool network = Application.internetReachability != NetworkReachability.NotReachable;
            bool vision = config.IsOnline && network && !string.IsNullOrWhiteSpace(config.vision.apiKey);

            if (seal == SealType.World && config.fallback.fastMode && FallbackMatcher.Available(SealType.World))
                return new FallbackJob(r, seal, vision, "fast mode");

            if (config.IsOnline && network)
            {
                if (seal == SealType.Object && config.HasObjectKeys) return new SummonJob(r);
                if (seal == SealType.World && config.HasWorldKeys) return new WorldSummonJob(r);
            }

            if (!FallbackMatcher.Available(seal)) return null; // a fake summoning
            string why = !config.IsOnline ? "offline mode" : !network ? "no network" : "no API keys";
            return new FallbackJob(r, seal, vision, why);
        }
    }
}
